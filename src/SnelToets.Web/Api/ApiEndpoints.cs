namespace SnelToets.Web.Api;

// TODO(api/mcp agent): REST endpoints under /api (import/export, subjects, topics, questions, tests).
public static class ApiEndpoints
{
    public static IServiceCollection AddSnelToetsApi(this IServiceCollection services) => services;

    public static IEndpointRouteBuilder MapSnelToetsApi(this IEndpointRouteBuilder app) => app;
}
