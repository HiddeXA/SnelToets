using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Protocol;
using SnelToets.Web.Api;

namespace SnelToets.Web.Mcp;

/// <summary>
/// MCP server (ModelContextProtocol.AspNetCore, Streamable HTTP, stateless) at <c>/mcp</c>.
/// An AI assistant can read the content and create subjects, topics, questions and tests. See docs/mcp.md.
/// </summary>
public static class McpSetup
{
    public const string Route = "/mcp";

    public const string Instructions = """
        SnelToets is a practice app for students (modelled on Noordhoff's Hogeschooltaal): subjects -> topics with theory -> a question bank.
        Students practise per topic; after a wrong answer they immediately see the question's tip and can open the theory.
        A score bar per topic rises with correct answers and drops with mistakes; 80% means "mastered".

        How to work:
        1. Call list_subjects first to see what exists (reuse subjects/topics instead of creating duplicates).
        2. Create a subject (create_subject) and topics with concise theory in Markdown (create_topic), or send everything at once with import_content.
        3. Add questions in batches with create_questions (10-20 per call). Check per-item errors, fix and resend only the failed ones.
        4. Optionally compose a test with create_test and give the user the returned studentUrl.
        Call get_import_format when you need the exact JSON shape and validation rules.

        Content rules:
        - Write ALL content (theory, questions, options, tips, feedback) in the students' language - usually Dutch - with flawless spelling.
        - ALWAYS write a tip for every question that applies the rule to that specific question (not a generic rule), like Hogeschooltaal does,
          e.g. "'Je' kun je hier niet vervangen door 'jij' -> geen onderwerp -> stam + t: 'Vind je'."
        - Give every wrong option a short 'feedback' that explains why that distractor is tempting but wrong.
        - Prefer multiple-choice with 3-4 plausible options; mix in some true-false and fill-in questions. Set difficulty 1-3.
        - Give questions a stable 'id' (e.g. "ww-tt-001") so they can be updated later instead of duplicated.
        """;

    public static IServiceCollection AddSnelToetsMcp(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.TryAddScoped<ContentOperations>();
        AddApiKeyGuard(services);

        services.AddMcpServer(o =>
            {
                o.ServerInfo = new Implementation
                {
                    Name = "sneltoets",
                    Title = "SnelToets",
                    Version = typeof(McpSetup).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"
                };
                o.ServerInstructions = Instructions;
            })
            .WithHttpTransport(o => o.Stateless = true)
            .WithTools<SnelToetsTools>(SnelToetsJson.Options)
            .WithPrompts<SnelToetsPrompts>(SnelToetsJson.Options);

        return services;
    }

    public static IEndpointRouteBuilder MapSnelToetsMcp(this IEndpointRouteBuilder app)
    {
        // Authentication is enforced by ApiKeyMiddleware (registered through an IStartupFilter) before routing.
        app.MapMcp(Route);
        return app;
    }

    /// <summary>Registers the API-key guard and its middleware once (shared by the REST API and MCP).</summary>
    internal static void AddApiKeyGuard(IServiceCollection services)
    {
        services.TryAddSingleton<ApiKeyGuard>();
        services.TryAddEnumerable(ServiceDescriptor.Transient<IStartupFilter, ApiKeyStartupFilter>());
    }
}
