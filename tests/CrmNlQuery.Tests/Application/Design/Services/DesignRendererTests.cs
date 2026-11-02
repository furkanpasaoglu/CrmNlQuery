using System.Text.Json;
using CrmNlQuery.Application.Design.Models;
using CrmNlQuery.Application.Design.Services;

namespace CrmNlQuery.Tests.Application.Design.Services;

/// <summary>
/// Tests rendering of design documents to Markdown.
/// </summary>
public class DesignRendererTests
{
    private static DesignResult Result(string language = "tr")
    {
        var doc = new DesignDocument
        {
            Summary = "Randevu takibi.",
            Tables =
            {
                new DesignTable
                {
                    LogicalName = "new_randevu", DisplayName = "Randevu", Ownership = "user", Description = "Servis | randevusu", PrimaryNameLogical = "new_ad", PrimaryNameDisplay = "Ad",
                    Fields =
                    {
                        new DesignField { LogicalName = "new_tarih", DisplayName = "Tarih", Type = "datetime", Required = true },
                        new DesignField { LogicalName = "new_tc_kimlik", DisplayName = "T.C. Kimlik No", Type = "string", Length = 11 },
                        new DesignField { LogicalName = "new_durum", DisplayName = "Durum", Type = "picklist", Options = { new DesignOption { Value = 1, Label = "Planlandı" }, new DesignOption { Value = 2, Label = "İptal" } } },
                    },
                },
                new DesignTable { LogicalName = "new_servis_kaydi", Extends = "new_servis_kaydi", DisplayName = "Servis Kaydı", Fields = { new DesignField { LogicalName = "new_not", DisplayName = "Not", Type = "memo" } } },
            },
            Relationships = { new DesignRelationship { Kind = "one-to-many", From = "new_servis_kaydi", To = "new_randevu", LookupField = "new_servisid", DeleteBehavior = "cascade", Description = "çok randevu" } },
            Views = { new DesignView { Table = "new_randevu", Name = "Aktif", Columns = { "new_ad", "new_tarih" } } },
            SecurityNotes = { "Plaka kişisel veri sayılabilir." },
            OpenQuestions = { "İptal edilenler silinsin mi?" },
        };
        return new DesignResult
        {
            Language = language, Prefix = "new", Request = "GİZLİ TALEP METNİ", Document = doc,
            Matches = new[] { new SchemaMatch("new_servis_kaydi", "Servis Kaydı", true, 5, 9, new[] { "servis" }, new[] { new MatchedField("new_aracid", "Araç", "Lookup", "araç") }, new[] { "new_tutar" }) },
            Decisions = new[] { new ReuseDecision { Entity = "new_servis_kaydi", Action = "extend", Reason = "Zaten var." } },
            ClosedTables = new[] { "new_servis_kaydi" },
        };
    }

    /// <summary>
    /// Verifies that the markdown has every section in the request language.
    /// </summary>
    [Fact]
    public void TheMarkdown_HasEverySection_InTheRequestLanguage()
    {
        var tr = DesignRenderer.Markdown(Result("tr"));
        var en = DesignRenderer.Markdown(Result("en"));

        foreach (var heading in new[] { "İhtiyaç özeti", "Mevcut şema kontrolü", "Önerilen tablolar", "İlişkiler", "Görünümler", "Güvenlik notları", "Açık sorular" })
        {
            Assert.Contains("## ", tr);
            Assert.Contains(heading, tr);
        }
        foreach (var heading in new[] { "Request summary", "Existing schema check", "Proposed tables", "Relationships", "Views", "Security notes", "Open questions" })
        {
            Assert.Contains(heading, en);
        }
        Assert.Contains("ÖNERİDİR", tr);
        Assert.Contains("Nothing was created in the CRM", en);
    }

    /// <summary>
    /// Verifies that the plaintext document shows the evidence the decision and the closed table note.
    /// </summary>
    [Fact]
    public void ThePlaintextDocument_ShowsTheEvidence_TheDecision_AndTheClosedTableNote()
    {
        var md = DesignRenderer.Markdown(Result("tr"));

        Assert.Contains("**Servis Kaydı** (`new_servis_kaydi`, özel, 5 alan)", md);
        Assert.Contains("Eşleşen alanlar: Araç (`new_aracid`)", md);
        Assert.Contains("Karar: **extend** — Zaten var.", md);
        Assert.Contains("Sohbet aracında kapalı", md);
    }

    /// <summary>
    /// Verifies that tables show the extension mark primary name field details and escape pipes.
    /// </summary>
    [Fact]
    public void Tables_ShowTheExtensionMark_PrimaryName_FieldDetails_AndEscapePipes()
    {
        var md = DesignRenderer.Markdown(Result("en"));

        Assert.Contains("Extends the existing table", md);
        Assert.Contains("| `new_ad` | Ad | string | yes | primary name |", md);
        Assert.Contains("1=Planlandı, 2=İptal", md);
        Assert.Contains("length 11", md);
        Assert.Contains("Servis | randevusu", md);
    }

    /// <summary>
    /// Verifies that a cell with a pipe does not break the table.
    /// </summary>
    [Fact]
    public void ACellWithAPipe_DoesNotBreakTheTable()
    {
        var result = Result("en");
        result.Document.Tables[0].Fields[0].Description = "a | b";

        var md = DesignRenderer.Markdown(result);

        Assert.Contains("a \\| b", md);
    }

    /// <summary>
    /// Verifies that sensitive fields are flagged by the same rule the chat tool uses.
    /// </summary>
    [Fact]
    public void Sensitive_FieldsAreFlaggedByTheSameRuleTheChatToolUses()
    {
        var md = DesignRenderer.Markdown(Result("en"));

        Assert.Contains("new_randevu.new_tc_kimlik", md);
        Assert.Contains("field-level security", md);
        Assert.Equal(new[] { "new_randevu.new_tc_kimlik (T.C. Kimlik No)" }, DesignRenderer.SensitiveFields(Result().Document));
    }

    /// <summary>
    /// Verifies that the diagram uses the relationships.
    /// </summary>
    [Fact]
    public void TheDiagram_UsesTheRelationships()
    {
        var md = DesignRenderer.Markdown(Result("en"));

        Assert.Contains("```mermaid", md);
        Assert.Contains("new_servis_kaydi ||--o{ new_randevu : \"new_servisid\"", md);
    }

    /// <summary>
    /// Verifies that problems appear under needs review and are absent when clean.
    /// </summary>
    [Fact]
    public void Problems_AppearUnderNeedsReview_AndAreAbsentWhenClean()
    {
        var result = Result("en");
        Assert.DoesNotContain("Needs review", DesignRenderer.Markdown(result));

        result.Problems = new[] { "Table 'x' is bad." };

        var md = DesignRenderer.Markdown(result);
        Assert.Contains("Needs review", md);
        Assert.Contains("- Table 'x' is bad.", md);
    }

    /// <summary>
    /// Verifies that the JSON attachment is versioned has the structure and never the request text.
    /// </summary>
    [Fact]
    public void TheJsonAttachment_IsVersioned_HasTheStructure_AndNeverTheRequestText()
    {
        var json = DesignRenderer.Json(Result("tr"));

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("new", doc.RootElement.GetProperty("publisherPrefix").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("tables").GetArrayLength());
        Assert.Equal("new_randevu", doc.RootElement.GetProperty("tables")[0].GetProperty("logicalName").GetString());
        Assert.Equal("extend", doc.RootElement.GetProperty("reuse")[0].GetProperty("action").GetString());
        Assert.DoesNotContain("GİZLİ TALEP METNİ", json);
    }
}
