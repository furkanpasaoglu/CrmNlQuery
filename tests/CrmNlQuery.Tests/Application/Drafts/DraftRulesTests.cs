using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Drafts.Models;
using CrmNlQuery.Application.Drafts.Services;

namespace CrmNlQuery.Tests.Application.Drafts;

/// <summary>
/// Tests the email draft rules: intent detection, tone, placeholder validation, filling and composer retries.
/// </summary>
public class DraftRulesTests
{
    /// <summary>
    /// Verifies that the intent rule recognises email draft requests and not data questions.
    /// </summary>
    [Theory]
    [InlineData("Brightwave için takip e-postası yaz", true)]
    [InlineData("Brightwave'e bir mail taslağı hazırla", true)]
    [InlineData("Write a follow-up email to Brightwave", true)]
    [InlineData("draft an email for this customer", true)]
    [InlineData("bu müşteriye eposta hazırla", true)]
    [InlineData("Compose a short email to Acme", true)]
    [InlineData("Brightwave'in mail adresini yaz", false)]
    [InlineData("e-postası olan kişileri listele", false)]
    [InlineData("show contacts with an email address", false)]
    [InlineData("Show accounts in London", false)]
    [InlineData("Brightwave için hatırlatma yaz", false)]
    [InlineData("Brightwave'e buluşma talebi için e-posta yaz", true)]
    [InlineData("Brightwave kişilerinin e-postalarını yazdır", false)]
    [InlineData("Brightwave'e gönderilen e-postalar", false)]
    [InlineData("show emails sent to Brightwave", false)]
    [InlineData("email", false)]
    [InlineData("", false)]
    public void TheIntentRule_RecognisesEmailDraftRequests_AndNotDataQuestions(string question, bool expected)
    {
        Assert.Equal(expected, DraftIntent.Matches(question));
    }

    /// <summary>
    /// Verifies that the tone is read from the question default formal.
    /// </summary>
    [Theory]
    [InlineData("kısa bir e-posta yaz", DraftIntent.Short)]
    [InlineData("write a brief email", DraftIntent.Short)]
    [InlineData("samimi bir mail hazırla", DraftIntent.Warm)]
    [InlineData("write a friendly email", DraftIntent.Warm)]
    [InlineData("resmi bir e-posta yaz", DraftIntent.Formal)]
    [InlineData("e-posta yaz", DraftIntent.Formal)]
    public void TheToneIsReadFromTheQuestion_DefaultFormal(string question, string expected)
    {
        Assert.Equal(expected, DraftIntent.ToneOf(question));
    }

    private static readonly string[] Offered = { "customer_name", "contact_first_name", "sender_name" };

    /// <summary>
    /// Verifies that a clean template passes.
    /// </summary>
    [Fact]
    public void ACleanTemplate_Passes()
    {
        var problems = DraftValidator.Validate("Following up with {{customer_name}}", "Dear {{contact_first_name}},\n\nI wanted to follow up.\n\nBest regards,\n{{sender_name}}", Offered);

        Assert.Empty(problems);
    }

    /// <summary>
    /// Verifies that a placeholder that was not offered is rejected even if it is in the catalog.
    /// </summary>
    [Fact]
    public void APlaceholderThatWasNotOffered_IsRejected_EvenIfItIsInTheCatalog()
    {
        var problems = DraftValidator.Validate("Hello", "You have {{open_case_count}} open cases.", Offered);

        Assert.Contains(problems, p => p.Contains("open_case_count"));
    }

    /// <summary>
    /// Verifies that an invented placeholder is rejected.
    /// </summary>
    [Fact]
    public void AnInventedPlaceholder_IsRejected()
    {
        Assert.NotEmpty(DraftValidator.Validate("Hello", "Hi {{customer_phone}}", Offered));
    }

    /// <summary>
    /// Verifies that digits outside placeholders are rejected because the model must not invent facts.
    /// </summary>
    [Theory]
    [InlineData("As we discussed on 12 March, here is more.")]
    [InlineData("We spoke 3 times last week.")]
    [InlineData("Our offer is 5000 dollars.")]
    public void DigitsOutsidePlaceholders_AreRejected_BecauseTheModelMustNotInventFacts(string body)
    {
        var problems = DraftValidator.Validate("Hello", body, Offered);

        Assert.Contains(problems, p => p.Contains("digits"));
    }

    /// <summary>
    /// Verifies that braces that are not a placeholder HTML and a multi line subject are rejected.
    /// </summary>
    [Fact]
    public void BracesThatAreNotAPlaceholder_Html_AndAMultiLineSubject_AreRejected()
    {
        Assert.NotEmpty(DraftValidator.Validate("Hello", "Hi {name}", Offered));
        Assert.NotEmpty(DraftValidator.Validate("Hello", "Hi <b>there</b>", Offered));
        Assert.NotEmpty(DraftValidator.Validate("Line one\nLine two", "Body", Offered));
    }

    /// <summary>
    /// Verifies that empty and too long texts are rejected.
    /// </summary>
    [Fact]
    public void EmptyAndTooLongTexts_AreRejected()
    {
        Assert.NotEmpty(DraftValidator.Validate("", "Body", Offered));
        Assert.NotEmpty(DraftValidator.Validate("Subject", " ", Offered));
        Assert.NotEmpty(DraftValidator.Validate(new string('s', 121), "Body", Offered));
        Assert.NotEmpty(DraftValidator.Validate("Subject", new string('b', 2001), Offered));
    }

    /// <summary>
    /// Verifies that the filler replaces placeholders and keeps a missing one as a visible marker.
    /// </summary>
    [Fact]
    public void TheFiller_ReplacesPlaceholders_AndKeepsAMissingOneAsAVisibleMarker()
    {
        var values = new Dictionary<string, string> { ["customer_name"] = "Brightwave" };

        var filled = DraftFiller.Fill("Hello {{customer_name}}, regards {{sender_name}}", values);

        Assert.Equal("Hello Brightwave, regards [your name]", filled.Text);
        Assert.Equal(new[] { "[your name]" }, filled.Unresolved);
    }

    /// <summary>
    /// Verifies that a brace sequence inside a value is not expanded again.
    /// </summary>
    [Fact]
    public void ABraceSequenceInsideAValue_IsNotExpandedAgain()
    {
        var values = new Dictionary<string, string> { ["customer_name"] = "Acme {{sender_name}}", ["sender_name"] = "SECRET" };

        var filled = DraftFiller.Fill("Hi {{customer_name}}", values);

        Assert.Equal("Hi Acme {{sender_name}}", filled.Text);
    }

    /// <summary>
    /// Verifies that the composer retries with the problem list and gives up after three attempts.
    /// </summary>
    [Fact]
    public async Task TheComposer_RetriesWithTheProblemList_AndGivesUpAfterThreeAttempts()
    {
        var bad = """{"subject":"Hi","body":"We met 3 times."}""";
        var llm = new FakeLlm(bad, bad, bad);

        var result = await new DraftComposer(llm).ComposeAsync("write an email", DraftIntent.Formal, new[] { DraftPlaceholders.All[0] });

        Assert.Null(result);
        Assert.Equal(3, llm.Calls.Count);
        Assert.Contains("digits", llm.Calls[1].User);
    }

    /// <summary>
    /// Verifies that the composer accepts a fixed reply and treats broken JSON as a failed attempt.
    /// </summary>
    [Fact]
    public async Task TheComposer_AcceptsAFixedReply_AndTreatsBrokenJsonAsAFailedAttempt()
    {
        var llm = new FakeLlm("not json", """{"subject":" Hello ","body":" Dear {{customer_name}} "}""");

        var result = await new DraftComposer(llm).ComposeAsync("write an email", DraftIntent.Formal, new[] { DraftPlaceholders.All[0] });

        Assert.NotNull(result);
        Assert.Equal("Hello", result!.Subject);
        Assert.Equal("Dear {{customer_name}}", result.Body);
    }

    /// <summary>
    /// Verifies that the prompt forbids invented facts and lists only the offered placeholders.
    /// </summary>
    [Fact]
    public async Task ThePromptForbidsInventedFacts_AndListsOnlyTheOfferedPlaceholders()
    {
        var llm = new FakeLlm("""{"subject":"Hello","body":"Hi {{customer_name}}"}""");

        await new DraftComposer(llm).ComposeAsync("write an email", DraftIntent.Warm, new[] { DraftPlaceholders.All[0] });

        Assert.Contains("NEVER write any fact", llm.Calls[0].System);
        Assert.Contains("as we discussed last week", llm.Calls[0].System);
        Assert.Contains("{{customer_name}}", llm.Calls[0].User);
        Assert.DoesNotContain("{{open_case_count}}", llm.Calls[0].User);
        Assert.Contains("Tone: warm", llm.Calls[0].User);
    }
}
