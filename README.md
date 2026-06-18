# Equinor.Osdu.Schemas

Typed C# domain models generated from the [OSDU schema registry][schemas].
Composes with [`Equinor.OsduCsharpClient`][client] through its `UntypedNode`
JSON bridge — no changes to the client required.

[client]: https://github.com/equinor/osdu-csharp-client
[schemas]: https://community.opengroup.org/osdu/data/data-definitions

## Why

`osdu-csharp-client` keeps `Record.data` as free-form `UntypedNode`, matching
the canonical Java `os-core-common` (`Map<String, Object>`). That's the right
call for the client. But consumers who want **intellisense on a specific
OSDU `kind` and version** still deserve it. This library provides those
typed POCOs as an opt-in package, scoped to the kinds and versions a team
actually uses.

```csharp
using V15 = Osdu.Schemas.WorkProductComponent.WellLog.V1_5_0;
using V14 = Osdu.Schemas.WorkProductComponent.WellLog.V1_4_0;
using Equinor.OsduCsharpClient.Facade;             // ToUntypedNode()

var data = new V15.Data
{
    Name             = "GR Log",
    WellboreID       = "partition:master-data--Wellbore:abc:",
    TopMeasuredDepth = 12345.6,
    Curves           = [ new V15.Curves { Mnemonic = "GR", NumberOfColumns = 1 } ],
};

var wellLog = new Record
{
    Kind  = "osdu:wks:work-product-component--WellLog:1.5.0",
    Acl   = ...,
    Legal = ...,
    Data  = data.ToUntypedNode(),     // ← typed POCO bridges into the record envelope
};

await osdu.WellboreDdms.Ddms.V3.Welllogs.PostAsync([wellLog]);
```

## Current scope (v0.2 — Wellbore DDMS surface)

Every OSDU entity that the Wellbore DDMS API handles, derived directly
from the WBDDMS OpenAPI spec (`/ddms/v3/*` endpoints), with **every
published version of each** in side-by-side namespaces:

| Group | Type | Versions |
|---|---|---|
| `work-product-component` | `WellLog` | 1.0.0, 1.1.0, 1.2.0, 1.3.0, 1.4.0, 1.5.0 |
| `work-product-component` | `WellboreTrajectory` | 1.0.0 → 1.4.0 |
| `work-product-component` | `WellboreIntervalSet` | 1.0.0 → 1.3.1 |
| `work-product-component` | `WellboreMarkerSet` | 1.0.0 → 1.5.1 |
| `work-product-component` | `PPFGDataset` | 1.0.0 → 1.2.0 |
| `work-product-component` | `WellPressureTestRawMeasurement` | 1.0.0, 1.1.0 |
| `master-data` | `Well` | 1.0.0 → 1.2.0 |
| `master-data` | `Wellbore` | 1.0.0 → 1.4.0 |
| `master-data` | `WellLogAcquisition` | 1.0.0 |

**43 schema versions** + 60 abstract building blocks = 103 input files.
Namespaces: `Osdu.Schemas.WorkProductComponent.<Type>.V<x>_<y>_<z>` and
`Osdu.Schemas.MasterData.<Type>.V<x>_<y>_<z>`. Generator:
[NJsonSchema][njs] (draft-07). Output: one `Data` class per version +
nested types, all with `[JsonExtensionData]` so unknown fields round-trip.
Date / time / date-time fields are emitted as `string` (OSDU example
payloads carry non-conformant variants that the strict `System.Text.Json`
parsers reject) — same pragmatic choice `os-core-common` makes with
`Map<String, Object>`.

[njs]: https://github.com/RicoSuter/NJsonSchema

## Repo layout

```
osdu-csharp-schemas/
├── README.md
├── schemas/2026.05.22/             # pinned snapshot of data-definitions Generated/
├── tools/SchemaGen/                # dotnet console: extracts `data`, flattens, runs NJsonSchema
├── src/Osdu.Schemas/               # the library — generated code (gitignored)
├── tests/Osdu.Schemas.Tests/       # round-trip + integration tests
└── samples/IngestWellLog/          # end-to-end: typed POCO + WBDDMS Record envelope
```

## Build & generate

```sh
# Regenerate the C# from the pinned snapshot
dotnet run --project tools/SchemaGen

# Build everything
dotnet build Osdu.Schemas.slnx

# Run tests
dotnet test

# Run the end-to-end sandbox (no network — just serializes the request body)
dotnet run --project samples/IngestWellLog
```

Generated code lives under `src/Osdu.Schemas/Generated/` and is gitignored
— regenerable from the pinned snapshot, never hand-edited.

## Updating the snapshot

The `schemas/<date>/` directory is a pinned copy of the OSDU `Generated/`
schemas. Bumping it is an explicit, reviewable PR:

1. Copy the new files into a new `schemas/<new-date>/` directory.
2. Update `tools/SchemaGen/manifest.json` to point at the new snapshot.
3. Run the generator, run tests, observe any breakage.

## Contributing

Contributions are welcome — see [`CONTRIBUTING.md`](CONTRIBUTING.md) for
development setup, the pull-request process, and commit conventions.

## Security

To report a security vulnerability, follow the process in
[`SECURITY.md`](SECURITY.md). Do not open a public issue.

## License

Licensed under the [Apache License 2.0](LICENSE).
