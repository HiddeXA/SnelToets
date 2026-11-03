using Markdig;

namespace SnelToets.Core.Text;

/// <summary>Renders user/AI supplied Markdown to safe HTML (raw HTML is disabled).</summary>
public static class Markdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseEmphasisExtras()
        .UsePipeTables()
        .UseListExtras()
        .UseAutoLinks()
        .Build();

    public static string ToHtml(string? markdown) =>
        string.IsNullOrWhiteSpace(markdown) ? "" : Markdig.Markdown.ToHtml(markdown, Pipeline);

    /// <summary>Inline rendering without a wrapping &lt;p&gt;, for prompts and options.</summary>
    public static string ToInlineHtml(string? markdown)
    {
        var html = ToHtml(markdown).Trim();
        if (html.StartsWith("<p>") && html.EndsWith("</p>") && html.IndexOf("<p>", 3, StringComparison.Ordinal) < 0)
            html = html[3..^4];
        return html;
    }
}
