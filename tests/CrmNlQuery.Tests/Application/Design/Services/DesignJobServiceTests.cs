using System.IO.Compression;
using System.Xml.Linq;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Design.Exceptions;
using CrmNlQuery.Application.Design.Models;
using CrmNlQuery.Application.Design.Services;
using CrmNlQuery.Infrastructure.Persistence.Memory.History;

namespace CrmNlQuery.Tests.Application.Design.Services;

/// <summary>
/// Tests the design job service: background run, limits, cancellation, time limit, failures, restart, deletion and docx output.
/// </summary>
public class DesignJobServiceTests
{
    private const string Extraction = """{"language":"en","summary":"s","concepts":[{"term":"servis randevusu","synonyms":["servis kaydı"],"kind":"table"}],"standardTables":[]}""";
    private const string Decisions = """{"decisions":[{"entity":"new_servis_kaydi","action":"extend","reason":"r"}]}""";
    private const string Design = """
    {"summary":"s","tables":[{"logicalName":"new_servis_kaydi","displayName":"Servis Kaydı","ownership":"user","extends":"new_servis_kaydi","fields":[{"logicalName":"new_not","displayName":"Not","type":"memo","required":false}]}],
     "relationships":[],"views":[],"securityNotes":[],"openQuestions":["a?","b?"]}
    """;

    private sealed class GateLlm : ILlmClient
    {
        public SemaphoreSlim Gate { get; } = new SemaphoreSlim(0);
        public bool Block { get; set; }
        public bool Throw { get; set; }
        /// <summary>
        /// Completes a JSON request with a fake model in the test.
        /// </summary>
        public async Task<string> CompleteJsonAsync(string system, string user, string jsonSchema, CancellationToken ct = default)
        {
            if (Throw) { throw new InvalidOperationException("http://internal-host:11434 refused"); }
            if (Block) { await Gate.WaitAsync(ct); }
            if (jsonSchema.Contains("\"concepts\"")) { return Extraction; }
            if (jsonSchema.Contains("\"decisions\"")) { return Decisions; }
            return Design;
        }
    }

    private static readonly string Text = "Bayilerimizin servis randevularını CRM'de takip etmek istiyoruz, araç ve müşteri ile.";

    private readonly MemoryDesignJobStore _store = new MemoryDesignJobStore();
    private readonly GateLlm _llm = new GateLlm();
    private TimeSpan _timeout = TimeSpan.FromMinutes(5);

    private DesignJobService Service() => new DesignJobService(_store, new DesignPlanner(_llm, new FakeMetadataSource(DesignTestData.All.ToArray())), () => _timeout);

    private async Task<DesignJob> Wait(DesignJobService service, string owner, string id, Func<DesignJob, bool> done)
    {
        for (var i = 0; i < 200; i++)
        {
            var job = (await service.GetAsync(owner, id))!;
            if (done(job)) { return job; }
            await Task.Delay(25);
        }
        throw new TimeoutException("the job did not reach the expected state");
    }

    /// <summary>
    /// Verifies that a job runs in the background and ends ready with a result only its owner can open.
    /// </summary>
    [Fact]
    public async Task AJob_RunsInTheBackground_AndEndsReady_WithAResultOnlyItsOwnerCanOpen()
    {
        var service = Service();

        var started = await service.StartAsync(@"CONTOSO\ann", Text, null);
        Assert.Equal(DesignJobStatus.Queued, started.Status);
        var job = await Wait(service, @"CONTOSO\ann", started.Id, j => j.Status == DesignJobStatus.Ready);

        Assert.Equal(100, job.Percent);
        var result = DesignJobService.ResultOf(job)!;
        Assert.Equal("new_servis_kaydi", result.Document.Tables[0].LogicalName);
        Assert.Null(await service.GetAsync(@"CONTOSO\bob", started.Id));
        Assert.Empty(await service.ListAsync(@"CONTOSO\bob"));
        Assert.All(await service.ListAsync(@"CONTOSO\ann"), j => Assert.Null(j.Result));
    }

    /// <summary>
    /// Verifies that a too short request is rejected.
    /// </summary>
    [Theory]
    [InlineData("kısa")]
    [InlineData("")]
    public async Task ATooShortRequest_IsRejected(string text)
    {
        var ex = await Assert.ThrowsAsync<DesignJobException>(() => Service().StartAsync("u", text, null));
        Assert.Equal("invalid", ex.Code);
    }

    /// <summary>
    /// Verifies that a too long request and a bad prefix are rejected.
    /// </summary>
    [Fact]
    public async Task ATooLongRequest_AndABadPrefix_AreRejected()
    {
        var service = Service();

        Assert.Equal("invalid", (await Assert.ThrowsAsync<DesignJobException>(() => service.StartAsync("u", new string('a', DesignJobService.MaxRequestChars + 1), null))).Code);
        Assert.Equal("invalid", (await Assert.ThrowsAsync<DesignJobException>(() => service.StartAsync("u", Text, "New Prefix!"))).Code);
    }

    /// <summary>
    /// Verifies that only one job per user and two on the server can be active.
    /// </summary>
    [Fact]
    public async Task OnlyOneActiveJobPerUser_AndTwoOnTheServer()
    {
        _llm.Block = true;
        var service = Service();
        var first = await service.StartAsync("a", Text, null);

        Assert.Equal("busy", (await Assert.ThrowsAsync<DesignJobException>(() => service.StartAsync("a", Text, null))).Code);
        await service.StartAsync("b", Text, null);
        Assert.Equal("busy", (await Assert.ThrowsAsync<DesignJobException>(() => service.StartAsync("c", Text, null))).Code);

        _llm.Gate.Release(100);
        await Wait(service, "a", first.Id, j => j.Status == DesignJobStatus.Ready);
        await service.StartAsync("c", Text, null);
        _llm.Gate.Release(100);
    }

    /// <summary>
    /// Verifies that cancelling stops the job and marks it cancelled.
    /// </summary>
    [Fact]
    public async Task Cancelling_StopsTheJob_AndMarksItCancelled()
    {
        _llm.Block = true;
        var service = Service();
        var job = await service.StartAsync("a", Text, null);
        await Wait(service, "a", job.Id, j => j.Status == DesignJobStatus.Running);

        Assert.True(await service.CancelAsync("a", job.Id));
        var done = await Wait(service, "a", job.Id, j => j.Status == DesignJobStatus.Cancelled);

        Assert.NotNull(done.FinishedAt);
        Assert.False(await service.CancelAsync("b", job.Id));
        await service.StartAsync("a", Text, null);
        _llm.Gate.Release(100);
    }

    /// <summary>
    /// Verifies that the time limit stops a long job with a clear message.
    /// </summary>
    [Fact]
    public async Task TheTimeLimit_StopsALongJob_WithAClearMessage()
    {
        _llm.Block = true;
        _timeout = TimeSpan.FromMilliseconds(150);
        var service = Service();
        var job = await service.StartAsync("a", Text, null);

        var done = await Wait(service, "a", job.Id, j => j.Status == DesignJobStatus.Failed);

        Assert.Contains("took too long", done.Error);
    }

    /// <summary>
    /// Verifies that a model failure shows a fixed message and never the internal detail.
    /// </summary>
    [Fact]
    public async Task AModelFailure_IsAFixedMessage_NeverTheInternalDetail()
    {
        _llm.Throw = true;
        var service = Service();
        var job = await service.StartAsync("a", Text, null);

        var done = await Wait(service, "a", job.Id, j => j.Status == DesignJobStatus.Failed);

        Assert.DoesNotContain("internal-host", done.Error);
        Assert.Contains("could not be completed", done.Error);
    }

    /// <summary>
    /// Verifies that a restart marks interrupted jobs as failed.
    /// </summary>
    [Fact]
    public async Task ARestart_MarksInterruptedJobsFailed()
    {
        await _store.AddAsync(new DesignJob { Id = "j1", Owner = "a", Status = DesignJobStatus.Running, Title = "t", Request = "r", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        await _store.AddAsync(new DesignJob { Id = "j2", Owner = "a", Status = DesignJobStatus.Ready, Title = "t", Request = "r", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });

        Assert.Equal(1, await Service().RecoverAsync());

        Assert.Equal(DesignJobStatus.Failed, (await _store.GetAsync("a", "j1"))!.Status);
        Assert.Equal(DesignJobStatus.Ready, (await _store.GetAsync("a", "j2"))!.Status);
    }

    /// <summary>
    /// Verifies that delete needs a finished job and its owner and retention purges old ones.
    /// </summary>
    [Fact]
    public async Task Delete_NeedsAFinishedJob_AndItsOwner_AndRetentionPurgesOldOnes()
    {
        var service = Service();
        var job = await service.StartAsync("a", Text, null);
        await Wait(service, "a", job.Id, j => j.Status == DesignJobStatus.Ready);

        Assert.False(await service.DeleteAsync("b", job.Id));
        Assert.True(await service.DeleteAsync("a", job.Id));
        Assert.Null(await service.GetAsync("a", job.Id));

        await _store.AddAsync(new DesignJob { Id = "old", Owner = "a", Status = DesignJobStatus.Ready, Title = "t", Request = "r", CreatedAt = DateTimeOffset.UtcNow.AddDays(-200), UpdatedAt = DateTimeOffset.UtcNow });
        await _store.AddAsync(new DesignJob { Id = "new", Owner = "a", Status = DesignJobStatus.Ready, Title = "t", Request = "r", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        Assert.Equal(1, await _store.PurgeOlderThanAsync(DateTimeOffset.UtcNow.AddDays(-90)));
        Assert.NotNull(await _store.GetAsync("a", "new"));
    }

    /// <summary>
    /// Verifies that a running job cannot be deleted.
    /// </summary>
    [Fact]
    public async Task ARunningJob_CannotBeDeleted()
    {
        _llm.Block = true;
        var service = Service();
        var job = await service.StartAsync("a", Text, null);

        Assert.Equal("busy", (await Assert.ThrowsAsync<DesignJobException>(() => service.DeleteAsync("a", job.Id))).Code);
        _llm.Gate.Release(100);
    }

    private static DesignResult ResultWithEverything()
    {
        var doc = new DesignDocument
        {
            Summary = "Özet & <tehlikeli> \"metin\"",
            Tables = { new DesignTable { LogicalName = "new_randevu", DisplayName = "Randevu", Ownership = "user", PrimaryNameLogical = "new_ad", PrimaryNameDisplay = "Ad",
                Fields = { new DesignField { LogicalName = "new_tc_kimlik", DisplayName = "T.C. Kimlik No", Type = "string", Length = 11 } } } },
            Relationships = { new DesignRelationship { Kind = "one-to-many", From = "a", To = "b", LookupField = "new_x", DeleteBehavior = "cascade" } },
            Views = { new DesignView { Table = "new_randevu", Name = "Aktif", Columns = { "new_ad" } } },
            OpenQuestions = { "Silinsin mi?\u0001" },
        };
        return new DesignResult { Language = "tr", Prefix = "new", Document = doc, Problems = new[] { "Sorun 1" } };
    }

    /// <summary>
    /// Verifies that the generated docx is a valid package with the required parts and well-formed XML.
    /// </summary>
    [Fact]
    public void TheDocx_IsAValidPackage_WithTheRequiredParts_AndWellFormedXml()
    {
        var bytes = DesignDocx.Build(ResultWithEverything());

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        foreach (var part in new[] { "[Content_Types].xml", "_rels/.rels", "word/document.xml", "word/_rels/document.xml.rels", "word/styles.xml" })
        {
            Assert.Contains(part, names);
            using var reader = new StreamReader(zip.GetEntry(part)!.Open());
            XDocument.Parse(reader.ReadToEnd());
        }
    }

    /// <summary>
    /// Verifies that the generated docx passes the official OpenXML schema validation in both languages.
    /// </summary>
    [Theory]
    [InlineData("tr")]
    [InlineData("en")]
    public void TheDocx_PassesTheOfficialOpenXmlSchemaValidation(string language)
    {
        var result = ResultWithEverything();
        result.Language = language;
        result.Matches = new[] { new SchemaMatch("new_servis_kaydi", "Servis Kaydı", true, 5, 9, new[] { "servis" }, new[] { new MatchedField("new_aracid", "Araç", "Lookup", "araç") }, new[] { "new_tutar" }) };
        result.Decisions = new[] { new ReuseDecision { Entity = "new_servis_kaydi", Action = "extend", Reason = "Zaten var." } };
        result.ClosedTables = new[] { "new_servis_kaydi" };

        using var stream = new MemoryStream(DesignDocx.Build(result));
        using var package = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(stream, false);
        var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator(DocumentFormat.OpenXml.FileFormatVersions.Office2019).Validate(package).ToList();

        Assert.True(errors.Count == 0, string.Join("; ", errors.Select(e => e.Description + " @ " + e.Path?.XPath)));
        Assert.NotEmpty(package.MainDocumentPart!.Document!.Body!.Elements<DocumentFormat.OpenXml.Wordprocessing.Table>());
    }

    /// <summary>
    /// Verifies that the DOCX holds the content escapes XML drops illegal characters and flags sensitive fields.
    /// </summary>
    [Fact]
    public void TheDocx_HoldsTheContent_EscapesXml_DropsIllegalCharacters_AndFlagsSensitiveFields()
    {
        var bytes = DesignDocx.Build(ResultWithEverything());

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var xml = reader.ReadToEnd();
        var text = string.Concat(XDocument.Parse(xml).Descendants(XName.Get("t", "http://schemas.openxmlformats.org/wordprocessingml/2006/main")).Select(e => e.Value));

        Assert.Contains("Özelleştirme tasarımı", text);
        Assert.Contains("Özet & <tehlikeli> \"metin\"", text);
        Assert.Contains("new_randevu", text);
        Assert.Contains("Silinsin mi?", text);
        Assert.False(xml.Contains((char)1));
        Assert.Contains("new_randevu.new_tc_kimlik", text);
        Assert.Contains("Gözden geçirilmeli", text);
        Assert.Contains("w:tbl", xml.Replace("<w:tbl>", "w:tbl"));
    }
}
