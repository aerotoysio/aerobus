# Stack configuration — where every setting lives

One page for standing up (or debugging) the whole aerotoys stack. Each service keeps its
settings in its own conventional place; this is the map. **No secrets belong in any repo** —
dev files carry clearly-labelled dev-only values, production takes secrets from the environment.

## The services

| Service | Repo | Config lives in | Notes |
| --- | --- | --- | --- |
| **Keycloak** (identity) | [aeroauth](https://github.com/aerotoysio/aeroauth) | `docker-compose.yml` / `render.yaml` + **`scripts/setup-realm.mjs`** | The realm builder is the **source of truth** for realm/clients/roles — see below |
| **DocumentForge** (datastore) | [documentforge](https://github.com/aerotoysio/documentforge) | CLI flags / env on `dfdb serve` | `--port 4300 --data-dir <dir> --api-key <key>`; keys also mintable at runtime via `POST /admin/keys` (`df_…` scoped keys) |
| **RuleForge** (rules engine) | [ruleforge](https://github.com/aerotoysio/ruleforge) | `RULEFORGE_*` env vars | `RULEFORGE_API_KEY` (the `X-AERO-Key` callers send), `RULEFORGE_DF_BASE_URL`, `RULEFORGE_DF_API_KEY`, `RULEFORGE_RULE_SOURCE=df`, `RULEFORGE_ENV` |
| **aerobus** (API backbone) | [aerobus](https://github.com/aerotoysio/aerobus) | `src/AeroBus.Api/appsettings*.json`, overridable per key via env `Section__Key` | Sections below |
| **aerostudio** (admin web UI) | [aerostudio](https://github.com/aerotoysio/aerostudio) | `.env.local` (from `.env.example`) | Auth.js reads `AUTH_KEYCLOAK_*` by convention |
| **aerodesk** (agent desktop) | [aerodesk](https://github.com/aerotoysio/aerodesk) | per-user `%AppData%\AeroDesk` (`settings.json`, `connections.json`, DPAPI `secrets.json`) | Everything is per-connection, entered in the Connect dialog |

## aerobus settings (`appsettings.json` sections)

Override any key with an environment variable using `Section__Key` (double underscore).

| Section | Keys | Meaning |
| --- | --- | --- |
| `DocumentForge` | `BaseUrl`, `ApiKey`, `Database` | The datastore. `Database` is the **static fallback** DB; authenticated org requests are routed to the org's own `db/{shortName}` by the tenancy middleware (see README "Multi-tenancy") |
| `Keycloak` | `BaseUrl`, `Realm`, `ClientId` (=`aerobus`), `ClientSecret`, `Audience` (=`aerobus`) | JWT validation (`{BaseUrl}/realms/{Realm}` issuer, `aud=aerobus`) **and** the admin service account used to create orgs/users. Leaving `BaseUrl`/`Realm` empty disables the Keycloak scheme (dev API-key-only mode) |
| `RuleForge` | `BaseUrl`, `ApiKey`, `TimeoutMs`, `Endpoints:*` | Decision points; degrade to Allow when the engine is down |
| `Events` | *(optional)* `PollSeconds`, `MaxAttempts`, `BackoffBaseSeconds`, `BackoffCapSeconds`, `BatchSize`, `WebhookTimeoutSeconds`, `RetentionDays` | Outbox dispatcher; all defaulted, runs unconfigured |

Docker Compose (`docker-compose.yml` + `.env` from `.env.example`) wires `DFDB_API_KEY` and
`RULEFORGE_API_KEY` into the right places for DF + RuleForge + aerobus. Keycloak is external
(aeroauth) — supply `Keycloak__BaseUrl` / `Keycloak__Realm` / `Keycloak__ClientSecret` via env.

## aerostudio settings (`.env.local`)

```dotenv
AUTH_SECRET=                # any random string (npx auth secret)
AUTH_KEYCLOAK_ID=aerostudio
AUTH_KEYCLOAK_SECRET=       # printed by aeroauth/scripts/setup-realm.mjs
AUTH_KEYCLOAK_ISSUER=https://<keycloak>/realms/aerotoys
AUTH_TRUST_HOST=true
AEROBUS_URL=http://localhost:5080
APP_URL=http://localhost:3000
```

## aerodesk (per connection, in the Connect dialog)

- **DocumentForge backend**: URL, database, API key (DPAPI-stored).
- **AeroBus backend**: URL, company slug, agent email/password, and for **Departure Control**
  the Keycloak fields: authority (e.g. `https://<keycloak>`), realm (`aerotoys`), client id
  (`aeroboard`). Blank Keycloak URL = retailing-only connection.

## Keycloak: the realm is code

**Never hand-configure the realm.** [aeroauth `scripts/setup-realm.mjs`](https://github.com/aerotoysio/aeroauth)
is idempotent and builds everything the apps expect:

```sh
KC_URL=https://<keycloak> KC_ADMIN_USER=admin KC_ADMIN_PASSWORD=<pw> \
APP_REDIRECT_URIS='http://localhost:3000/*,https://<studio-host>/*' \
node scripts/setup-realm.mjs
```

It creates/converges: realm `aerotoys` (Organizations **enabled**); realm roles
`platform-admin` / `org-admin` / `editor` / `viewer`; clients **`aerostudio`** (confidential,
code flow — secret → `AUTH_KEYCLOAK_SECRET`), **`aerobus`** (confidential, service account with
realm-management rights for org/user provisioning — secret → `Keycloak__ClientSecret`),
**`aeroboard`** (public, direct-access grant — the aerodesk DCS staff login); the
**`aerobus-aud`** client scope (puts `aud: aerobus` in aerostudio/aeroboard tokens, which
aerobus validates); and demo users. It prints the two client secrets on completion.

Two claims contracts worth knowing:
- `organization` is a **dynamic scope** — clients must request it at login
  (`scope=openid … organization`) for the org-membership claim to appear; aerobus resolves it
  to the caller's `companyId` (tenant routing).
- `realm_access.roles` carries the four system roles, which aerobus expands into `perm` claims.

## Fresh install, in order

1. **DocumentForge**: `dfdb serve` (or the installer's Windows service) with an admin `--api-key`;
   mint scoped `df_…` keys via Studio/`POST /admin/keys` as needed.
2. **Keycloak** (aeroauth): bring up the server, run `setup-realm.mjs` against it, capture the
   printed secrets.
3. **RuleForge**: `RULEFORGE_DF_*` pointed at DocumentForge, source `df`.
4. **aerobus**: `DocumentForge__*`, `RuleForge__*`, `Keycloak__*` (secret from step 2).
5. **aerostudio**: `.env.local` (issuer + `aerostudio` secret from step 2, `AEROBUS_URL`).
6. **aerodesk**: installed per workstation; connections entered in the Connect dialog.
7. **Onboard the first airline**: `POST /identity/onboarding` (or the aerostudio wizard, when it
   lands) — creates the Keycloak org + admin, provisions + seeds the org's own DocumentForge
   database, and registers it for tenant routing. *(Anonymous today — gate before production.)*
