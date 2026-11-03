using SnelToets.Core.Data;
using SnelToets.Core.Import;
using SnelToets.Core.Models;

namespace SnelToets.Core.Services;

// TODO(core agent): implement ITestService. Stub so the solution compiles while the UI is built in parallel.
public class TestService(SnelToetsDbContext db) : ITestService
{
    private readonly SnelToetsDbContext _db = db;

    public Task<List<Test>> GetTestsAsync(CancellationToken ct) => throw new NotImplementedException();
    public Task<Test?> GetTestAsync(int id, CancellationToken ct) => throw new NotImplementedException();
    public Task<Test> CreateTestAsync(Test test, CancellationToken ct) => throw new NotImplementedException();
    public Task<Test> UpdateTestAsync(Test test, CancellationToken ct) => throw new NotImplementedException();
    public Task DeleteTestAsync(int id, CancellationToken ct) => throw new NotImplementedException();
    public Task<int> CountQuestionsAsync(int testId, CancellationToken ct) => throw new NotImplementedException();
}
