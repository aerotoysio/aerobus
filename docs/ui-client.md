# Point an admin UI at AeroBus

> **OBSOLETE (2026-07-13).** The legacy ooms admin UI can no longer repoint at
> AeroBus: the `/admin/users` (incl. `authenticate`), `/admin/roles`,
> `/admin/permissions` and `/admin/api-tokens` routes this recipe depends on were
> removed when user management moved to Keycloak behind `/identity`. The admin UI
> is now **aerostudio** (`C:\data\aerostudio`), which signs in via Keycloak
> (aeroauth repo) and manages users/roles/permissions/agents through
> `/identity/*`. Kept for the catalogue/order route-shape notes only.

AeroBus deliberately preserved the ooms admin/order/offer route shapes, so an
existing Next.js admin UI can be repointed at it with **one config change** and no
code edits. This is the recipe.

## Which UI

Use **`C:\DATA\aerotoys-ooms\admin`** (Candidate A).

Two candidates were investigated:

| | `aerotoys-ooms\admin` (A) | `tailwind\tailwindAdminSite` (B) |
| --- | --- | --- |
| Sidebar / nav | Wired — per-domain `modules/*/nav.ts` aggregated in `kernel/shell/app-sidebar.tsx`, reflects real routes | Broken — menu component is a stub; `components/nav/config.tsx` lists RuleForge starter routes that don't exist |
| API base config | Ships `.env.local` with the repoint knob | No `.env*` file at all |
| Structure | Cleaned-up `modules/* + kernel/*` refactor | Older `backend/business/dataAccess` split, `.bak` files, dead git modules |
| `admin/api-tokens` screen | Yes (matches AeroBus `/admin/api-tokens`) | No |
| Auth code | `POST /admin/users/{slug}/authenticate`, `{email,password}` | Byte-identical, plus a dead dev-stub login path |

Both are the same lineage (identical deps, route tree, and auth code), so auth
compatibility and the dropped-route list are the same. A wins on completeness and
being pre-configured. **B is not recommended.**

## The repoint change

Edit **`C:\DATA\aerotoys-ooms\admin\.env.local`**:

```dotenv
# was: ADMIN_API_BASE=http://localhost:5102
ADMIN_API_BASE=http://localhost:5080
OFFER_API_BASE=http://localhost:5080
ORDER_API_BASE=http://localhost:5080
TENANT_DEFAULT_SLUG=aerotoys
```

- **`ADMIN_API_BASE`** is the primary base. It's a **server-side** var (not
  `NEXT_PUBLIC_*`), read in `kernel/http.ts` (`const ENV_BASE = process.env.ADMIN_API_BASE || ''`)
  and passed as `base:` by every server action (admin, catalogue, order, customer,
  offer). There is no axios and no hardcoded base — it's `fetch` with a per-call
  `base` fallback to `ENV_BASE`.
- **`OFFER_API_BASE`** / **`ORDER_API_BASE`** are used only by the call-centre
  shop/book/modify flows (`modules/ops/actions/callcentre/*`) and otherwise default
  to old ooms ports (`:5249` / `:5150`). Set them to `:5080` so those flows hit
  AeroBus too.
- Keep **`TENANT_DEFAULT_SLUG=aerotoys`** — it's the `{companySlug}` the login
  action posts to. You must seed a company with **slug `aerotoys`** in AeroBus
  (see "Login creds" below); the smoke test seeds a *random* company each run, so
  don't rely on it for the UI.

## Run it

```powershell
cd C:\DATA\aerotoys-ooms\admin
npm install
npm run dev        # next dev -p 3002
```

- **Node 20 LTS — required, not just recommended** (no `.nvmrc`/`engines`; deps imply
  Node 20 — `@types/node 20.x`).
- Next.js **14** (App Router), React 18.3, TypeScript 5.4.
- Dev server: **http://localhost:3002** (the README's "port 3000 / login a/a" text
  is stale starter boilerplate — ignore it).

Make sure AeroBus is up first (`scripts/run-stack.ps1`, AeroBus on `:5080`).

### Troubleshooting: "Something went wrong / An error occurred in the Server Components render"

If the **login page renders and submitting reaches AeroBus** (you see
`POST /admin/users/{slug}/authenticate → 200` in the AeroBus log) but the browser
then lands on a generic *"An error occurred in the Server Components render"* page,
this is almost always a **Node runtime mismatch, not an AeroBus problem**. Next.js
14.2's server-action + RSC handling is unstable on **Node 22 / 23 / 24** and throws
this exact digest-only error on the first authenticated (`(app)`) render, regardless
of the backend. Run the UI on **Node 20 LTS** (`nvm install 20 && nvm use 20`, or
fnm/volta) and it renders. The authenticate call succeeding proves the AeroBus wiring
is correct; the crash is downstream in the UI's own server render.

## Login creds

AeroBus's `POST /admin/users/{companySlug}/authenticate` currently **does not verify
the password** (ported as-is from ooms — a user is authenticated if a document
matches the email). So any password works; you just need a **user document whose
email you know, under a company whose slug is `aerotoys`**.

Seed one directly into DocumentForge (dfdb on `:4300`) so the UI has something to
log in as. Minimal seed (PowerShell; adjust the dfdb URL/key if needed):

```powershell
$dfdb = "http://localhost:4300"
$co   = [guid]::NewGuid()
$role = [guid]::NewGuid()
$perm = [guid]::NewGuid()

# admin.all permission
Invoke-RestMethod -Method Post "$dfdb/collections/permissions" -ContentType application/json -Body (@{
  Id=$perm; Code="admin.all"; Name="All"; Status="Active" } | ConvertTo-Json)

# role holding it
Invoke-RestMethod -Method Post "$dfdb/collections/roles" -ContentType application/json -Body (@{
  Id=$role; CompanyId=$co; Code="ADMIN"; Name="Administrator"; Status="Active"; PermissionIds=@($perm) } | ConvertTo-Json)

# company with slug 'aerotoys'
Invoke-RestMethod -Method Post "$dfdb/collections/companies" -ContentType application/json -Body (@{
  Id=$co; Name="AeroToys"; Slug="aerotoys"; Status="Active" } | ConvertTo-Json)

# user
Invoke-RestMethod -Method Post "$dfdb/collections/users" -ContentType application/json -Body (@{
  Id=[guid]::NewGuid(); Email="admin@aerotoys.io"; Name="Admin"; Status="Active";
  RoleId=$role; CompanyId=$co } | ConvertTo-Json)
```

Then log in with **`admin@aerotoys.io`** and any password.

> Note: `scripts/smoke.ps1` performs this same seed technique (permission → role →
> company → user via direct dfdb inserts) but with a fresh random company slug each
> run. This UI seed just pins the slug to `aerotoys` so `TENANT_DEFAULT_SLUG` lines up.

## Auth compatibility: COMPATIBLE

- The login server action (`kernel/auth/login.ts` → `modules/security/actions/users.ts`)
  does `POST /admin/users/${companySlug}/authenticate` with body `{ email, password }`
  and destructures the response as `{ user, accessToken, permissions }` — exactly
  AeroBus's response shape (`UsersEndpoints.cs`).
- The UI sends **camelCase** (`email`/`password`); AeroBus binds `[FromBody] User`
  with the default case-**insensitive** JSON policy, so `email`→`Email` binds fine.
  **No mismatch, no UI change.**
- On success the JWT is stored in an **httpOnly cookie `admin_token`** (8h); the
  server HTTP client attaches `Authorization: Bearer <token>` on every request.
  AeroBus accepts exactly that. No `refreshToken` is expected.

## Screens that WORK vs 404

AeroBus maps admin / catalogue / customer / offer(shop+price) / order / rules /
events, and **drops** pricing, the node-based offer engine, the rule designer, and
git/workspace-promote.

### Will work
- **Admin / security** — companies, company configs, roles, role-permissions,
  permissions, users, workspaces (list/CRUD), **api-tokens**.
- **Catalogue** — continents, countries, regions, airports, market-zones, equipment,
  layouts (+ seatmap), schedules, flights, connections, **flight-builder**, bundles,
  products, stock-keepers.
- **Order** — order list/detail, customer.
- **Call-centre** — Search (`POST /offer/shop`), Book (`POST /order/create`), Modify
  (`GET /order/{id}` + `POST /order/change`) — **once `OFFER_API_BASE` / `ORDER_API_BASE`
  are set to `:5080`** (they otherwise point at old ooms ports).
- **Offer → Rules & Slugs** — under `/offer` + `/rules`.
- **Events, Dashboard, Health** — `/events` + catalogue/order reads.

### Will 404 / 501 (routes AeroBus dropped)
- **Entire Pricing section** — `/pricing/markets`, `/pricing/pricing-definitions`,
  `/pricing/market-pricing-definitions`, `/pricing/x-axis-*`, `/pricing/exchange-rates`.
  (The Dashboard's stats tile pings `/pricing/markets` → that one card errors, page
  still loads.)
- **Offer → Discover Engine** — `GET /offer/offer-engine/{slug}` (hardcoded `:5249`
  in `modules/offer/ui/routes/discover/page.tsx`); explicitly dropped.
- **Offer → Rule Designer** (node graph) — `/offer/designer/*`, `/offer/nodes/templates`.
- **Workspace Promote** — `POST /admin/workspaces/{id}/promote` (basic workspace
  list/CRUD still works).

## One-line summary

Edit `C:\DATA\aerotoys-ooms\admin\.env.local` → `ADMIN_API_BASE=http://localhost:5080`
(+ `OFFER_API_BASE` / `ORDER_API_BASE` = `:5080`, keep `TENANT_DEFAULT_SLUG=aerotoys`),
seed an `aerotoys` company + user in dfdb, `npm install && npm run dev`, open
http://localhost:3002, log in as `admin@aerotoys.io` (any password). Admin,
Catalogue, Orders, Call-centre, Offer Rules/Slugs, and Events work; Pricing, Offer
Discover Engine, Rule Designer, and workspace promote 404.
