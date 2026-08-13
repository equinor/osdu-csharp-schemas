using System.Text.Json;
using System.Text.Json.Nodes;
using NJsonSchema;
using NJsonSchema.CodeGeneration.CSharp;
using Osdu.Schemas.SchemaGen;

// Snapshot-driven generator. The manifest pins the snapshot directory and the
// schema groups in scope; every entity schema in those groups (all versions) is
// discovered automatically, so a snapshot bump or adding a group needs no other
// change. For each discovered schema: load the OSDU schema, extract the `data`
// subschema, flatten its allOf/$ref chain into a single self-contained object
// schema, and emit a `Data.cs` file under the derived namespace and output dir.
//
// allOf flattening is done in JSON before NJsonSchema sees the schema because
// NJsonSchema 11.5 emits a class hierarchy for allOf chains with names that
// don't survive contact with OSDU titles ("OSDU Common Resources" → "Json").
// Shared abstracts will replace this (PLAN.md step 4) once cross-namespace
// refs are wired up.

var repoRoot = FindRepoRoot();
var manifestPath = Path.Combine(repoRoot, "tools", "SchemaGen", "manifest.json");
var manifest = JsonSerializer.Deserialize<Manifest>(
    await File.ReadAllTextAsync(manifestPath),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
    ?? throw new InvalidOperationException("Failed to deserialize manifest.");

var schemaRoot = Path.Combine(repoRoot, "schemas", manifest.Snapshot);
var generatedRoot = Path.Combine(repoRoot, "src", "Osdu.Schemas", "Generated");

var entries = DiscoverEntries(schemaRoot, manifest.Groups);

Console.WriteLine($"Snapshot: {manifest.Snapshot}");
Console.WriteLine($"Schemas:  {entries.Count}\n");

foreach (var entry in entries)
{
    var schemaFile = Path.Combine(schemaRoot, entry.File);
    var outputDir = Path.Combine(generatedRoot, entry.OutputDir);

    Console.WriteLine($"  {entry.File}");

    var rootJson = JsonNode.Parse(await File.ReadAllTextAsync(schemaFile))!.AsObject();
    var dataNode = rootJson["properties"]?["data"]?.AsObject()
        ?? throw new InvalidOperationException($"{entry.File} has no 'data' property.");

    var baseDir = Path.GetDirectoryName(schemaFile)!;
    var flattened = SchemaFlattener.Flatten(dataNode, baseDir);
    flattened["type"] = "object";

    // Strip `enum` / `const` constraints so constrained string fields generate
    // as plain `string` instead of strict C# enums. This library types `data`
    // for *lossless* round-tripping, not semantic validation: OSDU payloads
    // carry enum values outside the published set (and NJsonSchema sanitises
    // enum member names, which loses the original spelling on the way back
    // out). Same pragmatic choice already made for date / date-time / time.
    StripValueConstraints(flattened);

    var dataSchema = await JsonSchema.FromJsonAsync(flattened.ToJsonString());

    var settings = new CSharpGeneratorSettings
    {
        Namespace = entry.Namespace,
        ClassStyle = CSharpClassStyle.Poco,
        GenerateNullableReferenceTypes = true,
        GenerateOptionalPropertiesAsNullable = true,
        JsonLibrary = CSharpJsonLibrary.SystemTextJson,
        GenerateDataAnnotations = true,
        RequiredPropertiesMustBeDefined = false,
        // All date / date-time / time formats stay as raw strings. OSDU
        // example payloads carry non-conformant variants — `+0000` without a
        // colon, date-time values in `format: date` fields, time-of-day with
        // offset like `11:13:15+02:00` — that the strict System.Text.Json
        // parsers reject. Keeping these as strings mirrors os-core-common's
        // pragmatic approach and leaves any parsing to the consumer.
        DateType = "string",
        DateTimeType = "string",
        TimeType = "string",
    };

    var generator = new CSharpGenerator(dataSchema, settings);
    var code = generator.GenerateFile("Data");

    Directory.CreateDirectory(outputDir);
    var outputFile = Path.Combine(outputDir, "Data.cs");
    await File.WriteAllTextAsync(outputFile, code);

    Console.WriteLine(
        $"    -> {Path.GetRelativePath(repoRoot, outputFile)} ({code.Length:N0} chars)");
}

Console.WriteLine($"\nDone. Generated {entries.Count} schemas.");

return 0;

// Discover every entity schema (all versions) in the scoped groups. Schema
// files are named `<Type>.<major>.<minor>.<patch>.json`; the type may itself
// contain dots (e.g. dataset `File.Generic`), so the version is the last three
// dot-separated numeric segments and everything before is the type name.
static List<ManifestEntry> DiscoverEntries(string schemaRoot, IReadOnlyList<string> groups)
{
    var entries = new List<ManifestEntry>();
    foreach (var group in groups)
    {
        var groupDir = Path.Combine(schemaRoot, group);
        if (!Directory.Exists(groupDir))
            throw new DirectoryNotFoundException($"Schema group not found: {groupDir}");

        var groupNs = GroupToPascal(group);
        foreach (var path in Directory.EnumerateFiles(groupDir, "*.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            var (typeName, version) = ParseNameVersion(Path.GetFileName(path));
            var typeToken = ConcatSegments(typeName);
            var versionToken = "V" + version.Replace('.', '_');

            entries.Add(new ManifestEntry(
                File: $"{group}/{Path.GetFileName(path)}",
                Namespace: $"Osdu.Schemas.{groupNs}.{typeToken}.{versionToken}",
                OutputDir: Path.Combine(groupNs, typeToken, versionToken)));
        }
    }
    // Stable, readable order: by output dir.
    return entries.OrderBy(e => e.OutputDir, StringComparer.Ordinal).ToList();
}

static (string type, string version) ParseNameVersion(string fileName)
{
    var stem = fileName.EndsWith(".json", StringComparison.Ordinal)
        ? fileName[..^5]
        : fileName;
    var parts = stem.Split('.');
    if (parts.Length < 4)
        throw new FormatException($"Unexpected schema file name: {fileName}");
    var version = string.Join('.', parts[^3..]);
    var type = string.Join('.', parts[..^3]);
    return (type, version);
}

// Dotted type names (dataset `File.Generic`, `File.Image.JPEG`) concatenate
// into a single PascalCase identifier: `File.Generic` → `FileGeneric`.
static string ConcatSegments(string typeName) =>
    string.Concat(typeName.Split('.', StringSplitOptions.RemoveEmptyEntries));

static string GroupToPascal(string group) => group switch
{
    "work-product-component" => "WorkProductComponent",
    "master-data" => "MasterData",
    "dataset" => "Dataset",
    _ => string.Concat(group.Split('-').Select(s =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..])),
};

// Recursively remove `enum` and `const` keywords so constrained values generate
// as their plain underlying type (e.g. `string`) rather than strict C# enums.
static void StripValueConstraints(JsonNode? node)
{
    switch (node)
    {
        case JsonObject obj:
            obj.Remove("enum");
            obj.Remove("const");
            foreach (var (_, value) in obj.ToList())
            {
                StripValueConstraints(value);
            }
            break;
        case JsonArray arr:
            foreach (var item in arr)
            {
                StripValueConstraints(item);
            }
            break;
    }
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Osdu.Schemas.slnx")))
    {
        dir = dir.Parent;
    }
    return dir?.FullName
        ?? throw new InvalidOperationException("Could not locate Osdu.Schemas.slnx in ancestry.");
}

internal sealed record Manifest(string Snapshot, List<string> Groups);
internal sealed record ManifestEntry(string File, string Namespace, string OutputDir);
