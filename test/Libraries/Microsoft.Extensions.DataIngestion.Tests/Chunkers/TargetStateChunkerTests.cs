// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.ML.Tokenizers;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Chunkers.Tests;

public class TargetStateChunkerTests
{
    [Fact]
    public async Task LiteralContentAndPagesSurviveBuiltInChunking()
    {
        IngestionDocument document = new("literal");
        IngestionDocumentSection section = new();
        IngestionDocumentHeader header = IngestionDocumentHeader.FromText("Title *literal*");
        header.PageNumber = 1;
        section.Elements.Add(header);
        IngestionDocumentParagraph paragraph =
            IngestionDocumentParagraph.FromText("Use `code` and [links](https://example.test).");
        paragraph.PageNumber = 2;
        section.Elements.Add(paragraph);
        document.Sections.Add(section);

        IngestionChunk chunk = Assert.Single(await CreateChunker().ProcessAsync(document).ToListAsync());

        Assert.Equal(
            "Title *literal*\nUse `code` and [links](https://example.test).",
            Assert.IsType<TextContent>(chunk.Content).Text,
            ignoreLineEndingDifferences: true);
        Assert.Equal([1, 2], chunk.PageNumbers);
    }

    [Fact]
    public async Task CodeSplitsIntoCompleteFences()
    {
        Assert.Equal(
            "```\nline\n```",
            new IngestionDocumentCodeBlock("line\n").GetMarkdown(),
            ignoreLineEndingDifferences: true);

        IngestionDocument document = new("code");
        IngestionDocumentSection section = new();
        IngestionDocumentCodeBlock code = new(
            "line 1\nline 2\nline 3\nline 4\nline 5\nline 6\nline 7\nline 8")
        {
            PageNumber = 3,
        };
        section.Elements.Add(code);
        document.Sections.Add(section);

        IReadOnlyList<IngestionChunk> chunks = await CreateChunker(maxTokens: 12)
            .ProcessAsync(document)
            .ToListAsync();

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk =>
        {
            string text = Assert.IsType<TextContent>(chunk.Content).Text;
            string fence = text[..text.IndexOf('\n')].TrimEnd('\r');
            Assert.StartsWith("```", fence, StringComparison.Ordinal);
            Assert.EndsWith(fence, text, StringComparison.Ordinal);
            Assert.Equal([3], chunk.PageNumbers);
        });
    }

    [Fact]
    public async Task StructuredSpansAndBinaryImagesNeedNoMetadata()
    {
        Assert.Throws<ArgumentException>(() =>
            IngestionDocumentImage.FromContent(ReadOnlyMemory<byte>.Empty, "image/png"));

        IngestionDocument document = new("structured");
        IngestionDocumentSection section = new();
        IngestionDocumentParagraph mergedContent = IngestionDocumentParagraph.FromText("Merged");
        mergedContent.PageNumber = 1;
        section.Elements.Add(new IngestionDocumentTable(
            rowCount: 2,
            columnCount: 2,
            cells:
            [
                new IngestionDocumentTableCell(0, 0, [mergedContent], rowSpan: 2, kind: "rowHeader"),
                new IngestionDocumentTableCell(0, 1, [IngestionDocumentParagraph.FromText("Value")]),
                new IngestionDocumentTableCell(1, 1, [IngestionDocumentParagraph.FromText("42")]),
            ])
        {
            PageNumber = 1,
        });
        IngestionDocumentImage image = IngestionDocumentImage.FromContent(
            new byte[] { 1, 2, 3 },
            "image/png");
        image.PageNumber = 2;
        section.Elements.Add(image);
        document.Sections.Add(section);

        IReadOnlyList<IngestionChunk> chunks = await CreateChunker().ProcessAsync(document).ToListAsync();

        Assert.Contains(chunks, chunk =>
            chunk.Content is TextContent text
            && text.Text.Contains("rowSpan=2", StringComparison.Ordinal)
            && text.Text.Contains("kind=rowHeader", StringComparison.Ordinal)
            && chunk.PageNumbers.SequenceEqual([1]));
        IngestionChunk imageChunk = Assert.Single(chunks, chunk => chunk.Content is DataContent);
        Assert.Equal(0, imageChunk.TokenCount);
        Assert.Equal([2], imageChunk.PageNumbers);
        Assert.All(chunks, chunk => Assert.False(chunk.HasMetadata));
    }

    [Fact]
    public async Task StructuredCellKindIsConsumedWithoutSpans()
    {
        IngestionDocument document = new("cell-kind");
        IngestionDocumentSection section = new();
        section.Elements.Add(new IngestionDocumentTable(
            rowCount: 1,
            columnCount: 1,
            cells:
            [
                new IngestionDocumentTableCell(
                    0,
                    0,
                    [IngestionDocumentParagraph.FromText("Heading")],
                    kind: "columnHeader"),
            ]));
        document.Sections.Add(section);

        IngestionChunk chunk = Assert.Single(await CreateChunker().ProcessAsync(document).ToListAsync());

        TextContent content = Assert.IsType<TextContent>(chunk.Content);
        Assert.Contains("kind=columnHeader", content.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LegacyTableRetainsExplicitMarkdownAndCellPages()
    {
        IngestionDocumentParagraph cell = new("Value") { PageNumber = 4 };
        IngestionDocumentTable table = new(
            "| Heading |\n| --- |\n| Value |",
            new IngestionDocumentElement?[,] { { cell } });
        IngestionDocument document = new("legacy-table");
        document.Sections.Add(new IngestionDocumentSection
        {
            Elements = { table }
        });

        IngestionChunk chunk = Assert.Single(await CreateChunker().ProcessAsync(document).ToListAsync());

        Assert.Null(table.StructuredCells);
        Assert.Contains("| Heading |", Assert.IsType<TextContent>(chunk.Content).Text, StringComparison.Ordinal);
        Assert.Equal([4], chunk.PageNumbers);
    }

    private static IngestionChunker CreateChunker(int maxTokens = 2_000)
        => new SectionChunker(new(
            TiktokenTokenizer.CreateForModel("gpt-4o"))
        {
            MaxTokensPerChunk = maxTokens,
            OverlapTokens = 0,
        });
}
