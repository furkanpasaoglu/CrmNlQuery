using System.Text;
using CrmNlQuery.Domain.Common.Services;

namespace CrmNlQuery.Domain.Ask.Steps;

/// <summary>
/// Pre-filter that skips the decomposer model call unless the question has conditional, sequence or ranking wording, accepting that unlisted phrasing runs as one step.
/// </summary>
public static class ConditionalQuestionFilter
{
    private static readonly string[] Markers =
    {
        "yoksa", "yok ise", "bulunmazsa", "olmazsa", "bossa", "degilse", "cikmazsa", "donmezse",
        "if none", "if there are no", "if empty", "otherwise", "if not", "else",
    };

    private static readonly string[] RankingMarkers =
    {
        "en cok", "en az", "en fazla", "en yuksek", "en dusuk", "en buyuk", "en kucuk", "en yeni", "en eski", "en son",
        "en pahali", "en ucuz", "en iyi", "en kotu", "en cogu",
        "most", "least", "fewest", "highest", "lowest", "largest", "smallest", "biggest", "newest", "oldest", "latest",
    };

    private static readonly string[] SequenceMarkers =
    {
        "sonra", "ardindan", "ayrica",
        "then", "afterwards",
    };

    private static readonly string[] DependentMarkers = RankingMarkers.Concat(SequenceMarkers).ToArray();

    /// <summary>
    /// Tells whether a question contains conditional wording such as otherwise.
    /// </summary>
    public static bool MayBeConditional(string question)
    {
        return ContainsAny(question, Markers);
    }

    /// <summary>
    /// Tells whether a question contains ranking or superlative wording that may need a dependent step.
    /// </summary>
    public static bool MayBeDependent(string question)
    {
        return ContainsAny(question, DependentMarkers);
    }

    /// <summary>
    /// Classifies the question's split marker as always, ranking-only or none to decide whether the decomposer is needed.
    /// </summary>
    public static StepTrigger Classify(string question)
    {
        if (ContainsAny(question, Markers) || ContainsAny(question, SequenceMarkers))
        {
            return StepTrigger.Always;
        }
        return ContainsAny(question, RankingMarkers) ? StepTrigger.OnlyRanking : StepTrigger.None;
    }

    /// <summary>
    /// Tells whether the decomposer should be called because of conditional or dependency wording.
    /// </summary>
    public static bool MayNeedSteps(string question)
    {
        return MayBeConditional(question) || MayBeDependent(question);
    }

    private static bool ContainsAny(string question, string[] markers)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return false;
        }
        var padded = " " + Words(question) + " ";
        foreach (var marker in markers)
        {
            if (padded.Contains(" " + marker + " ", StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static string Words(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in NameNormalizer.Fold(text))
        {
            sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
