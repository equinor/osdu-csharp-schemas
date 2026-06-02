using System.Text.Json;
using System.Text.Json.Nodes;
using NJsonSchema;
using NJsonSchema.CodeGeneration.CSharp;
using Osdu.Schemas.SchemaGen;

// Manifest-driven generator. For each entry: load the OSDU schema, extract
// the `data` subschema, flatten its allOf/$ref chain into a single
// self-contained object schema, and emit a `Data.cs` file under the
// configured namespace and output directory.
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

Console.WriteLine($"Snapshot: {manifest.Snapshot}");
Console.WriteLine($"Schemas:  {manifest.Schemas.Count}\n");

foreach (var entry in manifest.Schemas)
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

Console.WriteLine($"\nDone. Generated {manifest.Schemas.Count} schemas.");

return 0;

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

internal sealed record Manifest(string Snapshot, List<ManifestEntry> Schemas);
internal sealed record ManifestEntry(string File, string Namespace, string OutputDir);
