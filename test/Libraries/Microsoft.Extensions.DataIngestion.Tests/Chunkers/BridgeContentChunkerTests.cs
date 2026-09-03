// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ML.Tokenizers;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Chunkers.Tests;

public class BridgeContentChunkerTests
{
    [Fact]
    public async Task SectionChunkerUsesLiteralTextAndCarriesPageProvenance()
    {
        IngestionDocument document = new("literal");
        document.Sections.Add(new()
        {
            Elements =
            {
                IngestionDocumentHeader.FromText("Title *literal*", level: 1)
                    .OnPage(1),
                IngestionDocumentParagraph.FromText("Use `code` and [links](https://example.test).")
                    .OnPage(2),
            },
        });

        IngestionChunk<string> chunk = Assert.Single(
            await CreateChunker().ProcessAsync(document).ToListAsync());

        Assert.Equal(
            "Title *literal*\nUse `code` and [links](https://example.test).",
            chunk.Content,
            ignoreLineEndingDifferences: true);
        Assert.Equal([1, 2], chunk.PageNumbers);
    }

    [Fact]
    public async Task AuthoredMarkdownStillWinsOverItsTextProjection()
    {
        IngestionDocument document = new("authored-markdown");
        document.Sections.Add(new()
        {
            Elements =
            {
                new IngestionDocumentParagraph("Use **bold** text.")
                {
                    Text = "Use bold text.",
                },
            },
        });

        IngestionChunk<string> chunk = Assert.Single(
            await CreateChunker().ProcessAsync(document).ToListAsync());

        Assert.Equal("Use **bold** text.", chunk.Content);
    }

    [Fact]
    public async Task SectionChunkerSplitsCodeIntoCompleteFences()
    {
        IngestionDocument document = new("code");
        document.Sections.Add(new()
        {
            Elements =
            {
                new IngestionDocumentCodeBlock(
                    "line 1\nline 2\nline 3\nline 4\nline 5\nline 6\nline 7\nline 8")
                    .OnPage(3),
            },
        });

        IReadOnlyList<IngestionChunk<string>> chunks = await CreateChunker(maxTokens: 12)
            .ProcessAsync(document)
            .ToListAsync();

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk =>
        {
            string fence = chunk.Content[..chunk.Content.IndexOf('\n')].TrimEnd('\r');
            Assert.StartsWith("```", fence, StringComparison.Ordinal);
            Assert.EndsWith(fence, chunk.Content, StringComparison.Ordinal);
            Assert.Equal([3], chunk.PageNumbers);
        });
    }

    [Fact]
    public async Task DocumentTokenChunkerCarriesExactPagesAcrossElementBoundaries()
    {
        IngestionDocument document = new("pages");
        document.Sections.Add(new()
        {
            Elements =
            {
                new IngestionDocumentParagraph("one two three four").OnPage(1),
                new IngestionDocumentParagraph(" five six seven eight").OnPage(2),
            },
        });

        DocumentTokenChunker chunker = new(new(TiktokenTokenizer.CreateForModel("gpt-4o"))
        {
            MaxTokensPerChunk = 4,
            OverlapTokens = 0,
        });
        IReadOnlyList<IngestionChunk<string>> chunks = await chunker
            .ProcessAsync(document)
            .ToListAsync();

        Assert.Equal([1], chunks[0].PageNumbers);
        Assert.Equal([2], chunks[^1].PageNumbers);
    }

    [Fact]
    public async Task StructuredTableFactsReachBuiltInChunking()
    {
        IngestionDocument document = new("table");
        document.Sections.Add(new()
        {
            Elements =
            {
                new IngestionDocumentTable(
                    2,
                    2,
                    [
                        new(
                            0,
                            0,
                            [IngestionDocumentParagraph.FromText("Region")],
                            rowSpan: 2,
                            kind: "rowHeader"),
                        new(0, 1, [IngestionDocumentParagraph.FromText("Value")]),
                        new(1, 1, [IngestionDocumentParagraph.FromText("42")]),
                    ]).OnPage(1),
            },
        });

        IngestionChunk<string> chunk = Assert.Single(
            await CreateChunker().ProcessAsync(document).ToListAsync());

        Assert.Contains("rowspan=\"2\"", chunk.Content, StringComparison.Ordinal);
        Assert.Contains("data-kind=\"rowHeader\"", chunk.Content, StringComparison.Ordinal);
        Assert.Equal([1], chunk.PageNumbers);
    }

    private static SectionChunker CreateChunker(int maxTokens = 2_000)
        => new(new(TiktokenTokenizer.CreateForModel("gpt-4o"))
        {
            MaxTokensPerChunk = maxTokens,
            OverlapTokens = 0,
        });
}

#pragma warning disable SA1402 // Test helper is scoped to this test file.
internal static class BridgeContentChunkerTestExtensions
{
    internal static T OnPage<T>(this T element, int pageNumber)
        where T : IngestionDocumentElement
    {
        element.PageNumber = pageNumber;
        return element;
    }
}
#pragma warning restore SA1402
