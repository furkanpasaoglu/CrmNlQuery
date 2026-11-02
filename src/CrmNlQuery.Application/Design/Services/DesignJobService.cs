using System.Text.Json;
using Microsoft.Extensions.Logging;
using CrmNlQuery.Application.Design.Abstractions;
using CrmNlQuery.Application.Design.Exceptions;
using CrmNlQuery.Application.Design.Models;

namespace CrmNlQuery.Application.Design.Services;

/// <summary>
/// Runs design jobs in the background with per-user and server-wide limits, cancellation and a timeout.
/// </summary>
public sealed class DesignJobService
{
    public const int MinRequestChars = 20;
    public const int MaxRequestChars = 4000;
    private const int MaxServerJobs = 2;

    private readonly IDesignJobStore _store;
    private readonly DesignPlanner _planner;
    private readonly Func<TimeSpan> _timeout;
    private readonly ILogger? _logger;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new object();
    private readonly Dictionary<string, CancellationTokenSource> _running = new Dictionary<string, CancellationTokenSource>(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _activeByOwner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public DesignJobService(IDesignJobStore store, DesignPlanner planner, Func<TimeSpan> timeout, ILogger? logger = null, Func<DateTimeOffset>? clock = null)
    {
        _store = store;
        _planner = planner;
        _timeout = timeout;
        _logger = logger;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Fails jobs left unfinished by a restart and returns how many.
    /// </summary>
    public Task<int> RecoverAsync(CancellationToken ct = default)
        => _store.FailActiveAsync("The server restarted while this design was running. Start it again.", ct);

    /// <summary>
    /// Creates a design job and starts it in the background, returning immediately.
    /// </summary>
    public async Task<DesignJob> StartAsync(string owner, string text, string? prefix, CancellationToken ct = default)
    {
        var request = (text ?? "").Trim();
        if (request.Length < MinRequestChars || request.Length > MaxRequestChars || request.Any(ch => char.IsControl(ch) && ch is not ('\n' or '\r' or '\t')))
        {
            throw new DesignJobException("invalid", $"Describe the requirement in {MinRequestChars}-{MaxRequestChars} characters.");
        }
        var cleanPrefix = string.IsNullOrWhiteSpace(prefix) ? null : prefix.Trim().ToLowerInvariant();
        if (cleanPrefix is not null && !System.Text.RegularExpressions.Regex.IsMatch(cleanPrefix, "^[a-z][a-z0-9]{1,7}$"))
        {
            throw new DesignJobException("invalid", "The publisher prefix must be 2-8 lower-case letters or digits, starting with a letter.");
        }

        var now = _clock();
        var job = new DesignJob
        {
            Id = Guid.NewGuid().ToString("N"),
            Owner = owner,
            Status = DesignJobStatus.Queued,
            Step = "Waiting to start",
            Title = Title(request),
            Prefix = cleanPrefix,
            Request = request,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var cts = new CancellationTokenSource();
        lock (_gate)
        {
            if (_activeByOwner.ContainsKey(owner))
            {
                throw new DesignJobException("busy", "You already have a design running. Wait for it to finish or cancel it.");
            }
            if (_running.Count >= MaxServerJobs)
            {
                throw new DesignJobException("busy", "The assistant is busy with other designs. Try again in a few minutes.");
            }
            _activeByOwner[owner] = job.Id;
            _running[job.Id] = cts;
        }
        try
        {
            await _store.AddAsync(job, ct);
        }
        catch
        {
            Release(job);
            throw;
        }
        _ = Task.Run(() => RunAsync(job, cts));
        return job;
    }

    private static string Title(string request)
    {
        var line = new string(request.Select(ch => char.IsControl(ch) ? ' ' : ch).ToArray()).Trim();
        return line.Length <= 80 ? line : line.Substring(0, 79) + "…";
    }

    private void Release(DesignJob job)
    {
        lock (_gate)
        {
            _running.Remove(job.Id);
            if (_activeByOwner.TryGetValue(job.Owner, out var id) && id == job.Id)
            {
                _activeByOwner.Remove(job.Owner);
            }
        }
    }

    private async Task Save(DesignJob job)
    {
        job.UpdatedAt = _clock();
        try
        {
            await _store.UpdateAsync(job, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not save design job {Job}", job.Id);
        }
    }

    private async Task RunAsync(DesignJob job, CancellationTokenSource cts)
    {
        using var timeout = new CancellationTokenSource(_timeout());
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, timeout.Token);
        try
        {
            job.Status = DesignJobStatus.Running;
            await Save(job);
            var pending = Task.CompletedTask;
            var result = await _planner.RunAsync(new DesignRequest(job.Request, job.Prefix), (step, percent) =>
            {
                job.Step = step;
                job.Percent = percent;
                pending = Save(job);
            }, linked.Token);
            await pending;
            job.Result = JsonSerializer.Serialize(result, DesignJson.Write);
            job.ProblemCount = result.Problems.Count;
            job.Status = DesignJobStatus.Ready;
            job.Step = "Ready";
            job.Percent = 100;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            job.Status = DesignJobStatus.Cancelled;
            job.Step = "Cancelled";
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            job.Status = DesignJobStatus.Failed;
            job.Error = "The design took too long and was stopped. Try a shorter requirement.";
            job.Step = "Failed";
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Design job {Job} failed", job.Id);
            job.Status = DesignJobStatus.Failed;
            job.Error = "The design could not be completed. The model did not return a usable answer or could not be reached. Try again.";
            job.Step = "Failed";
        }
        finally
        {
            job.FinishedAt = _clock();
            await Save(job);
            Release(job);
            cts.Dispose();
        }
    }

    /// <summary>
    /// Returns the design jobs belonging to the given owner, without loading their full results.
    /// </summary>
    public Task<IReadOnlyList<DesignJob>> ListAsync(string owner, CancellationToken ct = default) => _store.ListAsync(owner, 50, ct);

    /// <summary>
    /// Returns the owner design job, or null.
    /// </summary>
    public Task<DesignJob?> GetAsync(string owner, string id, CancellationToken ct = default) => _store.GetAsync(owner, id, ct);

    /// <summary>
    /// Cancels the owner running design job.
    /// </summary>
    public async Task<bool> CancelAsync(string owner, string id, CancellationToken ct = default)
    {
        var job = await _store.GetAsync(owner, id, ct);
        if (job is null)
        {
            return false;
        }
        CancellationTokenSource? cts;
        lock (_gate)
        {
            _running.TryGetValue(id, out cts);
        }
        cts?.Cancel();
        return true;
    }

    /// <summary>
    /// Deletes the owner's design job unless it is still active, returning false when the job is missing.
    /// </summary>
    public async Task<bool> DeleteAsync(string owner, string id, CancellationToken ct = default)
    {
        var job = await _store.GetAsync(owner, id, ct);
        if (job is null)
        {
            return false;
        }
        if (DesignJobStatus.IsActive(job.Status))
        {
            throw new DesignJobException("busy", "Cancel the design before deleting it.");
        }
        return await _store.DeleteAsync(owner, id, ct);
    }

    /// <summary>
    /// Returns the design result of a finished job, or null.
    /// </summary>
    public static DesignResult? ResultOf(DesignJob job)
    {
        return string.IsNullOrEmpty(job.Result) ? null : JsonSerializer.Deserialize<DesignResult>(job.Result, DesignJson.Read);
    }
}
