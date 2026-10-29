# RAG — indexation et récupération de contexte

Le module retourne des chunks pertinents et leur provenance. Il ne produit aucune
réponse générée. Le Domain Layer n'a pas été modifié : les ports RAG appartiennent
à Application, les adaptateurs et l'état vectoriel technique à Infrastructure.

## API

1. Créer ou mettre à jour un document avec `/api/knowledge` : le pipeline Knowledge
   effectue extraction, nettoyage et chunking, puis sauvegarde les métadonnées.
2. `POST /api/rag/documents/{id}/index`, sans body : calculer les embeddings et
   remplacer atomiquement l'index de la version courante pour le profil actif.
3. `POST /api/rag/retrieve` : rechercher et retourner le contexte courant.

```json
{
  "question": "SQL Server connection timeout",
  "limit": 5,
  "minimumScore": 0.35
}
```

Filtres optionnels : `documentId`, `category`, `kind`. La question est obligatoire,
limitée à 2000 caractères ; limit vaut 1..20 ; minimumScore est fini et dans [-1,1].
Les champs inconnus sont refusés, y compris un hypothétique `generateAnswer`.

La réponse contient `embeddingProfile` et `results`. Chaque résultat porte :

```text
documentId
title
chunkId
chunk
relevanceScore
metadata:
  documentId, documentVersion, title, source, category, kind, format,
  position, startOffset, endOffset, contentHash
```

Le score est la similarité cosinus, dans [-1,1], classée par ordre décroissant.
Ce n'est pas une probabilité de bonne réponse. Les égalités sont départagées par
ChunkId. Aucun résultat est représenté par `results: []`, avec HTTP 200.

Indexation : HTTP 200 avec DocumentId, DocumentVersion, ChunkCount, EmbeddingProfile,
Dimensions et IndexedAt. Réindexer la même version remplace les vecteurs de ce profil
sans les dupliquer. Un document absent donne 404 ; un document non préparé ou modifié
pendant le calcul donne 409. Les anciens documents doivent être préparés par PUT
Knowledge avant indexation. L'indexation reste explicite après chaque modification.

## Interfaces et orchestration

Dans `Application/Knowledge/Retrieval` :

- `IEmbeddingService` : profil immuable et génération ordonnée d'embeddings,
  avec une intention Document ou Query.
- `IVectorStore` : remplacement complet par document/profil, recherche et retrait
  des vecteurs d'un document.
- `IRagRetriever` : question → embedding → candidats → contexte courant validé.
- `IRagIndexer` : chunks préparés → embeddings → remplacement de l'index.
- `IKnowledgeChunkReader` : hydratation depuis la source documentaire courante.

Ces ports consolident les anciens contrats préparatoires `IEmbeddingGenerator` et
`IKnowledgeVectorStore`. Pour remplacer un fournisseur, implémenter le port concerné
et modifier son enregistrement DI dans Infrastructure. Les controllers, entités du
domaine et formats de résultats ne nécessitent aucune dépendance au fournisseur.
Un nouvel espace d'embeddings demande une réindexation.

L'indexeur traite des lots de 16 chunks maximum. Le texte documentaire envoyé est
le titre suivi du contenu du chunk. Tous les lots doivent réussir avant la sauvegarde.
Un échec de génération ne remplace donc pas un index déjà valide.

Le retriever demande jusqu'à quatre fois la limite de candidats, puis hydrate les
chunks depuis PostgreSQL et vérifie DocumentId, version et hash. Le titre et le texte
retournés viennent de cette source courante. Les candidats supprimés, obsolètes,
dupliqués ou hors filtres sont écartés avant d'appliquer la limite finale.

## Fournisseur d'embeddings local

L'adaptateur réel utilise Ollama et `nomic-embed-text:latest`, déjà disponible sur
le poste lors de l'implémentation. Il appelle exclusivement :

- GET `/api/tags` pour vérifier l'identité du modèle local.
- POST `/api/embed` avec les entrées ordonnées et `truncate: false`.

Les préfixes sont `search_document: ` et `search_query: `, conformément à la
[fiche du modèle Nomic](https://huggingface.co/nomic-ai/nomic-embed-text-v1.5).
Le contrat HTTP est celui de l'[API Ollama embed](https://github.com/ollama/ollama/blob/main/docs/api.md#generate-embeddings).
Aucun endpoint chat/generate n'est utilisé.

Configuration dans appsettings ou avec les variables suivantes :

```text
Rag__Ollama__BaseUrl=http://127.0.0.1:11434/
Rag__Ollama__Model=nomic-embed-text:latest
Rag__Ollama__ModelDigest=0a109f422b47e3a30ba2b10eca18548e944e8a23073ee3f3e947efcf3c45e59f
Rag__Ollama__Dimensions=768
Rag__Ollama__TimeoutSeconds=30
Rag__Ollama__DocumentPrefix=search_document: 
Rag__Ollama__QueryPrefix=search_query: 
```

Le digest correspond au modèle local vérifié, pas à une promesse qu'un tag mutable
restera identique. Sur une autre installation, lire `/api/tags` et configurer son
digest exact. Le profil identifie modèle, digest, dimensions, stratégie titre/chunk
et hash des préfixes. Les profils incompatibles ne sont jamais recherchés ensemble.
Un digest inattendu ou un modèle absent produit 503, sans téléchargement automatique.

Les réponses sont validées : modèle, nombre de vecteurs, dimensions, valeurs finies
et norme non nulle. Les vecteurs sont normalisés. Le timeout configurable (1..60 s)
couvre l'appel de registre et l'embedding d'un lot. Un premier chargement GPU a
dépassé 60 s sur ce poste ; l'appel à chaud a ensuite réussi. Réessayer une indexation
expirée après le chargement du modèle est sûr : aucun lot partiel n'est conservé.
Le démarrage et le CRUD Knowledge ne font aucun appel Ollama.

## Vector store PostgreSQL

`PostgresVectorStore` persiste des `real[]` dans `app.rag_vectors`. Il effectue une
recherche **exacte** par cosinus en SQL, avec jointures sur les documents/chunks
courants et filtres avant le top-k. Ce backend ne nécessite pas pgvector et ne
dispose pas d'index ANN ; son coût est linéaire dans le nombre de vecteurs candidats
et leurs dimensions. Il convient au corpus local de démonstration.

La migration `20261025223913_RagVectors` ajoute :

- Clé composée (chunk_id, profile), dimensions, embedding et dates UTC.
- FK vers knowledge_chunks avec cascade, index par profil/dimensions.
- CHECK constraints sur dimensions, forme des tableaux et valeurs non finies/nulles.

L'indexation calcule hors transaction. Au remplacement, un verrou court sur le
document vérifie sa version et l'ensemble des chunks/hashes avant de remplacer
les vecteurs dans une transaction. Les modifications et suppressions Knowledge
suppriment les anciens vecteurs par cascade. La réhydratation finale fournit un
second contrôle si une modification intervient entre recherche et lecture du texte.
Il s'agit d'une vue courante au moment de cette lecture, pas d'un verrou maintenu
pendant toute la vie de la réponse HTTP.

Un futur vector store externe doit implémenter la synchronisation des mises à jour,
les suppressions et le rejet des versions obsolètes. La validation finale reste
nécessaire ; elle peut retourner moins de résultats si des candidats sont périmés.

## Logs et erreurs

Les logs structurés contiennent profil, DocumentId/version, compteurs et durées.
Ils n'enregistrent ni question, ni contenu de chunk, ni tableaux de vecteurs.
Les erreurs du fournisseur sont enregistrées et remontent en ProblemDetails 503
avec traceId, sans recopier son body HTML/JSON dans la réponse publique. Réponses
invalides, dimensions incompatibles, modèle indisponible et dépassements de délai
sont gérés explicitement. L'annulation de l'appelant est propagée.
Aucune réponse de remplacement, aucun embedding factice et aucun retry automatique
ne masquent un échec du fournisseur en production.

## Exécution et vérifications

```powershell
./scripts/Initialize-Postgres.ps1
. ./scripts/Import-LocalEnvironment.ps1
dotnet build backend/ITTicketIntelligence.sln --configuration Release --no-restore
dotnet ef database update --project backend/src/ITTicketIntelligence.Infrastructure --startup-project backend/src/ITTicketIntelligence.Infrastructure --configuration Release --no-build
dotnet test backend/ITTicketIntelligence.sln --configuration Release --no-build
dotnet run --project backend/src/ITTicketIntelligence.Api --configuration Release --no-build
```

Avec Ollama et le modèle configuré disponibles :

```powershell
postman collection lint "postman/collections/IT Ticket Intelligence"
postman collection run "postman/collections/IT Ticket Intelligence" -i Rag --no-report-events
```

Le parcours crée trois documents (SQL, VPN, facturation), indexe avec les vrais
embeddings, vérifie que « SQL Server connection timeout » classe le guide SQL en
premier, teste les filtres, puis supprime les documents et vérifie leur exclusion.
Cette démonstration sur trois documents n'est pas une mesure de qualité générale
du modèle. Elle exige un profil de test sans autres documents concurrents dans le top-k.

Vérifications réalisées : build Release sans avertissement ni erreur, migration
appliquée, **420 tests xUnit réussis** (223 unitaires et 197 d'intégration).
Les tests isolent les ports et vérifient notamment lots, annulation, logs, normalisation,
scores connus, réindexation, profils, versions, cascades, concurrence, contrats HTTP
et erreurs. Les tests de contrat HTTP de l'adaptateur emploient un handler contrôlé ;
ils ne revendiquent pas une preuve de qualité sémantique.

Collection lintée : 48 requêtes, aucun avertissement ni erreur. Smoke : 12 requêtes /
34 assertions réussies. Postman RAG réel : **12 requêtes / 17 assertions réussies** contre Ollama + PostgreSQL.
La [simulation HTTP](../postman/simulations/rag-provider/rag-provider.context.md)
vérifie séparément succès, erreur HTML 500, dimensions invalides, timeout et reprise
(5 appels / 10 assertions). Seul ce test de frontière utilise des vecteurs synthétiques,
dans un profil de simulation distinct. Aucun de ces tests ne génère une réponse LLM.
