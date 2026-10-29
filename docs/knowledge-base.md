# Knowledge Base

Module Clean Architecture pour cinq types : `Document`, `Article`,
`TroubleshootingGuide`, `Faq` et `Procedure`. L'entrée est un contenu JSON en
`PlainText` ou `Markdown`. L'import binaire PDF/DOCX, l'OCR et le téléchargement
d'URL ne sont pas implémentés à cette étape. `source` est un identifiant de
provenance unique, jamais une instruction de lecture réseau ou disque.

## Contrat HTTP

| Méthode | Route | Résultat |
|---|---|---|
| POST | `/api/knowledge` | 201, document préparé et Location |
| GET | `/api/knowledge` | 200, liste paginée de résumés |
| GET | `/api/knowledge/{id}` | 200, contenu original, texte nettoyé et chunks |
| PUT | `/api/knowledge/{id}` | 200, nouvelle version et nouveaux chunks |
| DELETE | `/api/knowledge/{id}?version=2` | 204, suppression du document courant et de ses chunks |

Exemple de création :

```json
{
  "title": "SQL connection troubleshooting",
  "content": "# Connection checks\n\nCheck **SQL Server** status and the configured endpoint.",
  "source": "knowledge-base/sql-server/connection-errors.md",
  "category": "Database",
  "kind": "TroubleshootingGuide",
  "format": "Markdown"
}
```

`title`, `content` et `source` sont obligatoires. Les valeurs par défaut sont
Category=Other, Kind=Document et Format=PlainText. `createdById` est nullable ;
s'il est fourni, il doit désigner un utilisateur existant et actif.

PUT attend tous les champs descriptifs, Category/Kind/Format et la `version`
courante. La version commence à 1 et augmente à chaque PUT réussi. DELETE exige
la version en query string. Une ancienne version produit 409, sans effacer ni
remplacer les chunks courants. La suppression est physique : les chunks sont
des données dérivées remplaçables, et aucun autre module ne les référence encore.
Il n'y a pas de soft delete ni de conservation des anciennes versions documentaires.

GET collection accepte `page` (1..1000000), `pageSize` (1..100, défaut 20),
`search` (200 caractères maximum), `category` et `kind`. La recherche ILIKE porte
sur titre, contenu original et source ; `%` et `_` saisis sont traités littéralement.
Le tri est UpdatedAt décroissant puis ID. Les résumés excluent le contenu et
indiquent `chunkCount`.

Les bornes sont 160 caractères pour le titre, 100000 pour le contenu et 512 pour
la source. Les requêtes Knowledge autorisent un body de 1 Mio pour couvrir
également le JSON Unicode échappé. Les caractères NUL sont rejetés avant stockage.
Les champs inconnus, formats non reconnus, enums numériques et textes requis
vides produisent 400. Les réponses d'erreur utilisent ProblemDetails avec traceId :
400 validation, 404 document/auteur absent, 409 source déjà utilisée, version
obsolète ou auteur inactif, 503 base indisponible, 500 erreur inattendue.

## Pipeline de préparation

`IKnowledgeDocumentProcessor.PrepareAsync` orchestre :

1. **Extraction** via `IKnowledgeTextExtractor`. PlainText est déjà textuel.
   L'extracteur Markdown local traite les titres, liens simples, emphase forte,
   citations et fences. Il garde le texte des liens et le corps des blocs de code,
   avec leur indentation. C'est un sous-ensemble de Markdown, pas un parseur
   CommonMark complet. Les URLs ne sont jamais suivies.
2. **Cleaning** : Unicode NFC, fins de ligne LF, retrait du BOM et des contrôles
   hors tabulations/sauts de ligne, retrait des espaces de fin de ligne et réduction
   des lignes vides successives. L'indentation du code est conservée. Le texte
   original reste stocké séparément, sans trimming de son indentation.
3. **Chunking** déterministe : au plus 1200 unités UTF-16 par chunk, chevauchement
   cible de 150, recherche d'une séparation blanche dans le dernier quart du chunk.
   Les paires de surrogates ne sont pas coupées ; le chevauchement peut alors
   gagner une unité. Aucun segment vide n'est indexable.
4. **Metadata** : DocumentId, DocumentVersion, titre, source, catégorie, type,
   format, position à partir de zéro, StartOffset inclusif / EndOffset exclusif
   dans le **texte nettoyé**, empreinte SHA-256 du chunk encodé en UTF-8.

`processingVersion: "text-v1"` et `processedAt` identifient la préparation.
Chaque chunk reçoit un ID propre lors de sa sauvegarde. Les mêmes entrées
produisent les mêmes textes, positions et hashes ; une nouvelle version reçoit
de nouveaux IDs de chunks.

Les handlers transmettent les CancellationTokens. La préparation est locale et
bornée ; aucune tâche de fond ni requête réseau n'est nécessaire. Une extraction
sans texte exploitable est refusée avant toute sauvegarde. Les remplacements
sont préparés avant de modifier l'agrégat chargé.

## Couches et stockage

- **Domain** : `KnowledgeDocument`, `KnowledgeChunk`, enums Kind/Format et invariants.
  Chunks en lecture seule, contenu/offsets/version cohérents et couverture du texte.
- **Application** : DTOs, Commands/Queries, handlers, validation, orchestration,
  ports du repository, de l'extracteur et des services vectoriels.
- **Infrastructure** : extracteur texte/Markdown, repository EF Core, mappings,
  index, contraintes et migration `20261025221213_KnowledgeBase`.
- **API** : binding HTTP, codes de statut et délégation aux handlers.

La table `app.knowledge_chunks` référence `knowledge_documents` avec suppression
en cascade. Un index unique (document_id, position) conserve l'ordre. Les contraintes
contrôlent version, offsets, hash et timestamps. `knowledge_documents` conserve
son index source unique et ses tokens de concurrence Version/xmin, plus un index
par type/date/ID. Une unique sauvegarde EF Core conserve document et chunks dans
la même transaction ; une erreur de concurrence ou d'unicité annule tout le lot.
EF exige le chargement des chunks pour mettre à jour l'agrégat et refuse leur
réécriture en place : ils sont supprimés et remplacés par la nouvelle préparation.

La migration conserve les documents préexistants : Kind=Document, Format=PlainText,
champs de préparation null et aucun chunk généré implicitement. Un PUT valide les
prépare et augmente leur version. Aucun traitement automatique des anciennes lignes
n'est exécuté au démarrage.

## Abstractions embeddings et vector store

Dans `Application/Knowledge/Retrieval` :

- `IEmbeddingService.GenerateAsync(texts, purpose, token)` : embeddings dans l'ordre des
  entrées, avec modèle identifié et dimensions déterminées par les valeurs.
- `IVectorStore.ReplaceDocumentAsync(id, version, profile, vectors, token)` :
  remplacement atomique du jeu de vecteurs d'un document, rejet des versions obsolètes.
- `SearchAsync(queryEmbedding, limit, minimumScore, filter, token)` : résultats portant ChunkId,
  métadonnées/version et score, avec filtres document/catégorie/type.
- `DeleteDocumentAsync(id, token)` : suppression des vecteurs du document.

Ces contrats préparatoires sont désormais consolidés et implémentés par le
[module RAG](rag-pipeline.md) : embeddings Ollama et vector store PostgreSQL.
Les opérations CRUD ne déclenchent pas de calcul d'embeddings ; l'indexation est
explicite via les routes RAG. Le remplacement ou la suppression des chunks invalide
leurs vecteurs PostgreSQL par cascade. Aucun LLM de génération n'est appelé.

## Tests et commandes

```powershell
. ./scripts/Import-LocalEnvironment.ps1
dotnet build backend/ITTicketIntelligence.sln --configuration Release --no-restore
dotnet ef database update --project backend/src/ITTicketIntelligence.Infrastructure --startup-project backend/src/ITTicketIntelligence.Infrastructure --configuration Release --no-build
dotnet test backend/ITTicketIntelligence.sln --configuration Release --no-build
```

Tests : préparation déterministe, Unicode, limites et offsets, hashes, métadonnées,
annulation, invariants du domaine, handlers et absence de mutation après un échec.
Les tests HTTP sur PostgreSQL temporaire couvrent les cinq types, CRUD complet,
remplacement/suppression des chunks, entrées invalides, source unique, rollback,
versions obsolètes, concurrence EF, pagination/filtres et contrat OpenAPI.

API démarrée :

```powershell
postman collection lint "postman/collections/IT Ticket Intelligence"
postman collection run "postman/collections/IT Ticket Intelligence" -i Knowledge -i Smoke --no-report-events
```

Le parcours Knowledge crée, lit, filtre, modifie et supprime ses propres documents.
Il vérifie aussi le body de 100000 caractères contre Kestrel, puis supprime ce
document de test. Aucun utilisateur de référence ni compte Postman n'est nécessaire.

Vérification effectuée : compilation Release sans avertissement ni erreur,
migration appliquée sur PostgreSQL local, **420 tests xUnit réussis** (223 unitaires,
197 d'intégration, suite enrichie avec RAG). La collection complète est lintée (48 requêtes, aucune erreur
ni avertissement). Les parcours Postman Knowledge (9 requêtes / 18 assertions) et
Smoke (12 requêtes / 34 assertions) ont réussi contre l'API réelle, avec exit code 0.
