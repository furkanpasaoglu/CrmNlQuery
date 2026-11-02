using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Design;

/// <summary>
/// Provides a small lab-like schema for tests: standard tables plus custom vehicle and service tables.
/// </summary>
public static class DesignTestData
{
    private static AttributeInfo A(string name, string label, string type, params (int Value, string Label)[] options)
        => new AttributeInfo(name, label, type, options.Select(o => new OptionInfo(o.Value, o.Label)).ToList());

    public static readonly EntityInfo Account = new EntityInfo("account", "accounts", "Account", "accountid", "name",
        new List<AttributeInfo> { A("accountid", "Account", "Uniqueidentifier"), A("name", "Account Name", "String") }, new List<RelationshipInfo>());

    public static readonly EntityInfo Incident = new EntityInfo("incident", "incidents", "Case", "incidentid", "title",
        new List<AttributeInfo> { A("incidentid", "Case", "Uniqueidentifier"), A("title", "Case Title", "String"), A("prioritycode", "Priority", "Picklist", (1, "High"), (2, "Low")) }, new List<RelationshipInfo>());

    public static readonly EntityInfo Vehicle = new EntityInfo("new_arac", "new_aracs", "Araç", "new_aracid", "new_plaka",
        new List<AttributeInfo> { A("new_aracid", "Araç", "Uniqueidentifier"), A("new_plaka", "Plaka", "String"), A("new_musteriid", "Müşteri", "Lookup") }, new List<RelationshipInfo>());

    public static readonly EntityInfo ServiceRecord = new EntityInfo("new_servis_kaydi", "new_servis_kaydis", "Servis Kaydı", "new_servis_kaydiid", "new_ad",
        new List<AttributeInfo>
        {
            A("new_servis_kaydiid", "Servis Kaydı", "Uniqueidentifier"), A("new_ad", "Ad", "String"), A("new_aracid", "Araç", "Lookup"),
            A("new_tutar", "Tutar", "Money"), A("new_durum", "Durum", "Picklist", (1, "Açık"), (2, "Kapalı")),
        }, new List<RelationshipInfo>());

    public static readonly IReadOnlyList<EntityInfo> All = new[] { Account, Incident, Vehicle, ServiceRecord };

    public static IReadOnlyList<EntitySummary> Summaries => All.Select(e => new EntitySummary(e.LogicalName, e.EntitySetName, e.Label)).ToList();
}
