using Markdig;
using Markdig.Parsers;
using Markdig.Syntax;

namespace ViMarkdown;

internal static class AdjacentReferenceLinks
{
    public static MarkdownPipelineBuilder UseAdjacentReferenceLinks(this MarkdownPipelineBuilder builder)
    {
        for (var i = 0; i < builder.BlockParsers.Count; i++)
            if (builder.BlockParsers[i] is ParagraphBlockParser)
                builder.BlockParsers[i] = new ReferenceParagraphParser();
        return builder;
    }

    private sealed class ReferenceParagraphParser : ParagraphBlockParser
    {
        public override BlockState TryContinue(BlockProcessor processor, Block block)
        {
            // A valid definition may start immediately after paragraph text.
            // Paragraph continuation is handled specially by BlockProcessor:
            // returning None only skips the parser and does not reopen the line.
            // Explicitly close the old paragraph and open a new one for the
            // definition, preserving the standard registration and source spans.
            // Fenced/indented code uses its own block parser.
            var line = processor.Line;
            if (!processor.IsCodeIndent && line.CurrentChar == '[' &&
                LinkReferenceDefinition.TryParse(ref line, out _))
            {
                processor.Close(block);
                return base.TryOpen(processor);
            }
            return base.TryContinue(processor, block);
        }
    }
}
