using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SnelToets.Core.Import;

/// <summary>JSON settings for the exchange format (camelCase, lenient reading, readable writing).</summary>
public static class ImportJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Serialize(ImportDocument document) => JsonSerializer.Serialize(document, Options);

    /// <summary>
    /// Reads a document. Besides <c>{ "subjects": [...], "tests": [...] }</c> it accepts a bare array of
    /// subjects or a single subject object. Throws <see cref="JsonException"/> on malformed JSON.
    /// </summary>
    public static ImportDocument Deserialize(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });
        var root = doc.RootElement;
        switch (root.ValueKind)
        {
            case JsonValueKind.Array:
                return new ImportDocument { Subjects = root.Deserialize<List<SubjectDto>>(Options) ?? [] };
            case JsonValueKind.Object:
                var isDocument = root.EnumerateObject().Any(p =>
                    p.Name.Equals("subjects", StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Equals("tests", StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Equals("version", StringComparison.OrdinalIgnoreCase));
                if (isDocument) return root.Deserialize<ImportDocument>(Options) ?? new ImportDocument();
                var subject = root.Deserialize<SubjectDto>(Options);
                return new ImportDocument { Subjects = subject is null ? [] : [subject] };
            default:
                throw new JsonException("Het bestand bevat geen JSON-object of -lijst.");
        }
    }
}
