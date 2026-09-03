// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.VectorData.InMemory;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion.Chunkers;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.ML.Tokenizers;
using Xunit;

namespace Microsoft.Extensions.DataIngestion;

public class DocumentExtractionPipelineTests
{
    [Fact]
    public async Task MappedCorpusUsesBuiltInTextChunkingAndWriting()
    {
        IngestionDocument document = await ReadFixtureAsync();
        SectionChunker chunker = CreateSectionChunker();

        IReadOnlyList<IngestionChunk<string>> chunks = await chunker
            .ProcessAsync(document)
            .ToListAsync();

        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk => Assert.Single(chunk.PageNumbers));
        Assert.Contains(chunks, chunk => chunk.PageNumbers.SequenceEqual([1]));
        Assert.Contains(chunks, chunk => chunk.PageNumbers.SequenceEqual([2]));
        Assert.Contains(chunks, chunk =>
            ContainsOrdinal(chunk.Content, "Quarterly *Report*")
            && !ContainsOrdinal(chunk.Content, @"Quarterly \*Report\*"));
        Assert.Contains(chunks, chunk =>
            ContainsOrdinal(chunk.Content, "rowspan=\"2\"")
            && ContainsOrdinal(chunk.Content, "data-kind=\"rowHeader\""));
        Assert.DoesNotContain(chunks, chunk =>
            ContainsOrdinal(chunk.Content, DocumentExtractionBridgeFixture.PageOneMarkdown));
        Assert.All(chunks, chunk => Assert.False(chunk.HasMetadata));

        using TestEmbeddingGenerator<string> embeddingGenerator = new();
        using InMemoryVectorStore vectorStore = new(new()
        {
            EmbeddingGenerator = embeddingGenerator,
        });
        using VectorStoreWriter<string> writer = new(
            vectorStore,
            TestEmbeddingGenerator<string>.DimensionCount);

        await writer.WriteAsync(chunks.ToAsyncEnumerable());

        List<Dictionary<string, object?>> records = await writer.VectorStoreCollection
            .GetAsync(
                record => (string)record["documentid"]! == document.Identifier,
                top: 100)
            .ToListAsync();
        Assert.Equal(chunks.Count, records.Count);
        Assert.All(records, record =>
            Assert.Matches("^[12]$", Assert.IsType<string>(record["pagenumbers"])));
        Assert.True(embeddingGenerator.WasCalled);
    }

    [Fact]
    public async Task DocumentTokenChunkerCollectsPagesAcrossTheMappedDocument()
    {
        IngestionDocument document = await ReadFixtureAsync();
        DocumentTokenChunker chunker = new(new(TiktokenTokenizer.CreateForModel("gpt-4o"))
        {
            MaxTokensPerChunk = 2_000,
            OverlapTokens = 0,
        });

        IngestionChunk<string> chunk = Assert.Single(
            await chunker.ProcessAsync(document).ToListAsync());

        Assert.Equal([1, 2], chunk.PageNumbers);
        Assert.Contains("Quarterly *Report*", chunk.Content, System.StringComparison.Ordinal);
        Assert.Contains("Appendix", chunk.Content, System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenericBinaryPipelinePreservesCaptionlessImageAndPage()
    {
        IngestionDocument document = await ReadFixtureAsync();
        ImageChunker chunker = new();
        IngestionChunk<DataContent> chunk = Assert.Single(
            await chunker.ProcessAsync(document).ToListAsync());

        Assert.Equal(8, chunk.Content.Data.Length);
        Assert.Equal("image/png", chunk.Content.MediaType);
        Assert.Equal([1], chunk.PageNumbers);

        using TestEmbeddingGenerator<DataContent> embeddingGenerator = new();
        using InMemoryVectorStore vectorStore = new(new()
        {
            EmbeddingGenerator = embeddingGenerator,
        });
        using VectorStoreWriter<DataContent> writer = new(
            vectorStore,
            TestEmbeddingGenerator<DataContent>.DimensionCount);

        await writer.WriteAsync(new[] { chunk }.ToAsyncEnumerable());

        Dictionary<string, object?> record = await writer.VectorStoreCollection
            .GetAsync(
                candidate => (string)candidate["documentid"]! == document.Identifier,
                top: 1)
            .SingleAsync();
        Assert.Equal("1", record["pagenumbers"]);
        Assert.Equal(8, Assert.IsType<DataContent>(record["content"]).Data.Length);
        Assert.True(embeddingGenerator.WasCalled);
    }

    [Fact]
    public async Task ElementAwareChunkingKeepsEveryCodeSegmentFenced()
    {
        IngestionDocument document = await ReadFixtureAsync();
        SectionChunker chunker = CreateSectionChunker(maxTokens: 35);

        IReadOnlyList<IngestionChunk<string>> chunks = await chunker
            .ProcessAsync(document)
            .ToListAsync();
        IReadOnlyList<IngestionChunk<string>> codeChunks = chunks
            .Where(chunk => ContainsOrdinal(chunk.Content, "Console.WriteLine"))
            .ToList();

        Assert.True(codeChunks.Count > 1);
        Assert.All(codeChunks, chunk =>
        {
            string content = chunk.Content[chunk.Context!.Length..].TrimStart('\r', '\n');
            string fence = content[..content.IndexOf('\n')].TrimEnd('\r');
            Assert.StartsWith("````", fence, System.StringComparison.Ordinal);
            Assert.EndsWith(fence, content, System.StringComparison.Ordinal);
            Assert.Equal([1], chunk.PageNumbers);
        });
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

    private static SectionChunker CreateSectionChunker(int maxTokens = 2_000)
        => new(new(TiktokenTokenizer.CreateForModel("gpt-4o"))
        {
            MaxTokensPerChunk = maxTokens,
            OverlapTokens = 0,
        });

    private static bool ContainsOrdinal(string value, string expected)
#pragma warning disable CA2249 // String.Contains with StringComparison is unavailable on .NET Framework.
        => value.IndexOf(expected, System.StringComparison.Ordinal) >= 0;
#pragma warning restore CA2249

    private sealed class ImageChunker : IngestionChunker<DataContent>
    {
        public override IAsyncEnumerable<IngestionChunk<DataContent>> ProcessAsync(
            IngestionDocument document,
            CancellationToken cancellationToken = default)
            => document.EnumerateContent()
                .OfType<IngestionDocumentImage>()
                .Where(static image => image.Content.HasValue && image.MediaType is not null)
                .Select(image => new IngestionChunk<DataContent>(
                    new(image.Content!.Value, image.MediaType!),
                    document,
                    context: null,
                    pageNumbers: image.PageNumber is int pageNumber ? [pageNumber] : []))
                .ToAsyncEnumerable();
    }
}
