# Ollama local : chat et embeddings

## Modèles constatés le 29 octobre 2026

`ollama list` et `/api/tags` ont confirmé :

| Usage | Modèle local | Taille |
| --- | --- | --- |
| Chat retenu | qwen3:1.7b | 1,4 Go |
| Autre chat installé | qwen3.8:27b | 18 Go |
| Embedding retenu | nomic-embed-text:latest | 274 Mo, 768 dimensions |
| Autre embedding installé | embeddinggemma:300m | 621 Mo, 768 dimensions |

Les entrées DeepSeek portant `:cloud` référencent Ollama Cloud et ne sont pas des modèles exécutés localement. Elles ne sont pas autorisées dans ce chat.

## Architecture conservée

React → API ASP.NET Core 10 authentifiée → `AIChatConversations` → `IAIChatService` → le **même `HttpLLMService` existant**, étendu par `HttpLLMService.Chat.cs` → `IChatClient` Microsoft.Extensions.AI → OllamaSharp 5.5.0 → Ollama.

OllamaSharp apporte une cible net10.0 et l'implémentation de `IChatClient`. Aucun SDK AI n'entre dans Domain/Application. Les contrats `ILLMService` (réponse RAG structurée) et `ILLMAgentService` (outils/agent) sont conservés, ainsi que l'adaptateur d'embeddings et son digest épinglé. Tous les chemins de génération partagent une limite d'une requête simultanée, sans file d'attente. Le chat général n'appelle pas d'outils et ne prétend pas avoir analysé un ticket ; les investigations restent dans l'assistant existant.

Les conversations générales réutilisent `AIConversation`, `AIMessage`, leur repository, les positions de messages, la concurrence optimiste PostgreSQL et l'unité de travail existants. Elles ont `TicketId = null`. Les conversations de l'agent liées à un ticket sont exclues de ces endpoints et conservent leurs contrôles spécifiques. Aucune migration supplémentaire.

## Configuration

```dotenv
LLM__Provider=Ollama
LLM__BaseUrl=http://localhost:11434/
LLM__Model=qwen3:1.7b
LLM__TotalTimeoutSeconds=90
LLM__MaxOutputTokens=1024
Rag__Ollama__BaseUrl=http://localhost:11434/
Rag__Ollama__Model=nomic-embed-text:latest
Rag__Ollama__ModelDigest=0a109f422b47e3a30ba2b10eca18548e944e8a23073ee3f3e947efcf3c45e59f
Rag__Ollama__Dimensions=768
```

Ces paramètres peuvent être placés dans `.env` ignoré par Git puis importés par `scripts/Import-LocalEnvironment.ps1` avant démarrage. Aucune clé API nécessaire pour Ollama local. Les defaults existants utilisent l'adresse équivalente `127.0.0.1:11434`. Le modèle de chat autorisé est exactement `LLM__Model`. Le champ optionnel `model` d'une requête doit correspondre à cette valeur, sinon 400 ; aucune URL n'est acceptée dans les DTOs. Le chat local refuse un endpoint non loopback et un modèle `:cloud`.

Garder Ollama à l'écoute sur loopback (`OLLAMA_HOST=127.0.0.1:11434`), sans exposition Internet, reverse proxy public ni accès direct React. Les routes backend requièrent un JWT Admin, Support ou Agent. User reçoit 403. L'identité vient du JWT, jamais du corps JSON. HTTPS reste requis pour publier le backend.

Changer de modèle d'embeddings, même avec les mêmes 768 dimensions, change l'espace vectoriel : épingler le nouveau digest et réindexer les documents. Le projet conserve volontairement nomic pour éviter ce changement implicite.

## API

| Méthode | Route | Réponse |
| --- | --- | --- |
| GET | /api/ai/chat/health | 200 si le modèle local est présent, sinon 503 ; ne lance pas d'inférence |
| POST | /api/ai/chat/conversations | 201, `{id,title,messages}` ; corps `{ "title": "Support" }` |
| GET | /api/ai/chat/conversations?page=1 | Liste privée, 20 éléments et `hasMore` |
| GET | /api/ai/chat/conversations/{id} | Historique privé ; autre propriétaire ou conversation ticket : 404 |
| POST | /api/ai/chat/conversations/{id}/messages | `{ "text": "...", "conversationId": "..." }` |
| POST | /api/ai/chat/conversations/{id}/stream | NDJSON, événements `delta`, puis `done` après commit |

Corps d'envoi : `{ "message": "How can I check a DNS issue?", "model": "qwen3:1.7b" }`. `model` est facultatif. Le frontend existant n'est pas modifié à cette étape ; il peut consommer ces routes via son client JWT.

Exemple de flux :

```json
{"type":"delta","text":"Check ","conversationId":null}
{"type":"delta","text":"DNS settings.","conversationId":null}
{"type":"done","text":null,"conversationId":"..."}
```

Avant le premier fragment, les erreurs utilisent ProblemDetails. Après le premier fragment, une erreur produit `{"type":"error","code":"generation_failed"}` sans événement `done` ; le statut HTTP déjà envoyé ne peut plus changer. Une annulation via `RequestAborted` coupe l'inférence et libère la place. Aucun retry automatique d'un flux déjà commencé. Les fragments partiels ne sont pas enregistrés comme réponse complète. Le client doit traiter `done` comme la confirmation de sauvegarde ; s'il perd la connexion à cet instant, consulter l'historique avant de réenvoyer.

Limites : requête HTTP 64 Ko ; message utilisateur 8 000 caractères ; contexte maximal de 20 messages précédents, réduit à 32 000 caractères avec la question ; réponse 16 000 caractères et limite de tokens serveur ; conversation limitée à 50 échanges. Saturation du provider : 503 via le contrat d'erreur LLM existant. Les échanges terminés et créations sont audités avec leurs IDs, sans recopier les prompts dans les logs. L'historique lui-même contient les messages dans PostgreSQL. Les écritures concurrentes sur une conversation sont protégées par `xmin` ; un conflit ne confirme pas le second résultat comme sauvegardé.

## Vérification reproductible

Validation effectuée : build Release sans erreur/avertissement, 318 tests unitaires réussis et 32 tests ciblés `HttpLLMServiceTests`/`OllamaChatTests` réussis. Les adaptateurs .NET ont réellement appelé `qwen3:1.7b` (health, chat et streaming) et nomic avec digest épinglé (768 dimensions), puis le même chat a été exercé contre la simulation HTTP Postman. La collection de 10 requêtes passe le lint.

Limite de cette exécution : les tests API avec historique PostgreSQL et les deux exécutions complètes de la collection restent à vérifier. Docker Desktop échoue au démarrage sur son socket `dockerInference`, inaccessible à Windows ; PostgreSQL ne peut donc pas démarrer. Aucun volume n'a été supprimé ni réinitialisé. Les anciens échecs de tests HTTP sans JWT, décrits dans `authentication.md`, sont distincts de ce blocage et cette étape ne prétend pas remettre toute la suite historique au vert.

```powershell
ollama list
. ./scripts/Import-LocalEnvironment.ps1
dotnet build backend/ITTicketIntelligence.sln --configuration Release
dotnet test backend/tests/ITTicketIntelligence.UnitTests --configuration Release
dotnet test backend/tests/ITTicketIntelligence.IntegrationTests --configuration Release --filter "FullyQualifiedName~OllamaChatTests|FullyQualifiedName~AIChatApiTests|FullyQualifiedName~HttpLLMServiceTests"
./scripts/Verify-Ollama.ps1 -Simulation
./scripts/Verify-Ollama.ps1
```

Le script nécessite Docker/PostgreSQL démarré et des binaires Release à jour. Il crée une base et des credentials temporaires, démarre une API sur 5082, exécute la collection locale `Local Ollama Chat` sans publier de résultats distants, puis supprime l'environnement temporaire et la base. La variante Simulation réutilise `llm-provider` sur 4914 avec le modèle synthétique `chat-sim`. La variante réelle vérifie aussi un embedding nomic de 768 dimensions. Aucun téléchargement automatique de modèle.

Références : [IChatClient Microsoft](https://learn.microsoft.com/dotnet/ai/ichatclient), [adaptateur OllamaSharp](https://awaescher.github.io/OllamaSharp/docs/getting-started.html).
