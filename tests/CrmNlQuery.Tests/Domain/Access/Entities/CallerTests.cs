using CrmNlQuery.Domain.Access.Entities;

namespace CrmNlQuery.Tests.Domain.Access.Entities;

/// <summary>
/// Tests that every CRM request carries an explicit caller identity and that no null caller exists.
/// </summary>
public class CallerTests
{
    /// <summary>
    /// Verifies that a user caller carries the user ID.
    /// </summary>
    [Fact]
    public void AUserCaller_CarriesTheUserId()
    {
        var id = Guid.NewGuid();

        var caller = Caller.User(id);

        Assert.Equal(id, caller.UserId);
        Assert.False(caller.IsEvaluation);
    }

    /// <summary>
    /// Verifies that an empty GUID is not a user.
    /// </summary>
    [Fact]
    public void AnEmptyGuid_IsNotAUser()
    {
        Assert.Throws<ArgumentException>(() => Caller.User(Guid.Empty));
    }

    /// <summary>
    /// Verifies that the evaluation caller is named and has no user ID.
    /// </summary>
    [Fact]
    public void TheEvaluationCaller_IsNamed_AndHasNoUserId()
    {
        var caller = Caller.ForEvaluation();

        Assert.True(caller.IsEvaluation);
        Assert.Throws<InvalidOperationException>(() => caller.UserId);
    }

    /// <summary>
    /// Verifies that two callers of the same user are equal and the evaluation caller equals only itself.
    /// </summary>
    [Fact]
    public void TwoCallersOfTheSameUser_AreEqual_AndTheEvaluationCallerEqualsOnlyItself()
    {
        var id = Guid.NewGuid();

        Assert.Equal(Caller.User(id), Caller.User(id));
        Assert.NotEqual(Caller.User(id), Caller.User(Guid.NewGuid()));
        Assert.Equal(Caller.ForEvaluation(), Caller.ForEvaluation());
        Assert.NotEqual(Caller.User(id), Caller.ForEvaluation());
        Assert.Equal(Caller.User(id).GetHashCode(), Caller.User(id).GetHashCode());
    }

    /// <summary>
    /// Verifies that to string never shows the full user ID so it cannot leak into logs.
    /// </summary>
    [Fact]
    public void ToString_NeverShowsTheFullUserId_SoItCannotLeakIntoLogs()
    {
        var id = Guid.NewGuid();

        var text = Caller.User(id).ToString();

        Assert.DoesNotContain(id.ToString(), text);
        Assert.Contains(id.ToString().Substring(0, 8), text);
        Assert.Equal("evaluation", Caller.ForEvaluation().ToString());
    }
}
