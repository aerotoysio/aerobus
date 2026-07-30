# Rule-based retailing — Right to Fly, Policies, and the shapes that feed them

The target model for how an airline structures what it sells. Written 2026-07-28 as the
build brief for the next slices; the glossary is the contract — UI copy, endpoints and
collection names follow it.

## Concepts

| Concept | What it is | Where it lives |
| --- | --- | --- |
| **Right to Fly (RTF)** | The new name for a fare brand (Saver / Flex / Business): the right to purchase a seat on a flight solution — *without* seat selection, which stays a Product. Carries an eligibility rule and connected Policies. | `catalogue.righttofly` (new) |
| **Product** | A sellable thing: bag, seat selection, lounge access. Eligibility and pricing are rule-driven (with reference-table lookups). | `catalogue.products` (exists) |
| **Bundle** | A package of Products at a different price point, under generic rules. After this change a Bundle is ONLY a package — it is no longer the brand row on shopping (RTF replaces that). | `catalogue.bundles` (exists) |
| **Policy** | A **master rule** shared across RTFs, products, markets and channels — "Baggage Policy", "Lounge Policy". Authored once, invoked from many places. | The `rules` store, `category: "policy"`, endpoint `/v1/policy/{slug}` |
| **Flight Solution** | The physical output of the flight/connection builder: legs that get the customer A→B. No brands, no pricing. | Produced by shop (exists) |
| **Customer** | The customer object — or an anonymous "1 ADT" — fed into every rule as input. | `customers.customers` (exists) |

**The crux**: a Policy is not a new engine concept — it's a plain rule. **For now the
connection is managed OUTSIDE the rules** (decided 2026-07-28): the RTF record lists its
`PolicyIds`, and *aerobus orchestrates* — at benefits time it calls each connected policy
rule directly with the Shape 2 request and merges the outputs. No `ruleRef` sub-rule
nodes yet; the engine supports them, and we can move the orchestration inside rules
later if composing policies from policies ever becomes worth the complexity.

## How a shop works (target flow)

1. **Shop request** → flight solutions built (exists today).
2. For each flight solution × each active **Right to Fly**:
   - run the RTF's **eligibility rule** (Shape 1) → in/out for this solution + customers;
   - run the RTF's **pricing rule** (Shape 1) → the brand price (ref-set lookups, markups);
   - run the RTF's **benefits rule** (Shape 2, `mode: "included"`) — this is where
     connected Policies fire as sub-rules. Saver + Baggage Policy → hand luggage pops
     out; Business → 2×32kg + 2 hand luggage. Loyalty is in the request, so Gold's
     extra bags appear here with zero extra wiring.
3. Customer picks an RTF, sees the benefits, moves to options → **options request**
   (Shape 3, `mode: "optional"`) → the same Policies now emit the priced à-la-carte
   items (excess bags, paid seats, lounge) instead of the freebies.

**Included vs à-la-carte convention**: the request carries `mode: "included" | "optional"`
(and optionally `maxSpend`). Policy rules filter on it — a filter node on `mode` splits
the free allowance branch from the priced-extras branch. No engine change; it's a shape
convention every policy follows.

## Rule engine input shapes

Generic mapping is the eternal struggle, so it is a first-class layer (2026-07-29):

- **Policies are authored against ONE canonical contract** — the seeded
   shape. It is ALWAYS array-shaped (, even for one
  customer), so a policy never changes because a caller sends one passenger or five;
  "any customer is Gold" vs "a bag per Gold customer" is an authoring choice
  (arraySelector vs iterator), not a shape concern.
- **Caller shapes PROJECT into the contract**: each shape field maps to a canonical
  field — implicitly when its path already matches, or explicitly via the field's
  "Maps to (Policy Contract)" dropdown in the Studio shape editor. At policy time
  aerobus runs ShapeProjector (extract → lift singletons to arrays → derive paxIds)
  and evaluates the policy on the projected request. The ENGINE stays pure
  primitives — it never learns about shapes.
- **Authoring is label-driven**: the policy editor's field picker lists the
  contract's labels ("Loyalty tier", "Request mode"), never paths or JSON.
- Non-policy rules still map to a caller shape directly ( on the rule
  doc) and drive their pickers and test-console samples from it.

**Shape 1 — Shopping Engine** (`shopping`):

```json
{
  "mode": "included",
  "searchContext": { "channel": "web", "currency": "AED", "origin": "DXB", "destination": "LHR" },
  "customers": [
    { "id": "…", "type": "ADT", "age": 34, "loyaltyTier": "GOLD", "corporateCode": null }
  ],
  "flightSolution": {
    "origin": "DXB", "destination": "LHR",
    "tripDurationMinutes": 420, "maxStopoverMinutes": 0, "stops": 0,
    "legs": [
      {
        "flightNumber": "VF001", "from": "DXB", "to": "LHR",
        "departureLocal": "2026-08-01T08:00", "arrivalLocal": "2026-08-01T12:00",
        "equipment": "B788",
        "cabins": [ { "cabin": "Y", "available": 42 } ]
      }
    ]
  }
}
```

**Shape 2 — Right to Fly Benefits** (`rtf-benefits`): Shape 1 **plus** the selected RTF,
`mode: "included"` — asking for the freebies/allowance only:

```json
{ "…shape 1…": "…", "rightToFly": { "code": "SAVER", "name": "Saver" }, "mode": "included" }
```

**Shape 3 — À la carte** (`a-la-carte`): identical to Shape 2 with `mode: "optional"`
(and optional `maxSpend`) — asking for the priced extras.

## Data model

- **`catalogue.righttofly`** (new): `RtfCode`, `RtfName`, `Description`, `Rank`, `Status`,
  `EligibilityRuleId`, `PricingRuleId`, `BenefitsRuleId`, `PolicyIds[]` (the connected
  master rules — used to scaffold/validate the benefits rule's `ruleRef` nodes).
- **Policies** are rows in the existing `rules` store with `category: "policy"` — they get
  the full authoring stack (versions, environments, publish, test console, graph editor)
  for free. Product output: a policy's `product` nodes emit product codes that resolve
  against `catalogue.products`.
- **Migration**: today's Lite / Flex / Flex Plus fare bundles become RTF records; the
  `rule-shop-bundles` pricing rule becomes the SAVER/FLEX pricing template. Bundles keep
  existing only as product packages.

## AeroStudio UI

- **Policies** — new sidebar section: list (`GET /rules/?category=policy`), create from a
  template (baggage policy starter), edit = the graph editor, test = the test console.
  Product nodes get a **product picker** (from `catalogue.products`) instead of raw JSON.
- **Right to Fly** — new catalogue section: golden-path CRUD + attach eligibility/pricing/
  benefits rules + connect Policies (multi-select) + "preview on a flight solution".
- **Rule editor upgrades** (feedback from the first cut):
  - node palette becomes a **vertical list** (it will grow well past four entries);
  - **typed inspectors per node type** — no JSON editing for common nodes. First:
    the string/loyalty filter (field picker from the shape registry, operator dropdown,
    value input, on-missing select) and the product node (product picker). Raw JSON
    stays available under an "Advanced" fold for node types without a typed editor yet.

## Delivery phases (each = branch → PR → tests → live-verify)

| Phase | Scope | Done when |
| --- | --- | --- |
| **P1** | Editor UX: vertical palette + typed inspectors (stringFilter, product) | Author the gold-bag rule end-to-end without touching JSON |
| **P2** | Shape registry: aerobus `GET /rules/shapes` + editor field pickers + per-shape test-console samples | Filter node's field dropdown lists Shape 1 paths |
| **P3** | Policies vertical: category=policy filter on `GET /rules/`, Studio Policies section, baggage-policy template | Create + edit + test "Baggage Policy" wholly in Studio |
| **P4** | Right to Fly: collection, CRUD endpoints, Studio section, policy connections, bundle→RTF migration | SAVER/FLEX/BUSINESS exist with eligibility + connected baggage policy |
| **P5** | Shopping integration: shop evaluates RTF eligibility/pricing/benefits (Shapes 1–2); `POST /offer/options` for Shape 3 | Demo shop returns RTF brand rows with included benefits; options call returns priced extras |
| **P6** | aeroweb: brand rows = RTF with benefits shown; options page à la carte (+ the queued full order rendering) | Book end-to-end on RTF brands on the demo box |

## Open items

- Eligibility/pricing/benefits as three rules per RTF vs one rule with three outputs —
  start with three (simpler mapping, independent testing), merge later if authoring feels heavy.
- `maxSpend` semantics beyond the included/optional split — parked until a policy needs it.
- Per-market RTF availability — model as a normal filter node inside the eligibility rule
  (market zones are already reference data), not as new RTF fields.
