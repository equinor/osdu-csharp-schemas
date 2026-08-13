using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Xunit;
using V14 = Osdu.Schemas.WorkProductComponent.WellLog.V1_4_0;
using V15 = Osdu.Schemas.WorkProductComponent.WellLog.V1_5_0;

namespace Osdu.Schemas.Tests;

/// <summary>
/// Round-trip and typed-access coverage for <b>every</b> generated
/// <c>Data</c> class. The test matrix is derived from the same snapshot the
/// generator uses (<c>tools/SchemaGen/manifest.json</c> + the pinned
/// <c>schemas/&lt;snapshot&gt;/</c> tree), so adding an entity/version there
/// automatically extends coverage here — mirroring the osdu-python-models
/// suite.
///
/// Each case deserializes the canonical OSDU example payload into the matching
/// version's typed class and asserts that no JSON path is lost round-tripping
/// back out. Combined with the
/// <see cref="System.Text.Json.Serialization.JsonExtensionDataAttribute"/> on
/// every generated class (including nested ones), this is the forward-compat
/// guarantee for the library.
///
/// Example payloads live in the sibling <c>data-definitions</c> checkout at
/// <c>../data-definitions/Examples</c> (fetched in CI via a sparse clone). When
/// they are absent — or a specific version has no example — the corresponding
/// cases skip, exactly like the Python suite.
/// </summary>
public class RoundTripTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly Assembly SchemasAssembly = typeof(V15.Data).Assembly;

    private static readonly string RepoRoot = FindRepoRoot();

    // Canonical OSDU example payloads: sibling data-definitions checkout.
    private static readonly string ExamplesRoot =
        Path.Combine(RepoRoot, "..", "data-definitions", "Examples");

    /// <summary>
    /// Every (group, type, version) present in the pinned snapshot — the same
    /// discovery the generator performs. Emitted as theory data so each schema
    /// version is an individually-reported test case.
    /// </summary>
    public static TheoryData<string, string, string> AllTargets()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var (group, type, version) in DiscoverTargets())
        {
            data.Add(group, type, version);
        }
        return data;
    }

    /// <summary>
    /// Structural check that needs no example payload: every generated
    /// <c>Data</c> class exists and carries <c>[JsonExtensionData]</c> so
    /// unknown / forward fields round-trip (the C# equivalent of the Python
    /// suite's <c>extra='allow'</c> assertion).
    /// </summary>
    [Theory]
    [MemberData(nameof(AllTargets))]
    public void GeneratedType_HasExtensionData(string group, string type, string version)
    {
        var dataType = ResolveDataType(group, type, version);

        var hasExtensionData = dataType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Any(p => p.GetCustomAttribute<JsonExtensionDataAttribute>() is not null);

        Assert.True(
            hasExtensionData,
            $"{dataType.FullName} is missing a [JsonExtensionData] property.");
    }

    [Theory]
    [MemberData(nameof(AllTargets))]
    public void OfficialExample_PreservesAllFields(string group, string type, string version)
    {
        var original = LoadExampleDataOrSkip(group, type, version);
        var dataType = ResolveDataType(group, type, version);

        var deserialized = JsonSerializer.Deserialize(original.ToJsonString(), dataType, JsonOpts);
        Assert.NotNull(deserialized);

        var roundTripped = JsonNode.Parse(
            JsonSerializer.Serialize(deserialized, dataType, JsonOpts))!;

        var missing = CollectJsonPaths(original).Except(CollectJsonPaths(roundTripped)).ToList();
        Assert.True(
            missing.Count == 0,
            $"Round-trip dropped {missing.Count} path(s) in {type} {version} via {dataType.FullName}:\n  " +
            string.Join("\n  ", missing.Take(10)));
    }

    [Theory]
    [MemberData(nameof(AllTargets))]
    public void UnknownField_RoundTrips(string group, string type, string version)
    {
        var original = LoadExampleDataOrSkip(group, type, version).AsObject();
        original["SomeFutureFieldNotInSchema"] = new JsonObject { ["x"] = 1 };

        var dataType = ResolveDataType(group, type, version);
        var deserialized = JsonSerializer.Deserialize(original.ToJsonString(), dataType, JsonOpts);
        Assert.NotNull(deserialized);

        var roundTripped = JsonNode.Parse(
            JsonSerializer.Serialize(deserialized, dataType, JsonOpts))!.AsObject();

        var future = roundTripped["SomeFutureFieldNotInSchema"];
        Assert.NotNull(future);
        Assert.Equal(1, (int?)future!["x"]);
    }

    [Fact]
    public void Author_WellLog_V1_5_0_FromCode()
    {
        // Demonstrates the IntelliSense use case: instantiating a payload
        // from typed code rather than constructing JSON by hand.
        var data = new V15.Data
        {
            Name = "GR Log",
            WellboreID = "partition:master-data--Wellbore:abc:",
            TopMeasuredDepth = 12345.6,
            BottomMeasuredDepth = 13856.2,
            IsRegular = true,
        };

        var json = JsonNode.Parse(JsonSerializer.Serialize(data, JsonOpts))!.AsObject();

        Assert.Equal("GR Log", (string?)json["Name"]);
        Assert.Equal(12345.6, (double?)json["TopMeasuredDepth"]);
        Assert.True((bool?)json["IsRegular"]);
    }

    [Fact]
    public void Versions_CoexistSideBySide()
    {
        // Both V1_4_0 and V1_5_0 are usable in the same file.
        var v14 = new V14.Data { Name = "v1.4 log" };
        var v15 = new V15.Data { Name = "v1.5 log" };

        Assert.NotEqual(v14.GetType(), v15.GetType());
        Assert.Equal("v1.4 log", v14.Name);
        Assert.Equal("v1.5 log", v15.Name);
    }

    // --- discovery -------------------------------------------------------

    /// <summary>
    /// Discover every (group, type, version) in the pinned snapshot, reading
    /// the snapshot name and scoped groups from the generator's manifest so the
    /// two never drift.
    /// </summary>
    private static IEnumerable<(string group, string type, string version)> DiscoverTargets()
    {
        var manifestPath = Path.Combine(RepoRoot, "tools", "SchemaGen", "manifest.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var snapshot = doc.RootElement.GetProperty("snapshot").GetString()!;
        var groups = doc.RootElement.GetProperty("groups")
            .EnumerateArray().Select(e => e.GetString()!).ToList();

        var schemaRoot = Path.Combine(RepoRoot, "schemas", snapshot);
        foreach (var group in groups)
        {
            var groupDir = Path.Combine(schemaRoot, group);
            if (!Directory.Exists(groupDir))
                continue;

            foreach (var path in Directory.EnumerateFiles(groupDir, "*.json")
                         .OrderBy(p => p, StringComparer.Ordinal))
            {
                var (type, version) = ParseNameVersion(Path.GetFileName(path));
                yield return (group, type, version);
            }
        }
    }

    // Schema files are named `<Type>.<major>.<minor>.<patch>.json`; the type
    // may itself contain dots (dataset `File.Generic`), so the version is the
    // last three dot-separated segments and everything before is the type.
    private static (string type, string version) ParseNameVersion(string fileName)
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

    /// <summary>
    /// Maps a (group, type, version) target to the matching generated
    /// <c>Data</c> type, applying the same naming the generator uses: PascalCase
    /// group segment and dotted dataset type names concatenated
    /// (<c>File.Generic</c> → <c>FileGeneric</c>), e.g.
    /// <c>Osdu.Schemas.Dataset.FileGeneric.V1_1_0.Data</c>.
    /// </summary>
    private static Type ResolveDataType(string group, string type, string version)
    {
        var groupNs = GroupToPascal(group);
        var typeToken = string.Concat(type.Split('.', StringSplitOptions.RemoveEmptyEntries));
        var versionToken = "V" + version.Replace('.', '_');
        var fullName = $"Osdu.Schemas.{groupNs}.{typeToken}.{versionToken}.Data";

        return SchemasAssembly.GetType(fullName, throwOnError: false)
            ?? throw new ArgumentException(
                $"No generated type for {group}/{type}.{version} (expected {fullName}).");
    }

    private static string GroupToPascal(string group) => group switch
    {
        "work-product-component" => "WorkProductComponent",
        "master-data" => "MasterData",
        "dataset" => "Dataset",
        _ => string.Concat(group.Split('-').Select(s =>
            s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..])),
    };

    /// <summary>
    /// Loads the <c>data</c> block of the canonical example for a target, or
    /// skips the test if the sibling checkout / example file is absent.
    /// </summary>
    private static JsonNode LoadExampleDataOrSkip(string group, string type, string version)
    {
        var example = Path.Combine(ExamplesRoot, group, $"{type}.{version}.json");
        Assert.SkipUnless(
            File.Exists(example),
            $"No example payload: {group}/{type}.{version}.json");

        var record = JsonNode.Parse(File.ReadAllText(example))!.AsObject();
        var dataNode = record["data"];
        Assert.SkipWhen(dataNode is null, $"Example has no 'data' block: {example}");
        return dataNode!.DeepClone();
    }

    /// <summary>
    /// Returns the set of leaf JSON paths reachable in <paramref name="node"/>.
    /// </summary>
    private static IEnumerable<string> CollectJsonPaths(JsonNode node)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        Walk(node, "$");
        return paths;

        void Walk(JsonNode? n, string path)
        {
            switch (n)
            {
                case JsonObject obj:
                    foreach (var (key, value) in obj) Walk(value, $"{path}.{key}");
                    break;
                case JsonArray arr:
                    for (var i = 0; i < arr.Count; i++) Walk(arr[i], $"{path}[{i}]");
                    break;
                default:
                    paths.Add(path);
                    break;
            }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Osdu.Schemas.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate Osdu.Schemas.slnx in ancestry.");
    }
}
