using CrmNlQuery.Application.Preferences.Services;
using CrmNlQuery.Infrastructure.Persistence.Memory.Access;

namespace CrmNlQuery.Tests.Application.Preferences.Services;

/// <summary>
/// Tests the preferred display name: cleaned, limited, and removed when blank.
/// </summary>
public class PreferencesServiceTests
{
    private static PreferencesService Create() => new PreferencesService(new MemoryPreferencesStore());

    /// <summary>
    /// Verifies that the name is cleaned into one line and returned as saved.
    /// </summary>
    [Fact]
    public async Task TheName_IsCleanedIntoOneLine_AndReturnedAsSaved()
    {
        var service = Create();
        var me = Guid.NewGuid();

        var saved = await service.SetDisplayNameAsync(me, "  Deniz \n Arslan  ");

        Assert.Equal("Deniz Arslan", saved);
        Assert.Equal("Deniz Arslan", await service.GetDisplayNameAsync(me));
    }

    /// <summary>
    /// Verifies that hidden and control characters are removed and the length is capped.
    /// </summary>
    [Fact]
    public async Task HiddenAndControlCharacters_AreRemoved_AndTheLengthIsCapped()
    {
        var service = Create();
        var me = Guid.NewGuid();

        var saved = await service.SetDisplayNameAsync(me, "A​‮B" + new string('x', 200));

        Assert.DoesNotContain('​', saved!);
        Assert.DoesNotContain('‮', saved!);
        Assert.True(saved!.Length <= PreferencesService.MaxNameLength);
    }

    /// <summary>
    /// Verifies that a blank name clears the stored one.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t")]
    [InlineData(null)]
    public async Task ABlankName_ClearsTheStoredOne(string? blank)
    {
        var service = Create();
        var me = Guid.NewGuid();
        await service.SetDisplayNameAsync(me, "Deniz");

        var saved = await service.SetDisplayNameAsync(me, blank);

        Assert.Null(saved);
        Assert.Null(await service.GetDisplayNameAsync(me));
    }

    /// <summary>
    /// Verifies that each user has their own name.
    /// </summary>
    [Fact]
    public async Task EachUser_HasTheirOwnName()
    {
        var service = Create();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await service.SetDisplayNameAsync(a, "Ayşe");

        Assert.Equal("Ayşe", await service.GetDisplayNameAsync(a));
        Assert.Null(await service.GetDisplayNameAsync(b));
    }
}
