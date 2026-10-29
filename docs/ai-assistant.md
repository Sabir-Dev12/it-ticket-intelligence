# AI Assistant

La connexion utilisateur passe désormais par `/login` avec JWT. L'assistant est réservé à Agent/Support/Admin ; les tokens techniques ne servent plus de connexion REST utilisateur. Voir [Authentification JWT](authentication.md).

La page `/ai-assistant` ouvre une investigation privée à partir d’une question
comme `Why is ticket #1045 failing?`. `#1045`, `ticket 1045` et `IT-1045`
désignent la référence serveur `IT-1045`. La référence doit exister et appartenir
au scope du profil. Une conversation reste liée à un seul ticket.

## Parcours

1. L’API résout le ticket dans le scope autorisé et crée la conversation.
2. Elle enregistre le message utilisateur et un tour `Pending` avant l’inférence.
3. `TicketAgent` appelle obligatoirement `get_ticket` et `get_ticket_history`,
   exécute le pipeline ML.NET existant et recherche des chunks RAG courants.
4. Le LLM reçoit la question, les observations rapportées, les prédictions,
   l’historique du ticket et une mémoire bornée de la conversation. Il peut
   demander les outils de lecture contrôlés existants, via le service MCP.
5. L’agent valide la réponse structurée, ses citations et la révision du ticket,
   puis sauvegarde l’analyse immuable. Le tour référence ce résultat par RunId.
6. L’interface affiche résumé, diagnostic, causes possibles, actions proposées,
   sources et lectures d’outils réellement auditées.

Le domaine ne dépend d’aucune technologie nouvelle. `Application/Assistant`
définit le service et le port de stockage ; Infrastructure implémente la
persistance ; le contrôleur traduit les requêtes et l’identité authentifiée.
Le LLM ne reçoit ni DbContext, ni SQL, ni jeton de session.

Les faits sont des observations rapportées. ML reste une prédiction ; diagnostic
et causes restent des hypothèses. Toute correction nécessite une approbation
humaine. L’assistant ne change ni statut, ni équipe, ni classification. Il
enregistre réellement les prédictions, analyses, messages et audits.

## API

Toutes les routes exigent `Authorization: Bearer <session-token>`, la permission
`tickets.read`, un utilisateur actif associé au profil et le scope du ticket.
Toutes les réponses, y compris les erreurs, portent `Cache-Control: no-store`.

| Méthode et route | Contrat |
|---|---|
| `POST /api/assistant/conversations` | `{ requestId, question, ticketId? }` ; 201, conversation vide liée au ticket résolu |
| `GET /api/assistant/conversations?page=1` | Conversations du propriétaire, 20 par page, `hasMore` |
| `GET /api/assistant/conversations/{id}` | Messages, résultats structurés, état des tours et activité auditée |
| `POST /api/assistant/conversations/{id}/messages` | `{ requestId, content }` ; 200, état sauvegardé après investigation |

`requestId` est un UUID généré par le client pour chaque opération. Réutiliser le
même identifiant et le même contenu renvoie l’opération existante sans relancer
le modèle. Un autre contenu avec le même identifiant produit 409. Les identifiants
ne confèrent aucun droit d’accès. Le client ne transmet jamais de propriétaire.

Un tour retourne `userMessage`, `assistantMessage?`, `analysis?`, `activity`,
`runId`, `state`, `errorCode?` et les dates. `analysis` reprend le contrat de
l’agent : faits, prédictions, summary, diagnosis, possibleCauses,
recommendedActions, sources, confidence et requiresHumanReview. Les sources
proviennent uniquement des chunks vérifiés de cette investigation.

401 : session absente/invalide. 403 : permission, mapping ou utilisateur actif
manquant. 404 : conversation/ticket absent ou hors scope. 400 : paramètres
invalides ou changement de ticket dans une conversation. 409 : opération
concurrente, contenu incompatible avec un identifiant ou limite atteinte.
Les erreurs de transport utilisent ProblemDetails. Un échec d’investigation
déjà accepté est un tour `Failed` persisté, pas une réponse IA inventée.

## Session privée

Les profils MCP/SignalR existants peuvent recevoir un mapping supplémentaire
dans les variables d’environnement, hors Git :

```text
MCP__Clients__0__UserId=<UUID d’un utilisateur actif existant>
```

Le profil doit déjà posséder son TokenHash, Subject, `tickets.read` et un scope
TicketIds ou AllTickets explicite, selon [MCP](mcp-server.md). L’absence de
UserId conserve le comportement MCP/SignalR, mais refuse l’historique privé.
Un autre profil associé au même utilisateur représente la même identité ;
son scope de tickets continue de filtrer chaque lecture.

Dans Settings, saisir le jeton de ce profil. Il est envoyé à l’assistant dans
l’en-tête Authorization et conservé uniquement en mémoire. Un rechargement
nécessite une reconnexion ; les conversations restent en PostgreSQL. Les
changements de session effacent le brouillon et les vues privées précédentes.
Ce mécanisme reprend les profils techniques locaux existants ; il ne fournit
pas un écran de connexion utilisateur ou un fournisseur OIDC.

## Historique, erreurs et limites

- Tables existantes `ai_conversations` et `ai_messages`, plus `assistant_turns`.
  La migration `20261027205640_AssistantConversations` ajoute les FK, contraintes,
  index et contrôle de concurrence. `AssistantStateConstraints` interdit aussi
  un résultat ou un code d’échec NULL pour un tour terminé. Aucune suppression
  silencieuse d’historique.
- Un seul tour actif et au maximum dix tours par conversation. Question limitée
  à 2 000 caractères, requête limitée à 8 192 octets. Historique paginé.
- Mémoire LLM : quatre messages précédents au maximum, 1 900 caractères chacun.
  Les anciennes propositions sont explicitement étiquetées comme non exécutées.
  Le contexte courant est relu pour chaque question.
- Budget agent 180 secondes ; attente client 195 secondes. Les GET durent au
  maximum 15 secondes. Aucun retry automatique du POST côté navigateur.
- Le navigateur relit le tour toutes les deux secondes pendant l’attente, sans
  chevaucher ces lectures. SignalR invalide aussi les lectures ; la récupération
  d’un résultat ne dépend pas de la livraison d’une notification.
- Une annulation est enregistrée avec un jeton de nettoyage indépendant. Une
  opération abandonnée sans clôture devient `Interrupted` à la lecture après
  240 secondes ; un nouveau message réconcilie l’ancien tour. Aucun redémarrage
  automatique du modèle ou de correction métier.
- Une coupure HTTP laisse le brouillon et propose de relire le résultat sauvegardé.
  Réessayer le même message dans la même page conserve son requestId. Après
  rechargement, consulter le tour existant avant de poser une nouvelle question.
- L’activité affichée vient uniquement de `Agent.Tool.Started/Succeeded/Denied/Failed`.
  Les arguments et sorties brutes ne sont pas renvoyés au navigateur. Un appel
  démarré n’est jamais marqué terminé sans audit de succès.
- Une réponse ancienne signale que le ticket a changé de révision. Le résultat
  structuré est relu depuis l’analyse immuable avec vérification de provenance.

## Vérification effectuée

- Compilation Release .NET : zéro erreur et zéro avertissement ; 306 tests
  unitaires et 302 tests d’intégration PostgreSQL réussis.
- Tests assistant : résolution de référence naturelle, mémoire des suivis,
  citations vérifiées, persistance, idempotence, concurrence, isolation de
  propriétaire et scope, refus sans authentification/mapping et utilisateur
  inactif, échec LLM conservant les lectures réalisées.
- Frontend : 23 tests unitaires et 19 scénarios Chrome réussis. Ils couvrent la réponse, les
  suivis, l’historique après reconnexion, une autre session, les erreurs et le
  brouillon ; axe et les petits écrans sont vérifiés dans les deux thèmes.
- Parcours réel Chrome → API → ML.NET → PostgreSQL : deux tours sauvegardés,
  sources vérifiées, lectures auditées et historique après reconnexion ; révision,
  statut et équipe du ticket inchangés. Les frontières LLM et embeddings sont
  simulées par Postman, sans clé ni appel à un fournisseur réel.
- Collection Postman Assistant : cinq requêtes et huit assertions réussies sur
  l’API réelle. La collection complète est valide : 66 requêtes, zéro erreur de lint.
- Tokens et références du thème : contrôles réussis. Mesure de 51 états
  interactifs en clair et de 51 en sombre : contrôles WCAG AA réussis.
  Le gate global du kit, lancé depuis le dépôt, retourne 0/52 : il attend ses
  propres scripts/exemples en chemins relatifs et `python3`. Ce run global ne
  valide donc pas l’application. Les contrôles ciblés ci-dessus ont été lancés
  explicitement sur les fichiers et rendus de l’assistant.

Artefacts locaux ignorés : `artifacts/frontend/assistant-*`. Les fournisseurs
simulés et les jeux de données de test restent dans les outils de vérification ;
le produit ne possède aucun fallback contenant des réponses synthétiques.

Pour reproduire les tests navigateur, lancer la simulation frontend sur 4908,
Vite avec `API_PROXY_TARGET=http://127.0.0.1:4908/helpdesk` sur 5174, puis :

```powershell
$env:FRONTEND_URL='http://127.0.0.1:5174'
npm run test:e2e --prefix frontend
```

Le smoke réel réutilise les scripts `prepare-ticket-details-smoke.ps1` et
`start-assistant-api.ps1`, la simulation `ticket-details-providers` sur 4909,
puis `node scripts/assistant-live-smoke.mjs` depuis frontend. Utiliser ensuite
`cleanup-ticket-details-smoke.ps1` : archivage du ticket de test, suppression de
la source synthétique et désactivation de ses seuls utilisateur/équipe. Les
conversations, analyses et audits de vérification sont conservés.
