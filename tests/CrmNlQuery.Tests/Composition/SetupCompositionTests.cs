using CrmNlQuery.Composition;
using Microsoft.Extensions.Configuration;

namespace CrmNlQuery.Tests.Composition;

/// <summary>
/// Tests when the first-run setup wizard is needed.
/// </summary>
public class SetupCompositionTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();
    }

    [Fact]
    public void SetupIsNeededWhenNothingIsConfigured()
    {
        Assert.True(SetupComposition.Needed(Config()));
        Assert.True(SetupComposition.Needed(Config(("Crm:Password", ""))));
    }

    [Fact]
    public void ACrmPasswordFromTheEnvironmentSkipsSetup()
    {
        Assert.False(SetupComposition.Needed(Config(("Crm:Password", "secret"))));
    }

    [Fact]
    public void CompletedSetupIsNotNeededAgain()
    {
        Assert.False(SetupComposition.Needed(Config(("Setup:Completed", "true"))));
    }

    [Fact]
    public void TheKeyFileCanBeChosenWithASetting()
    {
        Assert.Equal("/x/key", SetupComposition.KeyFilePath(Config(("Setup:KeyFile", "/x/key"))));
    }
}
