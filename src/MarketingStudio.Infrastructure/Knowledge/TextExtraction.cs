using System.Net;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using HtmlAgilityPack;
using MarketingStudio.Application.Abstractions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace MarketingStudio.Infrastructure.Knowledge;

/// <summary>PDF (PdfPig), DOCX (Open XML SDK), HTML (HtmlAgilityPack), TXT/MD. Output is light Markdown.</summary>
internal sealed class TextExtractor : ITextExtractor
{
    public const string NoText = "No extractable text found. Scanned PDFs (images of text) are not supported yet.";

    public string Extract(Stream content, string extension)
    {
        var text = extension switch
        {
            ".pdf" => Pdf(content),
            ".docx" => Docx(content),
            ".html" or ".htm" => Html(ReadText(content)),
            ".txt" or ".md" => ReadText(content),
            _ => throw new TextExtractionException("Unsupported file type."),
        };

        text = text.Replace("\r\n", "\n").Trim();
        if (text.Count(char.IsLetterOrDigit) < 20)
        {
            throw new TextExtractionException(NoText);
        }

        return text;
    }

    private static string ReadText(Stream content)
    {
        using var reader = new StreamReader(content, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return reader.ReadToEnd();
    }

    private static string Pdf(Stream content)
    {
        try
        {
            using var document = PdfDocument.Open(content);
            var pages = document.GetPages().Select(page => ContentOrderTextExtractor.GetText(page)).ToList();
            return string.Join("\n\n", RemoveRepeatedLines(pages));
        }
        catch (Exception ex) when (ex is not TextExtractionException)
        {
            throw new TextExtractionException("The PDF couldn't be read. It may be damaged or password-protected.");
        }
    }

    /// <summary>Drops headers/footers: short lines that repeat on most pages (Knowledge Base Design.md → Clean).</summary>
    private static IEnumerable<string> RemoveRepeatedLines(List<string> pages)
    {
        if (pages.Count < 3)
        {
            return pages;
        }

        var repeated = pages
            .SelectMany(p => p.Split('\n').Select(l => l.Trim()).Where(l => l.Length is > 0 and < 80).Distinct())
            .GroupBy(l => l)
            .Where(g => g.Count() >= Math.Max(3, pages.Count / 2))
            .Select(g => g.Key)
            .ToHashSet();

        return pages.Select(p => string.Join('\n', p.Split('\n').Where(l => !repeated.Contains(l.Trim()))));
    }

    private static string Docx(Stream content)
    {
        try
        {
            using var document = WordprocessingDocument.Open(content, false);
            var body = document.MainDocumentPart?.Document?.Body;
            if (body is null)
            {
                return string.Empty;
            }

            var output = new StringBuilder();
            foreach (var element in body.ChildElements)
            {
                switch (element)
                {
                    case Paragraph paragraph:
                        var text = paragraph.InnerText.Trim();
                        if (text.Length == 0)
                        {
                            continue;
                        }

                        var level = HeadingLevel(paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value);
                        output.Append(level > 0 ? new string('#', level) + " " : string.Empty).Append(text).Append("\n\n");
                        break;
                    case Table table:
                        foreach (var row in table.Elements<TableRow>())
                        {
                            output.Append(string.Join(" | ", row.Elements<TableCell>().Select(c => c.InnerText.Trim()))).Append('\n');
                        }

                        output.Append('\n');
                        break;
                }
            }

            return output.ToString();
        }
        catch (Exception ex) when (ex is not TextExtractionException)
        {
            throw new TextExtractionException("The Word document couldn't be read. It may be damaged or password-protected.");
        }
    }

    private static int HeadingLevel(string? styleId) => styleId switch
    {
        null => 0,
        "Title" => 1,
        _ when styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(styleId.AsSpan(7), out var n) && n is >= 1 and <= 6 => n,
        _ => 0,
    };

    private static string Html(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        foreach (var node in document.DocumentNode.SelectNodes("//script|//style|//noscript|//nav|//footer|//header|//form|//svg")
                     ?? Enumerable.Empty<HtmlNode>())
        {
            node.Remove();
        }

        var output = new StringBuilder();
        Walk(document.DocumentNode, output);
        return output.ToString();
    }

    private static void Walk(HtmlNode node, StringBuilder output)
    {
        foreach (var child in node.ChildNodes)
        {
            var name = child.Name.ToLowerInvariant();
            if (child.NodeType == HtmlNodeType.Text)
            {
                output.Append(WebUtility.HtmlDecode(child.InnerText));
            }
            else if (name.Length == 2 && name[0] == 'h' && char.IsDigit(name[1]) && name[1] is >= '1' and <= '6')
            {
                output.Append("\n\n").Append(new string('#', name[1] - '0')).Append(' ')
                    .Append(WebUtility.HtmlDecode(child.InnerText).Trim()).Append("\n\n");
            }
            else if (name is "p" or "div" or "li" or "tr" or "section" or "article" or "br" or "blockquote")
            {
                output.Append("\n\n");
                Walk(child, output);
                output.Append("\n\n");
            }
            else
            {
                Walk(child, output);
            }
        }
    }
}
