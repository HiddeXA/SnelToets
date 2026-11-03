using SnelToets.Core.Data;
using SnelToets.Core.Import;
using SnelToets.Core.Models;

namespace SnelToets.Core.Services;

// TODO(core agent): implement IContentService. Stub so the solution compiles while the UI is built in parallel.
public class ContentService(SnelToetsDbContext db) : IContentService
{
    private readonly SnelToetsDbContext _db = db;

    public Task<List<Subject>> GetSubjectsAsync(CancellationToken ct) => throw new NotImplementedException();
    public Task<Subject?> GetSubjectAsync(int id, CancellationToken ct) => throw new NotImplementedException();
    public Task<Subject> CreateSubjectAsync(Subject subject, CancellationToken ct) => throw new NotImplementedException();
    public Task<Subject> UpdateSubjectAsync(Subject subject, CancellationToken ct) => throw new NotImplementedException();
    public Task DeleteSubjectAsync(int id, CancellationToken ct) => throw new NotImplementedException();
    public Task<Topic?> GetTopicAsync(int id, CancellationToken ct) => throw new NotImplementedException();
    public Task<List<Topic>> GetTopicsAsync(int subjectId, CancellationToken ct) => throw new NotImplementedException();
    public Task<Topic> CreateTopicAsync(Topic topic, CancellationToken ct) => throw new NotImplementedException();
    public Task<Topic> UpdateTopicAsync(Topic topic, CancellationToken ct) => throw new NotImplementedException();
    public Task DeleteTopicAsync(int id, CancellationToken ct) => throw new NotImplementedException();
    public Task<Dictionary<int, int>> CountQuestionsPerTopicAsync(int subjectId, CancellationToken ct) => throw new NotImplementedException();
    public Task<List<Question>> GetQuestionsAsync(int? topicId, string? search, int skip, int take, CancellationToken ct) => throw new NotImplementedException();
    public Task<Question?> GetQuestionAsync(int id, CancellationToken ct) => throw new NotImplementedException();
    public Task<Question> CreateQuestionAsync(Question question, CancellationToken ct) => throw new NotImplementedException();
    public Task<Question> UpdateQuestionAsync(Question question, CancellationToken ct) => throw new NotImplementedException();
    public Task DeleteQuestionAsync(int id, CancellationToken ct) => throw new NotImplementedException();
}
