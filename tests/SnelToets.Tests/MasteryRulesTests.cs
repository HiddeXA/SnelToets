using SnelToets.Core.Services;

namespace SnelToets.Tests;

public class MasteryRulesTests
{
    private static int CorrectAnswersToMastery(int difficulty)
    {
        double score = 0;
        var n = 0;
        while (score < MasteryRules.MasteredThreshold) { score = MasteryRules.Update(score, true, difficulty); n++; }
        return n;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Takes_10_to_14_correct_answers_from_zero_to_mastery(int difficulty)
    {
        var n = CorrectAnswersToMastery(difficulty);
        Assert.InRange(n, 10, 14);
    }

    [Fact]
    public void Harder_questions_count_more()
    {
        Assert.True(CorrectAnswersToMastery(3) < CorrectAnswersToMastery(2));
        Assert.True(CorrectAnswersToMastery(2) < CorrectAnswersToMastery(1));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void One_mistake_at_85_drops_below_mastered(int difficulty)
    {
        Assert.True(MasteryRules.Update(85, false, difficulty) < MasteryRules.MasteredThreshold);
    }

    [Fact]
    public void Score_stays_within_0_and_100()
    {
        Assert.Equal(0, MasteryRules.Update(3, false, 1));
        Assert.Equal(0, MasteryRules.Update(0, false, 2));
        Assert.InRange(MasteryRules.Update(99.99, true, 3), 99.99, 100);
        Assert.Equal(100, MasteryRules.Update(150, true, 1));
        Assert.True(MasteryRules.Update(50, true, 1) > 50);
        Assert.True(MasteryRules.Update(50, false, 1) < 50);
    }

    [Fact]
    public void Placement_update_is_capped()
    {
        double score = 0;
        for (var i = 0; i < 50; i++) score = MasteryRules.UpdatePlacement(score, true, 3);
        Assert.Equal(MasteryRules.PlacementCap, score);
        Assert.Equal(75, MasteryRules.UpdatePlacement(75, true, 3)); // an existing higher score is not raised
        Assert.True(MasteryRules.UpdatePlacement(75, false, 1) < 75);
    }
}
