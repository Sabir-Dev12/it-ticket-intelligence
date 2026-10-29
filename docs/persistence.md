# Persistance EF Core et PostgreSQL

## Frontières

- Domain ne référence toujours aucun package ni autre projet.
- Application définit les ports de persistance Ticket, Conversation, Knowledge, Intelligence et Agent, ainsi que `IUnitOfWork`, sans type EF Core.
- Infrastructure contient `AppDbContext`, les conversions de value objects, treize configurations séparées, les repositories et les migrations.
- API et Worker composent les dépendances avec `AddInfrastructure(configuration)`.

Les repositories apportent un chargement cohérent des collections de leur agrégat. Celui des conversations impose également l’ordre persistant des messages, même à dates identiques ; celui de Knowledge charge les chunks avant leur remplacement. Aucun repository CRUD générique n’est introduit. La sauvegarde utilise la même instance scoped d’AppDbContext, via IUnitOfWork.

## Modèle relationnel

Schéma PostgreSQL `app` :

| Table | Clé | Relations |
|---|---|---|
| users | id UUID | Email normalisé unique |
| teams | id UUID | Nom normalisé unique |
| team_members | team_id + user_id | teams, users |
| tickets | id UUID | created_by_id → users ; assigned_team_id nullable → teams |
| ticket_comments | id UUID | ticket_id → tickets ; author_id → users |
| ticket_predictions | id UUID | ticket_id → tickets ; recommended_team_id nullable → teams |
| ticket_agent_analyses | id UUID = RunId | ticket_id → tickets, RESTRICT ; proposition JSON et hash conservés |
| knowledge_documents | id UUID | created_by_id nullable → users |
| knowledge_chunks | id UUID | document_id → knowledge_documents ; cascade lors du remplacement ou de la suppression |
| rag_vectors | chunk_id + profile | chunk_id → knowledge_chunks, cascade ; dimensions et tableau real[] |
| ai_conversations | id UUID | user_id → users ; ticket_id nullable → tickets |
| ai_messages | id UUID | conversation_id → ai_conversations |
| audit_logs | id UUID | user_id nullable → users |

`AuditLog.EntityType` et `EntityId` restent une référence polymorphe logique : aucune fausse clé étrangère vers une table arbitraire n’est créée.

Les identifiants ne sont pas générés par EF ; ils sont fournis par le domaine. Les enums sont stockés en texte avec des contraintes de valeurs autorisées. EmailAddress et PredictionConfidence utilisent des conversions scalaires, sans table supplémentaire.

### Dates et historique

Les treize tables possèdent `created_at` et `updated_at`, de type `timestamp with time zone`.

- Les dates des agrégats mutables restent celles définies par leurs méthodes métier : Infrastructure ne remplace pas une date explicite par l’heure du serveur.
- Les faits immuables possèdent un UpdatedAt shadow EF égal à leur CreatedAt. Aucun setter technique n’est ajouté au domaine.
- Pour AuditLog, les deux dates shadow sont initialisées à OccurredAt.
- Les appartenances aux équipes sont des lignes ajoutées ou supprimées ; elles ne sont pas réécrites.
- Les colonnes shadow et leurs contraintes sont configurées uniquement dans Infrastructure.

Les changements de commentaires, prédictions, messages, audits et analyses d’agent sont refusés par AppDbContext. Les migrations ajoutent aussi des triggers PostgreSQL interdisant UPDATE, DELETE et TRUNCATE sur ces cinq tables historiques. Les analyses techniques d’agent portent leurs timestamps dans le modèle Infrastructure, avec UpdatedAt égal à CreatedAt.

Pas de soft delete général, ni de filtre global masquant l’historique. Les tickets disposent d’un archivage métier réversible (`is_archived`, `archived_at`) : leur statut et leurs historiques sont conservés ; la liste les exclut par défaut mais la consultation par ID reste possible. `User.IsActive` et `Team.IsActive` conservent leur fonction de désactivation. Les clés étrangères historiques utilisent RESTRICT. La suppression en cascade est limitée aux appartenances d’une équipe, qui ne sont pas des faits d’audit.

### Contraintes et concurrence

- UUID non vides, textes obligatoires non blancs et tailles maximales.
- Valeurs d’enums, révision/version positives, score entre 0 et 1 et résultat de prédiction cohérent avec son type.
- Cohérence du cycle de vie du ticket et de ses dates de résolution/clôture.
- Catégorie et priorité du ticket toutes deux absentes ou présentes.
- Métadonnées d’outil requises uniquement pour les messages Tool.
- Email normalisé, référence ticket unique et source documentaire unique.
- Position de message positive et unique au sein d’une conversation.
- Concurrence optimiste via xmin pour les agrégats mutables ; Revision du ticket et Version du document sont également des tokens de concurrence.

Les index couvrent les recherches par référence/email/source, les listes de tickets par créateur, statut/priorité ou équipe, les commentaires/prédictions d’un ticket, les conversations d’un propriétaire, l’ordre des messages et les cibles/auteurs/corrélations d’audit. Le RAG ajoute un index profil/dimensions ; la similarité est calculée exactement sur des tableaux real[], sans extension pgvector ni index ANN.

## Configuration et secrets

Les appsettings de l’API et du Worker contiennent seulement :

- hôte localhost ; port 55432 ; base it_ticket_intelligence ; utilisateur ticket_app ; délais de connexion/commande ;
- aucun mot de passe.

Les fournisseurs de configuration .NET permettent de surcharger :

- `Database__Password` : mot de passe local ;
- `ConnectionStrings__Postgres` : chaîne complète alternative, fournie par l’environnement.

La chaîne complète prend priorité ; son mot de passe est complété par Database__Password s’il manque. Les erreurs et logs n’activent pas l’affichage des détails sensibles.

`Initialize-Postgres.ps1` génère un secret aléatoire dans `.env` s’il n’existe pas, puis démarre PostgreSQL. Le fichier est ignoré par `.gitignore`. Il n’est jamais affiché par les scripts. `.env.example` contient uniquement un champ vide. La lecture .env est explicite via `Import-LocalEnvironment.ps1`, elle ne remplace pas les fournisseurs de configuration .NET.

L’image Docker est fixée par son digest, le volume est persistant et le port est publié uniquement sur 127.0.0.1. Changer un mot de passe dans .env ne modifie pas celui d’un volume PostgreSQL déjà initialisé.

## Installation et migration

Depuis la racine, dans PowerShell :

```powershell
./scripts/Initialize-Postgres.ps1
. ./scripts/Import-LocalEnvironment.ps1
dotnet tool restore
dotnet restore backend/ITTicketIntelligence.sln
dotnet ef database update --project backend/src/ITTicketIntelligence.Infrastructure --startup-project backend/src/ITTicketIntelligence.Api --configuration Release
```

Les migrations `20261023205759_InitialPersistence` et `20261023230302_TicketManagement`, ainsi que le snapshot, sont versionnés dans Infrastructure. La seconde ajoute l’archivage, son index et sa contrainte, ainsi qu’une séquence pour les références IT-numéro. Elle initialise la séquence au-delà des références numériques existantes, avec un départ minimum à 1000. Aucune migration implicite n’est exécutée au démarrage de l’API ou du Worker. La factory de design time réutilise le fournisseur et la configuration PostgreSQL, sans exécuter l’hôte API.

La troisième migration, `20261025194645_TicketIntelligencePipeline`, conserve les lots d'analyse, labels d'équipe et décisions de revue dans `ticket_predictions`, avec index unique par analyse/type et contraintes de cohérence. `ITicketIntelligenceStore` réalise une sauvegarde transactionnelle après vérification de la révision sous verrou de ligne. Le détail figure dans la [documentation du pipeline](ticket-intelligence-pipeline.md). Les colonnes historiques et leurs FK sont conservées.

La quatrième migration, `20261025221213_KnowledgeBase`, ajoute les types/formats documentaires, l'état de préparation, la table `knowledge_chunks`, son index unique par document/position et sa FK avec cascade. Les documents existants restent non préparés jusqu'à leur prochaine mise à jour explicite. Le [module Knowledge Base](knowledge-base.md) détaille les contraintes et la sauvegarde atomique des remplacements.

La cinquième migration, `20261025223913_RagVectors`, ajoute les vecteurs techniques en Infrastructure, leur clé chunk/profil, la FK avec cascade, les contrôles des tableaux et l'index profil/dimensions. Le [pipeline RAG](rag-pipeline.md) décrit la recherche cosinus exacte et le remplacement transactionnel après vérification de la version documentaire.

La sixième migration, `20261026193135_TicketAgentAnalysis`, ajoute les propositions immuables de l’agent, leur index ticket/date, leur FK restrictive et leur trigger d’historique. Le [workflow de l’agent](ai-ticket-agent.md) décrit les événements durables dans des contextes courts et la sauvegarde finale sous vérification des versions du ticket et des sources.

## Vérifier et tester

```powershell
. ./scripts/Import-LocalEnvironment.ps1
dotnet build backend/ITTicketIntelligence.sln --configuration Release
dotnet test backend/ITTicketIntelligence.sln --configuration Release --no-build
docker compose exec -T postgres psql -U ticket_app -d it_ticket_intelligence -c '\dt app.*'
```

Les tests de persistance exigent le serveur PostgreSQL réel ; ils ne sont pas ignorés si la configuration manque. Ils créent une base au nom aléatoire, y appliquent la migration, vérifient le schéma et les invariants, puis suppriment uniquement cette base de test. La base applicative n’est pas utilisée comme jeu de test. Le compte de développement Docker permet cette création de bases temporaires.

Les tests couvrent le graphe des neuf entités, les appartenances, les conversions, les dates shadow, les repositories/DI, l’ordre des messages, l’unicité, les FKs, les CHECK, les écritures concurrentes et les historiques append-only. Le modèle courant est comparé au snapshot pour détecter une migration oubliée.
