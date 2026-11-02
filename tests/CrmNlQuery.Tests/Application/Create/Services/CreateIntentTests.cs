using CrmNlQuery.Application.Create.Services;
using CrmNlQuery.Domain.Create.Entities;

namespace CrmNlQuery.Tests.Application.Create.Services;

/// <summary>
/// Unit tests for the rule-based detection of record-creation requests, including queries that must not trigger creation.
/// </summary>
public class CreateIntentTests
{
    private static readonly IReadOnlyList<CreateSchema> Writable = new[] { CreateValidatorTests.AccountSchema() };

    /// <summary>
    /// Verifies that the rule needs a create verb and a writable table.
    /// </summary>
    [Theory]
    [InlineData("Alpine adında hesap oluştur", true)]
    [InlineData("yeni bir hesap ekle", true)]
    [InlineData("hesabı kaydet: Alpine", true)]
    [InlineData("create an account named Alpine", true)]
    [InlineData("add a new account", true)]
    [InlineData("Londra'daki hesapları getir", false)]
    [InlineData("yeni hesapları göster", false)]
    [InlineData("hesaplara eklentiler getir", false)]
    [InlineData("kişi ekle", false)]
    [InlineData("", false)]
    public void TheRule_NeedsACreateVerb_AndAWritableTable(string text, bool expected)
    {
        Assert.Equal(expected, CreateIntent.Matches(text, Writable));
    }
}
