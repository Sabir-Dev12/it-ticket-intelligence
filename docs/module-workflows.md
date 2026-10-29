# Workflows Ticket / Assistant / Knowledge Base

## Comportements intégrés

1. `POST /api/tickets` conserve son contrat `201 TicketDto`. L'orchestrateur Application `CreateAnalyzedTicketHandler` enregistre le ticket, appelle le pipeline ML.NET existant, puis relit les prédictions sauvegardées. React ouvre les détails et les affiche. Une panne ML ne transforme pas un ticket déjà enregistré en échec de création et ne produit aucune prédiction factice : le ticket reste consultable avec sa liste réelle de prédictions. L'analyse IA reste une action explicite via le bouton existant ; elle utilise le pipeline Agent/RAG/Ollama existant et ses sources contrôlées. « Discuss with assistant » ouvre la conversation avec `ticketId`.

2. L'article Knowledge propose « Ask AI » vers `/ai-assistant?documentId={id}`. Le seul nouveau contrat est `POST /api/assistant/knowledge/{documentId}/answers`, corps `{ "question": "..." }`, réponse `LLMResponse` existante (`answer`, `citations`, `provider`, `model`, `attempts`, `needsHumanReview`). Aucun contenu documentaire ni paramètre de permission n'est fourni par React. `KnowledgeArticleAssistant` récupère les chunks autorisés de cet article avec le RAG, appelle `ILLMService`, valide les citations et relit les chunks actuels pour vérifier version/hash/droits. Une source inventée est rejetée ; un changement pendant l'inférence produit un conflit. Sans contexte indexé autorisé, 404 sans révéler l'existence d'un article privé. Les échanges du mode article restent dans la vue ouverte ; les conversations d'investigation de tickets conservent leur historique serveur existant.

3. La conversation ticket utilise les endpoints existants `/api/assistant/conversations` et `/{id}/messages`. Le backend résout une référence `IT-...` ou un `ticketId` dans le périmètre du compte, récupère historique et prédictions, recherche le contexte RAG avant l'appel Ollama et persiste la réponse structurée avec activité des outils. React affiche sources, recommandations et historique enregistré. Aucune action proposée n'est affichée comme exécutée.

## Permissions et accès aux sources

Les rôles précédents sont conservés : User crée/consulte ses tickets et voit les prédictions automatiques ; Agent/Support/Admin peuvent lancer l'IA selon leur périmètre. L'intégration ne donne pas à User le droit d'utiliser l'IA.

Agent et Support peuvent maintenant consulter la Knowledge Base en lecture pour ouvrir les citations. Les listes filtrent les droits avant pagination ; le détail revérifie les droits ; la création/modification/suppression et les permissions documentaires restent Admin. Un document restreint sans créateur n'est pas accessible à un appelant sans identifiant utilisateur. Les droits RAG SQL et la relecture des chunks utilisent la même règle public/restreint-propriétaire/Admin.

## Contrats et validation

Le contrat OpenAPI a été récupéré depuis l'API ASP.NET réelle dans `postman/specs/openapi.json`. Vérifications : présence de l'opération article, champs exacts de `LLMResponse`, champ `TicketDto.predictions`. Le client React possède le type `ArticleAnswer` correspondant. La collection Postman contient une requête article avec JWT et contrôle des citations ; ses 76 requêtes passent le lint. Les fixtures de simulation sont uniquement des données de test, jamais un fallback applicatif.

Vérifications exécutées : builds .NET et React ; 324 tests unitaires backend (dont orchestration et rejet des sources inventées/révoquées), 26 tests frontend ; quatre tests Playwright passant sur React/Vite relié à la simulation Postman : les trois workflows et une erreur fournisseur sans réponse ni sources fabriquées. Le mode article passe axe dans les deux thèmes et ne déborde pas à 280/390/1280 pixels. Captures dans `artifacts/frontend/article-workflow-*.png`.

Le gate global du kit de design donne **0/52** : ses scripts/harness de référence ne sont pas configurés dans ce dépôt. Son journal complet est `artifacts/frontend/module-design-gate.txt`. Les contrôles ciblés ci-dessus ne sont pas une certification de ce gate global.

**Limite bloquante : PostgreSQL sur 55432 demeure indisponible (Docker Desktop / socket dockerInference). Les trois workflows ne sont donc pas validés de bout en bout sur la pile réelle PostgreSQL + pgvector + Ollama.** Les succès Playwright établissent la navigation et les contrats UI sur simulateur, pas la persistance ou le raisonnement réels. Aucun succès API simulé n'est présenté comme un appel Ollama réel. Une base pgvector migrée, le Worker d'indexation et les modèles locaux doivent être disponibles avant cette validation réelle.

```powershell
dotnet test backend/tests/ITTicketIntelligence.UnitTests --configuration Release
npm test --prefix frontend
# Simulation Postman sur 4918, frontend avec API_PROXY_TARGET=http://127.0.0.1:4918/helpdesk sur 5183 :
$env:FRONTEND_URL='http://127.0.0.1:5183'
cd frontend
npx playwright test e2e/module-workflows.spec.ts
```
