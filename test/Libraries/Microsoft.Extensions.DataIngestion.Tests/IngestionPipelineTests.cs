// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion.Chunkers;
using Microsoft.ML.Tokenizers;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Tests;

public class IngestionPipelineTests
{
    [Fact]
    public async Task PreservesNonGenericChunkContractThroughWriting()
    {
        FileInfo source = new(Path.GetTempFileName());
        try
        {
            TestReader reader = new((stream, identifier, mediaType, cancellationToken) =>
                Task.FromResult(TestDocuments.Create(identifier, TestDocuments.Text("node", "content", pageNumber: 1))));
            CapturingWriter writer = new();
            using IngestionPipeline pipeline = new(
                reader,
                new SectionChunker(new(TiktokenTokenizer.CreateForModel("gpt-4")) { MaxTokensPerChunk = 100 }),
                writer);

            await foreach (IngestionResult result in pipeline.ProcessAsync([source]))
            {
                Assert.True(result.Succeeded);
            }

            IngestionChunk chunk = Assert.Single(writer.Chunks);
            Assert.IsType<TextContent>(chunk.Content);
            Assert.True(chunk.TokenCount > 0);
            Assert.Equal([1], chunk.PageNumbers);
        }
        finally
        {
            source.Delete();
        }
    }

    private sealed class CapturingWriter : IngestionChunkWriter
    {
        public List<IngestionChunk> Chunks { get; } = [];

        public override async Task WriteAsync(IAsyncEnumerable<IngestionChunk> chunks, CancellationToken cancellationToken = default)
        {
            await foreach (IngestionChunk chunk in chunks.WithCancellation(cancellationToken))
            {
                Chunks.Add(chunk);
            }
        }
    }
}
