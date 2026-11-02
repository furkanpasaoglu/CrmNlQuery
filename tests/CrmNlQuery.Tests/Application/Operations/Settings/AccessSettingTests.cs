using CrmNlQuery.Application.Operations.Settings;

namespace CrmNlQuery.Tests.Application.Operations.Settings;

/// <summary>
/// Tests the Access:RequireAppUse three-valued choice setting, defaulting to off.
/// </summary>
public class AccessSettingTests
{
    private static SettingDefinition Definition() => SettingsCatalog.Find(SettingsCatalog.AccessRequireAppUse)!;

    /// <summary>
    /// Verifies that the setting is in the catalog as a choice defaulting to off.
    /// </summary>
    [Fact]
    public void TheSettingIsInTheCatalog_AsAChoice_DefaultingToOff()
    {
        var definition = Definition();

        Assert.Equal(SettingKind.Choice, definition.Kind);
        Assert.Equal("off", definition.Default);
        Assert.Equal(new[] { "off", "report", "on" }, definition.Choices);
        Assert.Equal("Access", definition.Group);
        Assert.Contains("Access", SettingsCatalog.Groups);
    }

    /// <summary>
    /// Verifies that a choice is accepted case insensitively and stored in its listed form.
    /// </summary>
    [Theory]
    [InlineData("off", "off")]
    [InlineData("REPORT", "report")]
    [InlineData("  On ", "on")]
    public void AChoiceIsAccepted_CaseInsensitively_AndStoredInItsListedForm(string raw, string expected)
    {
        var (ok, value, error) = SettingsCatalog.Validate(Definition(), raw);

        Assert.True(ok);
        Assert.Equal(expected, value);
        Assert.Null(error);
    }

    /// <summary>
    /// Verifies that anything else is refused with the list of values.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("maybe")]
    [InlineData("true")]
    [InlineData("1")]
    public void AnythingElse_IsRefused_WithTheListOfValues(string raw)
    {
        var (ok, _, error) = SettingsCatalog.Validate(Definition(), raw);

        Assert.False(ok);
        Assert.Contains("off, report, on", error);
    }

    /// <summary>
    /// Verifies that the security log retention is its own setting defaulting to one year with zero meaning keep forever.
    /// </summary>
    [Fact]
    public void TheSecurityLogRetention_IsItsOwnSetting_DefaultingToOneYear_WithZeroMeaningKeepForever()
    {
        var definition = SettingsCatalog.Find(SettingsCatalog.SecurityLogRetentionDays)!;

        Assert.Equal("365", definition.Default);
        Assert.Equal("Retention", definition.Group);
        Assert.True(SettingsCatalog.Validate(definition, "0").Ok);
        Assert.True(SettingsCatalog.Validate(definition, "3650").Ok);
        Assert.False(SettingsCatalog.Validate(definition, "-1").Ok);
        Assert.False(SettingsCatalog.Validate(definition, "3651").Ok);
        Assert.NotEqual(SettingsCatalog.MetricsRetentionDays, definition.Key);
    }

    /// <summary>
    /// Verifies that every definition's default is valid for its own kind.
    /// </summary>
    [Fact]
    public void EveryDefinitionsDefault_IsValidForItsOwnKind()
    {
        foreach (var definition in SettingsCatalog.All)
        {
            var (ok, _, error) = SettingsCatalog.Validate(definition, definition.Default);
            Assert.True(ok, definition.Key + ": " + error);
        }
    }
}
