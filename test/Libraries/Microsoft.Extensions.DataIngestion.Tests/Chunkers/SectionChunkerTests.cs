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

public class SectionChunkerTests : DocumentChunkerTests
{
    private static readonly Tokenizer _tokenizer = TiktokenTokenizer.CreateForModel("gpt-4");

    protected override IngestionChunker CreateDocumentChunker(int maxTokensPerChunk = 2_000, int overlapTokens = 500) =>
        new SectionChunker(new(_tokenizer) { MaxTokensPerChunk = maxTokensPerChunk });

    [Fact]
    public async Task ProcessAsync_OneSectionProducesOneContextualChunk()
    {
        IngestionDocument document = TestDocuments.Create(
            "one-section",
            TestDocuments.Section(
                "section",
                TestDocuments.Text("heading", "Section 1", DocumentTextRole.Heading, 1, 1),
                TestDocuments.Text("body", "This is a paragraph.", pageNumber: 2)));

        IngestionChunk chunk = Assert.Single(await CreateDocumentChunker().ProcessAsync(document).ToListAsync());

        ChunkerTestAssertions.Equal(chunk, document, "Section 1\nThis is a paragraph.", "Section 1", ["heading", "body"], [1, 2], _tokenizer);
        Assert.Equal("section", document.Document.Children[0].Id.Value);
    }

    [Fact]
    public async Task ProcessAsync_TwoSectionsPreserveOrderAndIsolation()
    {
        IngestionDocument document = TestDocuments.Create(
            "two-sections",
            TestDocuments.Section(
                "section-1",
                TestDocuments.Text("heading-1", "First", DocumentTextRole.Heading, 1, 1),
                TestDocuments.Text("body-1", "alpha", pageNumber: 2)),
            TestDocuments.Section(
                "section-2",
                TestDocuments.Text("heading-2", "Second", DocumentTextRole.Heading, 1, 3),
                TestDocuments.Text("body-2", "beta", pageNumber: 4)));

        IReadOnlyList<IngestionChunk> chunks = await CreateDocumentChunker().ProcessAsync(document).ToListAsync();

        Assert.Equal(2, chunks.Count);
        ChunkerTestAssertions.Equal(chunks[0], document, "First\nalpha", "First", ["heading-1", "body-1"], [1, 2], _tokenizer);
        ChunkerTestAssertions.Equal(chunks[1], document, "Second\nbeta", "Second", ["heading-2", "body-2"], [3, 4], _tokenizer);
        Assert.DoesNotContain("Second", GetText(chunks[0]));
        Assert.DoesNotContain("First", GetText(chunks[1]));
    }

    [Fact]
    public async Task ProcessAsync_EmptySectionDoesNotProduceEmptyChunk()
    {
        IngestionDocument document = TestDocuments.Create(
            "empty-section",
            TestDocuments.Section(
                "section",
                TestDocuments.Text("heading", "Heading only", DocumentTextRole.Heading, 1, 1)));

        IReadOnlyList<IngestionChunk> chunks = await CreateDocumentChunker().ProcessAsync(document).ToListAsync();

        Assert.Empty(chunks);
        Assert.Equal("Heading only", document.Document.Text);
    }

    [Fact]
    public async Task ProcessAsync_NestedSectionsIncludeAncestorHeadingContext()
    {
        IngestionDocument document = TestDocuments.Create(
            "nested-sections",
            TestDocuments.Section(
                "outer",
                TestDocuments.Text("outer-heading", "Outer", DocumentTextRole.Heading, 1, 1),
                TestDocuments.Text("outer-body", "alpha", pageNumber: 2),
                TestDocuments.Section(
                    "inner",
                    TestDocuments.Text("inner-heading", "Inner", DocumentTextRole.Heading, 2, 3),
                    TestDocuments.Text("inner-body", "beta", pageNumber: 4))));

        IReadOnlyList<IngestionChunk> chunks = await CreateDocumentChunker().ProcessAsync(document).ToListAsync();

        Assert.Equal(2, chunks.Count);
        ChunkerTestAssertions.Equal(chunks[0], document, "Outer\nalpha", "Outer", ["outer-heading", "outer-body"], [1, 2], _tokenizer);
        ChunkerTestAssertions.Equal(chunks[1], document, "Outer Inner\nbeta", "Outer Inner", ["outer-heading", "inner-heading", "inner-body"], [1, 3, 4], _tokenizer);
        Assert.Collection(
            chunks,
            first => Assert.Equal("Outer", first.Context),
            second => Assert.Equal("Outer Inner", second.Context));
    }

    [Fact]
    public async Task ProcessAsync_SectionOverLimitSplitsWithinConfiguredSize()
    {
        IngestionDocument document = TestDocuments.Create(
            "split-section",
            TestDocuments.Section(
                "section",
                TestDocuments.Text("body", "The quick brown fox jumps over the lazy dog", pageNumber: 8)));

        IReadOnlyList<IngestionChunk> chunks = await CreateDocumentChunker(maxTokensPerChunk: 4).ProcessAsync(document).ToListAsync();

        Assert.Equal(3, chunks.Count);
        ChunkerTestAssertions.Equal(chunks[0], document, "The quick brown fox", string.Empty, ["body"], [8], _tokenizer);
        ChunkerTestAssertions.Equal(chunks[1], document, " jumps over the lazy", string.Empty, ["body"], [8], _tokenizer);
        ChunkerTestAssertions.Equal(chunks[2], document, " dog", string.Empty, ["body"], [8], _tokenizer);
        Assert.All(chunks, static chunk => Assert.InRange(chunk.TokenCount, 1, 4));
    }

    [Fact]
    public async Task ProcessAsync_EveryChunkHasExactPositiveTokenCount()
    {
        IngestionDocument document = TestDocuments.Create(
            "counts",
            TestDocuments.Section(
                "section",
                TestDocuments.Text("heading", "Report", DocumentTextRole.Heading, 1, 1),
                TestDocuments.Text("body", "alpha beta", pageNumber: 2)));

        IngestionChunk chunk = Assert.Single(await CreateDocumentChunker(maxTokensPerChunk: 100).ProcessAsync(document).ToListAsync());

        ChunkerTestAssertions.Equal(chunk, document, "Report\nalpha beta", "Report", ["heading", "body"], [1, 2], _tokenizer);
        Assert.InRange(chunk.TokenCount, 1, 100);
    }

    [Fact]
    public async Task ProcessAsync_CancellationBetweenSectionsStopsEnumeration()
    {
        IngestionDocument document = TestDocuments.Create(
            "cancel-between",
            TestDocuments.Section(
                "section-1",
                TestDocuments.Text("heading-1", "First", DocumentTextRole.Heading, 1, 1),
                TestDocuments.Text("body-1", "alpha", pageNumber: 2)),
            TestDocuments.Section(
                "section-2",
                TestDocuments.Text("heading-2", "Second", DocumentTextRole.Heading, 1, 3),
                TestDocuments.Text("body-2", "beta", pageNumber: 4)));
        using CancellationTokenSource cancellationSource = new();
        await using IAsyncEnumerator<IngestionChunk> enumerator =
            CreateDocumentChunker().ProcessAsync(document, cancellationSource.Token).GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        ChunkerTestAssertions.Equal(enumerator.Current, document, "First\nalpha", "First", ["heading-1", "body-1"], [1, 2], _tokenizer);
        cancellationSource.Cancel();

        OperationCanceledException exception =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await enumerator.MoveNextAsync().AsTask());
        Assert.True(cancellationSource.IsCancellationRequested);
    }
}
