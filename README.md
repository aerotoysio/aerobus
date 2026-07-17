# AeroBus

**The backbone of the open airline retailing stack** — one API and event bus that
carries offers, orders, and configuration between the distribution channels
(AeroDesk, AeroWeb, AeroMesh) and the open foundation:
[DocumentForge](https://github.com/aerotoysio/documentforge) for storage and
[RuleForge](https://github.com/aerotoysio/ruleforge) for dynamic rules.

```
        AeroDesk / AeroWeb / AeroMesh
                    │  HTTP + webhooks/SSE
                    ▼
                ┌─────────┐   HTTP (decisions)   ┌───────────┐
                │ AeroBus ├──────────────────────▶  RuleForge │
                └────┬────┘   X-AERO-Key         └─────┬─────┘
                     │ HTTP (Bearer)                   │  (rules read from DF)
                     ▼                                 ▼
              ┌────────────────────────────────────────────┐
              │                DocumentForge                │
              │   (the only datastore — every write here)   │
              └────────────────────────────────────────────┘
```

AeroBus is **one service, one API surface**. It owns no database of its own:
everything it persists goes through DocumentForge, and every pricing/eligibility
call goes to RuleForge (which itself reads its rules from DocumentForge). That
keeps AeroBus a thin, stateless backbone — scale it out, and the store and the
rules engine are the shared state.

## What's inside

| Concern | What it does |
| --- | --- |
| **Control plane** | Companies, users, roles, permissions, workspaces, company configs, API tokens. |
| **Catalogue** | Reference data (continents → countries → regions → airports), market zones, fleet (equipment + seat layouts), schedules and the **flight builder** (materialises flights + per-flight seat inventory). |
| **Products** | Products, bundles (LITE / FLEX / FLEXPLUS), stock keeping. |
| **Customer** | The customer aggregate (passports + stored cards embedded). |
| **Offer** | `POST /offer/shop` builds priced solutions for an O&D; RuleForge decides the bundles and pricing. Degrades gracefully when RuleForge is down. |
| **Order** | Create (atomic seat-inventory decrement) / retrieve / change (cancel releases inventory). |
| **Rules** | File and publish rules + reference sets into RuleForge's DocumentForge collections, bind them to an environment, and refresh the engine. |
| **Events** | A transactional outbox with a background dispatcher: signed webhooks, an SSE stream, and a queryable audit trail. |

## Multi-tenancy (SaaS: database per organisation)

Keycloak owns organisations + users; **each airline gets its own DocumentForge
database**, named by a short code (`ek`, `emirates`), created and seeded when the
org is onboarded.

- **Onboarding** (`POST /identity/onboarding`) now provisions a tenant end-to-end:
  Keycloak org + admin → create `db/{shortName}` → seed it (a `Company` settings
  doc + a reference starter pack of airports/equipment) → register the org in the
  control-plane `organisations` registry. Anonymous today — **gate before prod**
  (it creates databases).
- **Per-request routing**: `TenantDatabaseMiddleware` resolves the caller's
  `companyId` → the org's `shortName` (from the registry, cached) and stamps
  `ITenantDatabase`, so that request's business reads/writes go to the org's own
  database. Unauthenticated/unprovisioned callers fall back to the configured
  `DocumentForge:Database` (today's single-DB behaviour is preserved).
- **What's per-tenant vs shared.** The airline's business data (companies,
  catalogue, orders, customers, offers, stock, checkins) lives in its own database
  — full physical separation. A small set of collections read *at auth-time or by
  background jobs* stays in the **shared control database** because there's no
  resolved tenant DB at that moment: the org registry, identity/RBAC
  (`orgroles`/`orgroleassignments`/`userprofiles`), `apitokens`, the events outbox,
  and rules. These still carry `companyId` for scoping. (RBAC/tokens are candidates
  to move fully into Keycloak later.)

Deferred follow-ons: per-org **rules** (RuleForge tenant-awareness) and **events**
(a tenant-iterating dispatcher), a durable per-org **order-sequence counter**
(order codes are already prefixed by the org's designator), migrating DF
identity/security tables into Keycloak, and the aerostudio onboarding wizard UI.

## Quickstart

> **Where does every setting live?** See [`docs/configuration.md`](docs/configuration.md) —
> the one-page map of DocumentForge / Keycloak / RuleForge configuration across the whole
> stack (aerobus, aerostudio, aerodesk, aeroauth), plus the fresh-install order.

### Docker Compose (full stack)

Brings up DocumentForge + RuleForge + AeroBus together:

```bash
cp .env.example .env          # set DFDB_API_KEY + RULEFORGE_API_KEY (dev defaults are fine locally)
docker compose up -d

curl http://localhost:5080/health                 # AeroBus
curl http://localhost:5080/health/documentforge   # AeroBus → DocumentForge probe
curl http://localhost:5055/health                  # RuleForge (open)
```

`.env` documents the two secrets:

- **`DFDB_API_KEY`** — DocumentForge Bearer key, shared by DocumentForge,
  RuleForge (`RULEFORGE_DF_API_KEY`) and AeroBus (`DocumentForge__ApiKey`).
- **`RULEFORGE_API_KEY`** — the `X-AERO-Key` shared secret between AeroBus and
  RuleForge.

### Local (from source)

Run the three services locally with the helper script (starts dfdb + RuleForge
`--no-launch-profile` in `df` mode + AeroBus, and publishes the shop rule to the
`dev` environment **before** RuleForge boots so the decision endpoint binds):

```powershell
./scripts/run-stack.ps1      # dfdb :4300, RuleForge :5050, AeroBus :5080
./scripts/smoke.ps1          # end-to-end smoke test against the running stack
```

Or by hand:

```bash
# 1. DocumentForge
dfdb serve --port 4300 --data-dir ./data --insecure-dev-mode

# 2. RuleForge (df source, dev env) — see scripts/run-stack.ps1 for the full env
dotnet run --project <ruleforge>/src/RuleForge.Api --no-launch-profile

# 3. AeroBus
dotnet run --project src/AeroBus.Api          # http://localhost:5080
```

In Development, interactive API docs are at **http://localhost:5080/swagger**
(one "AeroBus API v1" document over every group; the **Authorize** button takes
either a user JWT or an `ab_` API key).

## Endpoint map

Every group is mounted in
[`AppEndpoints.cs`](src/AeroBus.Api/Bootstrap/AppEndpoints.cs). Route shapes match
the ooms admin/order services so existing UIs can repoint here (see
[`docs/ui-client.md`](docs/ui-client.md)).

| Group | Routes | Auth |
| --- | --- | --- |
| Diagnostics | `GET /health`, `GET /health/documentforge`, `GET /version` | open |
| Identity | `/identity/me`, `/identity/users`, `/identity/roles`, `/identity/permissions`, `/identity/agents`, `/identity/organizations` (+ anonymous `POST /identity/onboarding`) | Bearer + `identity.*` / `role.*` / `agent.*` perms |
| Admin | `/admin/companies`, `/admin/companies/config`, `/admin/workspaces` | Bearer |
| Catalogue | `/catalogue/{continents,countries,regions,airports,market-zones,equipment,layouts,schedules,flights,connection-rules,flight-builder,bundles,products,stockkeeper}` | Bearer + `catalogue.view` |
| Customer | `/customer` | Bearer + `customers.view` |
| Offer | `/offer/shop`, `/offer/price` | Bearer + `offers.view` |
| Order | `/order/create`, `/order/retrieve`, `/order/change` | Bearer + `orders.view` |
| Operations (DCS) | `/operations/departures`, `/operations/flights/{id}` (`/manifest`, `/status`, `/depart`, `/board-all`), `/operations/checkin`, `/operations/board` | Bearer + `operations.view` (writes: `operations.manage`) |
| Rules | `/rules/*`, `/rules/reference-sets/*`, `/rules/environments/*` | Bearer + `rules.view` |
| Policy Studio | `/policy-studio/{tree,spaces,folders,policies,schemas,datarefs,releases,settings}`, `/policy-studio/policies/{id}/{publish,compiled,tests,tests/run}` | Bearer + `policystudio.view` (writes: `policystudio.manage`) |
| Events | `/events`, `/events/stream`, `/events/subscriptions` | Bearer + `events.view` |

Two credentials work everywhere `[Authorize]` applies: a **Keycloak user token**
(OIDC — the aerostudio login; realm roles + custom org roles expand into `perm`
claims) or an **`ab_` API key** (a programmatic agent from `/identity/agents`).
The `Authorization: Bearer <token>` scheme routes on the token prefix. The
permission catalog lives in
[`PermissionCatalog.cs`](src/AeroBus.Core/Security/PermissionCatalog.cs); the
ooms-era user/role/permission/api-token admin routes (and the HS256
authenticate flow) were removed in favour of `/identity`.

## Event catalog

Events are written to the `outboxevents` outbox the instant the domain writes its
change, dispatched at-least-once, and delivered to webhooks (`X-AeroBus-Signature`,
HMAC-SHA256 over the exact body) + the SSE stream. Type filters support exact
(`order.created`) or trailing-glob (`order.*`) patterns.

| Type | Emitted when |
| --- | --- |
| `order.created` | An order is created (after inventory decrement). |
| `order.changed` | A non-cancel order transition. |
| `order.cancelled` | An order is cancelled (inventory released). |
| `inventory.adjusted` | Seat inventory decremented/restored (carries `delta` + `reason`). |
| `offer.created` | An offer shop persists a solution set. |
| `customer.created` | A customer aggregate is created. |
| `flight.built` | The flight builder materialises a flight. |
| `flight.cancelled` | A built flight is cancelled. |
| `schedule.changed` | A schedule is created/edited. |
| `product.changed` | A product is saved. |
| `bundle.changed` | A bundle is saved. |
| `rule.published` | A rule is published to an environment (global, no company scope). |
| `policy.published` | A Policy Studio policy is published — compiled + released to the engine (global, no company scope). |
| `flight.status-changed` | A flight's operational status advances (e.g. Scheduled→Boarding). |
| `flight.departed` | A flight is departed (Boarding→Departed). |
| `checkin.completed` | A passenger is checked in for a flight. |
| `passenger.boarded` | A passenger is boarded (carries boarding sequence + seat). |

Consume them three ways: **webhooks** (`POST /events/subscriptions` with a
`types[]` filter + optional `secret`), the **SSE stream**
(`GET /events/stream?from={seq}`), or the **audit query** (`GET /events?from={seq}`).

## Decision points (RuleForge)

AeroBus calls RuleForge at named decision points (configured under `RuleForge:Endpoints`).
Shop **degrades** if RuleForge is unavailable (empty bundles + a warning, never a
500); the order points **allow** by default (fail-open) unless configured otherwise.

| Decision point | RuleForge endpoint | Failure mode |
| --- | --- | --- |
| Shop bundles | `POST /v1/offer/shop-bundles` | Degrade |
| Offer pricing | `POST /v1/offer/price` | Degrade |
| Order validate | `POST /v1/order/validate` | Allow |
| Order change eligibility | `POST /v1/order/change-eligibility` | Allow |
| Refund eligibility | `POST /v1/order/refund-eligibility` | Allow |

File the rules with `scripts/seed-shop-rule.ps1` (or `run-stack.ps1`, which does it
for you): it PUTs the reference sets + `rule-shop-bundles` from [`rules/`](rules/)
and publishes them to the `dev` environment.

## Run the tests

Live round-trip tests against a local DocumentForge (36 tests):

```bash
# start DocumentForge on :4300
dotnet run --project <documentforge>/src/DocumentForge.Cli -- \
  serve --port 4300 --insecure-dev-mode --data-dir ./test-data

dotnet test
```

Point them elsewhere with `DOCUMENTFORGE_BASEURL` / `DOCUMENTFORGE_APIKEY`. Each
test uses a fresh company id and cleans up what it creates, so runs are isolated
and repeatable.

For a full-stack, every-module exercise (control plane → catalogue → flight
build → offer → order → oversell → events), run **`scripts/run-stack.ps1`** then
**`scripts/smoke.ps1`** — a single PASS/FAIL smoke test that drives a live stack.

## Related repositories

- [DocumentForge](https://github.com/aerotoysio/documentforge) — the document store AeroBus persists everything through.
- [RuleForge](https://github.com/aerotoysio/ruleforge) — the runtime rules engine AeroBus calls at its decision points.
