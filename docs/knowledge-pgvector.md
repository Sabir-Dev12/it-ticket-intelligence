# Knowledge Base : embeddings et pgvector

L'implémentation réutilise `IEmbeddingService`, `OllamaEmbeddingService`, `KnowledgeDocumentProcessor`, `RagIndexer`, `RagRetriever` et l'entité `KnowledgeChunk` existants. `KnowledgeChunk` est l'équivalent de `DocumentChunk` : document/version, position, contenu, offsets, hash et timestamps ; aucune seconde entité ni table de chunks n'est créée. `KnowledgeIndexingService` orchestre les jobs persistants et `SemanticSearchService` expose la recherche en réutilisant le retriever.

## Pipeline et dimension

La Knowledge API extrait, nettoie et prépare les chunks. `POST /api/rag/documents/{id}/index` retourne désormais **202** avec `{id,documentId,profile,state,attempts}` et Location vers `GET /api/rag/index-jobs/{id}`. L'API ne calcule aucun embedding pendant cette requête. Lancer le Worker avec le même environnement que l'API. Il réclame les jobs avec `FOR UPDATE SKIP LOCKED`, une réservation de 10 minutes et au plus 3 tentatives. Une réservation expirée peut être reprise ; le remplacement de vecteurs reste idempotent. Les jobs au profil obsolète deviennent `RequiresReindex`. Les erreurs exposent un état sans détails sensibles, avec logs par identifiant.

L'adaptateur Ollama vérifie chaque réponse : modèle, digest installé, nombre de vecteurs, dimension réelle, valeurs finies et norme non nulle. Le profil commun à l'indexation et à la recherche inclut modèle, digest, dimension et empreinte des préfixes. `nomic-embed-text:latest` reste configuré en 768 dimensions. Un changement de configuration ne mélange jamais les espaces vectoriels : les anciennes lignes restent conservées mais ne sont plus recherchées. Pour réindexer, déployer la même configuration sur API/Worker puis réenfiler chaque document via l'API paginée Knowledge. Le remplacement par document/profil est transactionnel et refuse un document modifié durant l'inférence.

## Stockage natif pgvector

La migration `PgVectorKnowledgeJobs` crée l'extension `vector`, la colonne `embedding_vector` générée depuis les tableaux `real[]` historiques et la file de jobs. Les écritures existantes restent compatibles ; la recherche utilise l'opérateur cosinus `<=>` pgvector. La colonne vectorielle sans dimension fixe permet de conserver plusieurs profils de dimensions différentes. La recherche est **exacte**, sans prétendre utiliser un index HNSW : un index approximatif nécessiterait une expression et un filtre par dimension/profil. Les métadonnées restent jointes aux documents/chunks courants ; les vecteurs obsolètes ne sont pas renvoyés.

## Droits documentaires

`IsRestricted=false` conserve la visibilité des documents historiques pour les identités autorisées à rechercher dans la KB. `IsRestricted=true` réserve le document à Admin et à son `CreatedById`, si défini. `PATCH /api/knowledge/{id}/permissions` avec `{ "isRestricted": true }` est réservé à Admin et audité. Un document restreint sans créateur est accessible uniquement à Admin. Aucun droit n'est envoyé par le frontend dans une recherche.

Le contexte d'accès est dérivé du JWT ou des permissions MCP authentifiées. Sans contexte autorisé, la recherche renvoie zéro résultat. Le filtrage SQL des droits précède classement et limite ; les droits sont aussi revérifiés lors de la relecture des chunks avant constitution du contexte RAG. Ce jalon fournit public/restreint-propriétaire, sans inventer de partage par équipe ni ACL individuelles supplémentaires.

## Démarrage et données existantes

L'image PostgreSQL alpine actuelle ne fournit pas pgvector. `compose.pgvector.yaml` propose PostgreSQL 17 avec pgvector 0.8.7 et un **nouveau volume séparé**. Il ne migre pas les données historiques automatiquement. Sauvegarder/restaurer l'ancien serveur vers le nouveau avant de basculer une installation existante ; ne pas supprimer le volume historique.

Pour une nouvelle base locale seulement :

```powershell
. ./scripts/Import-LocalEnvironment.ps1
docker compose -f compose.yaml -f compose.pgvector.yaml up -d
dotnet ef database update --project backend/src/ITTicketIntelligence.Infrastructure --startup-project backend/src/ITTicketIntelligence.Api --configuration Release
dotnet run --project backend/src/ITTicketIntelligence.Worker --configuration Release
```

L'installation de l'extension requiert les droits PostgreSQL adéquats et son binaire côté serveur. La migration n'a pas été appliquée à la base historique. Voir [pgvector officiel](https://github.com/pgvector/pgvector).

## Validation

318 tests unitaires et 12 tests de contrat Ollama réussis. Trois essais de pertinence avec le modèle nomic réel classent le bon document en premier pour SQL timeout, VPN et facture PDF (cosinus respectifs 0,8410 / 0,7977 / 0,7628). Ces trois exemples ne constituent pas une mesure de qualité représentative d'un corpus réel.

`PgVectorIndexingTests` ajoute un test d'intégration déterministe : file persistante sans inférence dans l'enqueue, traitement, classement SQL/VPN, filtrage avant top-k, refus sans identité et changement de profil/réindexation. **Son exécution est bloquée** par PostgreSQL indisponible sur 55432 : Docker Desktop conserve son erreur de socket `dockerInference`. Les commandes SQL/migration pgvector restent donc non validées sur un serveur dans cette session. Aucune réussite de ces tests n'est revendiquée.

```powershell
dotnet test backend/tests/ITTicketIntelligence.IntegrationTests --configuration Release --filter FullyQualifiedName~PgVectorIndexingTests
dotnet run --project tools/embedding-relevance --configuration Release
```

La collection Postman RAG accepte maintenant 202 et attend les trois jobs avant la recherche. Fournir un JWT Admin `accessToken`, lancer le Worker et utiliser une base pgvector. Les tests HTTP historiques non authentifiés restent à adapter, indépendamment de cette étape.
