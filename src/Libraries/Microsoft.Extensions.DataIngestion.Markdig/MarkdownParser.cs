// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.Extensions.Documents;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion;

internal static class MarkdownParser
{
    internal static IngestionDocument Parse(string markdown, string identifier)
    {
        _ = Throw.IfNullOrEmpty(markdown);
        _ = Throw.IfNullOrEmpty(identifier);

        MarkdownPipeline pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
        MarkdownDocument markdownDocument = Markdown.Parse(markdown, pipeline);
        NodeIds ids = new(identifier);
        List<DocumentNode> children = [];
        bool previousWasBreak = false;

        foreach (Block block in markdownDocument)
        {
            if (block is ThematicBreakBlock)
            {
                previousWasBreak = true;
                continue;
            }

            if (block is LinkReferenceDefinitionGroup || IsEmptyBlock(block))
            {
                continue;
            }

            children.Add(MapBlock(block, previousWasBreak, ids));
            previousWasBreak = false;
        }

        return new IngestionDocument(identifier, new Document(children));
    }

#if !NET
    internal static System.Threading.Tasks.Task<string> ReadToEndAsync(
        this System.IO.StreamReader reader,
        System.Threading.CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
            ? System.Threading.Tasks.Task.FromCanceled<string>(cancellationToken)
            : reader.ReadToEndAsync();
#endif

    private static bool IsEmptyBlock(Block block) =>
        block is LeafBlock leaf && (leaf.Inline is null || leaf.Inline.FirstChild is null) && block is not CodeBlock;

    private static DocumentNode MapBlock(Block block, bool previousWasBreak, NodeIds ids) =>
        block switch
        {
            HeadingBlock heading => new DocumentText(
                ids.Next(),
                GetText(heading.Inline),
                DocumentTextRole.Heading,
                level: heading.Level),
            ParagraphBlock paragraph when TryGetOnlyImage(paragraph, out LinkInline? image) => MapImage(image!, ids),
            ParagraphBlock paragraph => new DocumentText(
                ids.Next(),
                GetText(paragraph.Inline),
                previousWasBreak ? DocumentTextRole.Footer : DocumentTextRole.Paragraph),
            FencedCodeBlock code => new DocumentText(
                ids.Next(),
                code.Lines.ToString(),
                DocumentTextRole.Code,
                language: code.Info?.ToString()),
            CodeBlock code => new DocumentText(ids.Next(), code.Lines.ToString(), DocumentTextRole.Code),
            ListBlock list => MapList(list, previousWasBreak, ids),
            QuoteBlock quote => MapContainer(quote, DocumentContainerRole.Quote, previousWasBreak, ids),
            Table table => MapTable(table, ids),
            _ => throw new NotSupportedException($"Block type '{block.GetType().Name}' is not supported."),
        };

    private static DocumentContainer MapList(ListBlock list, bool previousWasBreak, NodeIds ids)
    {
        List<DocumentNode> items = [];
        foreach (Block block in list)
        {
            if (block is ListItemBlock item)
            {
                items.Add(MapContainer(item, DocumentContainerRole.ListItem, previousWasBreak, ids));
            }
        }

        return new DocumentContainer(ids.Next(), DocumentContainerRole.List, items);
    }

    private static DocumentContainer MapContainer(
        ContainerBlock container,
        DocumentContainerRole role,
        bool previousWasBreak,
        NodeIds ids)
    {
        DocumentNodeId id = ids.Next();
        List<DocumentNode> children = [];
        foreach (Block child in container)
        {
            if (!IsEmptyBlock(child))
            {
                children.Add(MapBlock(child, previousWasBreak, ids));
            }
        }

        return new DocumentContainer(id, role, children);
    }

    private static DocumentTable MapTable(Table table, NodeIds ids)
    {
        DocumentNodeId tableId = ids.Next();
        List<DocumentTableCell> cells = [];
        int columnCount = 0;
        int firstRowIndex = table.Count > 0 && IsEmptyTableRow((TableRow)table[0]) ? 1 : 0;

        for (int sourceRowIndex = firstRowIndex; sourceRowIndex < table.Count; sourceRowIndex++)
        {
            int rowIndex = sourceRowIndex - firstRowIndex;
            TableRow row = (TableRow)table[sourceRowIndex];
            int columnIndex = 0;
            foreach (TableCell cell in row)
            {
                List<DocumentNode> content = [];
                foreach (Block block in cell)
                {
                    if (!IsEmptyBlock(block))
                    {
                        content.Add(MapBlock(block, previousWasBreak: false, ids));
                    }
                }

                int columnSpan = Math.Max(1, cell.ColumnSpan);
                cells.Add(new(
                    ids.Next(),
                    rowIndex,
                    columnIndex,
                    content,
                    columnSpan: columnSpan,
                    role: rowIndex == 0 ? DocumentTableCellRole.ColumnHeader : DocumentTableCellRole.Content));
                columnIndex += columnSpan;
            }

            columnCount = Math.Max(columnCount, columnIndex);
        }

        return new DocumentTable(tableId, table.Count - firstRowIndex, columnCount, cells);
    }

    private static bool TryGetOnlyImage(ParagraphBlock paragraph, out LinkInline? image)
    {
        LinkInline[] images = paragraph.Inline!.Descendants<LinkInline>().Where(static link => link.IsImage).ToArray();
        image = images.Length == 1 ? images[0] : null;
        if (images.Length == 0)
        {
            return false;
        }

        LinkInline selectedImage = image!;
        bool hasOtherText = paragraph.Inline.Descendants<LiteralInline>()
            .Any(literal => !IsDescendantOf(literal, selectedImage) && !string.IsNullOrWhiteSpace(literal.Content.ToString()));
        if (images.Length != 1 || hasOtherText)
        {
            throw new NotSupportedException("Markdown paragraphs that mix images with other content are not supported.");
        }

        return true;
    }

    private static DocumentImage MapImage(LinkInline link, NodeIds ids)
    {
        string? description = GetText(link);
        byte[]? content = null;
        string? mediaType = null;
        Uri? source = null;

        if (link.Url is string url && url.StartsWith("data:", StringComparison.Ordinal))
        {
            int semicolon = url.IndexOf(';');
            int comma = url.IndexOf(',');
            if (semicolon > 5 && comma > semicolon && string.Equals(url.Substring(semicolon + 1, comma - semicolon - 1), "base64", StringComparison.Ordinal))
            {
                mediaType = url.Substring(5, semicolon - 5);
                content = Convert.FromBase64String(url.Substring(comma + 1));
            }
        }
        else if (!string.IsNullOrWhiteSpace(link.Url))
        {
            source = new Uri(link.Url!, UriKind.RelativeOrAbsolute);
        }

        return new DocumentImage(ids.Next(), content, mediaType, source, description);
    }

    private static bool IsDescendantOf(Inline inline, ContainerInline ancestor)
    {
        for (ContainerInline? current = inline.Parent; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEmptyTableRow(TableRow row)
    {
        foreach (TableCell cell in row)
        {
            foreach (Block block in cell)
            {
                if (block is LeafBlock { Inline: not null } leaf && !string.IsNullOrWhiteSpace(GetText(leaf.Inline)))
                {
                    return false;
                }

                if (block is CodeBlock code && !string.IsNullOrWhiteSpace(code.Lines.ToString()))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static string GetText(ContainerInline? container)
    {
        Debug.Assert(container is not null);
        StringBuilder text = new();
        foreach (Inline inline in container!)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    _ = text.Append(literal.Content);
                    break;
                case LineBreakInline:
                    _ = text.Append('\n');
                    break;
                case ContainerInline nested:
                    _ = text.Append(GetText(nested));
                    break;
                case CodeInline code:
                    _ = text.Append(code.Content);
                    break;
                case HtmlInline html:
                    _ = text.Append(html.Tag);
                    break;
                default:
                    throw new NotSupportedException($"Inline type '{inline.GetType().Name}' is not supported.");
            }
        }

        return text.ToString();
    }

    private sealed class NodeIds
    {
        private readonly string _prefix;
        private int _next;

        public NodeIds(string prefix)
        {
            _prefix = prefix;
        }

        public DocumentNodeId Next() => new($"{_prefix}:{_next++}");
    }
}
