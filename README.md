# AeroBus

**The backbone of the open airline retailing stack** — one API and event bus that
carries offers, orders, and configuration between the channels (AeroDesk, AeroWeb,
AeroMesh) and the open foundation ([DocumentForge](https://github.com/aerotoysio/documentforge)
for storage, [RuleForge](https://github.com/aerotoysio/ruleforge) for dynamic rules).

```
AeroDesk / AeroWeb / AeroMesh
            │
            ▼
        ┌────────┐      HTTP       ┌───────────┐
        │ AeroBus ├────────────────▶ RuleForge │
        └───┬────┘   decisions     └─────┬─────┘
            │ HTTP                       │
            ▼                            ▼
        ┌──────────────────────────────────┐
        │           DocumentForge          │
        └──────────────────────────────────┘
```

One service, one API surface: schedules & flight building, product/bundle
catalogue, offer shopping, order management, control plane (companies, users,
roles, API tokens), rule filing, and a pub/sub event backbone.

## Run it

```bash
docker compose up -d
curl http://localhost:5080/health
```

Or locally against a dev DocumentForge:

```bash
dfdb serve --port 4300 --data-dir ./data --insecure-dev-mode
dotnet run --project src/AeroBus.Api
```

## Tests

Live round-trip tests against a local DocumentForge:

```bash
dfdb serve --port 4300 --data-dir ./test-data --insecure-dev-mode
dotnet test
```

Point them elsewhere with `DOCUMENTFORGE_BASEURL` / `DOCUMENTFORGE_APIKEY`.

## Status

In active build. Current phase: skeleton + DocumentForge data layer.
