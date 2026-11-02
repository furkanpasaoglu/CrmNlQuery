using System.Diagnostics;
using System.Text.Json;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Application.Quality.Abstractions;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Quality.Entities;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Application.Quality.Runs;

/// <summary>
/// Runs acceptance-set case questions through the product's question pipeline, without plan or analysis caches and without seeing the case's reference.
/// </summary>
public sealed class PipelineQualityRunner : IQualityCaseRunner
{
    private const int MaxRounds = 5;

    private readonly AskPipeline _pipeline;

    public PipelineQualityRunner(AskPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    /// <summary>
    /// Runs one acceptance case as the caller and returns the candidate run.
    /// </summary>
    public async Task<QualityCandidateRun> RunAsync(QualityCase item, Caller caller, CancellationToken ct = default)
    {
        var watch = Stopwatch.StartNew();
        var fixedAnswers = ParseAnswers(item.AnswersJson);
        var answers = new Dictionary<string, string>();
        try
        {
            var outcome = await _pipeline.RunAsync(new AskRequest(item.Question, answers, caller), ct);
            for (var round = 0; round < MaxRounds && outcome is QuestionOutcome question; round++)
            {
                if (question.AnswerKey == "" || !fixedAnswers.TryGetValue(question.AnswerKey, out var value))
                {
                    return Result("failure", null, "The tool asked a question instead of answering: " + question.Text, watch);
                }
                answers[question.AnswerKey] = value;
                outcome = await _pipeline.RunAsync(new AskRequest(item.Question, answers, caller), ct);
            }
            return outcome switch
            {
                AnswerOutcome answer => Result("answer", answer.Table, "", watch),
                FailureOutcome failure => Result("failure", null, failure.Kind + ": " + failure.Message, watch),
                QuestionOutcome question => Result("failure", null, "The tool kept asking questions: " + question.Text, watch),
                _ => Result("failure", null, "The tool did not return a table answer.", watch),
            };
        }
        catch (CrmException ex) when (ex.IsPermissionDenied)
        {
            return Result("unreadable", null, "Your CRM permissions do not allow reading this data.", watch);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result("failure", null, "The question could not be run (" + ex.GetType().Name + ").", watch);
        }
    }

    private static QualityCandidateRun Result(string kind, CrmNlQuery.Domain.Ask.Entities.ResultTable? table, string message, Stopwatch watch)
        => new QualityCandidateRun(kind, table, message, (int)Math.Min(int.MaxValue, watch.ElapsedMilliseconds));

    private static Dictionary<string, string> ParseAnswers(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return new Dictionary<string, string>(); }
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>(); }
        catch (JsonException) { return new Dictionary<string, string>(); }
    }
}
