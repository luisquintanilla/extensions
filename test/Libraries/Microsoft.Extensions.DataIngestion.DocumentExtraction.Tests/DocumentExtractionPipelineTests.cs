// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.VectorData.InMemory;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion.Chunkers;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.VectorData;
using Microsoft.ML.Tokenizers;
using Xunit;

namespace Microsoft.Extensions.DataIngestion;

public class DocumentExtractionPipelineTests
{
    [Fact]
    public async Task BuiltInChunkerEmitsTextAndBinaryWithTypedPagesAndTokenCounts()
    {
        IngestionDocument document = await ReadFixtureAsync();
        IReadOnlyList<IngestionChunk> chunks = await CreateChunker()
            .ProcessAsync(document)
            .ToListAsync();

        Assert.Contains(chunks, IsPageOneTextChunk);
        Assert.Contains(chunks, chunk =>
            chunk.Content is TextContent text
            && text.Text.Contains("Retention policy is unchanged.", StringComparison.Ordinal)
            && chunk.PageNumbers.SequenceEqual([2])
            && chunk.TokenCount > 0);

        IngestionChunk binary = Assert.Single(chunks, chunk => chunk.Content is DataContent);
        Assert.Equal([1], binary.PageNumbers);
        Assert.Equal(0, binary.TokenCount);
        DataContent data = Assert.IsType<DataContent>(binary.Content);
        Assert.Equal(4, data.Data.Length);
        Assert.Equal("image/png", data.MediaType);
        Assert.All(chunks, chunk => Assert.False(chunk.HasMetadata));
    }

    [Fact]
    public async Task Preview2WriterPersistsPolymorphicContentPagesAndUsesConfiguredEmbedding()
    {
        IngestionDocument document = await ReadFixtureAsync();
        IReadOnlyList<IngestionChunk> chunks = await CreateChunker()
            .ProcessAsync(document)
            .ToListAsync();

        using RecordingEmbeddingGenerator embeddingGenerator = new();
        using InMemoryVectorStore vectorStore = new(new()
        {
            EmbeddingGenerator = embeddingGenerator,
        });
        VectorStoreCollection<Guid, IngestionChunkVectorRecord> collection =
            vectorStore.GetIngestionRecordCollection(
                "bridge",
                RecordingEmbeddingGenerator.DimensionCount);
        using VectorStoreWriter<IngestionChunkVectorRecord> writer = new(collection);

        await writer.WriteAsync(chunks.ToAsyncEnumerable());

        List<IngestionChunkVectorRecord> records = await collection
            .GetAsync(record => record.DocumentId == document.Identifier, top: 100)
            .ToListAsync();
        Assert.Equal(chunks.Count, records.Count);
        Assert.Contains(records, record => record.Content is TextContent && record.PageNumbers.SequenceEqual([1]));
        Assert.Contains(records, record => record.Content is DataContent && record.PageNumbers.SequenceEqual([1]));
        Assert.Contains(records, record => record.PageNumbers.SequenceEqual([2]));
        Assert.Contains(typeof(TextContent), embeddingGenerator.InputTypes);
        Assert.Contains(typeof(DataContent), embeddingGenerator.InputTypes);

        foreach (IngestionChunkVectorRecord record in records)
        {
            Assert.NotEmpty(record.SerializedContent!);
            IngestionChunkVectorRecord roundTripped = new()
            {
                SerializedContent = record.SerializedContent,
                SerializedPageNumbers = record.SerializedPageNumbers,
            };
            Assert.Equal(record.Content!.GetType(), roundTripped.Content!.GetType());
            Assert.Equal(record.PageNumbers, roundTripped.PageNumbers);
        }
    }

    [Fact]
    public void ChunkAndStructuredCollectionsAreDefensive()
    {
        int[] sourcePages = [2, 1, 2];
        IngestionChunk chunk = new(
            new TextContent("content"),
            new IngestionDocument("document"),
            tokenCount: 1,
            context: null,
            pageNumbers: sourcePages);
        sourcePages[0] = -1;
        Assert.Equal([1, 2], chunk.PageNumbers);
        Assert.Throws<NotSupportedException>(
            () => ((IList<int>)chunk.PageNumbers)[0] = -1);

        int[] recordPages = [3, 1, 3];
        IngestionChunkVectorRecord record = new() { PageNumbers = recordPages };
        recordPages[0] = -1;
        Assert.Equal([1, 3], record.PageNumbers);
        Assert.Equal("1,3", record.SerializedPageNumbers);
        Assert.Throws<NotSupportedException>(
            () => ((IList<int>)record.PageNumbers)[0] = -1);

        IngestionDocumentParagraph original = IngestionDocumentParagraph.FromText("Original");
        List<IngestionDocumentElement> nested = [original];
        IngestionDocumentTableCell originalCell = new(0, 0, nested);
        List<IngestionDocumentTableCell> cells = [originalCell];
        IngestionDocumentTable table = new(1, 1, cells);

        nested[0] = IngestionDocumentParagraph.FromText("Replacement");
        cells[0] = new(0, 0, [IngestionDocumentParagraph.FromText("Replacement")]);
        Assert.Same(original, Assert.Single(table.StructuredCells![0].Elements));
        IngestionDocumentElement?[,] returnedGrid = table.Cells;
        returnedGrid[0, 0] = nested[0];
        Assert.Same(original, table.Cells[0, 0]);
    }

    private static async Task<IngestionDocument> ReadFixtureAsync()
    {
        TestDocumentExtractionClient client = new()
        {
            ExtractAsyncCallback = (_, _, _, _) =>
                Task.FromResult(DocumentExtractionBridgeFixture.Create()),
        };
        DocumentExtractionReader reader = new(client);
        return await reader.ReadAsync(
            new MemoryStream([1, 2, 3]),
            "technical-review.pdf",
            "application/pdf");
    }

    private static SectionChunker CreateChunker()
        => new(new(TiktokenTokenizer.CreateForModel("gpt-4o"))
        {
            MaxTokensPerChunk = 256,
            OverlapTokens = 0,
        });

    private static bool IsPageOneTextChunk(IngestionChunk chunk)
    {
        if (chunk.Content is not TextContent text
            || !chunk.PageNumbers.SequenceEqual([1])
            || chunk.TokenCount <= 0)
        {
            return false;
        }

        return text.Text.Contains("Quarterly *Report*", StringComparison.Ordinal)
            && text.Text.Contains("Nested value", StringComparison.Ordinal)
            && text.Text.Contains("Nested chart", StringComparison.Ordinal);
    }

    private sealed class RecordingEmbeddingGenerator
        : IEmbeddingGenerator<AIContent, Embedding<float>>
    {
        internal const int DimensionCount = 4;

        internal List<Type> InputTypes { get; } = [];

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<AIContent> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            List<Embedding<float>> embeddings = [];
            foreach (AIContent value in values)
            {
                InputTypes.Add(value.GetType());
                embeddings.Add(new(new float[] { 0, 1, 2, 3 }));
            }

            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
