namespace SnelToets.Web.Mcp;

// TODO(api/mcp agent): MCP server (ModelContextProtocol.AspNetCore) at /mcp with tools to create content.
public static class McpSetup
{
    public static IServiceCollection AddSnelToetsMcp(this IServiceCollection services, IConfiguration configuration) => services;

    public static IEndpointRouteBuilder MapSnelToetsMcp(this IEndpointRouteBuilder app) => app;
}
