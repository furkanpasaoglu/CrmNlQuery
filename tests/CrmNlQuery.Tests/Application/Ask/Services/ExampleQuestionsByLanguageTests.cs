using CrmNlQuery.Application.Ask.Services;

namespace CrmNlQuery.Tests.Application.Ask.Services;

/// <summary>
/// Unit tests for choosing the example question list by language, falling back to the general list when the language list is empty.
/// </summary>
public class ExampleQuestionsByLanguageTests
{
    private static readonly IReadOnlyList<string> General = new List<string> { "Show accounts in London" };
    private static readonly IReadOnlyList<string> Turkish = new List<string> { "Londra'daki firmaları göster" };

    /// <summary>
    /// Verifies that a Turkish request with a Turkish list gets that list.
    /// </summary>
    [Fact]
    public void TurkishRequest_WithATurkishList_GetsIt()
        => Assert.Same(Turkish, ExampleQuestions.For("tr", General, Turkish));

    /// <summary>
    /// Verifies that a Turkish request without a Turkish list gets the general list.
    /// </summary>
    [Fact]
    public void TurkishRequest_WithoutATurkishList_GetsTheGeneralList()
        => Assert.Same(General, ExampleQuestions.For("tr", General, new List<string>()));

    /// <summary>
    /// Verifies that an English request gets the general list even when a Turkish list exists.
    /// </summary>
    [Fact]
    public void EnglishRequest_GetsTheGeneralList_EvenWhenATurkishListExists()
        => Assert.Same(General, ExampleQuestions.For("en", General, Turkish));
}
