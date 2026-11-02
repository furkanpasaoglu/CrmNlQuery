using CrmNlQuery.Application.Design.Models;
using CrmNlQuery.Application.Design.Services;

namespace CrmNlQuery.Tests.Application.Design.Services;

/// <summary>
/// Tests the design validator for customization designs: prefixes, names, fields, relationships, views and decisions.
/// </summary>
public class DesignValidatorTests
{
    private static DesignContext Context() => new DesignContext
    {
        Prefix = "new",
        ExistingEntities = new HashSet<string>(DesignTestData.All.Select(e => e.LogicalName)),
        Detailed = DesignTestData.All.ToDictionary(e => e.LogicalName),
        CandidateNames = new HashSet<string> { "new_servis_kaydi", "new_arac" },
    };

    private static DesignField Field(string name, string type = "string", string target = "") => new DesignField { LogicalName = name, DisplayName = name, Type = type, Target = target };

    private static DesignDocument Valid() => new DesignDocument
    {
        Summary = "s",
        Tables =
        {
            new DesignTable
            {
                LogicalName = "new_randevu", DisplayName = "Randevu", Ownership = "user", PrimaryNameLogical = "new_ad", PrimaryNameDisplay = "Ad",
                Fields = { Field("new_tarih", "datetime"), Field("new_servisid", "lookup", "new_servis_kaydi"),
                    new DesignField { LogicalName = "new_durum", DisplayName = "Durum", Type = "picklist", Options = { new DesignOption { Value = 1, Label = "Planlandı" }, new DesignOption { Value = 2, Label = "İptal" } } } },
            },
            new DesignTable { LogicalName = "new_servis_kaydi", Extends = "new_servis_kaydi", DisplayName = "Servis Kaydı", Fields = { Field("new_not", "memo") } },
        },
        Relationships = { new DesignRelationship { Kind = "one-to-many", From = "new_servis_kaydi", To = "new_randevu", LookupField = "new_servisid", DeleteBehavior = "restrict" } },
        Views = { new DesignView { Table = "new_randevu", Name = "Aktif", Columns = { "new_ad", "new_tarih", "new_durum" } } },
    };

    private static IReadOnlyList<string> Check(DesignDocument doc) => DesignValidator.Validate(doc, Context());

    /// <summary>
    /// Verifies that a valid design has no problems.
    /// </summary>
    [Fact]
    public void AValidDesign_HasNoProblems()
    {
        Assert.Empty(Check(Valid()));
    }

    /// <summary>
    /// Verifies that new names must use the prefix and must not collide with existing tables.
    /// </summary>
    [Fact]
    public void NewNames_MustUseThePrefix_AndMustNotCollideWithExistingTables()
    {
        var doc = Valid();
        doc.Tables[0].LogicalName = "randevu";
        doc.Tables[1].LogicalName = "new_servis_kaydi";
        doc.Tables.Add(new DesignTable { LogicalName = "new_arac", DisplayName = "Araç", Ownership = "user", PrimaryNameLogical = "new_ad", PrimaryNameDisplay = "Ad" });

        var errors = Check(doc);

        Assert.Contains(errors, e => e.Contains("must start with the publisher prefix"));
        Assert.Contains(errors, e => e.Contains("already exists in the CRM"));
        Assert.Contains(errors, e => e.Contains("set extends to"));
    }

    /// <summary>
    /// Verifies that an extension must target an existing candidate and list only new fields.
    /// </summary>
    [Fact]
    public void AnExtension_MustTargetAnExistingCandidate_AndListOnlyNewFields()
    {
        var doc = Valid();
        doc.Tables[1].Fields.Add(Field("new_tutar", "money"));
        doc.Tables.Add(new DesignTable { LogicalName = "account", Extends = "account", DisplayName = "Account", Fields = { Field("new_x") } });

        var errors = Check(doc);

        Assert.Contains(errors, e => e.Contains("already exists on the table"));
        Assert.Contains(errors, e => e.Contains("not one of the existing candidate tables"));
    }

    /// <summary>
    /// Verifies that field names must be prefixed ascii and short enough.
    /// </summary>
    [Theory]
    [InlineData("new_Bad Name")]
    [InlineData("other_field")]
    [InlineData("new_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void FieldNames_MustBePrefixedAscii_AndShortEnough(string name)
    {
        var doc = Valid();
        doc.Tables[0].Fields.Add(Field(name));

        Assert.NotEmpty(Check(doc));
    }

    /// <summary>
    /// Verifies that picklists need two unique labelled options and lookups need a known target.
    /// </summary>
    [Fact]
    public void Picklists_NeedTwoUniqueLabelledOptions_AndLookupsNeedAKnownTarget()
    {
        var doc = Valid();
        doc.Tables[0].Fields.Add(new DesignField { LogicalName = "new_tek", DisplayName = "t", Type = "picklist", Options = { new DesignOption { Value = 1, Label = "A" } } });
        doc.Tables[0].Fields.Add(new DesignField { LogicalName = "new_cift", DisplayName = "c", Type = "picklist", Options = { new DesignOption { Value = 1, Label = "A" }, new DesignOption { Value = 1, Label = "B" } } });
        doc.Tables[0].Fields.Add(Field("new_kime", "lookup", "no_such_table"));
        doc.Tables[0].Fields.Add(Field("new_tipi", "uuid"));

        var errors = Check(doc);

        Assert.Contains(errors, e => e.Contains("at least two options"));
        Assert.Contains(errors, e => e.Contains("values and labels must be unique"));
        Assert.Contains(errors, e => e.Contains("neither in the CRM nor in this design"));
        Assert.Contains(errors, e => e.Contains("type 'uuid' is not allowed"));
    }

    /// <summary>
    /// Verifies that a new table needs a primary name and must not repeat it as a field.
    /// </summary>
    [Fact]
    public void ANewTable_NeedsAPrimaryName_AndMustNotRepeatItAsAField()
    {
        var doc = Valid();
        doc.Tables[0].PrimaryNameLogical = "";
        Assert.Contains(Check(doc), e => e.Contains("needs a primary name field"));

        doc = Valid();
        doc.Tables[0].Fields.Add(Field("new_ad"));
        Assert.Contains(Check(doc), e => e.Contains("must not be repeated in fields"));
    }

    /// <summary>
    /// Verifies that relationships need valid ends a lookup on the many side and a pointing target.
    /// </summary>
    [Fact]
    public void Relationships_NeedValidEnds_ALookupOnTheManySide_AndAPointingTarget()
    {
        var doc = Valid();
        doc.Relationships[0].LookupField = "new_yok";
        Assert.Contains(Check(doc), e => e.Contains("must be a lookup field of 'new_randevu'"));

        doc = Valid();
        doc.Relationships[0].To = "no_such";
        Assert.Contains(Check(doc), e => e.Contains("both tables must exist"));

        doc = Valid();
        doc.Tables[0].Fields.First(f => f.LogicalName == "new_servisid").Target = "new_arac";
        Assert.Contains(Check(doc), e => e.Contains("points to 'new_arac'"));

        doc = Valid();
        doc.Relationships[0].DeleteBehavior = "explode";
        Assert.Contains(Check(doc), e => e.Contains("deleteBehavior 'explode'"));
    }

    /// <summary>
    /// Verifies that the lookup must live on a table that is part of the design.
    /// </summary>
    [Fact]
    public void TheLookupMustLiveOnATableThatIsInTheDesign()
    {
        var doc = Valid();
        doc.Relationships[0] = new DesignRelationship { Kind = "one-to-many", From = "new_randevu", To = "new_arac", LookupField = "new_x", DeleteBehavior = "restrict" };

        Assert.Contains(Check(doc), e => e.Contains("list 'new_arac' in tables"));
    }

    /// <summary>
    /// Verifies that a many-to-many relationship has no lookup field.
    /// </summary>
    [Fact]
    public void ManyToMany_HasNoLookupField()
    {
        var doc = Valid();
        doc.Relationships[0] = new DesignRelationship { Kind = "many-to-many", From = "new_randevu", To = "new_arac", LookupField = "new_x" };

        Assert.Contains(Check(doc), e => e.Contains("many-to-many relationship has no lookup field"));
    }

    /// <summary>
    /// Verifies that views must use existing columns on known tables.
    /// </summary>
    [Fact]
    public void Views_MustUseExistingColumns_OnKnownTables()
    {
        var doc = Valid();
        doc.Views.Add(new DesignView { Table = "new_randevu", Name = "Bad", Columns = { "new_yok" } });
        doc.Views.Add(new DesignView { Table = "no_table", Name = "Bad2", Columns = { "x" } });
        doc.Views.Add(new DesignView { Table = "account", Name = "Ok", Columns = { "name" } });

        var errors = Check(doc);

        Assert.Contains(errors, e => e.Contains("column 'new_yok' does not exist"));
        Assert.Contains(errors, e => e.Contains("table 'no_table'"));
        Assert.DoesNotContain(errors, e => e.Contains("View 'Ok'"));
    }

    /// <summary>
    /// Verifies that decisions may only address the candidates with a known action.
    /// </summary>
    [Fact]
    public void Decisions_MayOnlyAddressTheCandidates_WithAKnownAction()
    {
        var errors = DesignValidator.ValidateDecisions(new[]
        {
            new ReuseDecision { Entity = "new_arac", Action = "reuse" },
            new ReuseDecision { Entity = "account", Action = "extend" },
            new ReuseDecision { Entity = "new_servis_kaydi", Action = "destroy" },
        }, Context());

        Assert.Equal(2, errors.Count);
    }
}
