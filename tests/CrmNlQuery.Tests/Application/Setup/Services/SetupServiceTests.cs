using CrmNlQuery.Application.Setup.Abstractions;
using CrmNlQuery.Application.Setup.Models;
using CrmNlQuery.Application.Setup.Services;
using CrmNlQuery.Infrastructure.Persistence.Memory.Setup;

namespace CrmNlQuery.Tests.Application.Setup.Services;

/// <summary>
/// Tests the setup wizard service: the setup code and its lock, sessions, validation before saving and what is stored.
/// </summary>
public class SetupServiceTests
{
    private const string Code = "ABCD-EFGH-2345";

    private sealed class FakeCrm : ICrmProbe
    {
        public int Calls;
        public Task<IReadOnlyList<SetupCheck>> RunAsync(CrmProbeInput input, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<SetupCheck>>(new List<SetupCheck>());
        }
    }

    private sealed class FakeModel : IModelProbe
    {
        public Task<IReadOnlyList<SetupCheck>> RunAsync(ModelProbeInput input, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<SetupCheck>>(new List<SetupCheck>());
        }
    }

    private static (SetupService Service, MemoryAppConfigStore Store, FakeCrm Crm, Func<DateTimeOffset> Now, Action<TimeSpan> Advance) Create()
    {
        var store = new MemoryAppConfigStore();
        var crm = new FakeCrm();
        var now = DateTimeOffset.Parse("2026-10-27T10:00:00Z");
        var cipher = new AesGcmConfigCipher(new byte[32]);
        var service = new SetupService(Code, () => true, _ => Task.FromResult<IAppConfigStore?>(store), () => cipher, crm, new FakeModel(), () => now);
        return (service, store, crm, () => now, span => now = now.Add(span));
    }

    private static SetupInput ValidInput(string provider = "Ollama", string baseUrl = "http://localhost:11434")
    {
        return new SetupInput(
            new CrmProbeInput("http://crm.example/org", "DOM", "svc", "pass"),
            new ModelProbeInput(provider, baseUrl, "qwen", provider == "Anthropic" ? "key" : null),
            "http://nlq.example:5080", "DOM\\admin", false, provider == "Anthropic");
    }

    [Fact]
    public void ACorrectCodeGivesASessionAndAWrongOneDoesNot()
    {
        var (service, _, _, _, _) = Create();

        Assert.Equal("invalid_code", service.Unlock("WRONG").Error);
        var ok = service.Unlock("abcd efgh 2345");

        Assert.True(ok.Ok);
        Assert.False(string.IsNullOrEmpty(ok.Value));
    }

    [Fact]
    public void ThreeWrongCodesLockTheWizardUntilTheLockEnds()
    {
        var (service, _, _, _, advance) = Create();

        service.Unlock("a");
        service.Unlock("b");
        Assert.Equal("locked", service.Unlock("c").Error);
        Assert.Equal("locked", service.Unlock(Code).Error);

        advance(SetupService.LockFor + TimeSpan.FromSeconds(1));
        Assert.True(service.Unlock(Code).Ok);
    }

    [Fact]
    public async Task TestsNeedASession()
    {
        var (service, _, crm, _, _) = Create();

        var result = await service.TestCrmAsync("nope", ValidInput().Crm);

        Assert.Equal("unauthorized", result.Error);
        Assert.Equal(0, crm.Calls);
    }

    [Fact]
    public async Task ASessionExpires()
    {
        var (service, _, _, _, advance) = Create();
        var session = service.Unlock(Code).Value;

        advance(SetupService.SessionFor + TimeSpan.FromSeconds(1));

        Assert.Equal("unauthorized", (await service.TestCrmAsync(session, ValidInput().Crm)).Error);
    }

    [Fact]
    public async Task ACrmAddressWithCredentialsIsRefusedBeforeAnyTest()
    {
        var (service, _, crm, _, _) = Create();
        var session = service.Unlock(Code).Value;
        var input = new CrmProbeInput("http://user:pw@crm.example", "DOM", "svc", "pass");

        var result = await service.TestCrmAsync(session, input);

        Assert.Equal("invalid_url", result.Error);
        Assert.Equal(0, crm.Calls);
    }

    [Fact]
    public async Task AModelOutsideThisMachineNeedsTheNetworkConfirmation()
    {
        var (service, _, _, _, _) = Create();
        var session = service.Unlock(Code).Value;
        var model = ValidInput(baseUrl: "http://gpu.lan:11434").Model;

        Assert.Equal("confirmation_required", (await service.TestModelAsync(session, model, false, false)).Error);
        Assert.True((await service.TestModelAsync(session, model, true, false)).Ok);
    }

    [Fact]
    public async Task CompletingStoresSecretsEncryptedAndWorksOnlyOnce()
    {
        var (service, store, _, _, _) = Create();
        var session = service.Unlock(Code).Value;

        var first = await service.CompleteAsync(session, ValidInput());
        var second = await service.CompleteAsync(session, ValidInput());

        Assert.True(first.Ok);
        Assert.False(second.Ok);
        var all = await store.ListAsync();
        var password = all.Single(e => e.Key == "Crm:Password");
        Assert.True(password.Secret);
        Assert.DoesNotContain("pass", password.Value);
        Assert.Contains(all, e => e.Key == "Setup:Completed" && e.Value == "true");
        Assert.Contains(all, e => e.Key == "Identity:Mode" && e.Value == "CrmProof");
        Assert.Contains(all, e => e.Key == "Auth:SigningKey" && e.Secret);
        Assert.Contains(all, e => e.Key == "Cors:AllowedOrigins" && e.Value == "http://crm.example");
    }

    [Fact]
    public void ALocalModelDoesNotNeedATrustedHostButARemoteOneGetsOne()
    {
        var cipher = new AesGcmConfigCipher(new byte[32]);

        var local = SetupService.BuildEntries(ValidInput(), cipher);
        var remote = SetupService.BuildEntries(ValidInput(baseUrl: "http://gpu.lan:11434"), cipher);

        Assert.DoesNotContain(local, e => e.Key == "Ollama:TrustedHosts:0");
        Assert.Contains(remote, e => e.Key == "Ollama:TrustedHosts:0" && e.Value == "gpu.lan");
    }

    [Fact]
    public async Task TheCloudModelNeedsItsLabConfirmationAndSavesTheKeyEncrypted()
    {
        var (service, store, _, _, _) = Create();
        var session = service.Unlock(Code).Value;
        var withoutConfirm = ValidInput("Anthropic", "") with { ConfirmCloudLab = false };

        Assert.Equal("confirmation_required", (await service.CompleteAsync(session, withoutConfirm)).Error);
        Assert.True((await service.CompleteAsync(session, ValidInput("Anthropic", ""))).Ok);

        var key = (await store.ListAsync()).Single(e => e.Key == "Anthropic:ApiKey");
        Assert.True(key.Secret);
        Assert.NotEqual("key", key.Value);
    }

    [Fact]
    public async Task AnAdministratorWithoutADomainIsRefused()
    {
        var (service, _, _, _, _) = Create();
        var session = service.Unlock(Code).Value;

        var result = await service.CompleteAsync(session, ValidInput() with { AdminAccount = "admin" });

        Assert.Equal("adminAccount", result.Field);
    }

    [Fact]
    public void CodesLookLikeThreeGroupsOfFour()
    {
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$", SetupService.NewCode());
    }
}
