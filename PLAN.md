# `Equinor.Osdu.Schemas` — implementation plan

A separate C# library that ships typed POCOs generated from the OSDU schema
registry. Designed to compose with [`osdu-csharp-client`][client] through its
existing JSON bridge (`UntypedNode ⇄ JsonNode/POCO`), with **no changes
required to the client**.

[client]: https://github.com/equinor/osdu-csharp-client

## Why a separate library

The client decided — see [PR #39 discussion][pr39] — to keep `Record.data` as
free-form (`UntypedNode`), matching the canonical Java `os-core-common`
(`Map<String, Object> data`). That's the right call for the client: typed
domain models for every OSDU `kind` × every schema version would be an
unbounded vendoring commitment.

But consumers who *want* intellisense for a specific kind/version still
deserve it. Splitting that responsibility into a separate package gives both:

- **The client stays small, generic, low-maintenance.**
- **Typed classes are available as an opt-in** for consumers who want them,
  at the kind and version *they* care about.
- **Versioning becomes a consumer choice.** Reference `V1_4_0` or `V1_5_0`
  (or both side-by-side) based on the deployment.
- **Release cadences decouple.** Schema bumps don't force client releases.
- **Multiple providers can coexist** (Equinor, community canonical, vendor-
  specific) — the client is agnostic.

[pr39]: https://github.com/equinor/osdu-csharp-client/pull/39

## What we know from `data-definitions`

Source: `osdu/data/data-definitions` (`Generated/` — the `allOf`-merged,
consumable form). Local clone at `/Users/shjellvi/dev/equinor/data-definitions`.

- **1,387 schema files** across **879 unique types**, 100% JSON Schema
  **draft-07**, single-rooted at `schema.osdu.opengroup.org`.
- Distribution by group:

  | Group | Files |
  |---|---:|
  | `reference-data/` | 683 |
  | `work-product-component/` | 283 |
  | `master-data/` | 208 |
  | `abstract/` | 141 |
  | `dataset/` | 60 |
  | rest (content, manifest, type, work-product, data-collection) | 12 |

- Refs are filesystem-relative; the closure stays tightly bounded.
- **No fragmentation across entity groups.** Every WPC type's transitive
  `$ref` closure stays within `{itself, abstract/}`. `abstract/` itself
  refs only into `abstract/` (verified: 0 cross-group leaks). OSDU encodes
  cross-entity references as **tagged strings** (e.g.
  `partition:master-data--Wellbore:abc:`), not JSON-Schema `$ref` — so
  generating one WPC type doesn't cascade into others.

## v0.1 scope: WellLog, all versions

**Type scope:** `WellLog` only.
**Version scope:** all six — 1.0.0, 1.1.0, 1.2.0, 1.3.0, 1.4.0, 1.5.0.

Including older versions is essentially free:

| | Files |
|---|---:|
| WellLog schema files (1.0.0 → 1.5.0) | 6 |
| Union of abstract files they reference | 29 |
| **Total v0.1 footprint** | **35** |

Of the 29 abstracts, **26 are shared across ≥ 2 WellLog versions**. Only
three are version-specific (`AbstractRemark.1.0.0` for WellLog 1.5,
`AbstractTechnicalAssurance.1.1.0` and `AbstractWPCGroupType.1.1.0` for
WellLog 1.3).

A few abstract *types* exist in multiple versions across the WellLog
1.0–1.5 range and so will coexist in the package:

```
AbstractWPCGroupType:         1.0.0, 1.1.0, 1.2.0
AbstractTechnicalAssurance:   1.0.0, 1.1.0, 1.2.0
AbstractContact:              1.0.0, 1.1.0
AbstractSpatialLocation:      1.0.0, 1.1.0
AbstractAnyCrsFeatureCollection: 1.0.0, 1.1.0
AbstractWorkProductComponent: 1.0.0, 1.1.0
```

Same coexistence pattern as `WellLog.V1_x_0` — one uniform rule covers both.

## Library shape

```
Osdu.Schemas
├── Abstract
│   ├── CommonResources
│   │   └── V1_0_0                 → class CommonResources
│   ├── WPCGroupType
│   │   ├── V1_0_0
│   │   ├── V1_1_0
│   │   └── V1_2_0                 → class WPCGroupType
│   ├── WorkProductComponent
│   │   ├── V1_0_0
│   │   └── V1_1_0
│   └── …                          (≈ 29 abstracts for v0.1)
└── WorkProductComponent
    └── WellLog
        ├── V1_0_0                 → class Data (+ Curve, VerticalMeasurement, …)
        ├── V1_1_0
        ├── V1_2_0
        ├── V1_3_0
        ├── V1_4_0
        └── V1_5_0                 → class Data
```

Uniform namespace convention everywhere: `Osdu.Schemas.<Group>.<Type>.V<major>_<minor>_<patch>`.

### Consumer experience

```csharp
using V14 = Osdu.Schemas.WorkProductComponent.WellLog.V1_4_0;
using V15 = Osdu.Schemas.WorkProductComponent.WellLog.V1_5_0;
using Equinor.OsduCsharpClient.Facade;
using Equinor.OsduCsharpClient.WellboreDdms.Models;

// Latest schema — full intellisense on Data, Curve, VerticalMeasurement, …
var data = new V15.Data
{
    Name             = "GR Log",
    WellboreID       = "partition:master-data--Wellbore:abc:",
    TopMeasuredDepth = 12345.6,
    Curves = [ new V15.Curve { Mnemonic = "GR", NumberOfColumns = 1 } ],
};

var record = new Record
{
    Kind  = "osdu:wks:work-product-component--WellLog:1.5.0",
    Acl   = ...,
    Legal = ...,
    Data  = data.ToUntypedNode(),    // existing bridge in osdu-csharp-client
};

await osdu.WellboreDdms.Ddms.V3.Welllogs.PostAsync([record]);

// Older deployment — same pattern, different namespace
record.Data = new V14.Data { /* ... */ }.ToUntypedNode();

// Reading back
V15.Data? wl = result.Data.Deserialize<V15.Data>();
```

## Generator

**Tool: [NJsonSchema][njs]** (`NJsonSchema.CodeGeneration.CSharp`).

[njs]: https://github.com/RicoSuter/NJsonSchema

Why NJsonSchema:

- Native draft-07; native `allOf` merge; native `$ref` resolution.
- Configurable namespace, type naming, `JsonExtensionData` support.
- Propagates JSON Schema `description` to XML doc comments → useful
  intellisense pop-ups.
- Supports `ExternalReferenceCode` so cross-file `$ref`s can resolve to
  pre-generated C# types instead of being inlined or duplicated.
- Well-trusted in the .NET ecosystem (used by NSwag).

### Generator design notes

1. **Generate the `data` subschema, not the whole record.** OSDU top-level
   schemas describe the full record (envelope + `data`). The client owns
   the envelope (`id`/`kind`/`acl`/`legal`/…). The schemas package emits a
   `Data` class representing the contents of `record.data`, so
   `record.Data = new V1_5_0.Data { … }.ToUntypedNode()` composes naturally.
2. **Generate abstracts once, share via cross-namespace refs.** Each
   abstract type → one canonical class under `Osdu.Schemas.Abstract.<X>.V<v>`;
   each WellLog version's `Data` references those classes via
   `ExternalReferenceCode`. Avoids duplicating ≈ 26 identical classes 6 times.
3. **`[JsonExtensionData] public Dictionary<string, JsonElement>? Additional { get; set; }`**
   on every generated class. OSDU schemas evolve; without this, a client
   built on 1.4.0 strips fields a 1.5.0 server might add. Non-negotiable.
4. **Property casing.** OSDU JSON uses PascalCase, which aligns with C# —
   `[JsonPropertyName(...)]` is only needed when the JSON name differs.
5. **Constraints to attributes.** `pattern → [RegularExpression]`,
   `required → [Required]`, `minLength`/`maxLength → [StringLength]`. Emit
   them; consumers opt into `DataAnnotations` validation if they want.
6. **Reference IDs stay strings for v0.1.** `CurveSampleTypeID:
   "{partition}:reference-data--CurveSampleType:float:"` → `string` with a
   `[RegularExpression]` annotation. A tagged-string wrapper type is a
   later refinement.
7. **Date / date-time / time stay as strings.** OSDU example payloads
   carry non-conformant variants — `+0000` (no colon) instead of `+00:00`
   or `Z`, full date-time values inside `format: date` fields, time-of-day
   with timezone offset like `11:13:15+02:00` — that the strict
   `System.Text.Json` `DateTimeOffset`/`DateOnly`/`TimeOnly` parsers
   reject. Settings:
   `DateType = DateTimeType = TimeType = "string"`. Consumers parse
   leniently if they need a typed value. Same pragmatic stance as
   `os-core-common`'s `Map<String, Object>`.

## Repository layout

```
osdu-csharp-schemas/
├── PLAN.md                       # this file
├── README.md
├── LICENSE
├── schemas/                      # pinned snapshot of data-definitions
│   └── 2024.10.31/
│       ├── abstract/             # only the files needed for v0.1
│       └── work-product-component/
│           └── WellLog.{1.0.0..1.5.0}.json
├── tools/
│   └── SchemaGen/                # dotnet console app driving NJsonSchema
│       ├── SchemaGen.csproj
│       ├── Program.cs
│       └── manifest.json         # (schema file → namespace) mapping
├── src/
│   └── Osdu.Schemas/
│       ├── Osdu.Schemas.csproj
│       └── Generated/            # gitignored — re-run SchemaGen
└── tests/
    └── Osdu.Schemas.Tests/
        ├── Osdu.Schemas.Tests.csproj
        └── WellLogRoundTripTests.cs
```

Same philosophy as `osdu-csharp-client`: **schemas pinned, generated code
not committed**. Contributors run `dotnet run --project tools/SchemaGen`
after clone. Snapshot bumps are explicit, reviewable PRs.

## Build pipeline

1. CI runs the generator from the pinned snapshot.
2. Builds the library + runs tests.
3. On tag (`v0.1.0`, `v0.2.0`, …), publishes the NuGet package.

Versioning: semver. Generated-API-affecting snapshot bumps → minor; purely
additive snapshot bumps (new fields with `[JsonExtensionData]` already
catching them) → patch.

## Tests

Minimum for v0.1:

1. **Round-trip per WellLog version.** For each of 1.0.0…1.5.0:
   instantiate `Data` with realistic content → `ToUntypedNode()` →
   serialize through Kiota's `JsonSerializationWriter` → reparse →
   `Deserialize<Data>()` → assert equal.
2. **Example fixture.** Load
   `data-definitions/Examples/.../WellLog*.json`, deserialize into the
   matching `Data` class, assert all expected properties read. Catches
   schema-vs-generator mismatches against real-world payloads.
3. **Forward-compat.** Parse a payload containing an unknown property →
   round-trip preserves it via `[JsonExtensionData]`.
4. **Compile-time check.** A small project that references both
   `Osdu.Schemas` (v0.1) and `Equinor.OsduCsharpClient` and uses
   `record.Data = new V1_5_0.Data { … }.ToUntypedNode();` — proves the
   integration is real.

## Phased roadmap

| Milestone | Scope | Files in closure | Purpose |
|---|---|---:|---|
| **v0.1** | `WellLog` × {1.0.0…1.5.0} | 35 | ✅ Done. End-to-end proof; the issue's actual target. |
| **v0.2** | Wellbore DDMS surface — every entity the WBDDMS `/ddms/v3/*` endpoints handle: 6 WPC (`WellLog`, `WellboreTrajectory`, `WellboreIntervalSet`, `WellboreMarkerSet`, `PPFGDataset`, `WellPressureTestRawMeasurement`) + 3 master-data (`Well`, `Wellbore`, `WellLogAcquisition`) = 43 schema versions | 103 | ✅ Done. Scope derived directly from the WBDDMS OpenAPI spec. |
| **v0.3** | All `work-product-component` (93 types) | ~148 (93 WPC + 55 shared abstracts) | Full WPC coverage. |
| (deferred) | `master-data`, `dataset`, `reference-data` | — | Separate axes; bring on when demand exists. |

Each milestone is additive — new namespaces only — so consumers on v0.1 are
never broken by later releases.

## Open decisions

1. **`Data` vs `<Type>Data` class name.** Plan uses `Data` (terse,
   namespace-disambiguated). `WellLogData` is alternative but more verbose
   in usage.
2. **Snapshot cadence.** Quarterly? Pinned to OSDU milestones? Decide
   alongside the first snapshot.
3. **Where to host.** GitHub under `equinor/`? Personal repo first while
   bootstrapping? Decide before publishing v0.1.0-alpha.
4. **Package boundary.** One `Osdu.Schemas` package for all groups (grows
   over time) vs. one per group (`Osdu.Schemas.WorkProductComponent`,
   `Osdu.Schemas.MasterData`, …). For v0.1 a single package is fine; revisit
   if assembly size becomes a concern.
5. **Reference-data strategy.** Defer until needed; almost certainly stays
   as strings + regex.

## Risks

- **`allOf`-with-inline-schemas in `data`.** WellLog's `data` is a merged
  composition of three abstract refs and two inline schemas. NJsonSchema
  handles this but property-name collisions across merged sources can
  produce ugly C# names. Verify on the first generation pass.
- **Constructs that don't translate cleanly.** `oneOf`/`anyOf` with
  discriminators, `if`/`then`/`else`, `dependentSchemas`. Spot-check during
  v0.1 generation; design fallbacks per construct as encountered.
- **Doc comment quality.** Depends on NJsonSchema's faithfulness in
  propagating `description`. Inspect output.
- **Snapshot drift.** A snapshot bump that changes the public API breaks
  consumers. Mitigated by explicit-bump policy + semver.

## Concrete next steps (one sitting)

1. `mkdir -p schemas/$(date +%F)/abstract`, copy the 29 needed abstract
   files + the 6 `WellLog.*.json` files from `data-definitions/Generated/`.
2. Scaffold `tools/SchemaGen` (dotnet console, references
   `NJsonSchema.CodeGeneration.CSharp`). First pass: generate WellLog 1.5.0
   only, write to `src/Osdu.Schemas/Generated/`. Inspect output, iterate
   on naming and namespacing.
3. Add `manifest.json` listing the 6 WellLog versions and their target
   namespaces. Generate all six.
4. Wire `ExternalReferenceCode` so cross-file `$ref`s into `abstract/`
   resolve to shared abstract classes.
5. Add the round-trip test against a real payload from
   `data-definitions/Examples/`.
6. Locally `dotnet pack` v0.1.0-alpha; reference it from a sandbox project
   alongside `Equinor.OsduCsharpClient`; write the WellLog ingestion code
   with full intellisense. **That is the goal demonstrated end-to-end.**
7. Push to a GitHub repo + CI once the shape feels right.
