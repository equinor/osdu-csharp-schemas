using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;
using V14 = Osdu.Schemas.WorkProductComponent.WellLog.V1_4_0;
using V15 = Osdu.Schemas.WorkProductComponent.WellLog.V1_5_0;

namespace Osdu.Schemas.Tests;

/// <summary>
/// Reflection-driven round-trip coverage for every generated <c>Data</c>
/// class. For each official example payload in <c>Fixtures/</c> we deserialize
/// it into the matching version's typed class and assert that no JSON path
/// is lost when round-tripping back out. Combined with the
/// <see cref="System.Text.Json.Serialization.JsonExtensionDataAttribute"/> on
/// every generated class, this is the forward-compat guarantee for the library.
/// </summary>
public class RoundTripTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly Assembly SchemasAssembly = typeof(V15.Data).Assembly;

    private static readonly string FixturesDir = Path.Combine(
        Path.GetDirectoryName(typeof(RoundTripTests).Assembly.Location)!, "Fixtures");

    public static TheoryData<string> AllFixtures()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.EnumerateFiles(FixturesDir, "*.json").OrderBy(x => x))
        {
            data.Add(Path.GetFileName(path));
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void OfficialExample_PreservesAllFields(string fixture)
    {
        var dataType = ResolveDataType(fixture);
        var original = LoadDataNode(fixture);

        var deserialized = JsonSerializer.Deserialize(original.ToJsonString(), dataType, JsonOpts);
        Assert.NotNull(deserialized);

        var roundTripped = JsonNode.Parse(
            JsonSerializer.Serialize(deserialized, dataType, JsonOpts))!;

        var missing = CollectJsonPaths(original).Except(CollectJsonPaths(roundTripped)).ToList();
        Assert.True(
            missing.Count == 0,
            $"Round-trip dropped {missing.Count} path(s) in {fixture} via {dataType.FullName}:\n  " +
            string.Join("\n  ", missing.Take(10)));
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

    /// <summary>
    /// Resolves a fixture file name like <c>WellLog.1.5.0.json</c> to the
    /// matching generated type <c>Osdu.Schemas.WorkProductComponent.WellLog.V1_5_0.Data</c>.
    /// </summary>
    private static Type ResolveDataType(string fixtureFileName)
    {
        var match = Regex.Match(fixtureFileName, @"^(?<type>\w+)\.(?<maj>\d+)\.(?<min>\d+)\.(?<pat>\d+)\.json$");
        if (!match.Success)
        {
            throw new ArgumentException($"Fixture file name doesn't match expected pattern: {fixtureFileName}");
        }
        var fullName =
            $"Osdu.Schemas.WorkProductComponent.{match.Groups["type"].Value}." +
            $"V{match.Groups["maj"].Value}_{match.Groups["min"].Value}_{match.Groups["pat"].Value}.Data";

        return SchemasAssembly.GetType(fullName, throwOnError: false)
            ?? throw new ArgumentException(
                $"No generated type for fixture {fixtureFileName} (expected {fullName}).");
    }

    private static JsonNode LoadDataNode(string fixtureFileName)
    {
        var path = Path.Combine(FixturesDir, fixtureFileName);
        var record = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        return record["data"]!.DeepClone();
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
}
