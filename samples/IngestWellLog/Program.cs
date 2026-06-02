using System.Text.Json;
using Equinor.OsduCsharpClient.Facade;
using Equinor.OsduCsharpClient.WellboreDdms.Models;
using V15 = Osdu.Schemas.WorkProductComponent.WellLog.V1_5_0;
using V14 = Osdu.Schemas.WorkProductComponent.WellLog.V1_4_0;

// ---------------------------------------------------------------------------
// Demonstrates the v0.1 outcome end-to-end:
//
//   * Osdu.Schemas provides typed POCOs (`V1_5_0.Data`, `V1_5_0.Curve`, …)
//     with full intellisense.
//   * Equinor.OsduCsharpClient provides the WBDDMS Record envelope and the
//     UntypedNode JSON bridge.
//   * The two compose through `wellLog.Data = typed.ToUntypedNode()`.
//
// This program does NOT call OSDU — it just builds the request object and
// serializes it, so it can run without credentials.
// ---------------------------------------------------------------------------

// 1. Construct the WellLog data with the typed v1.5.0 POCO.
var data = new V15.Data
{
    Name = "GR Log",
    WellboreID = "partition:master-data--Wellbore:abc:",
    TopMeasuredDepth = 12345.6,
    BottomMeasuredDepth = 13856.2,
    IsRegular = true,
    Curves = new List<V15.Curves>
    {
        new()
        {
            CurveID = "GR_ID",
            Mnemonic = "GR",
            CurveDescription = "Gamma Ray",
            NumberOfColumns = 1,
        },
    },
};

// 2. Wrap it in the WBDDMS wellLog envelope from osdu-csharp-client. The
//    typed POCO is bridged into the free-form `Data` UntypedNode field.
var wellLog = new Record
{
    Kind = "osdu:wks:work-product-component--WellLog:1.5.0",
    Acl = new StorageAcl
    {
        Owners = ["data.default.owners@partition.example.com"],
        Viewers = ["data.default.viewers@partition.example.com"],
    },
    Legal = new Legal
    {
        Legaltags = new Legal.Legal_legaltags { String = ["partition-public-usa-dataset-1"] },
        OtherRelevantDataCountries =
            new Legal.Legal_otherRelevantDataCountries { String = ["US"] },
    },
    Data = data.ToUntypedNode(),    // ← the integration point
};

// 3. (Pretend) POST. We just serialize so the program is runnable without
//    OSDU credentials.
using var writer = new Microsoft.Kiota.Serialization.Json.JsonSerializationWriter();
writer.WriteObjectValue(string.Empty, wellLog);
using var stream = writer.GetSerializedContent();
using var reader = new StreamReader(stream);
var requestBody = reader.ReadToEnd();

Console.WriteLine("Constructed WellLog v1.5.0 request body:");
Console.WriteLine(JsonSerializer.Serialize(
    JsonSerializer.Deserialize<object>(requestBody),
    new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine();

// 4. Demonstrate version coexistence: an older deployment using v1.4.0.
var v14data = new V14.Data { Name = "older GR Log" };
wellLog.Data = v14data.ToUntypedNode();
wellLog.Kind = "osdu:wks:work-product-component--WellLog:1.4.0";
Console.WriteLine($"Switched to {wellLog.Kind} with V14.Data: {v14data.Name}");
