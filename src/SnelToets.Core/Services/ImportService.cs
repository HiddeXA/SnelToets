using SnelToets.Core.Data;
using SnelToets.Core.Import;
using SnelToets.Core.Models;

namespace SnelToets.Core.Services;

// TODO(core agent): implement IImportService. Stub so the solution compiles while the UI is built in parallel.
public class ImportService(SnelToetsDbContext db) : IImportService
{
    private readonly SnelToetsDbContext _db = db;

    public Task<ImportPreview> ParseAsync(Stream stream, string fileName, CancellationToken ct) => throw new NotImplementedException();
    public ImportPreview Validate(ImportDocument document) => throw new NotImplementedException();
    public Task<ImportResult> ImportAsync(ImportDocument document, ContentSource source, CancellationToken ct) => throw new NotImplementedException();
    public Task<ImportDocument> ExportAsync(int? subjectId, CancellationToken ct) => throw new NotImplementedException();
}
