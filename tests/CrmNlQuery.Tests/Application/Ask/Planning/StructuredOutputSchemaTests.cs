using CrmNlQuery.Application.Ask.Planning;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Ask.Planning;

/// <summary>
/// Holds tests that the structured-output schemas avoid keywords the cloud provider rejects.
/// </summary>
public class StructuredOutputSchemaTests
{
    private sealed class SchemaCapturingLlm : CrmNlQuery.Application.Ask.Abstractions.ILlmClient
    {
        public List<string> Schemas { get; } = new List<string>();
        private readonly string _reply;
        public SchemaCapturingLlm(string reply) { _reply = reply; }
        /// <summary>
        /// Test double LLM that captures the JSON schema it is asked to use.
        /// </summary>
        public Task<string> CompleteJsonAsync(string system, string user, string jsonSchema, CancellationToken ct = default)
        {
            Schemas.Add(jsonSchema);
            return Task.FromResult(_reply);
        }
    }

    private static readonly string[] Unsupported = { "\"minimum\"", "\"maximum\"", "\"exclusiveMinimum\"", "\"exclusiveMaximum\"", "\"multipleOf\"", "\"minLength\"", "\"maxLength\"" };

    /// <summary>
    /// Verifies that the decomposer schema carries no keywords the structured output API rejects.
    /// </summary>
    [Fact]
    public async Task TheDecomposerSchema_CarriesNoKeywordsTheStructuredOutputApiRejects()
    {
        var llm = new SchemaCapturingLlm("""{"steps":[{"question":"a","condition":"always"}]}""");

        await new LlmQuestionDecomposer(llm).DecomposeAsync("q");

        Assert.All(Unsupported, keyword => Assert.DoesNotContain(keyword, llm.Schemas[0]));
    }

    /// <summary>
    /// Verifies that the plan schema carries no keywords the structured output API rejects.
    /// </summary>
    [Fact]
    public async Task ThePlanSchema_CarriesNoKeywordsTheStructuredOutputApiRejects()
    {
        var llm = new SchemaCapturingLlm("{\"needsSteps\":true,\"steps\":[]}");
        var request = new PlanRequest("q", new List<EntityInfo> { TestMetadata.Product() }, new List<ResolvedRecord>(), new List<string> { "name" }, false);

        await new LlmPlanGenerator(llm, new PlanValidator(TestMetadata.Create())).GenerateAsync(request);

        Assert.All(Unsupported, keyword => Assert.DoesNotContain(keyword, llm.Schemas[0]));
    }
}
