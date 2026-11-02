using CrmNlQuery.Application.Operations.Settings;

namespace CrmNlQuery.Tests.Application.Operations.Settings;

/// <summary>
/// Holds tests for validating text settings such as table lists and field lists.
/// </summary>
public class SettingsTextValidationTests
{
    private static (bool Ok, string Value, string? Error) Validate(string key, string raw)
        => SettingsCatalog.Validate(SettingsCatalog.All.Single(d => d.Key == key), raw);

    /// <summary>
    /// Verifies that the table list accepts logical names.
    /// </summary>
    [Theory]
    [InlineData("account,contact", "account,contact")]
    [InlineData(" Account , Contact ,account", "account,contact")]
    [InlineData("", "")]
    public void TheTableListAcceptsLogicalNames(string raw, string expected)
    {
        var result = Validate(SettingsCatalog.CreateTables, raw);

        Assert.True(result.Ok);
        Assert.Equal(expected, result.Value);
    }

    /// <summary>
    /// Verifies that the table list refuses other characters.
    /// </summary>
    [Theory]
    [InlineData("account;contact")]
    [InlineData("account-contact")]
    [InlineData("account'")]
    public void TheTableListRefusesOtherCharacters(string raw)
    {
        Assert.False(Validate(SettingsCatalog.CreateTables, raw).Ok);
    }

    /// <summary>
    /// Verifies that the hidden field list accepts table dot field and an exclamation mark.
    /// </summary>
    [Fact]
    public void TheHiddenFieldListAcceptsTableDotFieldAndAnExclamationMark()
    {
        var result = Validate(SettingsCatalog.CreateHiddenFields, "contact.company, !Account.Telephone1");

        Assert.True(result.Ok);
        Assert.Equal("contact.company,!account.telephone1", result.Value);
    }

    /// <summary>
    /// Verifies that the run as setting accepts windows accounts and keeps the case.
    /// </summary>
    [Theory]
    [InlineData(@"CONTOSO\Administrator")]
    [InlineData(@"corp.example\svc-nlquery")]
    [InlineData("user@corp.example")]
    [InlineData("")]
    public void TheRunAsSettingAcceptsWindowsAccountsAndKeepsTheCase(string account)
    {
        var result = Validate(SettingsCatalog.QualityScheduleRunAs, account);

        Assert.True(result.Ok);
        Assert.Equal(account, result.Value);
    }

    /// <summary>
    /// Verifies that the run as setting refuses anything else.
    /// </summary>
    [Theory]
    [InlineData(@"CONTOSO\ops\boss")]
    [InlineData("two words")]
    [InlineData(@"CONTOSO\boss;drop")]
    [InlineData(@"\boss")]
    public void TheRunAsSettingRefusesAnythingElse(string account)
    {
        Assert.False(Validate(SettingsCatalog.QualityScheduleRunAs, account).Ok);
    }

    /// <summary>
    /// Verifies that the run as setting has aLength limit.
    /// </summary>
    [Fact]
    public void TheRunAsSettingHasALengthLimit()
    {
        Assert.False(Validate(SettingsCatalog.QualityScheduleRunAs, new string('a', 101)).Ok);
    }

    /// <summary>
    /// Verifies that the schedule settings need the quality permission on top of settings manage.
    /// </summary>
    [Fact]
    public void TheScheduleSettingsNeedTheQualityPermissionOnTopOfSettingsManage()
    {
        foreach (var key in new[] { SettingsCatalog.QualityScheduleEnabled, SettingsCatalog.QualityScheduleHour, SettingsCatalog.QualityScheduleRepeat, SettingsCatalog.QualityScheduleRunAs })
        {
            Assert.Equal("Quality.Manage", SettingsCatalog.RequiredPermission(key));
        }
        Assert.Equal("DataPolicy.Manage", SettingsCatalog.RequiredPermission(SettingsCatalog.DataPolicy));
        Assert.Null(SettingsCatalog.RequiredPermission(SettingsCatalog.QualityMaxCases));
    }
}
