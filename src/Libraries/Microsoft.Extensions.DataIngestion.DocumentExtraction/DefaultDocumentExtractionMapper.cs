// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion;

internal static class DefaultDocumentExtractionMapper
{
    internal static IngestionDocument Map(
        DocumentExtractionResult source,
        string identifier,
        MarkdownOnlyPagePolicy markdownOnlyPagePolicy)
    {
        _ = Throw.IfNull(source);
        IngestionDocument document = new(identifier);

        foreach (DocumentPage page in source.Pages)
        {
            int pageNumber = page.PageNumber;
            if (pageNumber <= 0)
            {
                Throw.InvalidOperationException(
                    $"Document extraction page number '{pageNumber}' must be a positive one-based value.");
            }

            IngestionDocumentSection section = new()
            {
                PageNumber = pageNumber,
            };

            foreach (DocumentElement sourceElement in page.Elements)
            {
                IngestionDocumentElement? element = MapElement(sourceElement, pageNumber);
                if (element is not null)
                {
                    section.Elements.Add(element);
                }
            }

            if (section.Elements.Count == 0)
            {
                MapElementlessPage(page, section, markdownOnlyPagePolicy);
            }

            document.Sections.Add(section);
        }

        return document;
    }

    private static void MapElementlessPage(
        DocumentPage page,
        IngestionDocumentSection section,
        MarkdownOnlyPagePolicy markdownOnlyPagePolicy)
    {
        if (string.IsNullOrEmpty(page.Markdown))
        {
            return;
        }

        if (markdownOnlyPagePolicy == MarkdownOnlyPagePolicy.RequireElements)
        {
            Throw.InvalidOperationException(
                $"Document extraction page {page.PageNumber} contains provider Markdown but no normalized elements. "
                + $"Set {nameof(DocumentExtractionReaderOptions)}.{nameof(DocumentExtractionReaderOptions.MarkdownOnlyPagePolicy)} "
                + $"to {nameof(MarkdownOnlyPagePolicy.PreserveAsMarkdown)} to preserve it as Markdown.");
        }

        section.Elements.Add(new IngestionDocumentParagraph(page.Markdown!)
        {
            PageNumber = page.PageNumber,
        });
    }

    private static IngestionDocumentElement? MapElement(DocumentElement source, int pageNumber)
    {
        IngestionDocumentElement? target = source switch
        {
            DocumentBlock block => MapBlock(block),
            DocumentTable table => MapTable(table, pageNumber),
            DocumentImage image => MapImage(image, pageNumber),
            _ => throw new NotSupportedException(
                $"Document extraction page {pageNumber} contains unsupported element type '{source.GetType().FullName}'."),
        };

#pragma warning disable IDE0031 // Keep the assignment as a statement on all supported language versions.
        if (target is not null)
        {
            target.PageNumber = pageNumber;
        }
#pragma warning restore IDE0031

        return target;
    }

    private static IngestionDocumentElement? MapBlock(DocumentBlock source)
    {
        if (source.Text.Length == 0)
        {
            return null;
        }

        if (source.Kind == DocumentBlockKind.Title)
        {
            return IngestionDocumentHeader.FromText(source.Text);
        }

        return source.Kind == DocumentBlockKind.Code
            ? new IngestionDocumentCodeBlock(source.Text)
            : IngestionDocumentParagraph.FromText(source.Text);
    }

    private static IngestionDocumentTable MapTable(DocumentTable source, int pageNumber)
    {
        if (source.Cells is null)
        {
            if (string.IsNullOrEmpty(source.MarkdownRepresentation))
            {
                Throw.InvalidOperationException(
                    $"Document extraction page {pageNumber} contains a table with neither structured cells nor provider Markdown.");
            }

            return new IngestionDocumentTable(
                source.MarkdownRepresentation!,
#pragma warning disable CA1814 // The existing table API requires a rectangular array.
                new IngestionDocumentElement?[Math.Max(0, source.RowCount), Math.Max(0, source.ColumnCount)]);
#pragma warning restore CA1814 // Prefer jagged arrays over multidimensional
        }

        if (source.RowCount <= 0 || source.ColumnCount <= 0)
        {
            Throw.InvalidOperationException(
                $"Document extraction page {pageNumber} contains a structured table with invalid dimensions "
                + $"{source.RowCount}x{source.ColumnCount}.");
        }

        List<IngestionDocumentTableCell> cells = new(source.Cells.Count);
        foreach (DocumentTableCell sourceCell in source.Cells)
        {
            List<IngestionDocumentElement> elements = [];
            foreach (DocumentElement sourceElement in sourceCell.Elements)
            {
                IngestionDocumentElement? element = MapElement(sourceElement, pageNumber);
                if (element is not null)
                {
                    elements.Add(element);
                }
            }

            cells.Add(new(
                sourceCell.RowIndex,
                sourceCell.ColumnIndex,
                elements,
                sourceCell.RowSpan,
                sourceCell.ColumnSpan,
                sourceCell.Kind?.Value));
        }

        return new(source.RowCount, source.ColumnCount, cells);
    }

    private static IngestionDocumentImage MapImage(DocumentImage source, int pageNumber)
    {
        if (source.Content is ReadOnlyMemory<byte> content && !content.IsEmpty)
        {
            return IngestionDocumentImage.FromContent(content, source.MediaType, source.Caption);
        }

        if (!string.IsNullOrEmpty(source.Caption))
        {
            return IngestionDocumentImage.FromText(source.Caption!);
        }

        Throw.InvalidOperationException(
            $"Document extraction page {pageNumber} contains an image with neither non-empty content nor a caption.");
        return null!;
    }
}
