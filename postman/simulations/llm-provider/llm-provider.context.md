# llm-provider — structured generation HTTP contracts

Local mock created 2026-10-25 from the Ollama chat and compatible Chat Completions
contracts. No owner workspace lookup was performed while signed out of Postman;
no mock was published. Fixtures and credentials are test-only.

## Purpose

Exercise the actual ILLMService and ILLMAgentService HTTP implementations without
a real model or a paid provider. The mock never stands in for our service.

## Execution

```powershell
postman simulation run postman/simulations/llm-provider/llm-provider.sim.yaml --port 4904 --no-history --output ndjson
```

In a second terminal, after compiling:

```powershell
$env:LLM_TEST_BASE_URL='http://127.0.0.1:4904/llm/'
dotnet test backend/tests/ITTicketIntelligence.IntegrationTests --configuration Release --no-build --filter FullyQualifiedName~LLMWireTests.RealHttpAdapterAgainstFakeLlm
# Agent tool round trips and final structured output:
dotnet test backend/tests/ITTicketIntelligence.IntegrationTests --configuration Release --no-build --filter FullyQualifiedName~AgentProtocolTests
```

Stop the simulation with Ctrl+C. Unset LLM_TEST_BASE_URL to restore the test suite's
self-contained local HTTP fake. No PostgreSQL or real provider is needed for this subset.

## Assumptions

- Tests supply their own bounded options and synthetic Authorization value; no real key is read or sent.
- Request model selects the fixture: success, retry-once, unavailable or timeout.
- retry-once alternates 503/success per provider route, making repeated sequential runs reproducible.
- Handler validates structured-message envelopes; fixtures prove transport, not model quality.
- Agent fixtures use model names prefixed agent-; the two phases require six read-only tools from the shared MCP catalog, then a final JSON schema without tools. They only use synthetic observations (observed 2026-10-26).

## Observations

<!-- begin:observations -->
- 2026-10-26 MCP stage: 16 agent protocol cases passed with the six-tool catalog. The llm member served both provider routes, tools/results, structured finals and failure fixtures; no member stayed silent.
- 2026-10-26: 24 HTTP cases passed across both interfaces and providers. The llm member served tool requests/results, schema-constrained finals, transient/exhausted 503s, refusals, malformed/truncated replies and delayed responses. No member stayed silent. This run proves transport behavior against the local stand-in, not model quality.
- 2026-10-25 UTC: eight tests passed. Simulation logs contained fourteen POST requests across Ollama chat and compatible Chat Completions, with 503 retry/exhaustion and ~3-second delayed replies cancelled by the client's 1-second budget. Authorization was redacted; all prompts were synthetic fixtures.
<!-- end:observations -->
# Extension chat local

Le modèle synthétique `chat-sim` expose aussi `/api/tags` et `/api/chat` en NDJSON. Le provider `HttpLLMService.Chat.cs` a été exécuté contre cette simulation : health, réponse agrégée et streaming terminent correctement. Cela valide le format HTTP de l'adaptateur Microsoft.Extensions.AI/OllamaSharp ; cela ne valide pas la qualité d'un modèle réel ni la persistance PostgreSQL. Les tests du provider couvrent séparément annulation, saturation, délai, interruption et modèles interdits.

