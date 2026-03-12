using Markdig;
using Microsoft.AspNetCore.Components;

namespace ResearchHarness.Web.Rendering;

public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    /// <summary>
    /// Converts a markdown string to a Blazor <see cref="MarkupString"/> for safe HTML rendering.
    /// Returns empty markup for null/whitespace input.
    /// </summary>
    public static MarkupString ToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return new MarkupString(string.Empty);

        var html = Markdown.ToHtml(markdown, Pipeline);
        return new MarkupString(html);
    }
}
