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

## v0.1 scope

| | |
|---|---|
| **Types** | `WellLog` only |
| **Versions** | 1.0.0, 1.1.0, 1.2.0, 1.3.0, 1.4.0, 1.5.0 |
| **Input** | 6 WellLog schema files + 29 abstract files (pinned snapshot) |
| **Generator** | [NJsonSchema][njs] (draft-07) |
| **Output** | One `Data` class per version + nested types (Curves, VerticalMeasurement, …) |
| **Forward-compat** | `[JsonExtensionData]` on every class — unknown fields round-trip |

See [PLAN.md](PLAN.md) for the full design rationale, future scopes, and
open decisions.

[njs]: https://github.com/RicoSuter/NJsonSchema

## Repo layout

```
osdu-csharp-schemas/
├── PLAN.md
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
