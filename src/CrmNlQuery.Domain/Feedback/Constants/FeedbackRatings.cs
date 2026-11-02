namespace CrmNlQuery.Domain.Feedback.Constants;

/// <summary>
/// Holds the allowed feedback rating values, thumbs up and thumbs down, and validates incoming ratings against them.
/// </summary>
public static class FeedbackRatings
{
    public const string Up = "up";
    public const string Down = "down";

    /// <summary>
    /// Checks whether a rating is one of the allowed values.
    /// </summary>
    public static bool IsValid(string? rating) => rating == Up || rating == Down;
}
