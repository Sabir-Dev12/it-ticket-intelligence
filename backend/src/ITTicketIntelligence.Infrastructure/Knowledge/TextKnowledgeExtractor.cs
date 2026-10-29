using System.Text;
using System.Text.RegularExpressions;
using ITTicketIntelligence.Application.Knowledge;
using ITTicketIntelligence.Domain.Enums;

namespace ITTicketIntelligence.Infrastructure.Knowledge;

/// <summary>Local extraction only. Source identifiers and Markdown URLs are never fetched.</summary>
public sealed class TextKnowledgeExtractor : IKnowledgeTextExtractor
{
    private static readonly Regex Heading = new(@"^ {0,3}#{1,6}(?:\s+|$)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Link = new(@"!?\[([^\]\r\n]*)\]\([^\)\r\n]*\)", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Quote = new(@"^ {0,3}> ?", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public Task<string> ExtractAsync(string content, KnowledgeContentFormat format, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (format == KnowledgeContentFormat.PlainText) return Task.FromResult(content);
        if (format != KnowledgeContentFormat.Markdown) throw new ArgumentOutOfRangeException(nameof(format));
        var result = new StringBuilder(content.Length);
        string? fence = null;
        foreach (var line in content.ReplaceLineEndings("\n").Split('\n'))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var trimmed = line.TrimStart();
            if (fence is null && (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal)))
            { fence = trimmed[..3]; continue; }
            if (fence is not null)
            {
                if (trimmed.StartsWith(fence, StringComparison.Ordinal)) { fence = null; continue; }
                result.Append(line).Append('\n');
                continue;
            }
            var text = Link.Replace(Quote.Replace(Heading.Replace(line, ""), ""), "$1")
                .Replace("**", "", StringComparison.Ordinal).Replace("__", "", StringComparison.Ordinal)
                .Replace("`", "", StringComparison.Ordinal);
            result.Append(text).Append('\n');
        }
        return Task.FromResult(result.ToString());
    }
}
