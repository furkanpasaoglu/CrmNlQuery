using CrmNlQuery.Application.DataPolicy.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Application.Operations.Settings;

namespace CrmNlQuery.Tests.Application.DataPolicy.Services;

/// <summary>
/// Tests the data policy defaults and switch.
/// </summary>
public class DataPolicyDefaultsTests
{
    /// <summary>
    /// Verifies that the built-in starting list is the service account's read tables.
    /// </summary>
    [Fact]
    public void TheBuiltInStartingList_IsTheServiceAccountsReadTables()
    {
        Assert.Equal(new[] { "account", "contact", "contract", "contractdetail", "incident", "opportunity", "product", "salesorder" },
            DataPolicyDefaults.Tables.OrderBy(x => x).ToArray());
    }

    /// <summary>
    /// Verifies that without a configured list the built in list is used.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" , ,")]
    public void WithoutAConfiguredList_TheBuiltInListIsUsed(string? configured)
    {
        Assert.Equal(DataPolicyDefaults.Tables.OrderBy(x => x), DataPolicyDefaults.Parse(configured).OrderBy(x => x));
    }

    /// <summary>
    /// Verifies that a configured list replaces the built-in one, trimmed, lowercased and without duplicates.
    /// </summary>
    [Fact]
    public void AConfiguredList_ReplacesTheBuiltInOne_TrimmedLowerCasedAndWithoutDuplicates()
    {
        var parsed = DataPolicyDefaults.Parse(" Account, new_Model ,account,, new_arac ");

        Assert.Equal(new[] { "account", "new_arac", "new_model" }, parsed.OrderBy(x => x).ToArray());
    }

    /// <summary>
    /// Verifies that the rule normaliser lowercases and rejects invalid rules.
    /// </summary>
    [Fact]
    public void TheRuleNormaliser_LowerCases_AndRejectsInvalidRules()
    {
        var rule = DataPolicyRules.Normalize(new DataPolicyRule("FIELD", " Account ", " NEW_X ", false, "a", DateTimeOffset.UnixEpoch));

        Assert.Equal("field", rule.Kind);
        Assert.Equal("account", rule.Entity);
        Assert.Equal("new_x", rule.Attribute);
        Assert.Throws<ArgumentException>(() => DataPolicyRules.Normalize(new DataPolicyRule("field", "account", null, true, "a", DateTimeOffset.UnixEpoch)));
    }

    /// <summary>
    /// Verifies that the switch is a runtime choice and on by default.
    /// </summary>
    [Fact]
    public void TheSwitch_IsARuntimeChoice_OnByDefault()
    {
        var setting = SettingsCatalog.Find("Access:DataPolicy")!;

        Assert.Equal(SettingKind.Choice, setting.Kind);
        Assert.Equal("on", setting.Default);
        Assert.Equal(new[] { "off", "on" }, setting.Choices!.OrderBy(x => x).ToArray());
        Assert.Contains(setting.Group, SettingsCatalog.Groups);
    }
}
