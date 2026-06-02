using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using V14 = Osdu.Schemas.WorkProductComponent.WellLog.V1_4_0;
using V15 = Osdu.Schemas.WorkProductComponent.WellLog.V1_5_0;

namespace Osdu.Schemas.Tests;

/// <summary>
/// End-to-end checks that the generated WellLog `Data` classes (a) deserialize
/// the official OSDU example payloads without loss and (b) round-trip back to
/// the same JSON shape.
/// </summary>
public class WellLogRoundTripTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static string LoadFixture(string fileName)
    {
        var dir = Path.GetDirectoryName(typeof(WellLogRoundTripTests).Assembly.Location)!;
        return File.ReadAllText(Path.Combine(dir, "Fixtures", fileName));
    }

    private static JsonNode DataNode(string fixtureFileName)
    {
        var record = JsonNode.Parse(LoadFixture(fixtureFileName))!.AsObject();
        return record["data"]!.DeepClone();
    }

    [Fact]
    public void WellLog_V1_5_0_ExampleDeserialises()
    {
        var dataJson = DataNode("WellLog.1.5.0.json").ToJsonString();

        var data = JsonSerializer.Deserialize<V15.Data>(dataJson, JsonOpts);

        Assert.NotNull(data);
        Assert.NotNull(data!.Name);
        Assert.NotEmpty(data.Curves!);
        Assert.NotNull(data.WellboreID);
    }

    [Theory]
    [InlineData("WellLog.1.0.0.json")]
    [InlineData("WellLog.1.1.0.json")]
    [InlineData("WellLog.1.2.0.json")]
    [InlineData("WellLog.1.3.0.json")]
    [InlineData("WellLog.1.4.0.json")]
    [InlineData("WellLog.1.5.0.json")]
    public void OfficialExample_PreservesAllFields(string fixture)
    {
        // Round-trip the official example through the matching version's
        // typed class and assert that no field is lost. We don't compare
        // byte-for-byte because the .NET JSON serializer normalizes some
        // values (e.g. DateTimeOffset emits `+00:00` instead of `Z`), but
        // the *set of paths* and the *semantic content* must be preserved.
        var original = DataNode(fixture);
        var serializerType = fixture switch
        {
            "WellLog.1.0.0.json" => typeof(Osdu.Schemas.WorkProductComponent.WellLog.V1_0_0.Data),
            "WellLog.1.1.0.json" => typeof(Osdu.Schemas.WorkProductComponent.WellLog.V1_1_0.Data),
            "WellLog.1.2.0.json" => typeof(Osdu.Schemas.WorkProductComponent.WellLog.V1_2_0.Data),
            "WellLog.1.3.0.json" => typeof(Osdu.Schemas.WorkProductComponent.WellLog.V1_3_0.Data),
            "WellLog.1.4.0.json" => typeof(V14.Data),
            "WellLog.1.5.0.json" => typeof(V15.Data),
            _ => throw new ArgumentException($"Unknown fixture: {fixture}"),
        };

        var data = JsonSerializer.Deserialize(original.ToJsonString(), serializerType, JsonOpts);
        Assert.NotNull(data);

        var roundTripped = JsonNode.Parse(JsonSerializer.Serialize(data, serializerType, JsonOpts))!;

        var missing = CollectJsonPaths(original).Except(CollectJsonPaths(roundTripped)).ToList();
        Assert.True(
            missing.Count == 0,
            $"Round-trip dropped {missing.Count} path(s) in {fixture}:\n  " +
            string.Join("\n  ", missing.Take(10)));
    }

    /// <summary>
    /// Returns the set of leaf JSON paths reachable in <paramref name="node"/>.
    /// Used to assert that a round-trip didn't drop any data.
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

    [Fact]
    public void Author_WellLog_V1_5_0_FromCode()
    {
        // Demonstrates the IntelliSense use case: instantiating a WellLog
        // payload from typed code rather than constructing JSON by hand.
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
}
