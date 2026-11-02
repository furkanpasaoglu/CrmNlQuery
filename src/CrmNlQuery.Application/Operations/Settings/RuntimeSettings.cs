using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;
using CrmNlQuery.Application.Operations.Abstractions;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Application.Access.Models;
using CrmNlQuery.Application.Operations.Models;

namespace CrmNlQuery.Application.Operations.Settings;

/// <summary>
/// Holds settings that can change at runtime, resolved from panel override, then file or environment, then code default, with every change audited.
/// </summary>
public sealed class RuntimeSettings
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(30);

    private readonly ISettingsStore _store;
    private readonly Func<string, string?> _fileValue;
    private readonly Func<AuditEntry, CancellationToken, Task> _audit;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _ttl;
    private readonly Action<Exception> _onStoreError;
    private readonly object _gate = new object();
    private IReadOnlyDictionary<string, string> _overrides = new Dictionary<string, string>();
    private GuardrailLimits _limits;

    public RuntimeSettings(ISettingsStore store, Func<string, string?> fileValue, Func<AuditEntry, CancellationToken, Task> audit,
        Func<DateTimeOffset>? clock = null, TimeSpan? ttl = null, Action<Exception>? onStoreError = null)
    {
        _store = store;
        _fileValue = fileValue;
        _audit = audit;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _ttl = ttl ?? DefaultTtl;
        _onStoreError = onStoreError ?? (_ => { });
        _limits = Compute(_overrides);
    }

    public GuardrailLimits Limits
    {
        get
        {
            lock (_gate)
            {
                return _limits;
            }
        }
    }

    /// <summary>
    /// Loads the stored overrides once at startup, falling back to file or default values and reporting the error if the store cannot be read.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await RefreshAsync(ct);
    }

    /// <summary>
    /// Reloads the setting overrides from the store.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            var all = await _store.GetAllAsync(ct);
            lock (_gate)
            {
                _overrides = new Dictionary<string, string>(all, StringComparer.Ordinal);
                _limits = Compute(_overrides);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _onStoreError(ex);
        }
    }

    /// <summary>
    /// Returns the current state of every managed setting.
    /// </summary>
    public IReadOnlyList<SettingState> GetStates()
    {
        IReadOnlyDictionary<string, string> overrides;
        lock (_gate)
        {
            overrides = _overrides;
        }
        var states = new List<SettingState>();
        foreach (var definition in SettingsCatalog.All)
        {
            var baseline = Baseline(definition, out var baselineSource);
            var hasOverride = overrides.TryGetValue(definition.Key, out var value) && SettingsCatalog.Validate(definition, value).Ok;
            var effective = hasOverride ? SettingsCatalog.Validate(definition, value).Value : baseline;
            states.Add(new SettingState(definition, effective, hasOverride ? "panel" : baselineSource, definition.Default));
        }
        return states;
    }

    /// <summary>
    /// Validates and saves a panel override for an allowed setting key and audits it.
    /// </summary>
    public async Task<AccessResult> SetAsync(string actor, string key, string? rawValue, CancellationToken ct = default)
    {
        var definition = SettingsCatalog.Find(key);
        if (definition is null)
        {
            return AccessResult.Fail(AccessError.Invalid, MessageCatalog.Now(Msg.AdmSettingFixed));
        }
        var (ok, value, error) = SettingsCatalog.Validate(definition, rawValue);
        if (!ok)
        {
            return AccessResult.Fail(AccessError.Invalid, error!);
        }
        var before = EffectiveOf(definition);
        try
        {
            await _store.SetAsync(key, value, actor, _clock(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _onStoreError(ex);
            return AccessResult.Fail(AccessError.Unavailable, MessageCatalog.Now(Msg.AdmSettingSaveFailed));
        }
        Replace(overrides => overrides[key] = value);
        await _audit(new AuditEntry(_clock(), actor, "setting.set", key, $"{before} -> {value}"), ct);
        return AccessResult.Success;
    }

    /// <summary>
    /// Removes the panel override so the setting returns to its file or default value.
    /// </summary>
    public async Task<AccessResult> ResetAsync(string actor, string key, CancellationToken ct = default)
    {
        var definition = SettingsCatalog.Find(key);
        if (definition is null)
        {
            return AccessResult.Fail(AccessError.Invalid, MessageCatalog.Now(Msg.AdmSettingFixed));
        }
        var before = EffectiveOf(definition);
        try
        {
            await _store.RemoveAsync(key, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _onStoreError(ex);
            return AccessResult.Fail(AccessError.Unavailable, MessageCatalog.Now(Msg.AdmSettingResetFailed));
        }
        Replace(overrides => overrides.Remove(key));
        await _audit(new AuditEntry(_clock(), actor, "setting.reset", key, $"{before} -> {EffectiveOf(definition)}"), ct);
        return AccessResult.Success;
    }

    private string EffectiveOf(SettingDefinition definition)
    {
        lock (_gate)
        {
            return Effective(definition, _overrides);
        }
    }

    /// <summary>
    /// Returns the effective value of a setting (panel, then file, then default); consumers read it on every use.
    /// </summary>
    public string Get(string key)
    {
        var definition = SettingsCatalog.Find(key) ?? throw new ArgumentException("Unknown setting: " + key, nameof(key));
        return EffectiveOf(definition);
    }

    /// <summary>
    /// Returns the effective value of a setting as an integer.
    /// </summary>
    public int GetInt(string key) => int.Parse(Get(key), System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Returns the effective value of a setting as a boolean.
    /// </summary>
    public bool GetBool(string key) => Get(key) == "true";

    /// <summary>
    /// Returns the effective value of a setting as a decimal.
    /// </summary>
    public decimal GetDecimal(string key) => decimal.Parse(Get(key), System.Globalization.CultureInfo.InvariantCulture);

    private void Replace(Action<Dictionary<string, string>> change)
    {
        lock (_gate)
        {
            var copy = new Dictionary<string, string>(_overrides, StringComparer.Ordinal);
            change(copy);
            _overrides = copy;
            _limits = Compute(copy);
        }
    }

    /// <summary>
    /// Runs the background loop that refreshes settings from the store every interval until shutdown, surviving errors.
    /// </summary>
    public async Task RunRefreshLoopAsync(CancellationToken stopping)
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                await Task.Delay(_ttl, stopping);
                await RefreshAsync(stopping);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private string Baseline(SettingDefinition definition, out string source)
    {
        var raw = _fileValue(definition.Key);
        if (raw is not null)
        {
            var (ok, value, _) = SettingsCatalog.Validate(definition, raw);
            if (ok)
            {
                source = "file";
                return value;
            }
        }
        source = "default";
        return definition.Default;
    }

    private string Effective(SettingDefinition definition, IReadOnlyDictionary<string, string> overrides)
    {
        var value = Baseline(definition, out _);
        if (overrides.TryGetValue(definition.Key, out var over))
        {
            var (ok, fixedValue, _) = SettingsCatalog.Validate(definition, over);
            if (ok)
            {
                value = fixedValue;
            }
        }
        return value;
    }

    private GuardrailLimits Compute(IReadOnlyDictionary<string, string> overrides)
    {
        var limits = new GuardrailLimits();
        foreach (var definition in SettingsCatalog.All.Where(d => SettingsCatalog.IsLimitKey(d.Key)))
        {
            limits = SettingsCatalog.Apply(limits, definition.Key, Effective(definition, overrides));
        }
        return limits.Normalize();
    }
}
