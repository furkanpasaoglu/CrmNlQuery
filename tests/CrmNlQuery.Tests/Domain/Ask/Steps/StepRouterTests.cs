using CrmNlQuery.Domain.Ask.Steps;
using CrmNlQuery.Domain.Ask.Entities;

namespace CrmNlQuery.Tests.Domain.Ask.Steps;

/// <summary>
/// Tests the step router that decides whether a question goes to the decomposer.
/// </summary>
public class StepRouterTests
{
    /// <summary>
    /// Verifies that the question wording is classified into the expected trigger.
    /// </summary>
    [Theory]
    [InlineData("Denema contractları yoksa Örnekova contractlarını getir", StepTrigger.Always)]
    [InlineData("Show Denema contracts, otherwise Ornekova contracts", StepTrigger.Always)]
    [InlineData("Brightwave kişilerini getir, sonra Alpine fırsatlarını getir", StepTrigger.Always)]
    [InlineData("Önce hesapları getir ardından kişileri getir", StepTrigger.Always)]
    [InlineData("Show the accounts, then show their contacts", StepTrigger.Always)]
    [InlineData("En çok fırsatı olan firmanın kişileri yoksa tüm kişileri getir", StepTrigger.Always)]
    [InlineData("En çok açık fırsatı olan firmanın kişilerini getir", StepTrigger.OnlyRanking)]
    [InlineData("En çok çalışanı olan 5 firmayı getir", StepTrigger.OnlyRanking)]
    [InlineData("EN YÜKSEK TUTARLI FIRSATI GETİR", StepTrigger.OnlyRanking)]
    [InlineData("Contacts of the account with the most open opportunities", StepTrigger.OnlyRanking)]
    [InlineData("Bütün productları getir", StepTrigger.None)]
    [InlineData("Açık fırsatı olan firmaların kişilerini getir", StepTrigger.None)]
    [InlineData("", StepTrigger.None)]
    public void TheQuestionWording_IsClassified(string question, StepTrigger expected)
    {
        Assert.Equal(expected, ConditionalQuestionFilter.Classify(question));
    }

    /// <summary>
    /// Verifies that classification agrees with the may-need-steps check.
    /// </summary>
    [Fact]
    public void Classify_AgreesWithMayNeedSteps()
    {
        foreach (var question in new[] { "Denema yoksa Örnekova", "En çok firma", "all products", "a, sonra b", "the most" })
        {
            Assert.Equal(ConditionalQuestionFilter.MayNeedSteps(question), ConditionalQuestionFilter.Classify(question) != StepTrigger.None);
        }
    }

    private static QuestionAnalysis Analysis(params string[] related) =>
        new QuestionAnalysis("account", related.ToList(), new List<Mention>(), new List<string>(), false);

    /// <summary>
    /// Verifies that a ranking question with no related entity is a single query and the decomposer is skipped.
    /// </summary>
    [Fact]
    public void ARankingQuestion_WithNoRelatedEntity_IsASingleQuery_TheDecomposerIsSkipped()
    {
        Assert.False(StepRouter.NeedsDecomposer("En çok çalışanı olan 5 firmayı getir", Analysis()));
        Assert.False(StepRouter.NeedsDecomposer("En yüksek tutarlı fırsatı getir", Analysis()));
    }

    /// <summary>
    /// Verifies that a ranking question with a related entity may ask for another kind of record so the decomposer runs.
    /// </summary>
    [Fact]
    public void ARankingQuestion_WithARelatedEntity_MayAskForAnotherKindOfRecord_SoTheDecomposerRuns()
    {
        Assert.True(StepRouter.NeedsDecomposer("En çok açık fırsatı olan firmanın kişilerini getir", Analysis("account", "opportunity")));
    }

    /// <summary>
    /// Verifies that conditional and sequential questions always need the decomposer whatever the analysis says.
    /// </summary>
    [Fact]
    public void ConditionalAndSequentialQuestions_AlwaysNeedTheDecomposer_WhateverTheAnalysisSays()
    {
        Assert.True(StepRouter.NeedsDecomposer("Denema yoksa Örnekova", Analysis()));
        Assert.True(StepRouter.NeedsDecomposer("A'yı getir, sonra B'yi getir", Analysis()));
    }

    /// <summary>
    /// Verifies that without any trigger wording nothing needs the decomposer.
    /// </summary>
    [Fact]
    public void WithoutAnyWording_NothingNeedsTheDecomposer()
    {
        Assert.False(StepRouter.NeedsDecomposer("Bütün productları getir", Analysis("contract")));
    }
}
