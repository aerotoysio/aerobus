# AeroBus permission model

One permission system covers every credential type. This documents the catalog,
the wildcard rules, how principals get their permissions, and how endpoints and
the aerostudio UI consume them.

**Source of truth:** [`PermissionCatalog.cs`](../src/AeroBus.Core/Security/PermissionCatalog.cs).
This file is the human-readable mirror — update both together.

## How a permission check works

Every authorization policy name IS a permission code (`PermissionPolicyProvider`
turns any policy name into a perm-claim requirement). A request passes when the
principal holds a `perm` claim that is:

1. the exact code (`offers.view`), or
2. `admin.all` — the platform-superuser override, or
3. `<resource>.all` — the resource-wide override (`offers.all` satisfies any
   `offers.*` action).

`admin.all` and `<resource>.all` are **grant-side wildcards only** — endpoints
never require them directly, and they are not assignable to custom roles or
agents (the catalog excludes them).

## The catalog

Every resource ships a `view` / `manage` pair. `view` gates reading (and the
aerostudio menu item); `manage` gates creating/changing. Resources align 1:1
with the AeroBus modules (`src/AeroBus.Api/Endpoints/<Area>`), so a menu
vertical, an endpoint group, and a permission resource are the same concept.

| Resource | Codes | Covers | AeroBus surface |
| --- | --- | --- | --- |
| `dashboard` | `dashboard.view` (only) | The landing dashboard | aerostudio only |
| `org` | `org.view` / `org.manage` | Organisation profile + site settings (stored as a Keycloak org attribute) | `/identity/organization` |
| `identity` | `identity.view` / `identity.manage` | Users of the organisation | `/identity/users*` |
| `role` | `role.view` / `role.manage` | Custom roles + the permission catalog | `/identity/roles*`, `/identity/permissions` |
| `agent` | `agent.view` / `agent.manage` | Programmatic (ab_ API-key) accounts | `/identity/agents*` |
| `offers` | `offers.view` / `offers.manage` | Offer distribution (shop/price) | `/offer/*` |
| `ibe` | `ibe.view` / `ibe.manage` | IBE content | aerostudio (module TBD) |
| `ancillary` | `ancillary.view` / `ancillary.manage` | Ancillary rules | aerostudio (module TBD) |
| `orders` | `orders.view` / `orders.manage` | Order lifecycle | `/order/*` |
| `customers` | `customers.view` / `customers.manage` | Customer aggregate | `/customer/*` |
| `catalogue` | `catalogue.view` / `catalogue.manage` | Reference data, fleet, schedules/flights, products/bundles/stock | `/catalogue/*` |
| `rules` | `rules.view` / `rules.manage` | Business-rules authoring (RuleForge proxy) | `/rules/*` |
| `events` | `events.view` / `events.manage` | Outbox audit, SSE stream, webhook subscriptions | `/events/*` |

Current group-level enforcement (see `AppEndpoints.cs`): `/offer`→`offers.view`,
`/order`→`orders.view`, `/customer`→`customers.view`, `/catalogue/*`→`catalogue.view`,
`/rules`→`rules.view`, `/events`→`events.view`, plus per-route policies across
`/identity`. Write-vs-read (`.manage`) enforcement inside the domain groups is a
planned refinement — today a group's `view` permission admits all its routes.

## How principals get permissions

**Keycloak users** (aerostudio sign-in) — two layers, expanded into `perm`
claims at token validation (`KeycloakClaimsTransformer`):

1. *System roles* (fixed Keycloak realm roles) carry static grants:

   | Role | Grants |
   | --- | --- |
   | `platform-admin` | `admin.all` (aerotoys staff — everything, every org) |
   | `org-admin` | `dashboard.view` + `.all` on: org, identity, role, agent, offers, ibe, ancillary, orders, customers, catalogue, rules, events |
   | `editor` | `dashboard.view`, `offers.all`, `ibe.all`, `ancillary.all`, `catalogue.view`, `orders.view`, `customers.view`, `rules.view` |
   | `viewer` | `dashboard.view` + `view` on offers, ibe, ancillary, catalogue, orders, customers |

2. *Custom org roles* — tenant-defined bundles of catalog codes, created on the
   aerostudio Roles page, stored in DocumentForge (`orgroles`), assigned per
   user (`orgroleassignments`). Expanded on top of system-role grants; cached
   60s with instant invalidation on any role/assignment change.

**Agents (`ab_` API keys)** — permissions are the key's scopes, chosen at
creation on the aerostudio API Agents page (snapshot, not a live role
reference). The key authenticates via the ApiKey scheme and its scopes become
`perm` claims directly.

There is no third credential type — the legacy self-issued HS256 JWTs and their
user/role/permission store were removed (2026-07-13).

## Introspection

- `GET /identity/me` — the caller's effective identity: roles, organisations,
  and the full expanded permission list. aerostudio gates its navigation from
  this response, so UI visibility and API enforcement can't drift.
- `GET /identity/permissions` — the assignable catalog, grouped by resource
  (drives the role-builder and agent-creation UIs, requires `role.view`).

## Adding a new resource

1. Add the pair in `PermissionCatalog.cs` (+ this file).
2. Enforce it: `app.MapGroup("/thing").…RequireAuthorization("thing.view")` in
   `AppEndpoints.cs` (and per-route `thing.manage` policies for writes).
3. Grant it: extend the system-role map in `KeycloakClaimsTransformer.cs` if the
   built-in roles should have it; custom roles pick it up automatically from the
   catalog.
4. Surface it: add the nav item with that permission code in aerostudio's
   `src/lib/sections.ts`.
