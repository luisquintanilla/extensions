// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Documents;
using Microsoft.ML.Tokenizers;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Chunkers.Tests;

public class HeaderChunkerTests : DocumentChunkerTests
{
    private static readonly Tokenizer _tokenizer = TiktokenTokenizer.CreateForModel("gpt-4");

    protected override IngestionChunker CreateDocumentChunker(int maxTokensPerChunk = 2_000, int overlapTokens = 500) =>
        new HeaderChunker(new(_tokenizer) { MaxTokensPerChunk = maxTokensPerChunk });

    [Fact]
    public async Task ProcessAsync_NestedHeadingsCarryExpectedHeadingStack()
    {
        IngestionDocument document = TestDocuments.Create(
            "nested-headings",
            TestDocuments.Text("h1", "Header 1", DocumentTextRole.Heading, 1, 1),
            TestDocuments.Text("h2", "Header 1_1", DocumentTextRole.Heading, 2, 2),
            TestDocuments.Text("body1", "Paragraph 1_1", pageNumber: 3),
            TestDocuments.Text("h3", "Header 1_1_1", DocumentTextRole.Heading, 3, 4),
            TestDocuments.Text("body2", "Paragraph 1_1_1", pageNumber: 5));

        IReadOnlyList<IngestionChunk> chunks = await CreateDocumentChunker().ProcessAsync(document).ToListAsync();

        Assert.Equal(2, chunks.Count);
        ChunkerTestAssertions.Equal(
            chunks[0],
            document,
            "Header 1 Header 1_1\nParagraph 1_1",
            "Header 1 Header 1_1",
            ["h1", "h2", "body1"],
            [1, 2, 3],
            _tokenizer);
        ChunkerTestAssertions.Equal(
            chunks[1],
            document,
            "Header 1 Header 1_1 Header 1_1_1\nParagraph 1_1_1",
            "Header 1 Header 1_1 Header 1_1_1",
            ["h1", "h2", "h3", "body2"],
            [1, 2, 4, 5],
            _tokenizer);
        Assert.Equal(["Header 1 Header 1_1", "Header 1 Header 1_1 Header 1_1_1"], chunks.Select(static chunk => chunk.Context));
    }

    [Fact]
    public async Task ProcessAsync_ResetsSiblingHeadingContext()
    {
        IngestionDocument document = TestDocuments.Create(
            "siblings",
            TestDocuments.Text("h1", "Root", DocumentTextRole.Heading, 1, 1),
            TestDocuments.Text("h2a", "First", DocumentTextRole.Heading, 2, 2),
            TestDocuments.Text("body1", "alpha", pageNumber: 3),
            TestDocuments.Text("h2b", "Second", DocumentTextRole.Heading, 2, 4),
            TestDocuments.Text("body2", "beta", pageNumber: 5));

        IReadOnlyList<IngestionChunk> chunks = await CreateDocumentChunker().ProcessAsync(document).ToListAsync();

        Assert.Equal(2, chunks.Count);
        ChunkerTestAssertions.Equal(chunks[0], document, "Root First\nalpha", "Root First", ["h1", "h2a", "body1"], [1, 2, 3], _tokenizer);
        ChunkerTestAssertions.Equal(chunks[1], document, "Root Second\nbeta", "Root Second", ["h1", "h2b", "body2"], [1, 4, 5], _tokenizer);
        Assert.DoesNotContain("First", GetText(chunks[1]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_LowerLevelSiblingClearsDeeperHeadingContext()
    {
        IngestionDocument document = TestDocuments.Create(
            "nested-siblings",
            TestDocuments.Text("h1", "Root", DocumentTextRole.Heading, 1, 1),
            TestDocuments.Text("h2a", "First", DocumentTextRole.Heading, 2, 2),
            TestDocuments.Text("h3", "Deep", DocumentTextRole.Heading, 3, 3),
            TestDocuments.Text("body1", "alpha", pageNumber: 4),
            TestDocuments.Text("h2b", "Second", DocumentTextRole.Heading, 2, 5),
            TestDocuments.Text("body2", "beta", pageNumber: 6));

        IReadOnlyList<IngestionChunk> chunks = await CreateDocumentChunker().ProcessAsync(document).ToListAsync();

        Assert.Equal(2, chunks.Count);
        ChunkerTestAssertions.Equal(
            chunks[0],
            document,
            "Root First Deep\nalpha",
            "Root First Deep",
            ["h1", "h2a", "h3", "body1"],
            [1, 2, 3, 4],
            _tokenizer);
        ChunkerTestAssertions.Equal(
            chunks[1],
            document,
            "Root Second\nbeta",
            "Root Second",
            ["h1", "h2b", "body2"],
            [1, 5, 6],
            _tokenizer);
        Assert.DoesNotContain("First", GetText(chunks[1]), StringComparison.Ordinal);
        Assert.DoesNotContain("Deep", GetText(chunks[1]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_SplitsLongTextOnNewlineWithinTokenLimit()
    {
        IngestionDocument document = TestDocuments.Create(
            "newlines",
            TestDocuments.Text("h1", "Header A", DocumentTextRole.Heading, 1, 1),
            TestDocuments.Text("h2", "Header B", DocumentTextRole.Heading, 2, 1),
            TestDocuments.Text("h3", "Header C", DocumentTextRole.Heading, 3, 1),
            TestDocuments.Text(
                "body",
                "This is a very long text. It's expressed with plenty of tokens. And it contains a new line.\nWith some text after the new line.",
                pageNumber: 2),
            TestDocuments.Text("tail", "And following paragraph.", pageNumber: 3));

        IReadOnlyList<IngestionChunk> chunks = await CreateDocumentChunker(maxTokensPerChunk: 29).ProcessAsync(document).ToListAsync();

        Assert.Equal(2, chunks.Count);
        const string Context = "Header A Header B Header C";
        ChunkerTestAssertions.Equal(
            chunks[0],
            document,
            Context + "\nThis is a very long text. It's expressed with plenty of tokens. And it contains a new line.\n",
            Context,
            ["h1", "h2", "h3", "body"],
            [1, 2],
            _tokenizer);
        ChunkerTestAssertions.Equal(
            chunks[1],
            document,
            Context + "\nWith some text after the new line.\nAnd following paragraph.",
            Context,
            ["h1", "h2", "h3", "body", "tail"],
            [1, 2, 3],
            _tokenizer);
        Assert.EndsWith("\n", GetText(chunks[0]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_ContextLargerThanLimit_FailsDeterministically()
    {
        const string Context = "Header A Header B Header C";
        int contextTokenCount = _tokenizer.CountTokens(Context, considerNormalization: false);
        IngestionDocument document = TestDocuments.Create(
            "context-limit",
            TestDocuments.Text("h1", "Header A", DocumentTextRole.Heading, 1),
            TestDocuments.Text("h2", "Header B", DocumentTextRole.Heading, 2),
            TestDocuments.Text("h3", "Header C", DocumentTextRole.Heading, 3),
            TestDocuments.Text("body", "body"));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await CreateDocumentChunker(maxTokensPerChunk: contextTokenCount).ProcessAsync(document).ToListAsync());

        Assert.Equal("Can't fit in the current chunk. Consider increasing max tokens per chunk.", exception.Message);
    }

    [Fact]
    public async Task ProcessAsync_MultiRowTableSplitsAtRowBoundaries()
    {
        DocumentTable table = CreateTable(includeSecondDataRow: true);
        IngestionDocument document = TestDocuments.Create(
            "multi-row",
            TestDocuments.Text("heading", "H", DocumentTextRole.Heading, 1, 1),
            table);

        IReadOnlyList<IngestionChunk> chunks = await CreateDocumentChunker(maxTokensPerChunk: 8).ProcessAsync(document).ToListAsync();

        Assert.Equal(2, chunks.Count);
        ChunkerTestAssertions.Equal(
            chunks[0],
            document,
            "H\nName\tValue\nA\t1",
            "H",
            ["heading", "table", "name-cell", "name", "value-cell", "value", "a-cell", "a", "one-cell", "one"],
            [1, 2],
            _tokenizer);
        ChunkerTestAssertions.Equal(
            chunks[1],
            document,
            "H\nName\tValue\nB\t2",
            "H",
            ["heading", "table", "name-cell", "name", "value-cell", "value", "b-cell", "b", "two-cell", "two"],
            [1, 3],
            _tokenizer);
        Assert.Equal(["A\t1", "B\t2"], chunks.Select(chunk => GetText(chunk).Split('\n').Last()));
    }

    [Fact]
    public async Task ProcessAsync_OneRowTableUsesNeutralTabSeparatedProjection()
    {
        DocumentTable table = new(
            new("table"),
            1,
            2,
            [
                Cell("left-cell", 0, 0, "left", "left", DocumentTableCellRole.Content, 4),
                Cell("right-cell", 0, 1, "right", "right", DocumentTableCellRole.Content, 5),
            ]);
        IngestionDocument document = TestDocuments.Create(
            "one-row",
            TestDocuments.Text("heading", "H", DocumentTextRole.Heading, 1, 1),
            table);

        IngestionChunk chunk = Assert.Single(await CreateDocumentChunker(maxTokensPerChunk: 100).ProcessAsync(document).ToListAsync());

        ChunkerTestAssertions.Equal(chunk, document, "H\nleft\tright", "H", ["heading", "table", "left-cell", "left", "right-cell", "right"], [1, 4, 5], _tokenizer);
        Assert.DoesNotContain('|', GetText(chunk));
    }

    [Fact]
    public async Task ProcessAsync_OversizedTableRow_FailsDeterministically()
    {
        DocumentTable table = new(
            new("table"),
            1,
            1,
            [Cell("cell", 0, 0, "text", "this row is much too large", DocumentTableCellRole.Content, 2)]);
        IngestionDocument document = TestDocuments.Create(
            "oversized-row",
            TestDocuments.Text("heading", "H", DocumentTextRole.Heading, 1, 1),
            table);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await CreateDocumentChunker(maxTokensPerChunk: 3).ProcessAsync(document).ToListAsync());

        Assert.Equal("Can't fit in the current chunk. Consider increasing max tokens per chunk.", exception.Message);
    }

    [Fact]
    public async Task ProcessAsync_EveryChunkHasExactPositiveTokenCount()
    {
        IngestionDocument document = TestDocuments.Create(
            "counts",
            TestDocuments.Text("heading", "Report", DocumentTextRole.Heading, 1, 1),
            TestDocuments.Text("body", "alpha beta", pageNumber: 2));

        IngestionChunk chunk = Assert.Single(await CreateDocumentChunker(maxTokensPerChunk: 100).ProcessAsync(document).ToListAsync());

        ChunkerTestAssertions.Equal(chunk, document, "Report\nalpha beta", "Report", ["heading", "body"], [1, 2], _tokenizer);
        Assert.InRange(chunk.TokenCount, 1, 100);
    }

    [Fact]
    public async Task ProcessAsync_PreCanceledToken_StopsPacking()
    {
        IngestionDocument document = TestDocuments.Create("canceled", TestDocuments.Text("body", "alpha", pageNumber: 1));
        using CancellationTokenSource cancellationSource = new();
        cancellationSource.Cancel();

        OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await CreateDocumentChunker().ProcessAsync(document, cancellationSource.Token).ToListAsync());

        Assert.Equal(cancellationSource.Token, exception.CancellationToken);
    }

    private static DocumentTable CreateTable(bool includeSecondDataRow)
    {
        List<DocumentTableCell> cells =
        [
            Cell("name-cell", 0, 0, "name", "Name", DocumentTableCellRole.ColumnHeader, 1),
            Cell("value-cell", 0, 1, "value", "Value", DocumentTableCellRole.ColumnHeader, 1),
            Cell("a-cell", 1, 0, "a", "A", DocumentTableCellRole.Content, 2),
            Cell("one-cell", 1, 1, "one", "1", DocumentTableCellRole.Content, 2),
        ];
        if (includeSecondDataRow)
        {
            cells.Add(Cell("b-cell", 2, 0, "b", "B", DocumentTableCellRole.Content, 3));
            cells.Add(Cell("two-cell", 2, 1, "two", "2", DocumentTableCellRole.Content, 3));
        }

        return new(new("table"), includeSecondDataRow ? 3 : 2, 2, cells);
    }

    private static DocumentTableCell Cell(
        string cellId,
        int row,
        int column,
        string textId,
        string text,
        DocumentTableCellRole role,
        int pageNumber) =>
        new(
            new(cellId),
            row,
            column,
            [TestDocuments.Text(textId, text, pageNumber: pageNumber)],
            role: role,
            pageReferences: [new(pageNumber)]);
}
