# Storage architecture — Postgres system of record, DocumentForge config store

Decided 2026-07-31. The stack runs TWO stores on purpose, split by data personality —
not by module habit. **The boundary below is the contract**: transactional/relational
data never drifts back into DocumentForge because "adding a collection is easier".

## The split

| PostgreSQL — system of record | DocumentForge — configuration store |
| --- | --- |
| **Orders domain**: orders, order items, passengers, charges | **Rules platform**: rules, versions, environments, reference sets |
| **Flight inventory** + per-org order counter (same transaction as orders) | Shapes, scenarios, **node templates** |
| **Customers** (identity, LTV, search) | **Offers** — shopped snapshots, cache-like with expiry |
| **Network & schedule**: airports, schedules, flights, market zones | Remaining master data: geo (continents/countries/regions), equipment, layouts, connection rules, stock keeper, attributes, media, loyalty tiers, Right to Fly |
| **Merchandising headliners**: products, bundles | Control plane: organisations, identity/RBAC, api tokens, platform + org config |
| | Events: outbox, cursors, webhook subscriptions *(v1 — see phase 2 note)* |

Why: order create is a real transaction (inventory + order + counter → one
`BEGIN…COMMIT`, compensation logic deleted); reporting/LTV/search are SQL; integrity
and row-level locking come free. DocumentForge keeps what it is genuinely best at —
versioned, schema-free JSON configuration.

## Tenancy

**Schema-per-org**: one `aerotoys` database, one schema per organisation named by the
org slug (`verify`, `aurora`) — the same isolation story as DocumentForge's
database-per-org. Schemas are created at onboarding (provisioning) and ensured
on-demand. The per-request tenant (from `ITenantDatabase`) selects the schema via
`search_path`; unresolved-tenant requests get NO schema and fail loudly rather than
reading another org's rows.

## Access pattern

- **Npgsql, hand-written SQL** — matches the repository style; no EF/ORM layer.
- **Table pattern**: promoted, indexed columns for everything queried or sorted
  (`order_id`, `status`, `created`, `profile_id`, `email`, `departure_date`, …) plus a
  `doc jsonb` column holding the full aggregate exactly as the wire serialises it
  (camelCase). Wire shapes do not change; consumers never notice the store swap.
  Fully-relational exceptions: `flight_inventory` and `order_counters` are plain
  columns — they exist to be updated atomically, not to carry documents.
- **Optimistic ops**: DocumentForge CAS calls become `UPDATE … SET available =
  available - n WHERE available >= n` (guarded atomic updates) and
  `INSERT … ON CONFLICT DO UPDATE … RETURNING` (counters).
- **DDL**: an idempotent bootstrap runner (`CREATE SCHEMA/TABLE/INDEX IF NOT EXISTS`)
  applied per schema at provisioning and on first tenant use. No migration framework
  until the schema stabilises; changes are additive scripts in `Data/Postgres/Ddl`.

## Configuration

`Postgres:ConnectionString` in appsettings (bootstrap section, like DocumentForge/
Keycloak). Empty = Postgres disabled and the legacy DocumentForge repositories serve
the migrated domains (with a startup warning) — the app still boots on boxes without
PG. Dev connection strings live in git-ignored `dev-settings.json` / `.env.demo`;
**no credentials in the repos**, production takes the string from the environment.

## Phases (each = branch → PR → tests → live-verify)

| Phase | Scope |
| --- | --- |
| P9.2 | Foundation: Npgsql, tenant schema resolution, DDL bootstrap, provisioning hook |
| P9.3 | Customers |
| P9.4 | Orders + flight inventory + order counter (storage swap, semantics unchanged) |
| P9.5 | Transactional order create — one transaction, compensation deleted |
| P9.6 | Airports, schedules, flights, market zones |
| P9.7 | Products + bundles |
| P9.8 | Demo cutover: schemas provisioned, Verify re-seeded, full live E2E |

**Phase 2 (parked, deliberate)**: transactional outbox — move order-domain events into
the same PG transaction and teach the dispatcher to pump both stores; until then
events remain DocumentForge-wide and order events stay best-effort exactly as today.
Tests: PG round-trip tests run against a local server via `AEROTOYS_PG` (connection
string env var); like the DocumentForge fixture they fail RED when the server is
missing rather than silently passing.
