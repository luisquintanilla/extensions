// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Microsoft.ML.Tokenizers;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Chunkers.Tests;

public class DocumentTokenChunkerTests : DocumentChunkerTests
{
    protected static readonly Tokenizer Tokenizer = TiktokenTokenizer.CreateForModel("gpt-4");

#pragma warning disable S1006 // No-overlap is the defining default for this concrete test suite.
    protected override IngestionChunker CreateDocumentChunker(int maxTokensPerChunk = 2_000, int overlapTokens = 0) =>
        new DocumentTokenChunker(new(Tokenizer)
        {
            MaxTokensPerChunk = maxTokensPerChunk,
            OverlapTokens = overlapTokens,
        });
#pragma warning restore S1006

    [Fact]
    public async Task ProcessAsync_NoOverlap_SingleChunkPreservesExactBoundary()
    {
        IngestionDocument document = TestDocuments.Create(
            "single",
            TestDocuments.Text("paragraph", "This short document remains one chunk.", pageNumber: 3));

        IngestionChunk chunk = Assert.Single(await CreateDocumentChunker(maxTokensPerChunk: 100).ProcessAsync(document).ToListAsync());

        ChunkerTestAssertions.Equal(
            chunk,
            document,
            "This short document remains one chunk.",
            string.Empty,
            ["paragraph"],
            [3],
            Tokenizer);
    }

    [Fact]
    public async Task ProcessAsync_EmitsNonGenericAIContentWithExactPositiveTokenCount()
    {
        IngestionDocument document = TestDocuments.Create(
            "projection",
            TestDocuments.Text("first", "hello", pageNumber: 2),
            TestDocuments.Text("second", "world", pageNumber: 4));

        IngestionChunk chunk = Assert.Single(await CreateDocumentChunker(maxTokensPerChunk: 100).ProcessAsync(document).ToListAsync());

        ChunkerTestAssertions.Equal(
            chunk,
            document,
            "hello\n\nworld",
            string.Empty,
            ["first", "second"],
            [2, 4],
            Tokenizer);
        Assert.IsAssignableFrom<AIContent>(chunk.Content);
    }

    [Fact]
    public async Task ProcessAsync_PreservesDocumentNodeAndPageProvenanceAcrossSplits()
    {
        IngestionDocument document = TestDocuments.Create(
            "provenance",
            TestDocuments.Text("alpha", "alpha alpha alpha", pageNumber: 2),
            TestDocuments.Text("omega", "omega omega", pageNumber: 5));

        IReadOnlyList<IngestionChunk> chunks = await CreateDocumentChunker(maxTokensPerChunk: 3).ProcessAsync(document).ToListAsync();

        Assert.Equal(2, chunks.Count);
        ChunkerTestAssertions.Equal(chunks[0], document, "alpha alpha alpha", string.Empty, ["alpha"], [2], Tokenizer);
        ChunkerTestAssertions.Equal(chunks[1], document, "\n\nomega omega", string.Empty, ["omega"], [5], Tokenizer);
        Assert.Equal(["alpha", "omega"], chunks.SelectMany(static chunk => chunk.SourceNodeIds).Select(static id => id.Value));
    }

    [Fact]
    public async Task ProcessAsync_PreCanceledToken_ObservesCancellationAtChunkBoundary()
    {
        IngestionDocument document = TestDocuments.Create(
            "canceled",
            TestDocuments.Text("first", "content", pageNumber: 1));
        using CancellationTokenSource cancellationSource = new();
        cancellationSource.Cancel();

        OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await CreateDocumentChunker().ProcessAsync(document, cancellationSource.Token).ToListAsync());

        Assert.Equal(cancellationSource.Token, exception.CancellationToken);
    }
}
