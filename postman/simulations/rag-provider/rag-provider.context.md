# rag-provider — HTTP embedding boundary

Created 2026-10-25 from the Ollama tags/embed contract. Local mock only; no owner
workspace lookup or publication was performed while signed out of Postman.

## Purpose

Run the actual API and HTTP adapter against controlled provider responses. The mock
uses a synthetic model/profile and 3D vectors; this does not measure semantic relevance.

## Execution

```powershell
postman simulation run postman/simulations/rag-provider/rag-provider.sim.yaml --port 4902 --no-history --output ndjson
```

In a second terminal, import the local database environment and start the API with:

```powershell
. ./scripts/Import-LocalEnvironment.ps1
$env:Rag__Ollama__BaseUrl='http://127.0.0.1:4902/ollama/'
$env:Rag__Ollama__Model='simulation-embedding'
$env:Rag__Ollama__ModelDigest='aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
$env:Rag__Ollama__Dimensions='3'
$env:Rag__Ollama__TimeoutSeconds='1'
$env:ASPNETCORE_URLS='http://127.0.0.1:5081'
$env:ASPNETCORE_ENVIRONMENT='Testing'
dotnet run --project backend/src/ITTicketIntelligence.Api --configuration Release --no-build --no-launch-profile
```

For each mode `valid`, `error`, `invalid`, `timeout`, then `valid` again:

```powershell
Invoke-RestMethod http://127.0.0.1:4902/ollama/control -Method Post -ContentType application/json -Body '{"mode":"error"}'
postman collection run "postman/collections/IT Ticket Intelligence" -i RagProvider --env-var "baseUrl=http://127.0.0.1:5081" --env-var "expectedStatus=503" --no-report-events
```

Use expectedStatus=200 in valid mode. Stop both owned processes with Ctrl+C.

## Assumptions

- 2026-10-25: a distinct simulation model/digest prevents comparison with real Nomic vectors.
- 2026-10-25: valid mode queries an empty simulation profile, so success returns an empty context.
- Source contract and limitations are in docs/rag-pipeline.md; this mock never stands in for the API or PostgreSQL.

## Observations

<!-- begin:observations -->
- 2026-10-25 UTC: five API calls passed ten assertions. Simulation logs recorded tags and embed traffic in every mode, including HTML HTTP 500 and a 3005 ms delayed response; the API timed out near 1072 ms and recovered on the next valid call. No chat/generate route was called.
<!-- end:observations -->

## What didn't work

- 2026-10-25: this installed CLI rejected a simulation member pointing to a directory; the explicit `config.yaml` path in rag-provider.sim.yaml works.
