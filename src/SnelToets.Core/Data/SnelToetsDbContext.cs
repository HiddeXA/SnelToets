using Microsoft.EntityFrameworkCore;
using SnelToets.Core.Models;

namespace SnelToets.Core.Data;

public class SnelToetsDbContext(DbContextOptions<SnelToetsDbContext> options) : DbContext(options)
{
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Topic> Topics => Set<Topic>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<AnswerOption> AnswerOptions => Set<AnswerOption>();
    public DbSet<Test> Tests => Set<Test>();
    public DbSet<TestQuestion> TestQuestions => Set<TestQuestion>();
    public DbSet<TestTopic> TestTopics => Set<TestTopic>();
    public DbSet<Attempt> Attempts => Set<Attempt>();
    public DbSet<AttemptItem> AttemptItems => Set<AttemptItem>();
    public DbSet<TopicMastery> TopicMasteries => Set<TopicMastery>();
    public DbSet<QuestionStat> QuestionStats => Set<QuestionStat>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Subject>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasMany(x => x.Topics).WithOne(x => x.Subject).HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Topic>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasMany(x => x.Questions).WithOne(x => x.Topic).HasForeignKey(x => x.TopicId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.SubjectId, x.SortOrder });
        });

        b.Entity<Question>(e =>
        {
            e.Property(x => x.Prompt).IsRequired();
            e.Property(x => x.ExternalId).HasMaxLength(200);
            e.PrimitiveCollection(x => x.AcceptedAnswers);
            e.HasMany(x => x.Options).WithOne().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TopicId, x.ExternalId });
        });

        b.Entity<AnswerOption>(e => e.Property(x => x.Text).IsRequired());

        b.Entity<Test>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.HasMany(x => x.Questions).WithOne().HasForeignKey(x => x.TestId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Topics).WithOne().HasForeignKey(x => x.TestId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TestQuestion>(e =>
        {
            e.HasKey(x => new { x.TestId, x.QuestionId });
            e.HasOne(x => x.Question).WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TestTopic>(e =>
        {
            e.HasKey(x => new { x.TestId, x.TopicId });
            e.HasOne(x => x.Topic).WithMany().HasForeignKey(x => x.TopicId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Attempt>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.LearnerId, x.StartedAt });
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Test).WithMany().HasForeignKey(x => x.TestId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<AttemptItem>(e =>
        {
            e.HasOne(x => x.Question).WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TopicMastery>(e =>
        {
            e.HasKey(x => new { x.LearnerId, x.TopicId });
            e.HasOne(x => x.Topic).WithMany().HasForeignKey(x => x.TopicId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<QuestionStat>(e =>
        {
            e.HasKey(x => new { x.LearnerId, x.QuestionId });
            e.HasOne<Question>().WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
