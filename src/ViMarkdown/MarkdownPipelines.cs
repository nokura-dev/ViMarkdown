using Markdig;
using Markdig.Extensions.EmphasisExtras;

namespace ViMarkdown;

// Keep each consumer's parsing options explicit. Export supports more extensions
// than the editor; source coloring additionally needs delimiter trivia.
internal static class MarkdownPipelines
{
    public static MarkdownPipeline Projection { get; } = new MarkdownPipelineBuilder()
        .UsePipeTables().UseTaskLists().UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .UseDefinitionLists().UsePreciseSourceLocation().UseAdjacentReferenceLinks().Build();
    public static MarkdownPipeline Highlighting { get; } = new MarkdownPipelineBuilder()
        .UsePipeTables().UseTaskLists().UseDefinitionLists()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .UsePreciseSourceLocation().EnableTrackTrivia().UseAdjacentReferenceLinks().Build();
    public static MarkdownPipeline MarkdownExport { get; } = CreateExport(trackTrivia: true);
    public static MarkdownPipeline DocxExport { get; } = CreateExport(trackTrivia: false);

    private static MarkdownPipeline CreateExport(bool trackTrivia)
    {
        var builder = new MarkdownPipelineBuilder().UseAdvancedExtensions();
        if (trackTrivia) builder.EnableTrackTrivia();
        return builder.UseAdjacentReferenceLinks().Build();
    }
}
