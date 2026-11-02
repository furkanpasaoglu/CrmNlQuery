using CrmNlQuery.Application.Setup.Models;
using CrmNlQuery.Application.Setup.Services;
using CrmNlQuery.Composition;
using Microsoft.Extensions.Configuration;

namespace CrmNlQuery.Tests.Composition;

/// <summary>
/// Tests that settings from the setup wizard sit between appsettings.json and environment variables, and that an unreadable store simply provides nothing.
/// </summary>
public class AppConfigurationTests
{
    private static AesGcmConfigCipher Cipher() => new AesGcmConfigCipher(Enumerable.Repeat((byte)3, AesGcmConfigCipher.KeyBytes).ToArray());

    private static IConfigurationRoot Build(Func<IReadOnlyList<AppConfigEntry>> read, Dictionary<string, string?> appsettings, Dictionary<string, string?> environment)
    {
        var builder = new ConfigurationBuilder();
        builder.AddInMemoryCollection(appsettings);
        builder.Add(new AppConfigurationSource(read, () => Cipher()));
        builder.AddInMemoryCollection(environment);
        return builder.Build();
    }

    [Fact]
    public void TheOrderIsAppsettingsThenStoredSettingsThenEnvironment()
    {
        var read = () => (IReadOnlyList<AppConfigEntry>)new[]
        {
            new AppConfigEntry("Crm:BaseUrl", "https://stored.example", false),
            new AppConfigEntry("Crm:Domain", "STORED", false),
            new AppConfigEntry("Crm:Password", Cipher().Encrypt("stored-secret"), true),
        };

        var config = Build(read,
            new Dictionary<string, string?> { ["Crm:BaseUrl"] = "http://default", ["Crm:Domain"] = "DEFAULT", ["Crm:Username"] = "Administrator" },
            new Dictionary<string, string?> { ["Crm:Domain"] = "FROM_ENV" });

        Assert.Equal("https://stored.example", config["Crm:BaseUrl"]);
        Assert.Equal("FROM_ENV", config["Crm:Domain"]);
        Assert.Equal("Administrator", config["Crm:Username"]);
        Assert.Equal("stored-secret", config["Crm:Password"]);
    }

    [Fact]
    public void WhenTheStoreCannotBeReadNothingIsProvidedAndTheNextReloadPicksUpTheSettings()
    {
        var available = false;
        var read = () => (IReadOnlyList<AppConfigEntry>)(available ? new[] { new AppConfigEntry("Crm:BaseUrl", "https://later.example", false) } : new AppConfigEntry[0]);
        var config = Build(read, new Dictionary<string, string?> { ["Crm:BaseUrl"] = "http://default" }, new Dictionary<string, string?>());
        Assert.Equal("http://default", config["Crm:BaseUrl"]);

        available = true;
        config.Reload();

        Assert.Equal("https://later.example", config["Crm:BaseUrl"]);
    }

    [Fact]
    public void ASecretThatCannotBeDecryptedIsReportedByKeyAndNeverShowsItsStoredText()
    {
        var read = () => (IReadOnlyList<AppConfigEntry>)new[] { new AppConfigEntry("Crm:Password", "v1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", true) };

        var config = Build(read, new Dictionary<string, string?>(), new Dictionary<string, string?>());

        Assert.Null(config["Crm:Password"]);
        Assert.Equal("Crm:Password", config["Setup:UndecryptableKeys"]);
    }
}
