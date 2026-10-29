# Abstraction LLM

`ILLMService` appartient à Application. `HttpLLMService` et ses adaptateurs de
protocole Ollama / OpenAICompatible appartiennent à Infrastructure. Domain ne
change pas. L'interface reçoit un prompt et un contexte RAG fourni explicitement,
puis retourne une réponse typée et validée.

```csharp
Task<LLMResponse> GenerateAsync(
    LLMRequest request,
    CancellationToken cancellationToken = default);
```

Exemple d'utilisation par un futur cas d'usage Application :

```csharp
RetrievedContext context = await retriever.RetrieveAsync(new RagQuery(question), token);
LLMResponse response = await llm.GenerateAsync(new LLMRequest(question, context), token);
```

`ILLMService` n'ajoute pas d'appel LLM automatique au pipeline ML `/analyze`
ou à la recherche RAG. L'étape suivante, [AI Ticket Agent](ai-ticket-agent.md),
utilise le port distinct `ILLMAgentService` et `/investigate` pour ses outils.
Le transport HTTP partagé ne possède ni DbContext, ni
repository, ni IServiceProvider. Il ne lit pas lui-même la Knowledge Base.
Les citations doivent être comparées à la version documentaire courante par le
futur workflow d'approbation avant une éventuelle application opérationnelle.

## Contrat structuré

Le fournisseur reçoit un schéma JSON imposant les quatre champs suivants :

```json
{
  "summary": "Check SQL connectivity.",
  "suggestedSteps": ["Verify TCP port 1433."],
  "citedChunkIds": ["11111111-1111-4111-8111-111111111111"],
  "insufficientContext": false
}
```

Ce JSON est un exemple de fixture, pas une réponse d'un modèle réel.
`LLMResponse` contient ensuite :

- `answer` : Summary, SuggestedSteps et InsufficientContext.
- `citations` : DocumentId, ChunkId, DocumentVersion, Title, Source et ContentHash,
  reconstruits depuis le contexte fourni, jamais depuis une provenance inventée par le modèle.
- `provider`, `model`, `attempts` et `needsHumanReview: true`, fixé par le serveur.

Le contexte est copié à la création de la requête. Le prompt est limité à 8000
caractères et le contexte à 20 chunks / 24000 caractères de texte, titres et sources.
Un dépassement est refusé sans troncature silencieuse. Ces bornes en caractères
ne constituent pas un comptage de tokens : le modèle choisi doit avoir une fenêtre
adaptée au prompt, au schéma, au contexte et au budget de sortie configurés.

La validation refuse : champs manquants/inconnus ou dupliqués, réponse vide,
types incorrects, citations dupliquées/inconnues, réponse tronquée, refus du modèle,
tool calls inattendus et réponse dépassant 256 Kio. Une réponse considérée comme
suffisamment étayée doit citer au moins un chunk fourni. Avec un contexte vide,
`insufficientContext` doit être true.

Les limites de sortie sont : résumé 4000 caractères, 10 étapes de 800 caractères
et 20 citations maximum. Cette validation établit le format et l'appartenance des
citations ; elle ne prouve pas la véracité technique de la recommandation.
Le modèle n'exécute aucune action et ne décide jamais de l'approbation.

## Configuration exclusivement par environnement

La factory DI lit `LLM__*` directement depuis l'environnement lors de la première
résolution du service. Elle ne lit pas de section LLM dans appsettings. Un changement
de configuration nécessite une nouvelle instance / un redémarrage du processus.

| Variable | Défaut / règle |
|---|---|
| LLM__Provider | Ollama ; autre valeur supportée : OpenAICompatible |
| LLM__BaseUrl | Ollama : http://127.0.0.1:11434/ ; Compatible : https://api.openai.com/v1/ |
| LLM__Model | Ollama : qwen3:1.7b ; obligatoire pour OpenAICompatible |
| LLM__ApiKey | vide pour Ollama ; obligatoire pour OpenAICompatible |
| LLM__AttemptTimeoutSeconds | 30 ; entre 1 et 120 |
| LLM__TotalTimeoutSeconds | 90 ; entre le timeout d'une tentative et 300 |
| LLM__MaxRetries | 2 ; entre 0 et 2, soit 3 tentatives maximum |
| LLM__RetryDelayMilliseconds | 200 ; entre 0 et 2000 |
| LLM__MaxOutputTokens | 1024 ; entre 16 et 4096 |
| LLM__ContextWindowTokens | 16384 ; Ollama uniquement, entre 2048 et 32768 |
| LLM__TokenLimitParameter | max_completion_tokens ; max_tokens disponible pour serveurs compatibles |

Les endpoints distants exigent HTTPS ; HTTP est permis sur loopback pour les
services locaux. Les redirections HTTP sont désactivées. Les clés sont transmises
uniquement dans l'en-tête Authorization Bearer, pas dans les messages ou les URLs.
Les options ne sont pas un record imprimant ses propriétés ; ApiKey est ignorée
par la sérialisation JSON.

`.env.example` contient uniquement des réglages non secrets et un champ ApiKey vide.
Les valeurs réelles doivent rester dans l'environnement, un gestionnaire de secrets
ou le `.env` local ignoré par Git, importé avec `scripts/Import-LocalEnvironment.ps1`.
Le démarrage de l'API, `/analyze` et les routes de recherche/indexation RAG ne
déclenchent aucun appel LLM. `/investigate` est le cas d’usage explicite de l’agent.

Exemple local :

```powershell
$env:LLM__Provider = 'Ollama'
$env:LLM__BaseUrl = 'http://127.0.0.1:11434/'
$env:LLM__Model = 'qwen3:1.7b'
```

Pour un serveur OpenAICompatible, configurer Provider, BaseUrl, Model et ApiKey
dans l'environnement. Le serveur et le modèle doivent supporter Chat Completions
avec `response_format: json_schema` strict ; aucun fallback silencieux en texte
libre n'est implémenté. L'adaptateur Ollama utilise `/api/chat`, `format` JSON Schema,
`stream: false`, `think: false` et un budget de sortie/fenêtre explicite.
Les modèles génératifs réels n'ont pas été appelés pendant cette étape.

Références de contrat : [Structured Outputs — OpenAI Docs](https://developers.openai.com/api/docs/guides/structured-outputs),
[Chat — Ollama](https://docs.ollama.com/api/chat).

## Timeout, cancellation et retry

Une échéance couvre toute l'opération, y compris la lecture du body et les attentes
entre tentatives. Chaque tentative a son propre timeout. L'annulation de l'appelant
est propagée comme OperationCanceledException et n'est jamais retentée.

Sont transitoires : erreurs réseau/lecture, timeout d'une tentative et HTTP
408/429/500/502/503/504. Les retries emploient un backoff exponentiel borné.
Retry-After est respecté ; s'il impose plus de 10 secondes d'attente, l'appel échoue
sans retenter prématurément. Le budget global continue de s'appliquer pendant le backoff.
Un retry peut répéter du calcul facturable chez un fournisseur distant ; les bornes
de tentatives et de tokens sont explicites.

Authentification refusée, autre erreur HTTP permanente, réponse invalide, refus et
troncature ne sont pas retentés. `LLMServiceException.Kind` distingue Unavailable,
Timeout, RateLimited, Authentication, ProviderRejected, InvalidResponse et Refused.
Le handler HTTP global est prêt à mapper ces erreurs en ProblemDetails 503 si un
futur endpoint utilise le service, sans exposer le body du fournisseur.

Les logs structurés contiennent fournisseur, modèle configuré, tentatives, compteurs
de citations, durées et type d'échec. Ils n'incluent ni clé, prompt, contexte, réponse
brute du fournisseur, ni exception brute susceptible de contenir ces données.

## Tests

```powershell
. ./scripts/Import-LocalEnvironment.ps1
dotnet build backend/ITTicketIntelligence.sln --configuration Release --no-restore
dotnet test backend/ITTicketIntelligence.sln --configuration Release --no-build
```

Vérification réalisée : compilation Release sans avertissement ni erreur,
**463 tests réussis** (230 unitaires, 233 d'intégration). Les nouveaux tests emploient
un fake ILLMService, des handlers HTTP contrôlés et un vrai serveur HTTP fake local.
Ils couvrent contexte immuable, schémas des deux fournisseurs, citations, budgets,
annulation, retries bornés, erreurs permanentes, refus/troncature, redaction des logs,
configuration et absence de dépendance des composants LLM au DbContext/service locator.

Huit tests de transport ont aussi été exécutés contre la [simulation Postman](../postman/simulations/llm-provider/llm-provider.context.md).
Les quatorze appels enregistrés correspondent aux succès, au retry après 503, à
l'épuisement des trois tentatives et aux timeouts des deux protocoles. Cette preuve
concerne l'intégration HTTP ; elle ne mesure pas la qualité d'un modèle génératif réel.
