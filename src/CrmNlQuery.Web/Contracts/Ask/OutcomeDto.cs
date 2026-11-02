using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Explain.Models;
using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;

namespace CrmNlQuery.Web.Contracts.Ask;

/// <summary>
/// Request or response data transfer type for outcome.
/// </summary>
public static class OutcomeDto
{
    /// <summary>
    /// Creates a value from for outcome dto.
    /// </summary>
    public static object From(AskOutcome outcome, string? language = null)
    {
        switch (outcome)
        {
            case AnswerOutcome a:
                return new
                {
                    kind = "answer",
                    entity = a.Entity,
                    understood = a.Understood,
                    fetchXml = a.ShowFetchXml ? a.FetchXml : null,
                    explanation = a.Explanation is null ? null : ExplanationDto(a.Explanation),
                    truncated = a.Truncated,
                    stepNotes = a.StepNotes,
                    stepSummaries = a.StepSummaries.Select(s => new { number = s.Number, question = s.Question, rows = s.Rows, columns = s.Columns, preview = s.Preview, names = s.Names }).ToList(),
                    fromChain = a.FromChain,
                    resultId = a.ResultId,
                    chatId = a.ChatId,
                    openInCrm = a.OpenInCrm,
                    narrowHints = a.NarrowHints.Select(h => new { attribute = h.Attribute, label = h.Label, kind = h.Kind, options = h.Options }).ToList(),
                    basedOn = a.BasedOn,
                    aboutRecord = a.AboutRecord,
                    glossary = a.GlossaryApplied.Select(g => new { term = g.Term, kind = g.Kind, text = g.Text }).ToList(),
                    table = new { columns = a.Table.Columns, labels = a.ColumnLabels, rows = a.Table.Rows, rowIds = a.Table.RowIds },
                    suggestions = a.Suggestions.Select(s => new { id = s.Id, kind = s.Kind, label = s.Label, question = s.Question }).ToList(),
                    chart = a.Chart is null ? null : new
                    {
                        kind = a.Chart.Kind,
                        xKind = a.Chart.XKind,
                        measure = a.Chart.MeasureName,
                        fn = a.Chart.Fn,
                        labels = a.Chart.XLabels,
                        series = a.Chart.Series.Select(s => new { name = s.Name, values = s.Values }).ToList(),
                        hasOther = a.Chart.HasOther,
                        omitted = a.Chart.OmittedCategories,
                        partial = a.Chart.Partial,
                    },
                    chartReason = a.ChartReason,
                    recordUrlBase = a.Table.RowIds is null ? null : a.RecordUrlBase,
                };
            case SummaryOutcome s:
                return new
                {
                    kind = "summary",
                    entity = s.Entity,
                    entityLabel = s.EntityLabel,
                    recordId = s.RecordId,
                    recordName = s.RecordName,
                    aboutRecord = s.AboutRecord,
                    resultId = s.ResultId,
                    chatId = s.ChatId,
                    recordUrlBase = s.RecordUrlBase,
                    sections = s.Sections.Select(x => new
                    {
                        kind = x.Kind,
                        entity = x.Entity,
                        title = x.Title,
                        status = x.Status,
                        via = x.Via,
                        viaEntity = x.ViaEntity,
                        canOpen = x.CanOpen,
                        metrics = x.Metrics.Select(m => new { key = m.Key, label = m.Label, value = m.Value }).ToList(),
                        items = x.Items.Select(i => new { text = i.Text, detail = i.Detail, entity = i.Entity, id = i.Id }).ToList(),
                    }).ToList(),
                };
            case ReportOutcome r:
                return new
                {
                    kind = "report",
                    query = r.Query,
                    listedAll = r.ListedAll,
                    reports = r.Reports.Select(x => new { id = x.Id, name = x.Name, description = x.Description, url = x.Url, forRecord = x.ForRecord }).ToList(),
                    resultId = r.ResultId,
                    chatId = r.ChatId,
                };
            case DraftOutcome d:
                return new
                {
                    kind = "draft",
                    entity = d.Entity,
                    recordId = d.RecordId,
                    recordName = d.RecordName,
                    aboutRecord = d.AboutRecord,
                    subject = d.Subject,
                    body = d.Body,
                    to = d.To,
                    tone = d.Tone,
                    unresolved = d.Unresolved,
                    resultId = d.ResultId,
                    chatId = d.ChatId,
                };
            case CrmNlQuery.Application.Create.Models.CreateOutcome c:
                return new
                {
                    kind = "create",
                    phase = c.Kind,
                    entity = c.Entity,
                    entityLabel = c.EntityLabel,
                    draftId = c.DraftId,
                    resultId = c.ResultId,
                    chatId = c.ChatId,
                    recordUrlBase = c.RecordUrlBase,
                    rows = c.Rows.Select(r => new
                    {
                        name = r.Name,
                        status = r.Status,
                        recordId = r.RecordId,
                        error = r.Error,
                        fields = r.Fields.Select(f => new { label = f.Label, value = f.Value }).ToList(),
                        problems = r.Problems.Select(p => new { code = p.Code, label = p.Label, detail = p.Detail }).ToList(),
                        duplicates = r.Duplicates,
                    }).ToList(),
                };
            case QuestionOutcome q:
                return new
                {
                    kind = "question",
                    questionKind = q.Kind,
                    key = q.AnswerKey,
                    text = q.Text,
                    choices = q.Choices.Select(c => new { label = c.Label, value = c.Value }).ToList(),
                    allowFreeText = q.AllowFreeText,
                };
            case FailureOutcome f:
                return new { kind = "failure", message = f.Key is null ? f.Message : CrmNlQuery.Application.Localization.Services.MessageCatalog.Render(f.Key, language ?? RequestLanguage.Current, f.Args) };
            default:
                return new { kind = "failure", message = MessageCatalog.Now(Msg.UnexpectedOutcome) };
        }
    }

    private static object ExplanationDto(QueryExplanation e)
    {
        return new
        {
            entity = e.Entity,
            entityLabel = e.EntityLabel,
            filters = e.Filters.Select(f => new
            {
                id = f.Id,
                field = f.Field,
                fieldLabel = f.FieldLabel,
                op = f.Op,
                values = f.Values.Select(v => new { value = v.Value, label = v.Label }).ToList(),
                from = f.From,
                to = f.To,
                via = f.Via,
                group = f.Group,
                edit = f.Edit is null ? null : new { kind = f.Edit.Kind, options = f.Edit.Options?.Select(o => new { value = o.Value, label = o.Label }).ToList() },
            }).ToList(),
            sort = e.Sort.Select(s => new { field = s.Field, label = s.Label, descending = s.Descending, fn = s.Fn }).ToList(),
            measures = e.Measures.Select(m => new { fn = m.Fn, fieldLabel = m.FieldLabel }).ToList(),
            top = e.Top,
            aggregate = e.Aggregate,
            groupBy = e.GroupBy,
        };
    }
}
