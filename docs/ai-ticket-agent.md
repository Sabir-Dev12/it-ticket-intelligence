# AI Ticket Agent

`POST /api/tickets/{id}/investigate`, sans body, déclenche un cas d’usage
Application via `ITicketAgent`. La route ML `/analyze` conserve son contrat.

## Workflow

1. Créer un RunId et persister `Agent.Run.Started`.
2. Appeler `get_ticket` avec un identifiant imposé par le serveur. Refuser un
   ticket absent, archivé, résolu ou fermé.
3. Exécuter le pipeline ML existant : catégorie, priorité, recommandation
   d’équipe et sauvegarde des trois prédictions.
4. Exécuter `search_knowledge` pour obtenir un premier contexte RAG courant.
5. Demander au modèle les outils de lecture utiles. Le serveur valide et
   exécute chaque demande, puis retourne son résultat au modèle.
6. Terminer par une requête sans outils, avec schéma JSON de sortie imposé.
7. Valider la structure et les citations, reconstruire leur provenance depuis
   les chunks reçus, imposer la revue humaine.
8. Dans une transaction courte, verrouiller le ticket et les documents cités,
   vérifier leurs versions, enregistrer la proposition et l’audit de fin.

L’inférence et les appels réseau restent hors transaction PostgreSQL. Un ticket
ou une source modifiés pendant l’analyse produisent HTTP 409. Les prédictions ML
sont des faits historiques déjà validés et sauvegardés par leur propre étape :
un échec ultérieur du RAG ou LLM les conserve, sans analyse agent complétée.

## Réponse

Les sept champs demandés sont accompagnés de `runId`, `ticketRevision`, `facts`
et `predictions` pour rattacher la proposition à ses entrées. Exemple abrégé de
forme, sans valeur de score mesurée :

```json
{
  "summary": "Investigate the reported SQL connection timeout.",
  "diagnosis": "Hypothesis: the connection path may be blocked.",
  "possibleCauses": ["Hypothesis: firewall configuration may block TCP."],
  "recommendedActions": [
    { "description": "Ask an operator to verify connectivity before any firewall change.", "requiresHumanApproval": true }
  ],
  "sources": [],
  "confidence": 0.0,
  "requiresHumanReview": true
}
```

- `facts` est un snapshot du ticket fourni par le serveur. Son titre et sa
  description restent des symptômes rapportés, pas des diagnostics vérifiés.
- `predictions` est explicitement séparé des faits. Ses sorties ML n’autorisent
  aucun changement de catégorie, priorité ou équipe.
- Résumé, diagnostic, causes et actions sont du contenu proposé par le modèle.
  Le serveur préfixe diagnostic et causes par `Hypothesis:`.
- Une action contient une description et `requiresHumanApproval: true`, fixé
  côté serveur. Le modèle ne peut pas transmettre de commande exécutable ou
  désactiver cette politique.
- Chaque source expose documentId, chunkId, documentVersion, title, source et
  contentHash. Le modèle fournit uniquement des IDs de chunks ; aucune URL ou
  provenance générée ne remplace les métadonnées du RAG.
- `confidence` est un indicateur conservateur, **pas une probabilité calibrée**.
  Il est au plus égal au minimum des trois scores ML et du score déclaré par le
  modèle. Une preuve insuffisante ou aucune citation impose zéro. La revue
  humaine reste obligatoire même avec un score élevé.

La validation contrôle format, provenance et versions. Elle ne démontre pas
que chaque phrase est techniquement vraie ou correctement étayée. Le reviewer
doit confronter les hypothèses aux sources et à l’environnement réel.

## Outils

`ITicketAgentTools` utilise maintenant `McpAgentTools`, client interne du
[service MCP contrôlé](mcp-server.md). Le serveur `/mcp` et l’agent partagent
validation, permissions, scope et audit, sans fournir DbContext au modèle.

| Nom | Arguments | Périmètre |
|---|---|---|
| get_ticket | `{ "ticketId": "UUID" }` | Ticket de la requête courante uniquement |
| search_tickets | `{ "query": "SQL" }` | Recherche limitée au ticket autorisé de cet agent |
| get_ticket_history | `{ "ticketId": "UUID" }` | Au plus 10 commentaires tronqués à 500 caractères, 6 prédictions et 10 événements Ticket connus |
| search_knowledge | `{ "query": "SQL timeout" }` | Question de 500 caractères maximum, 3 chunks par défaut, 5 maximum |
| get_team | `{ "name": "Database" }` | Une des six équipes du vocabulaire ML ; objet team nullable |
| get_system_status | `{}` | État grossier de la connexion PostgreSQL |

Les arguments manquants, inconnus, dupliqués ou de type incorrect sont refusés.
Un outil interdit renvoie une erreur bornée au modèle et un audit `Denied`.
Le modèle peut poursuivre avec les outils permis. Il ne peut pas choisir un
autre ticket, injecter une requête SQL ou demander une mutation.

Les appels sont exécutés séquentiellement : ni accès EF concurrent, ni appel
arbitraire par réflexion, URL externe, shell, assignation ou résolution de ticket.
Les données et résultats d’outils sont présentés au modèle comme données non
fiables ; les limites et permissions restent imposées par le code du serveur.

## Limites et configuration

Le même `HttpLLMService` implémente les ports `ILLMService` et `ILLMAgentService`.
Le premier conserve sa réponse existante ; le second gère des tours d’outils et
une synthèse structurée. Les deux partagent le transport HTTP, les erreurs,
timeouts, retries et la configuration exclusivement par variables `LLM__*`
décrits dans [llm-abstraction.md](llm-abstraction.md). Aucune nouvelle clé n’est
nécessaire pour les tests ; aucune clé réelle n’est incluse dans les fixtures.

Le modèle configuré doit supporter à la fois les function tools et le schéma
JSON final. La compatibilité effective d’un modèle distant n’est pas déduite
des tests avec fake. Contrats de référence :
[Ollama tool calling](https://docs.ollama.com/capabilities/tool-calling) et
[OpenAI function calling](https://developers.openai.com/api/docs/guides/function-calling).

Bornes serveur :

- 180 secondes pour tout le workflow ; cancellation transmise aux dépendances.
- 4 tours de collecte, au plus 8 appels demandés par le modèle, plus les deux
  lectures initiales obligatoires et une synthèse finale sans outils.
- 100000 caractères de transcript ; contexte cumulé au plus 20 chunks et
  24000 caractères de texte/titres/sources ; dépassement refusé.
- Résumé 2000 caractères, diagnostic 3000, 8 causes de 500 caractères,
  10 propositions de 800 caractères et 20 citations au maximum.
- Au plus deux retries par appel HTTP LLM ; les outils ne sont jamais
  réexécutés par un retry du fournisseur. Un Retry-After trop long est refusé.
- Réponse LLM non conforme, tronquée, refusée, avec propriétés inconnues ou
  citations inventées : HTTP 503, sans proposition persistée ni appliquée.

Ces limites en caractères ne garantissent pas un nombre précis de tokens.
La fenêtre de contexte du modèle doit être adaptée ; un rejet du fournisseur
reste une erreur explicite, jamais une réponse présentée comme réussie.

## Audit et persistance

`IAgentRunStore` écrit chaque événement dans un contexte EF court indépendant.
Tous les événements partagent le RunId via CorrelationId. Les entrées incluent
début/fin d’étape ML et LLM, outil commencé/réussi/refusé/échoué, et fin de run.
Les détails contiennent ordre, noms d’outils autorisés, hashes des arguments et
sorties, fournisseur/modèle et nombre de tentatives. Le texte du ticket,
commentaires, contexte, clés et réponses brutes ne sont pas inscrits dans les
logs techniques ou détails d’événements.

La migration `20261026193135_TicketAgentAnalysis` ajoute
`app.ticket_agent_analyses` : RunId, TicketId, révision, résultat JSON complet,
hash SHA-256, fournisseur/modèle et timestamps. L’index TicketId/CreatedAt sert
la consultation historique future ; la FK interdit un effacement du ticket
référencé. La proposition et `Agent.Run.Completed` sont insérés atomiquement.
Le hash est calculé sur la sérialisation UTF-8 typée, avant normalisation jsonb.
Le résultat complet est conservé en base pour audit ; ses données doivent
bénéficier des mêmes contrôles d’accès que le ticket dans le futur module auth.

Un trigger PostgreSQL et AppDbContext interdisent la modification/suppression
des analyses. Aucun changement du Domain Layer n’est nécessaire pour cet
archivage technique. CreatedAt et UpdatedAt sont identiques pour ces faits.

Les acteurs d’audit sont actuellement système (`UserId = null`), conformément
au fonctionnement des analyses existantes sans identité authentifiée. Les
outils ne font aucune approbation au nom d’un utilisateur.

Une annulation utilise un token distinct, borné à 5 secondes, pour écrire sa
trace terminale. Une panne d’audit bloque le workflow ; une panne PostgreSQL
peut aussi empêcher d’écrire la trace de cette panne. Les événements déjà
validés restent conservés. Les analyses commencées sans événement terminal
permettent alors de repérer un run interrompu.

## Validation

Les tests couvrent orchestration, catalogue partagé de six outils, provenance,
séparation faits/hypothèses, approbation obligatoire, confiance bornée, outils
interdits, arguments invalides, limites, annulation, pannes et versions obsolètes.
Les tests API exécutent les modèles ML réels et PostgreSQL avec RAG/LLM fake.
Les tests de protocole exécutent le transport HTTP réel contre des fournisseurs
locaux fake pour les deux protocoles, incluant retries, refus, troncature,
réponse malformée et timeout. Ils peuvent utiliser la simulation Postman :

```powershell
postman simulation run postman/simulations/llm-provider/llm-provider.sim.yaml --port 4904 --no-history --output ndjson
# Autre terminal
$env:LLM_TEST_BASE_URL='http://127.0.0.1:4904/llm/'
dotnet test backend/tests/ITTicketIntelligence.IntegrationTests --configuration Release --no-build --filter FullyQualifiedName~AgentProtocolTests
```

Lancer `dotnet test backend/ITTicketIntelligence.sln --configuration Release`
après import de l’environnement PostgreSQL pour toute la régression. La
collection Postman conserve un exemple `Agent` pour un ticket actif, une KB
indexée et un modèle configuré. Les requêtes Smoke vérifient les erreurs sans
déclencher d’inférence LLM. Les fixtures démontrent le contrat et les protections,
pas la qualité des résolutions d’un modèle réel.

Validation du 2026-10-26 : compilation Release sans erreur ni avertissement ;
514 tests xUnit réussis (257 unitaires et 257 d’intégration), 24 cas HTTP
rejoués contre la simulation Postman et 38 assertions Smoke réussies sur
14 requêtes de l’API locale. La collection de 51 requêtes passe le lint.
