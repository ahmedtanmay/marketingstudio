using System.Text;
using System.Text.RegularExpressions;

namespace MarketingStudio.Application.Knowledge;

public sealed record TextChunk(string HeadingPath, string Text);

/// <summary>
/// Structure-aware chunking (Knowledge Base Design.md → Chunk): split on Markdown headings, then
/// paragraphs, then sentences. Target ≈ 500 tokens (≈ 2,000 characters), max ≈ 800 tokens, with
/// ≈ 75 tokens (≈ 300 characters) of overlap between consecutive chunks of the same section.
/// </summary>
public static partial class Chunker
{
    public const int TargetChars = 2000;
    public const int MaxChars = 3200;
    public const int OverlapChars = 300;

    public static IReadOnlyList<TextChunk> Chunk(string markdown)
    {
        var chunks = new List<TextChunk>();
        foreach (var (path, body) in Sections(markdown))
        {
            var pieces = Paragraphs(body).SelectMany(SplitLong).ToList();
            var current = new StringBuilder();
            foreach (var piece in pieces)
            {
                if (current.Length > 0 && current.Length + piece.Length + 2 > TargetChars)
                {
                    var text = current.ToString().Trim();
                    chunks.Add(new TextChunk(path, text));
                    current.Clear().Append(Tail(text)).Append("\n\n");
                }

                current.Append(piece).Append("\n\n");
            }

            var last = current.ToString().Trim();
            var previous = chunks.LastOrDefault();
            // Skip a trailing chunk that is nothing but overlap already contained in the previous chunk.
            if (last.Length > 0 && !(previous is not null && previous.HeadingPath == path && previous.Text.EndsWith(last, StringComparison.Ordinal)))
            {
                chunks.Add(new TextChunk(path, last));
            }
        }

        return chunks;
    }

    /// <summary>Approximate token count (≈ 4 characters per token for English).</summary>
    public static int EstimateTokens(string text) => (text.Length + 3) / 4;

    private static IEnumerable<(string Path, string Body)> Sections(string markdown)
    {
        var headings = new string?[7];
        var body = new StringBuilder();
        var path = string.Empty;

        foreach (var rawLine in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var match = HeadingLine().Match(rawLine);
            if (match.Success)
            {
                if (body.ToString().Trim().Length > 0)
                {
                    yield return (path, body.ToString());
                }

                body.Clear();
                var level = match.Groups[1].Value.Length;
                headings[level] = match.Groups[2].Value.Trim();
                for (var i = level + 1; i < headings.Length; i++)
                {
                    headings[i] = null;
                }

                path = string.Join(" > ", headings.Skip(1).Where(h => !string.IsNullOrEmpty(h)));
                continue;
            }

            body.Append(rawLine).Append('\n');
        }

        if (body.ToString().Trim().Length > 0)
        {
            yield return (path, body.ToString());
        }
    }

    private static IEnumerable<string> Paragraphs(string body) =>
        BlankLines().Split(body)
            .Select(p => Whitespace().Replace(p, " ").Trim())
            .Where(p => p.Length > 0);

    /// <summary>Paragraphs longer than the maximum are split by sentence, then by words as a last resort.</summary>
    private static IEnumerable<string> SplitLong(string paragraph)
    {
        if (paragraph.Length <= MaxChars)
        {
            yield return paragraph;
            yield break;
        }

        var current = new StringBuilder();
        foreach (var sentence in SentenceEnd().Split(paragraph).Where(s => s.Length > 0))
        {
            foreach (var part in HardSplit(sentence))
            {
                if (current.Length > 0 && current.Length + part.Length + 1 > TargetChars)
                {
                    yield return current.ToString().Trim();
                    current.Clear();
                }

                current.Append(part).Append(' ');
            }
        }

        if (current.Length > 0)
        {
            yield return current.ToString().Trim();
        }
    }

    private static IEnumerable<string> HardSplit(string text)
    {
        for (var i = 0; i < text.Length; i += TargetChars)
        {
            yield return text.Substring(i, Math.Min(TargetChars, text.Length - i));
        }
    }

    /// <summary>The last ≈ 300 characters, starting at a word boundary.</summary>
    private static string Tail(string text)
    {
        if (text.Length <= OverlapChars)
        {
            return text;
        }

        var start = text.Length - OverlapChars;
        var space = text.IndexOf(' ', start);
        return space < 0 ? text[start..] : text[(space + 1)..];
    }

    [GeneratedRegex(@"^\s{0,3}(#{1,6})\s+(.+?)\s*#*\s*$")]
    private static partial Regex HeadingLine();

    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex BlankLines();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceEnd();
}
