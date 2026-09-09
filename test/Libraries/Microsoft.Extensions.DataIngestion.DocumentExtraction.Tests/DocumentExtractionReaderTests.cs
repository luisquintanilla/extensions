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
using Microsoft.Extensions.Documents;
using Microsoft.Extensions.VectorData;
using Microsoft.ML.Tokenizers;
using Xunit;

namespace Microsoft.Extensions.DataIngestion;

public class DocumentExtractionReaderTests
{
    [Fact]
    public async Task ReaderPassesExactSharedDocumentWithoutEvidenceLeakage()
    {
        DocumentText text = new(new("text"), "literal", pageReferences: [new(1)]);
        object raw = new();
        DocumentExtractionResult extraction = new(
        [
            new DocumentPage(
                1,
                new Document([text]),
                "# provider markdown",
                [new(text.Id) { Confidence = 0.9, RawRepresentation = raw }]),
        ]);
        using StubClient client = new(extraction);

        IngestionDocument result = await new DocumentExtractionReader(client)
            .ReadAsync(new MemoryStream([1]), "fixture", "application/pdf");

        Assert.Same(extraction.Document, result.Document);
        Assert.Equal("literal", result.Document.Text);
        Assert.Equal("# provider markdown", extraction.Pages[0].Markdown);
        Assert.Same(raw, extraction.Pages[0].Evidence[0].RawRepresentation);
        Assert.Null(typeof(DocumentNode).GetProperty("Confidence"));
    }

    [Fact]
    public async Task SharedTreeFlowsThroughNonGenericChunksAndTypedWriter()
    {
        DocumentExtractionResult extraction = new(
        [
            new DocumentPage(2, new Document([new DocumentText(new("two"), "second page", pageReferences: [new(2)])])),
            new DocumentPage(1, new Document([new DocumentText(new("one"), "first page", pageReferences: [new(1)])])),
        ]);
        using StubClient client = new(extraction);
        IngestionDocument ingestion = await new DocumentExtractionReader(client)
            .ReadAsync(new MemoryStream([1]), "fixture", "application/pdf");
        SectionChunker chunker = new(new(TiktokenTokenizer.CreateForModel("gpt-4")) { MaxTokensPerChunk = 100 });
        using TestEmbeddingGenerator<AIContent> embeddingGenerator = new();
        using InMemoryVectorStore store = new(new() { EmbeddingGenerator = embeddingGenerator });
        VectorStoreCollection<Guid, IngestionChunkVectorRecord> collection =
            store.GetIngestionRecordCollection<IngestionChunkVectorRecord>(
                "chunks", TestEmbeddingGenerator<AIContent>.DimensionCount);
        using VectorStoreWriter<IngestionChunkVectorRecord> writer = new(collection);

        await writer.WriteAsync(chunker.ProcessAsync(ingestion));

        List<IngestionChunkVectorRecord> records = await collection
            .GetAsync(filter: record => record.DocumentId == "fixture", top: 10)
            .ToListAsync();

        Assert.Equal(2, records.Count);
        Assert.Contains(records, record => Assert.IsType<TextContent>(record.Content).Text == "first page" && record.SerializedPageNumbers == "1");
        Assert.Contains(records, record => Assert.IsType<TextContent>(record.Content).Text == "second page" && record.SerializedPageNumbers == "2");
        Assert.True(embeddingGenerator.WasCalled);
    }

    private sealed class StubClient : IDocumentExtractionClient
    {
        private readonly DocumentExtractionResult _result;

        public StubClient(DocumentExtractionResult result) => _result = result;

        public Task<DocumentExtractionResult> ExtractAsync(
            Stream document,
            string mediaType,
            DocumentExtractionOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);

        public IAsyncEnumerable<DocumentExtractionPageResult> ExtractPagesAsync(
            Stream document,
            string mediaType,
            DocumentExtractionOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
