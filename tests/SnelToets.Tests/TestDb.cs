using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SnelToets.Core.Data;
using SnelToets.Core.Models;

namespace SnelToets.Tests;

/// <summary>SQLite in-memory database that lives as long as the connection.</summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;
    public SnelToetsDbContext Db { get; }

    public TestDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        Db = NewContext();
        Db.Database.EnsureCreated();
    }

    public SnelToetsDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SnelToetsDbContext>().UseSqlite(_connection).Options);

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }

    /// <summary>Creates a subject with one topic and <paramref name="questions"/> multiple-choice questions.</summary>
    public (Subject Subject, Topic Topic, List<Question> Questions) Seed(int questions = 5, string subject = "Nederlands", string topic = "Werkwoorden", int difficulty = 1)
    {
        var s = Db.Subjects.FirstOrDefault(x => x.Name == subject) ?? new Subject { Name = subject };
        var t = new Topic { Name = topic, Subject = s, SortOrder = Db.Topics.Count() + 1, TheoryMarkdown = "Theorie" };
        var list = Enumerable.Range(1, questions).Select(i => Mc($"{topic} vraag {i}", difficulty)).ToList();
        t.Questions = list;
        Db.Topics.Add(t);
        Db.SaveChanges();
        Db.ChangeTracker.Clear();
        return (s, t, list);
    }

    public static Question Mc(string prompt, int difficulty = 1, string? tip = "Let op de regel.") => new()
    {
        Type = QuestionType.MultipleChoice,
        Prompt = prompt,
        Difficulty = difficulty,
        Tip = tip,
        Options =
        [
            new AnswerOption { Text = "Goed antwoord", IsCorrect = true, SortOrder = 0 },
            new AnswerOption { Text = "Fout antwoord", IsCorrect = false, SortOrder = 1, Feedback = "Dit is de valkuil." },
            new AnswerOption { Text = "Ook fout", IsCorrect = false, SortOrder = 2 }
        ]
    };
}
