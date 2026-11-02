using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;
using CrmNlQuery.Application.Operations.Abstractions;
using CrmNlQuery.Application.Operations.Services;
using CrmNlQuery.Application.Operations.Models;

namespace CrmNlQuery.Tests.Application.Localization;

/// <summary>
/// Tests that server messages sent to users are produced in the request's language.
/// </summary>
[Collection("RequestLanguage")]
public class ServerMessagesLocalizationTests
{
    private sealed class Limits : ILimitsSource
    {
        public GuardrailLimits Current { get; set; } = new GuardrailLimits();
    }

    /// <summary>
    /// Verifies that the common messages are english by default and turkish in a turkish request.
    /// </summary>
    [Fact]
    public void TheCommonMessages_AreEnglishByDefault_AndTurkishInATurkishRequest()
    {
        try
        {
            RequestLanguage.Set("en");
            Assert.Contains("language model", UserMessages.ModelUnavailable);
            Assert.Equal("The question took longer than 120 seconds and was stopped. Please try again, or ask something simpler.", UserMessages.TimedOut(120));
            Assert.Equal("The question is too long (the limit is 1000 characters). Please shorten it.", UserMessages.QuestionTooLong(1000));

            RequestLanguage.Set("tr");
            Assert.Contains("Dil modeline", UserMessages.ModelUnavailable);
            Assert.Equal("Soru 120 saniyeden uzun sürdü ve durduruldu. Lütfen tekrar deneyin ya da daha basit bir soru sorun.", UserMessages.TimedOut(120));
            Assert.Equal("Soru çok uzun (sınır 1000 karakter). Lütfen kısaltın.", UserMessages.QuestionTooLong(1000));
            Assert.Contains("en çok 100", UserMessages.TooManyIds(2, 150));
            Assert.Contains("{step}", MessageCatalog.Render(Msg.TooManyIds, "en"));
            Assert.DoesNotContain("{", UserMessages.TooManyIds(2, 150));
        }
        finally
        {
            RequestLanguage.Set("en");
        }
    }

    /// <summary>
    /// Verifies that the examples suffix and the alternative word follow the language.
    /// </summary>
    [Fact]
    public void TheExamplesSuffix_AndTheAlternativeWord_FollowTheLanguage()
    {
        try
        {
            RequestLanguage.Set("en");
            Assert.Equal("Oops. You could ask, for example: \"a\" or \"b\".", UserMessages.WithExamples("Oops.", new[] { "a", "b", "c" }));
            RequestLanguage.Set("tr");
            Assert.Equal("Oops. Örneğin şunu sorabilirsiniz: \"a\" ya da \"b\".", UserMessages.WithExamples("Oops.", new[] { "a", "b", "c" }));
            Assert.Equal("Oops.", UserMessages.WithExamples("Oops.", null));
        }
        finally
        {
            RequestLanguage.Set("en");
        }
    }

    /// <summary>
    /// Verifies that the question gate refusals are written in the request language.
    /// </summary>
    [Fact]
    public void TheQuestionGateRefusals_AreWrittenInTheRequestLanguage()
    {
        try
        {
            var limits = new Limits { Current = new GuardrailLimits { AskEnabled = false } };
            var gate = new AskGate(limits, () => new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));

            RequestLanguage.Set("en");
            Assert.Contains("paused by an administrator", gate.TryEnter(Guid.NewGuid()).Denial!.Message);
            RequestLanguage.Set("tr");
            Assert.Contains("duraklatıldı", gate.TryEnter(Guid.NewGuid()).Denial!.Message);
        }
        finally
        {
            RequestLanguage.Set("en");
        }
    }

    /// <summary>
    /// Verifies that every server message that is a parameterised sentence has its parameters filled by the helpers.
    /// </summary>
    [Fact]
    public void EveryServerMessage_ThatIsAParameterisedSentence_HasItsParametersFilledByTheHelpers()
    {
        try
        {
            RequestLanguage.Set("tr");
            Assert.DoesNotContain("{", UserMessages.WhichKindOfRecord(new List<CrmNlQuery.Domain.Metadata.Entities.EntitySummary> { new("account", "accounts", "Hesap"), new("contact", "contacts", "Kişi") }));
            Assert.Contains("(örneğin: Hesap, Kişi)", UserMessages.WhichKindOfRecord(new List<CrmNlQuery.Domain.Metadata.Entities.EntitySummary> { new("account", "accounts", "Hesap"), new("contact", "contacts", "Kişi") }));
            Assert.DoesNotContain("(örneğin", UserMessages.WhichKindOfRecord(new List<CrmNlQuery.Domain.Metadata.Entities.EntitySummary>()));
        }
        finally
        {
            RequestLanguage.Set("en");
        }
    }
}
