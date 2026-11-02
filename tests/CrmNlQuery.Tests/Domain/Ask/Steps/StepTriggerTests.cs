using CrmNlQuery.Domain.Ask.Steps;

namespace CrmNlQuery.Tests.Domain.Ask.Steps;

/// <summary>
/// Tests the step trigger rules, where the main risk is failing to trigger the decomposer for multi-step questions.
/// </summary>
public class StepTriggerTests
{
    /// <summary>
    /// Verifies that step wording is detected.
    /// </summary>
    [Theory]
    [InlineData("En çok açık fırsatı olan firmanın kişilerini getir")]
    [InlineData("Açık fırsatı olan firmalar arasında en az fırsatı olanların kişilerini getir")]
    [InlineData("En çok case'i olan firmaların kişilerini getir")]
    [InlineData("En çok çalışanı olan firmanın kişilerini getir")]
    [InlineData("Kazanılmış fırsatlar arasında en çok fırsatı olan firmanın kişilerini getir")]
    [InlineData("En çok açık fırsatı olan firmanın case'lerini getir")]
    [InlineData("EN ÇOK AÇIK FIRSATI OLAN FİRMANIN KİŞİLERİNİ GETİR")]
    [InlineData("en yüksek cirolu müşterinin fırsatlarını göster")]
    [InlineData("En FAZLA çalışanı olan firmanın kişileri")]
    [InlineData("en düşük tutarlı fırsatın sahibini getir")]
    [InlineData("En büyük firmanın kişileri")]
    [InlineData("en eski müşterinin case'leri")]
    [InlineData("Son açılan fırsatın firmasının kişileri; en son fırsat")]
    [InlineData("Show the contacts of the account with the most open opportunities")]
    [InlineData("Contacts of the account with the highest revenue")]
    [InlineData("Cases of the company with the fewest employees")]
    [InlineData("Opportunities of the largest account")]
    [InlineData("Contacts of the account with the least cases")]
    [InlineData("Brightwave firmasının kişilerini getir, sonra Alpine firmasının açık fırsatlarını getir")]
    [InlineData("Önce hesapları getir ardından kişileri getir")]
    [InlineData("Show the accounts, then show their contacts")]
    [InlineData("Denema contractları yoksa Örnekova contractlarını getir")]
    public void StepWording_IsDetected(string question)
    {
        Assert.True(ConditionalQuestionFilter.MayNeedSteps(question));
    }

    /// <summary>
    /// Verifies that plain questions are not flagged.
    /// </summary>
    [Theory]
    [InlineData("Bütün productları getir")]
    [InlineData("Hamburg'daki şirketleri getir")]
    [InlineData("Londra'daki firmaların kişilerini getir")]
    [InlineData("Açık fırsatı olan firmaların kişilerini getir")]
    [InlineData("Nordbeispiel Insurance AG firmasının kişilerini getir")]
    [InlineData("Çalışan sayısı 1000'den fazla olan firmaları getir")]
    [InlineData("Fırsatı olmayan firmaları getir")]
    [InlineData("Her firmanın kaç açık fırsatı olduğunu getir")]
    [InlineData("How many accounts do we have?")]
    [InlineData("Show mostly harmless accounts")]
    [InlineData("")]
    public void PlainQuestions_AreNotFlagged(string question)
    {
        Assert.False(ConditionalQuestionFilter.MayNeedSteps(question));
    }

    /// <summary>
    /// Verifies that the old conditional check stays exactly as it was.
    /// </summary>
    [Fact]
    public void TheOldConditionalCheck_StaysExactlyAsItWas()
    {
        Assert.False(ConditionalQuestionFilter.MayBeConditional("En çok açık fırsatı olan firmanın kişilerini getir"));
        Assert.True(ConditionalQuestionFilter.MayBeConditional("Denema contractları yoksa Örnekova"));
    }
}
