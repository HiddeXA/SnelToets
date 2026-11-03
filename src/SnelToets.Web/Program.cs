using Microsoft.EntityFrameworkCore;
using SnelToets.Core.Data;
using SnelToets.Core.Services;
using SnelToets.Web.Api;
using SnelToets.Web.Components;
using SnelToets.Web.Infrastructure;
using SnelToets.Web.Mcp;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("SnelToets") ?? "Data Source=sneltoets.db";
builder.Services.AddSnelToetsCore(connectionString);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<LearnerSession>();
builder.Services.AddScoped<AdminSession>();

// REST API + MCP server (Streamable HTTP at /mcp) so an AI can create questions and tests.
builder.Services.AddSnelToetsApi();
builder.Services.AddSnelToetsMcp(builder.Configuration);

var app = builder.Build();

// Create the database and seed demo content on first start.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SnelToetsDbContext>();
    db.Database.EnsureCreated();
    await scope.ServiceProvider.GetRequiredService<IDemoSeeder>().SeedIfEmptyAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

// Every visitor gets an anonymous learner id in a cookie, so progress is kept without accounts.
app.UseMiddleware<LearnerCookieMiddleware>();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapSnelToetsApi();
app.MapSnelToetsMcp();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program;
