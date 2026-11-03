using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SnelToets.Core.Import;
using SnelToets.Core.Models;
using SnelToets.Core.Services;

namespace SnelToets.Web.Api;

/// <summary>
/// REST API under <c>/api</c> (JSON, camelCase). GET endpoints are public; POST/PUT/DELETE require the
/// API key from "Mcp:ApiKey" when configured (see <see cref="Mcp.ApiKeyGuard"/>). Documented in README.md.
/// </summary>
public static class ApiEndpoints
{
    public static IServiceCollection AddSnelToetsApi(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.TryAddScoped<ContentOperations>();
        services.AddProblemDetails();
        services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            o.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
            o.SerializerOptions.AllowTrailingCommas = true;
            o.SerializerOptions.ReadCommentHandling = JsonCommentHandling.Skip;
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });
        Mcp.McpSetup.AddApiKeyGuard(services);
        return services;
    }

    public static IEndpointRouteBuilder MapSnelToetsApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api")
            .AddEndpointFilter(HandleErrors)
            .WithTags("SnelToets");

        api.MapGet("/health", () => Results.Ok(new { status = "ok", app = "SnelToets", time = DateTime.UtcNow }));

        api.MapGet("/subjects", async (ContentOperations ops, CancellationToken ct) =>
            Results.Ok(await ops.ListSubjectsAsync(ct)));

        api.MapGet("/subjects/{id:int}", async (int id, ContentOperations ops, CancellationToken ct) =>
            await ops.GetSubjectAsync(id, ct) is { } s ? Results.Ok(s) : NotFound($"Vak {id} bestaat niet."));

        api.MapGet("/topics/{id:int}", async (int id, bool? questions, ContentOperations ops, CancellationToken ct) =>
            await ops.GetTopicAsync(id, questions ?? false, ct) is { } t ? Results.Ok(t) : NotFound($"Onderwerp {id} bestaat niet."));

        api.MapGet("/topics/{id:int}/questions", async (int id, string? search, int? skip, int? take, IContentService content, CancellationToken ct) =>
        {
            if (await content.GetTopicAsync(id, ct) is null) return NotFound($"Onderwerp {id} bestaat niet.");
            var list = await content.GetQuestionsAsync(id, search, Math.Max(0, skip ?? 0), Math.Clamp(take ?? 200, 1, 1000), ct);
            return Results.Ok(list.Select(QuestionMapper.ToView));
        });

        api.MapGet("/tests", async (ContentOperations ops, CancellationToken ct) =>
            Results.Ok(await ops.ListTestsAsync(ct)));

        api.MapGet("/export", async (int? subjectId, bool? download, IImportService import, IContentService content, CancellationToken ct) =>
        {
            if (subjectId is int sid && await content.GetSubjectAsync(sid, ct) is null) return NotFound($"Vak {sid} bestaat niet.");
            var doc = await import.ExportAsync(subjectId, ct);
            if (download != true) return Results.Ok(doc);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(doc, SnelToetsJson.Create(indented: true));
            return Results.File(bytes, "application/json", subjectId is null ? "sneltoets-export.json" : $"sneltoets-vak-{subjectId}.json");
        });

        api.MapPost("/import", ImportAsync).DisableAntiforgery();

        api.MapPost("/tests", async (HttpRequest http, ContentOperations ops, CancellationToken ct) =>
        {
            CreateTestRequest? request;
            try { request = await JsonSerializer.DeserializeAsync<CreateTestRequest>(http.Body, SnelToetsJson.Options, ct); }
            catch (JsonException) { return Invalid("De JSON in het verzoek kon niet worden gelezen. Controleer de opbouw en de veldnamen (camelCase)."); }
            if (request is null) return Invalid("Lege invoer: stuur een toets als JSON.");
            var created = await ops.CreateTestAsync(request, ContentSource.Manual, ct);
            return Results.Created(created.AdminUrl, created);
        }).DisableAntiforgery();

        api.MapDelete("/questions/{id:int}", async (int id, IContentService content, CancellationToken ct) =>
        {
            if (await content.GetQuestionAsync(id, ct) is null) return NotFound($"Vraag {id} bestaat niet.");
            await content.DeleteQuestionAsync(id, ct);
            return Results.NoContent();
        });

        return app;
    }

    /// <summary>
    /// <c>POST /api/import[?dryRun=true]</c>. Body: an ImportDocument as JSON, a raw CSV (<c>text/csv</c>),
    /// or <c>multipart/form-data</c> with a .json/.csv file. Dry run returns only the validation preview.
    /// </summary>
    private static async Task<IResult> ImportAsync(HttpRequest request, bool? dryRun, IImportService import, CancellationToken ct)
    {
        ImportPreview preview;
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Invalid("Geen bestand ontvangen. Stuur een .json- of .csv-bestand mee als formulierveld 'file'.");
            await using var stream = file.OpenReadStream();
            preview = await import.ParseAsync(stream, file.FileName, ct);
        }
        else if (request.ContentType?.Contains("csv", StringComparison.OrdinalIgnoreCase) == true)
        {
            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, ct);
            buffer.Position = 0;
            preview = await import.ParseAsync(buffer, "upload.csv", ct);
        }
        else
        {
            // JSON body: parsed by the importer, which also accepts a bare subject or an array of subjects
            // and reports malformed JSON with a Dutch message.
            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, ct);
            if (buffer.Length == 0) return Invalid("Lege invoer: stuur een importdocument (zie docs/import-format.md).");
            buffer.Position = 0;
            preview = await import.ParseAsync(buffer, "upload.json", ct);
        }

        var summary = new
        {
            valid = preview.IsValid,
            subjects = preview.SubjectCount,
            topics = preview.TopicCount,
            questions = preview.QuestionCount,
            tests = preview.TestCount,
            errors = preview.Errors,
            warnings = preview.Warnings
        };
        if (dryRun == true) return Results.Ok(summary);
        if (!preview.IsValid)
            return Results.Problem(
                title: "Het importbestand bevat fouten",
                detail: "Er is niets geïmporteerd. Verbeter de fouten en probeer het opnieuw.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["errors"] = preview.Errors, ["warnings"] = preview.Warnings });

        var result = await import.ImportAsync(preview.Document!, ContentSource.Import, ct);
        return Results.Ok(result);
    }

    private static async ValueTask<object?> HandleErrors(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (ValidationException ex)
        {
            return Results.Problem(
                title: "De invoer klopt niet",
                detail: string.Join(" ", ex.Errors),
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["errors"] = ex.Errors });
        }
    }

    private static IResult NotFound(string message) =>
        Results.Problem(title: "Niet gevonden", detail: message, statusCode: StatusCodes.Status404NotFound);

    private static IResult Invalid(string message) =>
        Results.Problem(title: "De invoer klopt niet", detail: message, statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?> { ["errors"] = new[] { message } });
}
