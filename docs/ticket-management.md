# Ticket Management — contrat v1

Contrat défini avant implémentation. L’OpenAPI exposé en Development/Testing à `/openapi/v1.json` décrit les DTO exposés ; son export local est conservé dans `postman/specs/openapi.json`. Le code API génère ce contrat : ne pas modifier l’export pour changer une route.

| Méthode et route | Entrée | Réponse |
|---|---|---|
| POST /api/tickets | title, description, createdById | 201, ticket et Location |
| GET /api/tickets | page=1, pageSize=20, search, status, priority, category, assignedTeamId, createdById, includeArchived=false | 200, page de résumés |
| GET /api/tickets/{id} | UUID | 200, détail avec commentaires et prédictions existantes |
| PUT /api/tickets/{id} | title, description, category?, priority?, revision | 200, ticket actualisé |
| PATCH /api/tickets/{id}/status | status, resolution?, revision | 200, ticket actualisé |
| PATCH /api/tickets/{id}/assignment | teamId? (null pour désaffecter), revision | 200, ticket actualisé |
| POST /api/tickets/{id}/comments | authorId, message, revision | 201, commentaire et nouvelle révision du ticket |
| DELETE /api/tickets/{id}?revision=... | révision obligatoire | 204, archivage sans suppression physique |
| POST /api/tickets/{id}/restore | revision | 200, ticket restauré |

- Tous les champs de PUT sont des valeurs de remplacement ; category/priority doivent être simultanément absentes ou présentes. Le couple null/null efface la classification.
- Les enums JSON sont des noms, jamais des nombres. Les champs JSON inconnus sont rejetés.
- Pagination : page de 1 à 1 000 000, pageSize de 1 à 100, ordre createdAt puis id décroissants. Recherche littérale sur titre, description et référence, maximum 200 caractères.
- Révision attendue positive sur toutes les modifications : 409 si dépassée. La sauvegarde contrôle également xmin pour les écritures concurrentes.
- Cycle métier conservé : Open → InProgress → Resolved → Closed ; Resolved → InProgress réouvre. Résolution obligatoire seulement pour la cible Resolved. Une transition interdite répond 409.
- Créateur et auteur de commentaire : utilisateur existant et actif. Équipe affectée : existante et active. Référence inexistante : 404 ; inactive : 409.
- Les tickets archivés sont consultables par ID et via includeArchived ; les autres mutations sont refusées jusqu’à restauration. Archiver/restaurer l’état déjà demandé n’ajoute pas de nouvel audit si la révision est courante.
- Chaque changement effectif est enregistré avec son audit dans la même transaction SaveChanges. Création et commentaire portent l’utilisateur référencé ; les autres actions sont enregistrées sans acteur authentifié à cette étape.
- L’authentification est hors de ce module : createdById et authorId sont des références explicites, pas une preuve d’identité. Aucun contrôle d’accès utilisateur n’est revendiqué.
- Les références IT-numéro proviennent d’une séquence PostgreSQL ; les annulations peuvent laisser des trous.

Erreurs : 400 validation/JSON, 404 ressource absente, 409 règle métier/concurrence, 503 base indisponible, 500 erreur inattendue. Format application/problem+json, traceId et instance, sans détail SQL ni stack trace. Les annulations sont propagées jusqu’à EF Core ; un client déconnecté n’entraîne pas une tentative d’écriture d’erreur.

## Organisation

- `Application/Tickets/Commands` : commandes et orchestrations des mutations ; aucune dépendance EF/HTTP.
- `Application/Tickets/Queries` : validation des lectures et orchestration des requêtes.
- `Application/Tickets/DTOs` et `Validation` : contrats applicatifs, projections et règles de validation.
- `Application/Abstractions/Persistence` : ports pour agrégats, références utilisateurs/équipes et audits.
- `Infrastructure/Persistence` : requêtes EF asynchrones, séquence PostgreSQL, sauvegarde atomique et migration.
- `Api/Contracts`, `Controllers` et `Errors` : binding HTTP, adaptation vers les handlers et ProblemDetails.

## Exemples

Création (`createdById` doit désigner un utilisateur actif existant) :

```json
{
  "title": "Cannot connect to database",
  "description": "The application reports a connection timeout.",
  "createdById": "11111111-1111-1111-1111-111111111111"
}
```

La réponse 201 fournit `id`, `reference` et `revision: 1`. Passage en investigation :

```json
{ "status": "InProgress", "revision": 1 }
```

Utiliser ensuite la nouvelle révision retournée pour chaque modification. Pour résoudre : `status: "Resolved"` avec une `resolution` non blanche ; pour clôturer : `status: "Closed"` après résolution. L’archivage conserve le statut courant et les commentaires.

## Vérification reproductible

```powershell
. ./scripts/Import-LocalEnvironment.ps1
dotnet build backend/ITTicketIntelligence.sln --configuration Release
dotnet test backend/ITTicketIntelligence.sln --configuration Release --no-build
```

Les tests unitaires couvrent les handlers, validations, annulations, révisions et invariants d’archivage. Les tests HTTP xUnit utilisent WebApplicationFactory et une base PostgreSQL temporaire : parcours complet des neuf opérations, filtres/pagination, entrées invalides, références inactives, concurrence, erreurs globales et rollback du ticket si son audit échoue. Les fixtures créent leurs propres utilisateurs/équipes ; aucune donnée de test n’est ajoutée à la base applicative.

Pour les contrôles Postman, démarrer l’API en Development puis exécuter :

```powershell
postman collection lint "postman/collections/IT Ticket Intelligence"
postman collection run "postman/collections/IT Ticket Intelligence" -i Smoke --no-report-events
```

Le dossier Smoke exécute désormais douze requêtes et trente-quatre assertions : contrôles Ticket Management, classifieur Category, prédicteur Priority, recommandation Team, erreurs du pipeline d'analyse et contrats Knowledge Base/RAG. Aucun compte Postman n’est requis pour cette exécution locale ; la CLI peut afficher un message invitant à se connecter tout en terminant correctement.

La collection contient également les neuf requêtes métier, issues du contrat puis paramétrées. Pour exécuter leur parcours, fournir les IDs d’un utilisateur et d’une équipe actifs déjà présents dans la base cible :

```powershell
postman collection run "postman/collections/IT Ticket Intelligence" -i Tickets --env-var "userId=<UUID utilisateur>" --env-var "teamId=<UUID équipe>" --no-report-events
```

Ce parcours crée un ticket puis l’actualise, le commente, l’archive et le restaure ; il laisse donc un ticket et ses audits dans la base choisie. Les scripts transmettent `ticketId` et `revision` entre les requêtes. Le parcours HTTP complet est vérifié automatiquement en base isolée par xUnit. Les contrôles Smoke et le parcours Intelligence ont été exécutés avec Postman sur la base applicative locale ; le parcours Tickets reste vérifié par xUnit.

Pour rafraîchir l’export, API démarrée :

```powershell
Invoke-WebRequest http://127.0.0.1:5080/openapi/v1.json -OutFile postman/specs/openapi.json
```

La génération initiale des requêtes utilise `postman spec generate collection postman/specs/openapi.json -n "IT Ticket Intelligence"`. Ne pas écraser la collection maintenue sans conserver ses variables et assertions.
