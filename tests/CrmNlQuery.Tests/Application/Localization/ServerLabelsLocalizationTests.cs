using CrmNlQuery.Application.Drafts.Models;
using CrmNlQuery.Application.Drafts.Services;
using CrmNlQuery.Application.Localization.Services;
using CrmNlQuery.Application.Suggestions.Services;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Localization;

/// <summary>
/// Tests that in-answer server labels follow the request language while CRM labels stay as they are.
/// </summary>
[Collection("RequestLanguage")]
public class ServerLabelsLocalizationTests
{
    private static AttributeInfo A(string name, string type, string label, params (int Value, string Label)[] options)
        => new AttributeInfo(name, label, type, options.Select(o => new OptionInfo(o.Value, o.Label)).ToList());

    private static EntityInfo Opportunity() => new EntityInfo("opportunity", "opportunities", "Fırsat", "opportunityid", "name",
        new List<AttributeInfo>
        {
            A("opportunityid", "Uniqueidentifier", "Fırsat"), A("name", "String", "Ad"),
            A("statecode", "State", "Durum", (0, "Açık"), (1, "Kazanılmış")),
            A("prioritycode", "Picklist", "Öncelik", (1, "Yüksek"), (2, "Düşük")),
            A("estimatedvalue", "Money", "Tahmini Gelir"), A("createdon", "DateTime", "Oluşturma Tarihi"),
        },
        new List<RelationshipInfo> { new RelationshipInfo("incident_opp", "incident", "opportunityid", "opportunity", "opportunityid") });

    private static readonly IReadOnlyList<EntitySummary> Entities = new List<EntitySummary>
    {
        new EntitySummary("opportunity", "opportunities", "Fırsat"), new EntitySummary("incident", "incidents", "Vaka"),
    };

    private const string Plain = "<fetch top=\"500\"><entity name=\"opportunity\"><attribute name=\"name\" /><attribute name=\"statecode\" /><attribute name=\"prioritycode\" /></entity></fetch>";

    private static ResultTable Rows(int n)
        => new ResultTable(new[] { "name", "statecode", "prioritycode" },
            Enumerable.Range(0, n).Select(i => (IReadOnlyList<string>)new[] { "O" + i, i % 3 == 0 ? "Kazanılmış" : "Açık", i % 2 == 0 ? "Yüksek" : "Düşük" }).ToList());

    private static EntityInfo Plain3() => new EntityInfo("opportunity", "opportunities", "Fırsat", "opportunityid", "name",
        new List<AttributeInfo> { A("opportunityid", "Uniqueidentifier", "Fırsat"), A("name", "String", "Ad"), A("estimatedvalue", "Money", "Tahmini Gelir"), A("createdon", "DateTime", "Oluşturma Tarihi") },
        new List<RelationshipInfo> { new RelationshipInfo("incident_opp", "incident", "opportunityid", "opportunity", "opportunityid") });

    private static ResultTable NameRows(int n)
        => new ResultTable(new[] { "name" }, Enumerable.Range(0, n).Select(i => (IReadOnlyList<string>)new[] { "O" + i }).ToList());

    /// <summary>
    /// Verifies that suggestion labels follow the request language but CRM labels stay as they are.
    /// </summary>
    [Fact]
    public void SuggestionLabels_FollowTheRequestLanguage_ButContosoelsStayAsTheyAre()
    {
        try
        {
            RequestLanguage.Set("tr");
            var first = SuggestionBuilder.For(Plain, Opportunity(), Rows(30), false, Entities);
            Assert.Contains(first, s => s.Kind == "narrow" && s.Label == "Durum: Açık");

            var tr = SuggestionBuilder.For("<fetch top=\"500\"><entity name=\"opportunity\"><attribute name=\"name\" /></entity></fetch>", Plain3(), NameRows(30), false, Entities);
            Assert.Contains(tr, s => s.Kind == "related" && s.Label == "Bunların Vaka kayıtları" && s.Question == "Bu Fırsat kayıtlarının Vaka kayıtlarını göster");
            Assert.Contains(tr, s => s.Kind == "time" && s.Label == "Son 30 günde oluşturulanlar");
            Assert.Contains(tr, s => s.Kind == "sort" && s.Label == "Tahmini Gelir alanına göre en büyük 10");

            RequestLanguage.Set("en");
            var en = SuggestionBuilder.For("<fetch top=\"500\"><entity name=\"opportunity\"><attribute name=\"name\" /></entity></fetch>", Plain3(), NameRows(30), false, Entities);
            Assert.Contains(en, s => s.Kind == "related" && s.Label == "Their Vaka records");
            Assert.Contains(en, s => s.Kind == "time" && s.Label == "Created in the last 30 days");
            Assert.Contains(en, s => s.Kind == "sort" && s.Label == "Largest 10 by Tahmini Gelir");
        }
        finally
        {
            RequestLanguage.Set("en");
        }
    }

    /// <summary>
    /// Verifies that an unfilled draft placeholder shows its marker in the request language.
    /// </summary>
    [Fact]
    public void AnUnfilledDraftPlaceholder_ShowsItsMarkerInTheRequestLanguage()
    {
        try
        {
            RequestLanguage.Set("tr");
            var tr = DraftFiller.Fill("Saygılarımla, {{sender_name}} ({{open_case_count}})", new Dictionary<string, string>());
            Assert.Equal("Saygılarımla, [adınız] ([açık vaka sayısı])", tr.Text);
            Assert.Equal(new[] { "[adınız]", "[açık vaka sayısı]" }, tr.Unresolved);

            RequestLanguage.Set("en");
            Assert.Equal("Regards, [your name]", DraftFiller.Fill("Regards, {{sender_name}}", new Dictionary<string, string>()).Text);
        }
        finally
        {
            RequestLanguage.Set("en");
        }
    }

    /// <summary>
    /// Verifies that every draft placeholder has a localized marker label in both languages.
    /// </summary>
    [Fact]
    public void EveryDraftPlaceholder_HasALocalizedMarkerLabel_InBothLanguages()
    {
        foreach (var placeholder in DraftPlaceholders.All)
        {
            var key = "msg.ph." + placeholder.Name;
            Assert.NotEqual(key, MessageCatalog.Render(key, "en"));
            Assert.NotEqual(key, MessageCatalog.Render(key, "tr"));
            Assert.Equal(placeholder.Label, MessageCatalog.Render(key, "en"));
        }
    }
}
