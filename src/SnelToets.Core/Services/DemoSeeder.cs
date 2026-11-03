using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SnelToets.Core.Data;
using SnelToets.Core.Import;
using SnelToets.Core.Models;

namespace SnelToets.Core.Services;

/// <summary>
/// Seeds demo content on first start (when there are no subjects): "Nederlands – taalverzorging (demo)" with four topics,
/// "Engels – grammar basics (demo)" with two topics, and two demo tests. The content is the embedded resource
/// built from <c>samples/demo-nederlands.json</c>, so it needs no file paths at runtime.
/// </summary>
public class DemoSeeder(SnelToetsDbContext db, IImportService import, ILogger<DemoSeeder>? logger = null) : IDemoSeeder
{
    public const string ResourceName = "SnelToets.Core.Seed.demo-nederlands.json";

    private readonly ILogger _logger = logger ?? NullLogger<DemoSeeder>.Instance;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public async Task SeedIfEmptyAsync(CancellationToken ct = default)
    {
        if (await db.Subjects.AnyAsync(ct)) return;

        var document = LoadDemoDocument();
        var preview = import.Validate(document);
        if (!preview.IsValid)
        {
            _logger.LogError("Demo content is invalid and was not seeded: {Errors}", string.Join(" | ", preview.Errors));
            return;
        }

        var result = await import.ImportAsync(preview.Document ?? document, ContentSource.Seed, ct);
        _logger.LogInformation(
            "Demo content seeded: {Subjects} subjects, {Topics} topics, {Questions} questions, {Tests} tests.",
            result.SubjectsCreated, result.TopicsCreated, result.QuestionsCreated, result.TestsCreated);
    }

    /// <summary>Reads the embedded demo document (same content as samples/demo-nederlands.json).</summary>
    public static ImportDocument LoadDemoDocument()
    {
        using var stream = typeof(DemoSeeder).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        return JsonSerializer.Deserialize<ImportDocument>(stream, JsonOptions)
            ?? throw new InvalidOperationException("Demo document is empty.");
    }
}
