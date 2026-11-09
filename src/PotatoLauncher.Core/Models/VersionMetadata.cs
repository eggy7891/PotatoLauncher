using System.Text.Json;
using System.Text.Json.Serialization;

namespace PotatoLauncher.Core.Models;

public sealed class VersionMetadata
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("inheritsFrom")]
    public string? InheritsFrom { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "release";

    [JsonPropertyName("mainClass")]
    public string? MainClass { get; set; }

    [JsonPropertyName("assets")]
    public string? Assets { get; set; }

    [JsonPropertyName("assetIndex")]
    public VersionAssetIndex? AssetIndex { get; set; }

    [JsonPropertyName("downloads")]
    public Dictionary<string, VersionDownload> Downloads { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("libraries")]
    public List<LibraryEntry> Libraries { get; set; } = [];

    [JsonPropertyName("arguments")]
    public VersionArguments? Arguments { get; set; }

    [JsonPropertyName("minecraftArguments")]
    public string? LegacyMinecraftArguments { get; set; }

    [JsonPropertyName("javaVersion")]
    public JavaVersionRequirement? JavaVersion { get; set; }

    [JsonPropertyName("logging")]
    public VersionLogging? Logging { get; set; }

    [JsonPropertyName("releaseTime")]
    public DateTimeOffset? ReleaseTime { get; set; }
}

public sealed class VersionAssetIndex
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public class VersionDownload
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }
}

public sealed class JavaVersionRequirement
{
    [JsonPropertyName("component")]
    public string Component { get; set; } = "jre";

    [JsonPropertyName("majorVersion")]
    public int MajorVersion { get; set; }
}

public sealed class VersionLogging
{
    [JsonPropertyName("client")]
    public LoggingClient? Client { get; set; }
}

public sealed class LoggingClient
{
    [JsonPropertyName("argument")]
    public string Argument { get; set; } = "";

    [JsonPropertyName("file")]
    public LoggingFile? File { get; set; }
}

public sealed class LoggingFile : VersionDownload;

public sealed class VersionArguments
{
    [JsonPropertyName("game")]
    public List<ArgumentEntry> Game { get; set; } = [];

    [JsonPropertyName("jvm")]
    public List<ArgumentEntry> Jvm { get; set; } = [];
}

[JsonConverter(typeof(ArgumentEntryConverter))]
public sealed class ArgumentEntry
{
    [JsonPropertyName("rules")]
    public List<RuleEntry>? Rules { get; set; }

    [JsonPropertyName("value")]
    public JsonArgumentValue Value { get; set; } = new();

    public IEnumerable<string> Values => Value.Values;
}

public sealed class ArgumentEntryConverter : JsonConverter<ArgumentEntry>
{
    public override ArgumentEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new ArgumentEntry
            {
                Value = new JsonArgumentValue([reader.GetString() ?? ""])
            };
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return new ArgumentEntry();
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var entry = new ArgumentEntry();
        if (root.TryGetProperty("rules", out var rules) && rules.ValueKind == JsonValueKind.Array)
        {
            entry.Rules = JsonSerializer.Deserialize<List<RuleEntry>>(rules.GetRawText(), options);
        }

        if (root.TryGetProperty("value", out var value))
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                entry.Value = new JsonArgumentValue([value.GetString() ?? ""]);
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                entry.Value = new JsonArgumentValue(
                    value.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString() ?? ""));
            }
        }
        return entry;
    }

    public override void Write(Utf8JsonWriter writer, ArgumentEntry value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.Rules is not null)
        {
            writer.WritePropertyName("rules");
            JsonSerializer.Serialize(writer, value.Rules, options);
        }
        writer.WritePropertyName("value");
        JsonSerializer.Serialize(writer, value.Value, options);
        writer.WriteEndObject();
    }
}

[JsonConverter(typeof(JsonArgumentValueConverter))]
public sealed class JsonArgumentValue
{
    public JsonArgumentValue()
    {
    }

    public JsonArgumentValue(IEnumerable<string> values)
    {
        Values = values.ToList();
    }

    public List<string> Values { get; init; } = [];
}

public sealed class JsonArgumentValueConverter : JsonConverter<JsonArgumentValue>
{
    public override JsonArgumentValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new JsonArgumentValue([reader.GetString() ?? ""]);
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var values = new List<string>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType == JsonTokenType.String)
                {
                    values.Add(reader.GetString() ?? "");
                }
                else
                {
                    reader.Skip();
                }
            }

            return new JsonArgumentValue(values);
        }

        reader.Skip();
        return new JsonArgumentValue();
    }

    public override void Write(Utf8JsonWriter writer, JsonArgumentValue value, JsonSerializerOptions options)
    {
        if (value.Values.Count == 1)
        {
            writer.WriteStringValue(value.Values[0]);
            return;
        }

        writer.WriteStartArray();
        foreach (var item in value.Values)
        {
            writer.WriteStringValue(item);
        }
        writer.WriteEndArray();
    }
}

public sealed class LibraryEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("downloads")]
    public LibraryDownloads? Downloads { get; set; }

    [JsonPropertyName("natives")]
    public Dictionary<string, string>? Natives { get; set; }

    [JsonPropertyName("rules")]
    public List<RuleEntry>? Rules { get; set; }

    [JsonPropertyName("extract")]
    public ExtractRule? Extract { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public sealed class LibraryDownloads
{
    [JsonPropertyName("artifact")]
    public VersionDownload? Artifact { get; set; }

    [JsonPropertyName("classifiers")]
    public Dictionary<string, VersionDownload>? Classifiers { get; set; }
}

public sealed class ExtractRule
{
    [JsonPropertyName("exclude")]
    public List<string> Exclude { get; set; } = [];
}

public sealed class RuleEntry
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = "allow";

    [JsonPropertyName("os")]
    public OsRule? Os { get; set; }

    [JsonPropertyName("features")]
    public Dictionary<string, bool>? Features { get; set; }
}

public sealed class OsRule
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arch")]
    public string? Arch { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}
