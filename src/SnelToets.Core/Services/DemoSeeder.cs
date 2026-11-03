using SnelToets.Core.Data;

namespace SnelToets.Core.Services;

// TODO(content agent): seed a demo subject via IImportService when the database is empty.
public class DemoSeeder(SnelToetsDbContext db, IImportService import) : IDemoSeeder
{
    public Task SeedIfEmptyAsync(CancellationToken ct = default) => Task.CompletedTask;
}
