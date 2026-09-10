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

public class SemanticSimilarityChunkerTests : DocumentChunkerTests
{
    private static readonly Tokenizer _tokenizer = TiktokenTokenizer.CreateForModel("gpt-4");

#pragma warning disable CA2000 // The base-class factory does not expose a disposal path for its fake dependency.
    protected override IngestionChunker CreateDocumentChunker(int maxTokensPerChunk = 2_000, int overlapTokens = 500) =>
        new SemanticSimilarityChunker(
            new RecordingEmbeddingGenerator([[1f, 0f]]),
            new(_tokenizer) { MaxTokensPerChunk = maxTokensPerChunk },
            thresholdPercentile: 50);
#pragma warning restore CA2000

    [Theory]
    [InlineData(0f)]
    [InlineData(100f)]
    public async Task Constructor_AcceptsInclusiveThresholdPercentileBounds(float thresholdPercentile)
    {
        using RecordingEmbeddingGenerator generator = new([[1f, 0f]]);
        IngestionDocument document = TestDocuments.Create(
            "threshold-boundary",
            TestDocuments.Text("paragraph", "Boundary percentile content.", pageNumber: 1));
        SemanticSimilarityChunker chunker = new(
            generator,
            new(_tokenizer) { MaxTokensPerChunk = 100 },
            thresholdPercentile);

        IngestionChunk chunk = Assert.Single(await chunker.ProcessAsync(document).ToListAsync());

        ChunkerTestAssertions.Equal(
            chunk,
            document,
            "Boundary percentile content.",
            string.Empty,
            ["paragraph"],
            [1],
            _tokenizer);
        Assert.Equal(["Boundary percentile content."], generator.Inputs);
        Assert.Equal(1, generator.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_SingleParagraphProducesSingleChunk()
    {
        using RecordingEmbeddingGenerator generator = new([[1f, 0f]]);
        IngestionDocument document = TestDocuments.Create(
            "single",
            TestDocuments.Text("paragraph", "A single semantic paragraph.", pageNumber: 2));

        IngestionChunk chunk = Assert.Single(await CreateChunker(generator).ProcessAsync(document).ToListAsync());

        ChunkerTestAssertions.Equal(chunk, document, "A single semantic paragraph.", string.Empty, ["paragraph"], [2], _tokenizer);
        Assert.Equal(["A single semantic paragraph."], generator.Inputs);
    }

    [Fact]
    public async Task ProcessAsync_TopicChangeSplitsAtExpectedPercentileBoundary()
    {
        using RecordingEmbeddingGenerator generator = new(
            [
                [1f, 0f],
                [1f, 0f],
                [-1f, 0f],
                [-1f, 0f],
            ]);
        IngestionDocument document = TestDocuments.Create(
            "topics",
            TestDocuments.Text("dotnet-1", ".NET builds applications.", pageNumber: 1),
            TestDocuments.Text("dotnet-2", "C# runs on .NET.", pageNumber: 2),
            TestDocuments.Text("greece-1", "Zeus rules Olympus.", pageNumber: 3),
            TestDocuments.Text("greece-2", "Athena values wisdom.", pageNumber: 4));

        IReadOnlyList<IngestionChunk> chunks = await CreateChunker(generator).ProcessAsync(document).ToListAsync();

        Assert.Equal(2, chunks.Count);
        ChunkerTestAssertions.Equal(chunks[0], document, ".NET builds applications.\nC# runs on .NET.", string.Empty, ["dotnet-1", "dotnet-2"], [1, 2], _tokenizer);
        ChunkerTestAssertions.Equal(chunks[1], document, "Zeus rules Olympus.\nAthena values wisdom.", string.Empty, ["greece-1", "greece-2"], [3, 4], _tokenizer);
        Assert.Equal([".NET builds applications.", "C# runs on .NET.", "Zeus rules Olympus.", "Athena values wisdom."], generator.Inputs);
    }

    [Fact]
    public async Task ProcessAsync_MixedTextAndTableGroupsBySemanticTopic()
    {
        using RecordingEmbeddingGenerator generator = new(
            [
                [1f, 0f],
                [1f, 0f],
                [1f, 0f],
                [-1f, 0f],
            ]);
        DocumentTable table = new(
            new("table"),
            1,
            2,
            [
                Cell("language-cell", 0, 0, "language", "C#", 2),
                Cell("status-cell", 0, 1, "status", "Primary", 2),
            ]);
        IngestionDocument document = TestDocuments.Create(
            "mixed",
            TestDocuments.Text("intro", ".NET languages:", pageNumber: 1),
            table,
            TestDocuments.Text("summary", "C# is the primary language.", pageNumber: 3),
            TestDocuments.Text("other", "Zeus rules Olympus.", pageNumber: 4));

        IReadOnlyList<IngestionChunk> chunks = await CreateChunker(generator).ProcessAsync(document).ToListAsync();

        Assert.Equal(2, chunks.Count);
        ChunkerTestAssertions.Equal(
            chunks[0],
            document,
            ".NET languages:\nC#\tPrimary\nC# is the primary language.",
            string.Empty,
            ["intro", "table", "language-cell", "language", "status-cell", "status", "summary"],
            [1, 2, 3],
            _tokenizer);
        ChunkerTestAssertions.Equal(chunks[1], document, "Zeus rules Olympus.", string.Empty, ["other"], [4], _tokenizer);
        Assert.Equal([".NET languages:", "C#\tPrimary", "C# is the primary language.", "Zeus rules Olympus."], generator.Inputs);
    }

    [Fact]
    public async Task ProcessAsync_EmbeddingInputsPreserveProjectionOrder()
    {
        using RecordingEmbeddingGenerator generator = new(
            [
                [1f, 0f],
                [1f, 0f],
                [1f, 0f],
            ]);
        IngestionDocument document = TestDocuments.Create(
            "ordered",
            TestDocuments.Text("first", "first", pageNumber: 3),
            TestDocuments.Text("second", "second", pageNumber: 1),
            TestDocuments.Text("third", "third", pageNumber: 2));

        IngestionChunk chunk = Assert.Single(await CreateChunker(generator).ProcessAsync(document).ToListAsync());

        ChunkerTestAssertions.Equal(chunk, document, "first\nsecond\nthird", string.Empty, ["first", "second", "third"], [1, 2, 3], _tokenizer);
        Assert.Equal(["first", "second", "third"], generator.Inputs);
        Assert.Equal(1, generator.CallCount);
        Assert.Null(generator.Options);
    }

    [Fact]
    public async Task ProcessAsync_EmbeddingCountMismatchThrowsDeterministically()
    {
        using RecordingEmbeddingGenerator generator = new([[1f, 0f]]);
        IngestionDocument document = TestDocuments.Create(
            "mismatch",
            TestDocuments.Text("first", "first"),
            TestDocuments.Text("second", "second"));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await CreateChunker(generator).ProcessAsync(document).ToListAsync());

        Assert.Equal("The number of embeddings returned does not match the number of document elements.", exception.Message);
        Assert.Equal(["first", "second"], generator.Inputs);
    }

    [Fact]
    public async Task ProcessAsync_ForwardsCancellationToEmbeddingGenerator()
    {
        using CancellationTokenSource cancellationSource = new();
        cancellationSource.Cancel();
        using RecordingEmbeddingGenerator generator = new([[1f, 0f]]) { ThrowOnCancellation = true };
        IngestionDocument document = TestDocuments.Create(
            "canceled",
            TestDocuments.Text("paragraph", "content", pageNumber: 1));

        OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await CreateChunker(generator).ProcessAsync(document, cancellationSource.Token).ToListAsync());

        Assert.Equal(cancellationSource.Token, exception.CancellationToken);
        Assert.Equal(cancellationSource.Token, generator.CancellationToken);
        Assert.Equal(["content"], generator.Inputs);
        Assert.Equal(1, generator.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_EveryChunkHasNonGenericAIContentAndExactPositiveTokenCount()
    {
        using RecordingEmbeddingGenerator generator = new(
            [
                [1f, 0f],
                [1f, 0f],
            ]);
        IngestionDocument document = TestDocuments.Create(
            "counts",
            TestDocuments.Text("first", "alpha", pageNumber: 1),
            TestDocuments.Text("second", "beta", pageNumber: 2));

        IngestionChunk chunk = Assert.Single(await CreateChunker(generator).ProcessAsync(document).ToListAsync());

        ChunkerTestAssertions.Equal(chunk, document, "alpha\nbeta", string.Empty, ["first", "second"], [1, 2], _tokenizer);
        Assert.IsAssignableFrom<AIContent>(chunk.Content);
        Assert.Equal(1, generator.CallCount);
    }

    private static SemanticSimilarityChunker CreateChunker(RecordingEmbeddingGenerator generator) =>
        new(generator, new(_tokenizer) { MaxTokensPerChunk = 100 }, thresholdPercentile: 50);

    private static DocumentTableCell Cell(
        string cellId,
        int row,
        int column,
        string textId,
        string text,
        int pageNumber) =>
        new(
            new(cellId),
            row,
            column,
            [TestDocuments.Text(textId, text, pageNumber: pageNumber)],
            pageReferences: [new(pageNumber)]);

    private sealed class RecordingEmbeddingGenerator : IEmbeddingGenerator<TextContent, Embedding<float>>
    {
        private readonly IReadOnlyList<float[]> _vectors;

        internal RecordingEmbeddingGenerator(IReadOnlyList<float[]> vectors)
        {
            _vectors = vectors;
        }

        internal List<string> Inputs { get; } = [];

        internal int CallCount { get; private set; }

        internal EmbeddingGenerationOptions? Options { get; private set; }

        internal CancellationToken CancellationToken { get; private set; }

        internal bool ThrowOnCancellation { get; set; }

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<TextContent> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Inputs.AddRange(values.Select(static value => value.Text));
            Options = options;
            CancellationToken = cancellationToken;
            if (ThrowOnCancellation)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return Task.FromResult(
                new GeneratedEmbeddings<Embedding<float>>(
                    _vectors.Select(static vector => new Embedding<float>(vector))));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
