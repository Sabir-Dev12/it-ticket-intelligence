# Frontend read API simulation

Local contract fixture created on 2026-10-27 from the existing Ticket/Knowledge
Application DTOs, controllers and saved OpenAPI contract.

## Purpose

Exercise the real React fetch client through Vite over HTTP with a deterministic
English helpdesk queue. Handler: `postman/mocks/frontend-read-api/default.js`.

## Execution

From repository root:
`postman simulation run postman/simulations/frontend-read-api/frontend-read-api.sim.yaml --port 4908 --no-history`.
The listening event supplies `http://localhost:4908/helpdesk`.
Set API_PROXY_TARGET to that address before starting Vite; run
`npm run test:e2e --prefix frontend`. Stop both processes with Ctrl+C.

## Assumptions

- Observed 2026-10-27: Vite preserves the target path prefix, so `/api/tickets`
  reaches the simulation as `/helpdesk/api/tickets` and the handler as `/api/tickets`.
- Observed 2026-10-27: the fixture covers REST reads and five controlled Ticket
  Details actions. SignalR authentication,
  live delivery and PostgreSQL are outside this simulation and need the real API.
- Observed 2026-10-27: 36 synthetic tickets include Open/InProgress/Resolved;
  Closed is intentionally empty. No application fallback imports this fixture.

## Observations

<!-- begin:observations -->
- 2026-10-27: Ticket Details used shared fixture state for saved ML/AI,
  assignment, status and comments. The 409 mode required explicit revision review;
  503 retained ML results. Test-only reset isolates each Chrome scenario.
- 2026-10-27: the helpdesk member served dashboard counters, filtered/paginated
  ticket reads, ticket details and knowledge reads in Chrome E2E runs. The
  unavailable search produced 503; clearing it recovered the normal queue.
<!-- end:observations -->

## What didn't work

- 2026-10-27: the first sandboxed CLI start failed in dataset-daemon with
  `Exception 0xc0000008` before listening. An approved unrestricted local run
  started normally; no handler workaround was required.
- 2026-10-27: Chrome outside the sandbox could not reach sandboxed Vite
  (`ERR_CONNECTION_TIMED_OUT`). Running Vite with the same approved local access
  as Chrome resolved the issue.
