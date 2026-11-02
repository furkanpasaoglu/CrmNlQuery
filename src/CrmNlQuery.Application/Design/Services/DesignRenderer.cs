using System.Text;
using System.Text.Json;
using CrmNlQuery.Application.DataPolicy.Services;
using CrmNlQuery.Application.Design.Models;

namespace CrmNlQuery.Application.Design.Services;

/// <summary>
/// Renders a design result as a Markdown document and a JSON attachment using templates.
/// </summary>
public static class DesignRenderer
{
    /// <summary>
    /// Holds the localized fixed texts used when rendering the design document.
    /// </summary>
    internal sealed record Texts(string Title, string Proposal, string Summary, string SchemaCheck, string NoMatches, string Tables, string Relationships,
        string Views, string Security, string Open, string NeedsReview, string Extends, string New, string Custom, string Standard, string Fields, string Matched,
        string Decision, string Closed, string Sensitive, string None, string Column, string Display, string Type, string Required, string Detail, string Kind,
        string From, string To, string Lookup, string OnDelete, string Ownership, string Description, string Diagram, string Yes, string No, string Unrelated, string FieldCount);

    private static readonly Texts En = new Texts("Customization design", "This is a proposal generated from the CRM schema (metadata only). Nothing was created in the CRM, and no record data was read. Review it before building anything.",
        "Request summary", "Existing schema check", "No existing table or field looks related to this request.", "Proposed tables", "Relationships", "Views", "Security notes", "Open questions",
        "Needs review (the design check found problems)", "Extends the existing table", "New table", "custom", "standard", "Fields", "Matching fields", "Decision", "Closed in the chat tool (the table exists, the chat does not use it)",
        "Fields that look sensitive by name; consider field-level security or leaving them out", "None.", "Logical name", "Display name", "Type", "Required", "Details", "Kind",
        "From", "To", "Lookup field", "On delete", "Ownership", "Description", "Diagram", "yes", "no", "Other candidates the search found but judged unrelated", "fields");

    private static readonly Texts Tr = new Texts("Özelleştirme tasarımı", "Bu belge CRM şemasından (yalnız metadata) üretilmiş bir ÖNERİDİR. CRM'de hiçbir şey oluşturulmadı ve kayıt verisi okunmadı. Geliştirmeye başlamadan önce gözden geçirin.",
        "İhtiyaç özeti", "Mevcut şema kontrolü", "Bu talebe benzeyen mevcut bir tablo ya da alan bulunamadı.", "Önerilen tablolar", "İlişkiler", "Görünümler", "Güvenlik notları", "Açık sorular",
        "Gözden geçirilmeli (tasarım denetimi sorun buldu)", "Mevcut tabloyu genişletir", "Yeni tablo", "özel", "standart", "Alanlar", "Eşleşen alanlar", "Karar", "Sohbet aracında kapalı (tablo CRM'de var, sohbet kullanmıyor)",
        "Adından hassas görünen alanlar; alan düzeyinde güvenlik ya da hiç eklememeyi düşünün", "Yok.", "Mantıksal ad", "Görünen ad", "Tip", "Zorunlu", "Ayrıntı", "Tür",
        "Kaynak", "Hedef", "Arama alanı", "Silinince", "Sahiplik", "Açıklama", "Diyagram", "evet", "hayır", "Arama bulmuş ama ilgisiz sayılan diğer adaylar", "alan");

    /// <summary>
    /// Returns the fixed texts for the given language.
    /// </summary>
    internal static Texts For(string language) => language.StartsWith("tr", StringComparison.OrdinalIgnoreCase) ? Tr : En;

    private static string Cell(string? text) => (text ?? "").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    /// <summary>
    /// Renders the design result as a Markdown document with sections and a Mermaid entity diagram.
    /// </summary>
    public static string Markdown(DesignResult result)
    {
        var t = For(result.Language);
        var sb = new StringBuilder();
        sb.Append("# ").AppendLine(t.Title).AppendLine();
        sb.Append("> ").AppendLine(t.Proposal).AppendLine();

        sb.Append("## 1. ").AppendLine(t.Summary).AppendLine();
        sb.AppendLine(string.IsNullOrWhiteSpace(result.Document.Summary) ? t.None : result.Document.Summary).AppendLine();

        sb.Append("## 2. ").AppendLine(t.SchemaCheck).AppendLine();
        if (result.Matches.Count == 0)
        {
            sb.AppendLine(t.NoMatches).AppendLine();
        }
        var ignored = new List<string>();
        foreach (var m in result.Matches)
        {
            var decision = result.Decisions.FirstOrDefault(d => string.Equals(d.Entity, m.Entity, StringComparison.OrdinalIgnoreCase));
            if (decision is not null && decision.Action == "ignore")
            {
                ignored.Add($"{m.Label} (`{m.Entity}`)");
                continue;
            }
            sb.Append("- **").Append(m.Label).Append("** (`").Append(m.Entity).Append("`, ").Append(m.IsCustom ? t.Custom : t.Standard).Append(", ").Append(m.AttributeCount).AppendLine(" " + t.FieldCount + ")");
            if (m.Fields.Count > 0)
            {
                sb.Append("  - ").Append(t.Matched).Append(": ").AppendLine(string.Join(", ", m.Fields.Select(f => $"{f.Label} (`{f.Logical}`)")));
            }
            if (decision is not null)
            {
                sb.Append("  - ").Append(t.Decision).Append(": **").Append(decision.Action).Append("**");
                sb.AppendLine(string.IsNullOrWhiteSpace(decision.Reason) ? "" : " — " + decision.Reason);
            }
            if (result.ClosedTables.Contains(m.Entity, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append("  - ").AppendLine(t.Closed);
            }
        }
        if (ignored.Count > 0)
        {
            sb.Append("- ").Append(t.Unrelated).Append(": ").AppendLine(string.Join(", ", ignored));
        }
        sb.AppendLine();

        sb.Append("## 3. ").AppendLine(t.Tables).AppendLine();
        foreach (var table in result.Document.Tables)
        {
            var extends = !string.IsNullOrWhiteSpace(table.Extends);
            sb.Append("### ").Append(table.DisplayName).Append(" (`").Append(table.LogicalName).Append("`) — ").AppendLine(extends ? t.Extends : t.New).AppendLine();
            if (!extends)
            {
                sb.Append("- ").Append(t.Ownership).Append(": ").AppendLine(table.Ownership);
            }
            if (!string.IsNullOrWhiteSpace(table.Description))
            {
                sb.Append("- ").Append(t.Description).Append(": ").AppendLine(table.Description);
            }
            sb.AppendLine();
            sb.Append("| ").Append(t.Column).Append(" | ").Append(t.Display).Append(" | ").Append(t.Type).Append(" | ").Append(t.Required).Append(" | ").Append(t.Detail).AppendLine(" |");
            sb.AppendLine("|---|---|---|---|---|");
            if (!extends && !string.IsNullOrWhiteSpace(table.PrimaryNameLogical))
            {
                sb.Append("| `").Append(Cell(table.PrimaryNameLogical)).Append("` | ").Append(Cell(table.PrimaryNameDisplay)).Append(" | string | ").Append(t.Yes).AppendLine(" | primary name |");
            }
            foreach (var f in table.Fields)
            {
                sb.Append("| `").Append(Cell(f.LogicalName)).Append("` | ").Append(Cell(f.DisplayName)).Append(" | ").Append(Cell(f.Type)).Append(" | ").Append(f.Required ? t.Yes : t.No).Append(" | ").Append(Cell(FieldDetail(f))).AppendLine(" |");
            }
            sb.AppendLine();
        }

        sb.Append("## 4. ").AppendLine(t.Relationships).AppendLine();
        if (result.Document.Relationships.Count == 0)
        {
            sb.AppendLine(t.None);
        }
        else
        {
            sb.Append("| ").Append(t.Kind).Append(" | ").Append(t.From).Append(" | ").Append(t.To).Append(" | ").Append(t.Lookup).Append(" | ").Append(t.OnDelete).Append(" | ").Append(t.Description).AppendLine(" |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (var r in result.Document.Relationships)
            {
                sb.Append("| ").Append(Cell(r.Kind)).Append(" | `").Append(Cell(r.From)).Append("` | `").Append(Cell(r.To)).Append("` | ").Append(string.IsNullOrEmpty(r.LookupField) ? "" : "`" + Cell(r.LookupField) + "`")
                    .Append(" | ").Append(r.Kind == "many-to-many" ? "" : Cell(r.DeleteBehavior)).Append(" | ").Append(Cell(r.Description)).AppendLine(" |");
            }
        }
        sb.AppendLine();
        sb.AppendLine("```mermaid").AppendLine("erDiagram");
        foreach (var r in result.Document.Relationships)
        {
            var link = r.Kind switch { "one-to-many" => "||--o{", "many-to-one" => "}o--||", _ => "}o--o{" };
            sb.Append("    ").Append(r.From).Append(' ').Append(link).Append(' ').Append(r.To).Append(" : \"").Append(string.IsNullOrEmpty(r.LookupField) ? "m:n" : r.LookupField).AppendLine("\"");
        }
        sb.AppendLine("```").AppendLine();

        sb.Append("## 5. ").AppendLine(t.Views).AppendLine();
        if (result.Document.Views.Count == 0)
        {
            sb.AppendLine(t.None);
        }
        foreach (var v in result.Document.Views)
        {
            sb.Append("- **").Append(v.Name).Append("** (`").Append(v.Table).Append("`): ").AppendLine(string.Join(", ", v.Columns.Select(c => "`" + c + "`")));
        }
        sb.AppendLine();

        sb.Append("## 6. ").AppendLine(t.Security).AppendLine();
        var sensitive = SensitiveFields(result.Document);
        foreach (var note in result.Document.SecurityNotes)
        {
            sb.Append("- ").AppendLine(note);
        }
        if (sensitive.Count > 0)
        {
            sb.Append("- ").Append(t.Sensitive).Append(": ").AppendLine(string.Join(", ", sensitive));
        }
        if (result.Document.SecurityNotes.Count == 0 && sensitive.Count == 0)
        {
            sb.AppendLine(t.None);
        }
        sb.AppendLine();

        sb.Append("## 7. ").AppendLine(t.Open).AppendLine();
        if (result.Document.OpenQuestions.Count == 0)
        {
            sb.AppendLine(t.None);
        }
        foreach (var q in result.Document.OpenQuestions)
        {
            sb.Append("- ").AppendLine(q);
        }
        sb.AppendLine();

        if (result.Problems.Count > 0)
        {
            sb.Append("## ").AppendLine(t.NeedsReview).AppendLine();
            foreach (var p in result.Problems)
            {
                sb.Append("- ").AppendLine(p);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Formats the type and option details of one designed field.
    /// </summary>
    internal static string FieldDetail(DesignField f)
    {
        var parts = new List<string>();
        if (f.Type == "string" && f.Length > 0) { parts.Add("length " + f.Length); }
        if (f.Type == "picklist") { parts.Add(string.Join(", ", f.Options.Select(o => $"{o.Value}={o.Label}"))); }
        if (f.Type == "lookup" && !string.IsNullOrEmpty(f.Target)) { parts.Add("→ " + f.Target); }
        if (!string.IsNullOrWhiteSpace(f.Description)) { parts.Add(f.Description); }
        return string.Join("; ", parts);
    }

    /// <summary>
    /// Lists fields whose names look sensitive, using the same rule as the chat data allowlist.
    /// </summary>
    public static IReadOnlyList<string> SensitiveFields(DesignDocument doc)
    {
        return doc.Tables.SelectMany(t => t.Fields.Where(f => SensitiveNames.IsSensitive(f.LogicalName, f.DisplayName)).Select(f => $"{t.LogicalName}.{f.LogicalName} ({f.DisplayName})")).ToList();
    }

    /// <summary>
    /// Renders the JSON attachment with a version field and without the request text.
    /// </summary>
    public static string Json(DesignResult result)
    {
        var payload = new
        {
            version = 1,
            language = result.Language,
            publisherPrefix = result.Prefix,
            reuse = result.Decisions.Select(d => new { entity = d.Entity, action = d.Action, reason = d.Reason }),
            tables = result.Document.Tables,
            relationships = result.Document.Relationships,
            views = result.Document.Views,
            securityNotes = result.Document.SecurityNotes,
            sensitiveFields = SensitiveFields(result.Document),
            openQuestions = result.Document.OpenQuestions,
            problems = result.Problems,
        };
        return JsonSerializer.Serialize(payload, DesignJson.Write);
    }
}
