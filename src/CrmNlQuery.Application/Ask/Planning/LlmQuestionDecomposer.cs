using System.Text.Json;
using CrmNlQuery.Domain.Ask.Steps;
using CrmNlQuery.Application.Ask.Abstractions;

namespace CrmNlQuery.Application.Ask.Planning;

/// <summary>
/// Asks the model to split a question into ordered steps for conditional and dependent queries.
/// </summary>
public sealed class LlmQuestionDecomposer : IQuestionDecomposer
{
    private const int MaxAttempts = 2;
    private const int MaxSteps = 3;

    private const string Schema = """
    {"type":"object",
     "properties":{
       "steps":{"type":"array","items":{"type":"object",
         "properties":{
           "question":{"type":"string"},
           "condition":{"type":"string","enum":["always","if_previous_empty","if_previous_not_empty"]},
           "usesStep":{"type":"integer"}},
         "required":["question","condition"]}}},
     "required":["steps"]}
    """;

    private const string SystemPrompt = """
    You split a business analyst's question about a CRM into ordered steps. Reply with JSON only.
    Most questions are ONE step: return exactly one step whose question is the user's question unchanged, condition "always" and usesStep 0.
    Split into several steps ONLY in these cases:
    1. The user explicitly gives a fallback or a condition, for example "if there are none, show ...", "otherwise ...", "yoksa ...", "bulunmazsa ...", "boşsa ...".
    2. DEPENDENT steps: the answer needs the records found by an earlier step. Example: "the contacts of the account with the most open opportunities": step 1 finds the account (a ranking or an aggregate), step 2 asks for the contacts of THOSE accounts. Set usesStep of step 2 to 1 and write its question about "those" records, for example "the contacts of those accounts". Another example: "the contacts of the account with the most employees": step 1 ranks accounts by employees and picks the top one, step 2 asks for the contacts of that account (a different kind of record, so it IS dependent). Never write the name or id of the found record into a question.
    3. SEPARATE requests asked one after another about different things, for example "show the contacts of Acme, then the open opportunities of Beta": one step per request, each with usesStep 0 and condition "always" (they do not use each other's records).
    Do NOT split when ONE query can answer the question: related records reached through a link ("contacts of accounts that have open opportunities", "contacts of accounts in London"), or a ranking with a limit on the same records ("the 5 accounts with the most employees", "the highest value opportunity of Acme").
    A question is dependent only when it ranks or aggregates records of one kind to PICK some of them, and then asks for records of ANOTHER kind (or other details) of the picked ones.
    Lists joined by "and" / "ve" (for example "contracts of Denema and ZKV") are NOT separate steps; keep them in one step.
    Rules:
    - At most 3 steps. The first step always has condition "always" and usesStep 0.
    - usesStep: 0 for a step that needs no earlier step; otherwise the number (starting at 1) of the earlier step whose records it uses. A step that uses an earlier step must have condition "always".
    - "if_previous_empty": run this step only when the previous step returned no rows.
    - "if_previous_not_empty": run this step only when the previous step returned at least one row.
    - Each step question must be complete on its own, in the same language as the user, and keep company and record names exactly as typed.
    - Do not invent steps the user did not ask for.
    """;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

    private readonly ILlmClient _llm;

    public LlmQuestionDecomposer(ILlmClient llm)
    {
        _llm = llm;
    }

    /// <summary>
    /// Splits the question into steps using the model, with retries and a step limit.
    /// </summary>
    public async Task<IReadOnlyList<QuestionStep>> DecomposeAsync(string question, CancellationToken ct = default)
    {
        var user = $"Question: {question}";
        string? lastProblem = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var prompt = lastProblem is null ? user : user + $"\n\nYour previous reply was not usable ({lastProblem}). Reply again with valid JSON.";
            var raw = await _llm.CompleteJsonAsync(SystemPrompt, prompt, Schema, ct);
            try
            {
                var dto = JsonSerializer.Deserialize<DecompositionDto>(raw, JsonOptions);
                lastProblem = Check(dto, out var steps);
                if (lastProblem is null)
                {
                    return steps;
                }
            }
            catch (JsonException ex)
            {
                lastProblem = ex.Message;
            }
        }

        return new List<QuestionStep> { new QuestionStep(question, StepCondition.Always) };
    }

    private static string? Check(DecompositionDto? dto, out IReadOnlyList<QuestionStep> steps)
    {
        steps = new List<QuestionStep>();
        if (dto?.Steps is null || dto.Steps.Count == 0)
        {
            return "no steps";
        }
        if (dto.Steps.Count > MaxSteps)
        {
            return $"more than {MaxSteps} steps";
        }

        var result = new List<QuestionStep>();
        for (var i = 0; i < dto.Steps.Count; i++)
        {
            var step = dto.Steps[i];
            if (string.IsNullOrWhiteSpace(step.Question))
            {
                return $"step {i + 1} has an empty question";
            }
            var condition = ParseCondition(step.Condition);
            if (condition is null)
            {
                return $"step {i + 1} has an unknown condition";
            }
            if (i == 0 && condition != StepCondition.Always)
            {
                return "the first step must have condition \"always\"";
            }
            var uses = step.UsesStep ?? 0;
            if (uses < 0 || uses > i)
            {
                return $"step {i + 1} has an invalid usesStep ({uses}); it may only use an earlier step";
            }
            if (uses > 0 && condition != StepCondition.Always)
            {
                return $"step {i + 1} uses step {uses} so its condition must be \"always\"";
            }
            result.Add(new QuestionStep(step.Question.Trim(), condition.Value, uses > 0 ? uses : null));
        }
        steps = result;
        return null;
    }

    private static StepCondition? ParseCondition(string? text)
    {
        switch ((text ?? "").Trim().ToLowerInvariant())
        {
            case "always": return StepCondition.Always;
            case "if_previous_empty": return StepCondition.IfPreviousEmpty;
            case "if_previous_not_empty": return StepCondition.IfPreviousNotEmpty;
            default: return null;
        }
    }

    private sealed class DecompositionDto
    {
        public List<StepDto>? Steps { get; set; }
    }

    private sealed class StepDto
    {
        public string? Question { get; set; }
        public string? Condition { get; set; }
        public int? UsesStep { get; set; }
    }
}
