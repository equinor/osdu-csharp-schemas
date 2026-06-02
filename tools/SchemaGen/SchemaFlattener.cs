using System.Text.Json.Nodes;

namespace Osdu.Schemas.SchemaGen;

/// <summary>
/// Resolves <c>$ref</c> and merges <c>allOf</c> chains into a single inline
/// JSON Schema object, so NJsonSchema sees one flat schema per generated
/// class instead of an OSDU inheritance hierarchy. Refs are followed
/// relative to the file they appear in.
/// </summary>
/// <remarks>
/// This is a deliberately narrow operation for v0.1: it produces the
/// equivalent of "give me one self-contained class for <c>WellLog.Data</c>"
/// without dragging in NJsonSchema's allOf-as-inheritance behaviour, which
/// would emit confusingly-named base classes (the abstracts get titles like
/// "OSDU Common Resources" that NJsonSchema sanitises down to "Json").
/// Shared-abstract generation (PLAN.md step 4) will replace this with proper
/// cross-namespace refs.
/// </remarks>
public static class SchemaFlattener
{
    public static JsonObject Flatten(JsonObject schema, string baseDirectory)
    {
        var result = new JsonObject();

        foreach (var (key, value) in schema)
        {
            if (key is "$ref" or "allOf") continue;
            result[key] = value?.DeepClone();
        }

        if (schema["$ref"]?.GetValue<string>() is { } refPath)
        {
            var (refContent, refDir) = LoadRef(refPath, baseDirectory);
            var refFlat = Flatten(refContent, refDir);
            MergeInto(result, refFlat);
        }

        if (schema["allOf"] is JsonArray allOf)
        {
            foreach (var item in allOf)
            {
                if (item is not JsonObject sub) continue;
                var subFlat = Flatten(sub, baseDirectory);
                MergeInto(result, subFlat);
            }
        }

        // Recurse into every subschema container so nested `$ref`s further
        // down also get resolved (oneOf members inside abstracts in particular).
        if (result["properties"] is JsonObject props)
        {
            var rewritten = new JsonObject();
            foreach (var (name, propSchema) in props)
            {
                rewritten[name] = propSchema is JsonObject po
                    ? Flatten(po, baseDirectory)
                    : propSchema?.DeepClone();
            }
            result["properties"] = rewritten;
        }

        if (result["items"] is JsonObject items)
        {
            result["items"] = Flatten(items, baseDirectory);
        }

        if (result["additionalProperties"] is JsonObject apSchema)
        {
            result["additionalProperties"] = Flatten(apSchema, baseDirectory);
        }

        foreach (var arrayKey in new[] { "oneOf", "anyOf" })
        {
            if (result[arrayKey] is JsonArray arr)
            {
                var rewritten = new JsonArray();
                foreach (var item in arr)
                {
                    rewritten.Add(item is JsonObject o
                        ? Flatten(o, baseDirectory)
                        : item?.DeepClone());
                }
                result[arrayKey] = rewritten;
            }
        }

        if (result["not"] is JsonObject notSchema)
        {
            result["not"] = Flatten(notSchema, baseDirectory);
        }

        return result;
    }

    private static (JsonObject, string dir) LoadRef(string refPath, string baseDirectory)
    {
        var full = Path.GetFullPath(Path.Combine(baseDirectory, refPath));
        var text = File.ReadAllText(full);
        var node = JsonNode.Parse(text)
            ?? throw new InvalidOperationException($"Empty or invalid JSON at {full}");
        if (node is not JsonObject obj)
            throw new InvalidOperationException($"Expected object at {full}, got {node.GetType().Name}");
        return (obj, Path.GetDirectoryName(full)!);
    }

    private static void MergeInto(JsonObject target, JsonObject source)
    {
        foreach (var (key, value) in source)
        {
            switch (key)
            {
                case "properties":
                    if (value is not JsonObject srcProps) continue;
                    if (target["properties"] is not JsonObject tgtProps)
                    {
                        tgtProps = new JsonObject();
                        target["properties"] = tgtProps;
                    }
                    foreach (var (propName, propSchema) in srcProps)
                    {
                        if (!tgtProps.ContainsKey(propName))
                        {
                            tgtProps[propName] = propSchema?.DeepClone();
                        }
                    }
                    break;

                case "required":
                    if (value is not JsonArray srcReq) continue;
                    var union = new SortedSet<string>(StringComparer.Ordinal);
                    if (target["required"] is JsonArray tgtReq)
                    {
                        foreach (var r in tgtReq) union.Add(r!.GetValue<string>());
                    }
                    foreach (var r in srcReq) union.Add(r!.GetValue<string>());
                    target["required"] = new JsonArray(union.Select(s => (JsonNode)s).ToArray());
                    break;

                default:
                    if (!target.ContainsKey(key))
                    {
                        target[key] = value?.DeepClone();
                    }
                    break;
            }
        }
    }
}
