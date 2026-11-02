using CrmNlQuery.Application.Knowledge.Services;

namespace CrmNlQuery.Tests.Application.Knowledge.Services;

/// <summary>
/// Tests splitting document text into chunks by size, paragraphs, headings and sentences.
/// </summary>
public class ChunkerTests
{
    /// <summary>
    /// Verifies that short text is one chunk with the title in front.
    /// </summary>
    [Fact]
    public void ShortText_IsOneChunk_WithTheTitleInFront()
    {
        var chunks = Chunker.Split("Aktif müşteri", "Son 12 ayda siparişi olan hesap.");

        Assert.Equal(new[] { "Aktif müşteri\nSon 12 ayda siparişi olan hesap." }, chunks);
    }

    /// <summary>
    /// Verifies that empty or blank text gives no chunks.
    /// </summary>
    [Fact]
    public void EmptyOrBlankText_GivesNoChunks()
    {
        Assert.Empty(Chunker.Split("T", ""));
        Assert.Empty(Chunker.Split("T", "  \n\n  "));
    }

    /// <summary>
    /// Verifies that paragraphs are packed together until the limit.
    /// </summary>
    [Fact]
    public void Paragraphs_ArePackedTogether_UntilTheLimit()
    {
        var a = new string('a', 300);
        var b = new string('b', 300);
        var c = new string('c', 300);

        var chunks = Chunker.Split("T", a + "\n\n" + b + "\n\n" + c, 700);

        Assert.Equal(2, chunks.Count);
        Assert.Contains(a, chunks[0]);
        Assert.Contains(b, chunks[0]);
        Assert.Contains(c, chunks[1]);
    }

    /// <summary>
    /// Verifies that a heading starts a new chunk even when there is room.
    /// </summary>
    [Fact]
    public void AHeading_StartsANewChunk_EvenWhenThereIsRoom()
    {
        var chunks = Chunker.Split("T", "# Segment\nKOBİ = 2\n\n# Eski alan\nnew_segment kullanma");

        Assert.Equal(2, chunks.Count);
        Assert.Contains("KOBİ = 2", chunks[0]);
        Assert.Contains("new_segment", chunks[1]);
    }

    /// <summary>
    /// Verifies that a long paragraph is cut at sentence ends and nothing is lost.
    /// </summary>
    [Fact]
    public void ALongParagraph_IsCutAtSentenceEnds_AndNothingIsLost()
    {
        var sentence = "Bu bir cümledir ve oldukça uzundur. ";
        var text = string.Concat(Enumerable.Repeat(sentence, 60)).Trim();

        var chunks = Chunker.Split("T", text, 300);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Length <= 300 + 2));
        Assert.All(chunks, c => Assert.EndsWith(".", c));
        Assert.Equal(60, chunks.Sum(c => c.Split("cümledir").Length - 1));
    }

    /// <summary>
    /// Verifies that a word without any break is cut hard not looped.
    /// </summary>
    [Fact]
    public void AWordWithoutAnyBreak_IsCutHard_NotLooped()
    {
        var chunks = Chunker.Split("T", new string('x', 2000), 500);

        Assert.Equal(4, chunks.Count);
    }

    /// <summary>
    /// Verifies that windows line endings are handled.
    /// </summary>
    [Fact]
    public void WindowsLineEndings_AreHandled()
    {
        var chunks = Chunker.Split("T", "Bir.\r\n\r\nİki.");

        Assert.Single(chunks);
        Assert.DoesNotContain("\r", chunks[0]);
    }
}
