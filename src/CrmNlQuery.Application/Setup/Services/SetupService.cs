using System.Security.Cryptography;
using System.Text;
using CrmNlQuery.Application.Setup.Abstractions;
using CrmNlQuery.Application.Setup.Models;

namespace CrmNlQuery.Application.Setup.Services;

/// <summary>
/// The first-run setup wizard: checks the one-time setup code (with a lock after wrong tries), tests the CRM and the model, and saves the settings with secrets encrypted.
/// </summary>
public sealed class SetupService
{
    public const int MaxWrongCodes = 3;
    public static readonly TimeSpan LockFor = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan SessionFor = TimeSpan.FromMinutes(30);

    private readonly object _gate = new object();
    private readonly Func<CancellationToken, Task<IAppConfigStore?>> _openStore;
    private readonly Func<IConfigCipher> _createCipher;
    private readonly Func<bool> _needsSetup;
    private readonly ICrmProbe _crm;
    private readonly IModelProbe _model;
    private readonly Func<DateTimeOffset> _clock;
    private readonly string _codeHash;
    private int _wrongCodes;
    private DateTimeOffset _lockedUntil = DateTimeOffset.MinValue;
    private string? _sessionHash;
    private DateTimeOffset _sessionUntil = DateTimeOffset.MinValue;
    private bool _completed;

    public SetupService(string code, Func<bool> needsSetup, Func<CancellationToken, Task<IAppConfigStore?>> openStore, Func<IConfigCipher> createCipher, ICrmProbe crm, IModelProbe model, Func<DateTimeOffset>? clock = null)
    {
        _codeHash = Hash(Normalize(code));
        _needsSetup = needsSetup;
        _openStore = openStore;
        _createCipher = createCipher;
        _crm = crm;
        _model = model;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Makes a new setup code that is easy to read out and type: three groups of four letters and digits without look-alike characters.
    /// </summary>
    public static string NewCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var chars = new char[12];
        for (var i = 0; i < chars.Length; i++) { chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]; }
        var text = new string(chars);
        return text.Substring(0, 4) + "-" + text.Substring(4, 4) + "-" + text.Substring(8, 4);
    }

    /// <summary>
    /// Tells whether setup is still needed, whether the code is locked and whether the database and the encryption key work.
    /// </summary>
    public async Task<SetupStatus> StatusAsync(CancellationToken ct = default)
    {
        var needs = _needsSetup() && !Completed;
        if (!needs)
        {
            return new SetupStatus(false, false, true, false);
        }
        var databaseAvailable = false;
        try { databaseAvailable = await _openStore(ct) is not null; }
        catch (Exception ex) when (ex is not OperationCanceledException) { databaseAvailable = false; }
        lock (_gate)
        {
            return new SetupStatus(true, _clock() < _lockedUntil, databaseAvailable, false);
        }
    }

    /// <summary>
    /// Checks the setup code and returns a session value for the next steps, or an error code: locked (too many wrong codes) or invalid_code.
    /// </summary>
    public SetupResult<string> Unlock(string? code)
    {
        lock (_gate)
        {
            if (!_needsSetup() || _completed)
            {
                return SetupResult<string>.Failure("not_available");
            }
            var now = _clock();
            if (now < _lockedUntil)
            {
                return SetupResult<string>.Failure("locked");
            }
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(Normalize(code ?? ""))), Encoding.ASCII.GetBytes(_codeHash)))
            {
                _wrongCodes++;
                if (_wrongCodes >= MaxWrongCodes)
                {
                    _wrongCodes = 0;
                    _lockedUntil = now.Add(LockFor);
                    return SetupResult<string>.Failure("locked");
                }
                return SetupResult<string>.Failure("invalid_code");
            }
            _wrongCodes = 0;
            var session = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            _sessionHash = Hash(session);
            _sessionUntil = now.Add(SessionFor);
            return SetupResult<string>.Success(session);
        }
    }

    /// <summary>
    /// Tests the CRM connection typed in the wizard; nothing is saved.
    /// </summary>
    public async Task<SetupResult<IReadOnlyList<SetupCheck>>> TestCrmAsync(string? session, CrmProbeInput input, CancellationToken ct = default)
    {
        var refusal = Refuse<IReadOnlyList<SetupCheck>>(session);
        if (refusal is not null) { return refusal; }
        var invalid = SetupInputValidator.ValidateCrm(input);
        if (invalid is not null) { return SetupResult<IReadOnlyList<SetupCheck>>.Failure(invalid.Value.Error, invalid.Value.Field); }
        return SetupResult<IReadOnlyList<SetupCheck>>.Success(await _crm.RunAsync(input, ct));
    }

    /// <summary>
    /// Tests the model server typed in the wizard; nothing is saved.
    /// </summary>
    public async Task<SetupResult<IReadOnlyList<SetupCheck>>> TestModelAsync(string? session, ModelProbeInput input, bool confirmLocalNetwork, bool confirmCloudLab, CancellationToken ct = default)
    {
        var refusal = Refuse<IReadOnlyList<SetupCheck>>(session);
        if (refusal is not null) { return refusal; }
        var invalid = SetupInputValidator.ValidateModel(input, confirmLocalNetwork, confirmCloudLab);
        if (invalid is not null) { return SetupResult<IReadOnlyList<SetupCheck>>.Failure(invalid.Value.Error, invalid.Value.Field); }
        return SetupResult<IReadOnlyList<SetupCheck>>.Success(await _model.RunAsync(input, ct));
    }

    /// <summary>
    /// Saves the settings (secrets encrypted, a signing key generated, sign-in through the CRM chosen) and marks setup finished; it can succeed only once.
    /// </summary>
    public async Task<SetupResult<bool>> CompleteAsync(string? session, SetupInput input, CancellationToken ct = default)
    {
        var refusal = Refuse<bool>(session);
        if (refusal is not null) { return refusal; }
        var invalid = SetupInputValidator.Validate(input);
        if (invalid is not null) { return SetupResult<bool>.Failure(invalid.Value.Error, invalid.Value.Field); }
        lock (_gate)
        {
            if (_completed) { return SetupResult<bool>.Failure("not_available"); }
            _completed = true;
        }
        try
        {
            var store = await _openStore(ct);
            if (store is null) { throw new InvalidOperationException("database"); }
            await store.SetManyAsync(BuildEntries(input, _createCipher()), ct);
            return SetupResult<bool>.Success(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lock (_gate) { _completed = false; }
            return SetupResult<bool>.Failure(ex.Message == "database" ? "database_unavailable" : "save_failed");
        }
    }

    /// <summary>
    /// Builds the stored settings from the wizard input; secrets are encrypted with the given cipher and a new random signing key is generated.
    /// </summary>
    public static IReadOnlyList<AppConfigEntry> BuildEntries(SetupInput input, IConfigCipher cipher)
    {
        var crmUri = SetupInputValidator.ParseHttpUrl(input.Crm.BaseUrl)!;
        var list = new List<AppConfigEntry>
        {
            new AppConfigEntry("Crm:BaseUrl", input.Crm.BaseUrl.Trim().TrimEnd('/'), false),
            new AppConfigEntry("Crm:Domain", input.Crm.Domain.Trim(), false),
            new AppConfigEntry("Crm:Username", input.Crm.Username.Trim(), false),
            new AppConfigEntry("Crm:Password", cipher.Encrypt(input.Crm.Password), true),
            new AppConfigEntry("Cors:AllowedOrigins", crmUri.GetLeftPart(UriPartial.Authority), false),
            new AppConfigEntry("Service:PublicUrl", input.ServiceUrl.Trim().TrimEnd('/'), false),
            new AppConfigEntry("Identity:Mode", "CrmProof", false),
            new AppConfigEntry("Auth:SigningKey", cipher.Encrypt(Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))), true),
            new AppConfigEntry("Admin:Bootstrap:0", input.AdminAccount.Trim(), false),
            new AppConfigEntry("Llm:Provider", input.Model.Provider, false),
            new AppConfigEntry("Setup:Completed", "true", false),
        };
        if (input.Model.Provider == "Anthropic")
        {
            list.Add(new AppConfigEntry("Llm:AllowCloud", "true", false));
            list.Add(new AppConfigEntry("Anthropic:Model", input.Model.Model.Trim(), false));
            list.Add(new AppConfigEntry("Anthropic:ApiKey", cipher.Encrypt(input.Model.ApiKey!), true));
            return list;
        }
        var section = input.Model.Provider;
        var uri = SetupInputValidator.ParseHttpUrl(input.Model.BaseUrl)!;
        list.Add(new AppConfigEntry(section + ":BaseUrl", input.Model.BaseUrl.Trim().TrimEnd('/'), false));
        list.Add(new AppConfigEntry(section + ":Model", input.Model.Model.Trim(), false));
        if (!uri.IsLoopback)
        {
            list.Add(new AppConfigEntry(section + ":TrustedHosts:0", uri.Host, false));
        }
        if (!string.IsNullOrWhiteSpace(input.Model.ApiKey))
        {
            list.Add(new AppConfigEntry(section + ":ApiKey", cipher.Encrypt(input.Model.ApiKey), true));
        }
        return list;
    }

    private bool Completed
    {
        get { lock (_gate) { return _completed; } }
    }

    private SetupResult<T>? Refuse<T>(string? session)
    {
        lock (_gate)
        {
            if (!_needsSetup() || _completed) { return SetupResult<T>.Failure("not_available"); }
            if (session is null || _sessionHash is null || _clock() >= _sessionUntil
                || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(session)), Encoding.ASCII.GetBytes(_sessionHash)))
            {
                return SetupResult<T>.Failure("unauthorized");
            }
            return null;
        }
    }

    private static string Normalize(string code) => new string(code.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
