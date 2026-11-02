using CrmNlQuery.Application.Setup.Abstractions;
using CrmNlQuery.Application.Setup.Models;
using Microsoft.Extensions.Configuration;

namespace CrmNlQuery.Composition;

/// <summary>
/// Configuration source for the settings stored by the setup wizard.
/// </summary>
public sealed class AppConfigurationSource : IConfigurationSource
{
    private readonly Func<IReadOnlyList<AppConfigEntry>> _read;
    private readonly Func<IConfigCipher?> _cipher;

    public AppConfigurationSource(Func<IReadOnlyList<AppConfigEntry>> read, Func<IConfigCipher?> cipher)
    {
        _read = read;
        _cipher = cipher;
    }

    /// <summary>
    /// Creates the provider that reads the settings.
    /// </summary>
    public IConfigurationProvider Build(IConfigurationBuilder builder) => new AppConfigurationProvider(_read, _cipher);
}
