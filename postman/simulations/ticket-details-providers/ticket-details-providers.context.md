# Ticket Details provider boundaries

Created 2026-10-27 from the existing owned embedding and structured agent mocks.
No owner lookup or publication: local, signed-out CLI execution only.

## Purpose

Run the actual Ticket Details frontend, API, ML, PostgreSQL, MCP application tools
and SignalR while the two HTTP model providers return deterministic fixtures.
The product under test is not a simulation member.

## Execution

From repository root, run
`postman simulation run postman/simulations/ticket-details-providers/ticket-details-providers.sim.yaml --port 4909 --no-history`.
Then use the start/prepare/live/cleanup commands in `docs/ticket-details.md`.
The listening event supplies `/ollama` and `/llm` addresses. Stop owned processes
with Ctrl+C and restore the ordinary API before normal use.

## Assumptions

- Observed 2026-10-27: the synthetic 3D embedding profile uses a distinct model
  and digest, so its vectors are never compared with the real Nomic profile.
- Observed 2026-10-27: the loopback frontend port 5174 needs an explicitly allowed
  Origin on the temporary API host. The default production policy is unchanged.
- Observed 2026-10-27: this verifies model HTTP contracts and agent orchestration,
  not semantic relevance, LLM accuracy or a real provider's output quality.

## Observations

<!-- begin:observations -->
- 2026-10-27: both members served the real agent path: tags/embed from ollama,
  tool rounds and final structured generation from llm. Five real ticket events
  refreshed a second browser session, preserving its unsaved comment draft.
<!-- end:observations -->

## What didn't work

- No provider failure in the successful intersession run. Use the separate
  frontend contract fixture to exercise 409 and 503 UI states deterministically.
