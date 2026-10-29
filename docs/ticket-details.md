# Ticket Details

Page `/tickets/:id`, dans le design system existant. Les couleurs, espacements,
rayons et typographie proviennent du thème partagé ; aucune nouvelle palette.
Composition : en-tête et actions, description/propriétés, trois prédictions ML
comparables, proposition IA avec sources, puis conversation. Les propriétés sont
compactes pour laisser la place aux résultats. Les formulaires restent dans la page.

## Informations et intelligence

- Informations : référence, titre, description, catégorie, priorité, statut,
  équipe assignée par son nom, dates, révision et résolution éventuelle.
- ML.NET Intelligence : catégorie/confiance, priorité/confiance et équipe
  recommandée/confiance. Seul le dernier lot est présenté ; les prédictions de
  deux lots distincts ne sont pas assemblées. Un résultat absent est indiqué.
- Les prédictions historiques ne modifient jamais les propriétés du ticket.
  Une faible confiance conserve la décision serveur de revue humaine ; le client
  ne remplace pas ce seuil et n’affecte pas automatiquement l’équipe recommandée.
- AI Analysis : Summary, Diagnosis, Possible Causes, Recommended Actions et
  Sources. Diagnostic/causes sont des hypothèses ; les actions sont des propositions
  soumises à validation humaine. Aucun bouton ne les exécute automatiquement.
- Les sources affichent uniquement les citations sauvegardées : document, titre,
  chemin, version et chunk. Les liens pointent vers la page locale du document,
  jamais vers une URL inventée par le modèle. Le contenu est rendu comme texte React,
  sans HTML interprété. La source peut avoir été modifiée ou supprimée depuis.
- Une ancienne révision ML/IA est signalée ; le résultat précédent reste lisible.

## Actions connectées

| Action | Appel | Comportement |
|---|---|---|
| Analyze Ticket | POST `/api/tickets/{id}/analyze` | Trois modèles ; relecture des prédictions sauvegardées |
| Ask AI | POST `/api/tickets/{id}/investigate` | ML/RAG/outils/LLM, puis lecture de la proposition persistée |
| Assign Team | PATCH `/api/tickets/{id}/assignment` | Choix explicite d’une équipe active ou désaffectation |
| Change Status | PATCH `/api/tickets/{id}/status` | Seulement les transitions autorisées ; résolution obligatoire pour Resolved |
| Add Comment | POST `/api/tickets/{id}/comments` | Auteur actif fourni explicitement et message non vide, 4 000 caractères maximum |

Le formulaire ne devine pas l’identité de l’auteur et n’emprunte pas celle du
créateur du ticket. Le module de connexion utilisateur n’existe pas encore :
le mode local exige un identifiant d’utilisateur actif, explicitement saisi.
Les routes REST conservent leur mécanisme actuel ; cette page n’ajoute pas de
nouveau système d’authentification.

Les transitions suivent le Domain : Open → InProgress → Resolved → Closed,
et Resolved → InProgress pour rouvrir. Les analyses et affectations exigent
Open/InProgress ; commentaires interdits sur les tickets fermés/archivés.

Une action en cours désactive les autres envois. Les mutations transmettent la
révision capturée à l’ouverture du formulaire. Si le ticket change, le brouillon
reste présent et l’utilisateur doit revoir la révision courante avant de confirmer.
Un 409 n’entraîne aucun nouvel envoi automatique. Après erreur/timeout, la page
relit les sauvegardes et indique que l’issue n’a pas pu être confirmée, plutôt que
d’annoncer une réussite. Les POST/PATCH ne possèdent aucun retry automatique.
Le délai client Ask AI est de 195 secondes, supérieur au budget agent de 180 secondes.

## Lecture complémentaire minimale

`GET /api/tickets/{id}/workspace` → `TicketWorkspaceDto` :

```text
assignedTeam: { id, name, isActive } | null
assignableTeams: [{ id, name, isActive }]
teamsTruncated: boolean
analysis: { result: TicketAgentResult, createdAt } | null
```

200 : contexte trouvé ; 400 : UUID vide ; 404 : ticket absent ; erreurs globales
ProblemDetails. Au plus 200 équipes actives, triées ; une équipe affectée inactive
reste identifiée mais ne figure pas dans les choix. Si la liste est tronquée,
le formulaire permet de fournir un ID connu, toujours validé côté serveur.

Le handler et le contrat vivent dans Application ; le reader EF Core dans
Infrastructure ; le controller délègue. Domain ne change pas. La dernière analyse
est lue depuis la table immutable existante, sans exécuter de LLM. La provenance
ticket/run/révision et le hash de la sérialisation du résultat sont vérifiés.
PostgreSQL jsonb réordonne le JSON ; le contrôle utilise la même sérialisation DTO
que le writer. Aucune migration nécessaire.

## SignalR

Le client existant rejoint le ticket avec le profil autorisé. Les sept événements
invalident les lectures ; PredictionCompleted, AIAnalysisCompleted, CommentAdded,
TicketStatusChanged et TicketAssigned rafraîchissent donc les sections concernées.
Les snapshots sauvegardés sont la source de vérité. EventId déduplique les
notifications ; une révision identique reste valide pour plusieurs analyses.
Reconnexion et réabonnement relisent l’état. Sans session autorisée, la page annonce
le mode manuel et propose les réglages. Aucun token dans le stockage navigateur.

Les relectures gardent le snapshot précédent ; elles ne démontent pas un formulaire
en cours. Les réponses d’un autre ticket après navigation sont écartées. Les
requêtes de lecture précédentes et l’attente d’une action sont annulées au départ
de la page ; une nouvelle visite relit le résultat serveur.

## Validation du 2026-10-27

- Backend Release : zéro erreur, zéro avertissement. **592 tests xUnit** réussis
  (298 unitaires, 294 intégration), dont lecture des équipes actives/inactives,
  400/404 et restitution d’une analyse sauvegardée avec citations/actions.
- Frontend : build TypeScript/Vite réussi, **21 tests unitaires** réussis.
- **14 scénarios Chrome** : cinq actions, relecture après navigation, conflit sans
  retry, brouillon conservé, panne LLM avec ML conservé, statuts terminaux, clavier,
  thèmes, axe et responsive jusqu’à 280 px avec texte à 125 %.
- Postman : collection valide, **61 requêtes sans erreur de lint** ; deux requêtes
  workspace réelles, **5 assertions réussies**. OpenAPI actualisé.
- Test intersessions : vrai frontend/API, vrais modèles ML, PostgreSQL et hub
  SignalR ; seuls les fournisseurs HTTP embeddings/LLM sont simulés. La session
  observatrice reçoit les cinq événements et montre les prédictions, sources IA,
  équipe, statut et commentaire sans Refresh. Son brouillon est conservé. Cela
  vérifie le transport et les workflows, pas la qualité sémantique d’un vrai LLM.

Design-screen : rapport global réellement exécuté, **0/52** (installation partagée
incomplète : exemples/Python/dépendances absents), pas présenté comme un succès.
Contrôles ciblés avec navigateur obligatoire : **54 états par thème** pour le
snapshot complet, **66 par thème** pour le formulaire commentaire ; contrastes
default/hover/focus OK. Responsive 280/320/414 à 125 % OK. Slop strict : un LOW sur
les neutres du thème existant ; taste strict : MED sur les trois lignes ML de même
taille, correspondant aux sorties parallèles demandées. Aucun HIGH bloquant.
Les règles Axe activées ne détectent aucune violation dans les formulaires des
deux thèmes ; cela ne constitue pas une certification avec lecteur d’écran.

Critique indépendante : **ship**, aucun Critical/Major. Finitions appliquées :
état sans résultat pour la recherche d’équipe, pluriel des commentaires, focus
du choix de statut, titre vide IA explicite, erreur de commentaire plus précise
et identifiants techniques repliables. Le verdict est un jugement, pas un score.

## Rejouer les vérifications

Les scénarios Chrome utilisent la simulation locale existante frontend-read-api,
désormais enrichie de mutations contrôlées et d’états conservés, jamais importée
par le produit. Son contexte documente lancement et contraintes.

```powershell
npm run build --prefix frontend
npm test --prefix frontend
npm run test:e2e --prefix frontend
```

Test réel intersessions, depuis la racine, après compilation :

```powershell
# Terminal fournisseurs, aucune publication
postman simulation run postman/simulations/ticket-details-providers/ticket-details-providers.sim.yaml --port 4909 --no-history
# Autre terminal : profil de vérification local et fournisseurs synthétiques
./frontend/scripts/start-ticket-details-api.ps1
# Autre terminal : préparation de références possédées par le test
./frontend/scripts/prepare-ticket-details-smoke.ps1
# Autre terminal frontend
$env:API_PROXY_TARGET='http://127.0.0.1:5080'
npm run dev --prefix frontend -- --port 5174
# Terminal test
$env:FRONTEND_URL='http://127.0.0.1:5174'
$env:FRONTEND_SMOKE_TOKEN='ticket-details-local-verification-0123456789abcdef'
Push-Location frontend
node scripts/ticket-details-live.mjs
Pop-Location
./frontend/scripts/cleanup-ticket-details-smoke.ps1
```

Le token de cette commande est une valeur synthétique liée uniquement au serveur
de vérification temporaire, en lecture seule. Les scripts ne modifient pas `.env`.
Après test, arrêter cet hôte et les fournisseurs, puis relancer l’API normale.
Le nettoyage vérifie la propriété des références, archive son ticket, désactive
son auteur/équipe et supprime son document synthétique ; l’audit et les prédictions
restent conservés. Aucun SQL arbitraire n’est exposé à l’agent.
