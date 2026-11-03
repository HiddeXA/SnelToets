using SnelToets.Core.Data;
using SnelToets.Core.Import;
using SnelToets.Core.Models;

namespace SnelToets.Core.Services;

// TODO(core agent): implement IPracticeService. Stub so the solution compiles while the UI is built in parallel.
public class PracticeService(SnelToetsDbContext db) : IPracticeService
{
    private readonly SnelToetsDbContext _db = db;

    public Task<Attempt> StartTopicPracticeAsync(Guid learnerId, int topicId, int count, CancellationToken ct) => throw new NotImplementedException();
    public Task<Attempt> StartMixPracticeAsync(Guid learnerId, int subjectId, int count, CancellationToken ct) => throw new NotImplementedException();
    public Task<Attempt> StartPlacementAsync(Guid learnerId, int subjectId, int perTopic, CancellationToken ct) => throw new NotImplementedException();
    public Task<Attempt> StartTestAsync(Guid learnerId, int testId, CancellationToken ct) => throw new NotImplementedException();
    public Task<Attempt?> GetAttemptAsync(Guid attemptId, CancellationToken ct) => throw new NotImplementedException();
    public Task<List<Attempt>> GetRecentAttemptsAsync(Guid learnerId, int take, CancellationToken ct) => throw new NotImplementedException();
    public Task<AnswerResult> SubmitAnswerAsync(Guid attemptId, int itemId, AnswerSubmission answer, CancellationToken ct) => throw new NotImplementedException();
    public Task<AttemptResult> FinishAsync(Guid attemptId, CancellationToken ct) => throw new NotImplementedException();
    public Task<List<TopicMastery>> GetMasteryAsync(Guid learnerId, int? subjectId, CancellationToken ct) => throw new NotImplementedException();
    public Task<StudyAdvice> GetAdviceAsync(Guid learnerId, int subjectId, CancellationToken ct) => throw new NotImplementedException();
    public Task ResetTopicAsync(Guid learnerId, int topicId, CancellationToken ct) => throw new NotImplementedException();
}
