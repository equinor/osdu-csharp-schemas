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

## Current scope

**All `work-product-component`, `master-data` and `dataset` entity types**
in the pinned OSDU snapshot, with **every published version of each** in
side-by-side namespaces:

| Group | Types | Versions |
|---|---|---|
| `work-product-component` | 97 | 291 |
| `master-data` | 79 | 222 |
| `dataset` | 28 | 60 |

**204 entity types across 573 schema versions** + 145 abstract building
blocks = 718 input files. The generator is data-driven: `tools/SchemaGen/manifest.json`
pins the snapshot and lists the scoped groups, and every type and version is
discovered from the snapshot automatically, so a snapshot bump (or adding a
group) needs no other code change.

Namespaces: `Osdu.Schemas.WorkProductComponent.<Type>.V<x>_<y>_<z>`,
`Osdu.Schemas.MasterData.<Type>.V<x>_<y>_<z>` and
`Osdu.Schemas.Dataset.<Type>.V<x>_<y>_<z>`. Dotted dataset type names are
concatenated into a single identifier (`File.Generic` → `FileGeneric`).
Generator:
[NJsonSchema][njs] (draft-07). Output: one `Data` class per version +
nested types, all with `[JsonExtensionData]` so unknown fields round-trip.
Date / time / date-time fields are emitted as `string` (OSDU example
payloads carry non-conformant variants that the strict `System.Text.Json`
parsers reject) — same pragmatic choice `os-core-common` makes with
`Map<String, Object>`. For the same reason `enum` / `const` constraints are
stripped so constrained fields stay plain `string`: this library types `data`
for *lossless* round-tripping, not semantic validation, and OSDU payloads
carry values outside the published enum sets.

[njs]: https://github.com/RicoSuter/NJsonSchema

## Repo layout

```
osdu-csharp-schemas/
├── README.md
├── schemas/M27.0/                 # pinned snapshot of data-definitions Generated/
├── tools/SchemaGen/                # dotnet console: extracts `data`, flattens, runs NJsonSchema
├── src/Osdu.Schemas/               # the library — generated code (gitignored)
├── tests/Osdu.Schemas.Tests/       # round-trip coverage for every generated version
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

The round-trip tests validate every generated version against the canonical
OSDU example payloads in a sibling `data-definitions` checkout, expected at
`../data-definitions/Examples`. When that checkout is absent, the
example-based cases skip and only the structural checks run. To fetch just the
examples:

```sh
git clone --depth 1 --filter=blob:none --sparse \
  https://community.opengroup.org/osdu/data/data-definitions.git ../data-definitions
git -C ../data-definitions sparse-checkout set Examples
```

## Updating the snapshot

The `schemas/<snapshot>/` directory is a pinned copy of the OSDU `Generated/`
schemas (the current one, `M27.0`, is data-definitions tag `v0.30.0` — the
M27 milestone publication). Bumping it is an explicit, reviewable PR:

1. Copy the new `work-product-component`, `master-data`, `dataset` and
   `abstract` folders into a new `schemas/<new-snapshot>/` directory.
2. Update the `snapshot` field in `tools/SchemaGen/manifest.json`.
3. Run the generator, run tests, observe any breakage.

New types and versions are picked up automatically — the generator discovers
every schema in the scoped `groups`, so no per-entity manifest edits are needed.

## Contributing

Contributions are welcome — see [`CONTRIBUTING.md`](CONTRIBUTING.md) for
development setup, the pull-request process, and commit conventions.

## Security

To report a security vulnerability, follow the process in
[`SECURITY.md`](SECURITY.md). Do not open a public issue.

## License

Licensed under the [Apache License 2.0](LICENSE).
