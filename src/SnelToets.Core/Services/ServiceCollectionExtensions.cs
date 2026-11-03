using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SnelToets.Core.Data;

namespace SnelToets.Core.Services;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the DbContext (SQLite) and all core services as scoped.</summary>
    public static IServiceCollection AddSnelToetsCore(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<SnelToetsDbContext>(o => o.UseSqlite(connectionString));
        services.AddScoped<IContentService, ContentService>();
        services.AddScoped<ITestService, TestService>();
        services.AddScoped<IPracticeService, PracticeService>();
        services.AddScoped<IImportService, ImportService>();
        services.AddScoped<IDemoSeeder, DemoSeeder>();
        return services;
    }
}
