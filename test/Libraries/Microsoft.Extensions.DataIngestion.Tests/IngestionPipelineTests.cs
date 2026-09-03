// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Documents;
using Microsoft.ML.Tokenizers;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Tests;

public class IngestionPipelineTests
{
    [Fact]
    public async Task ReadsChunksAndWritesSharedDocument()
    {
        FileInfo source = new(Path.GetTempFileName());
        try
        {
            TestReader reader = new((stream, identifier, mediaType, cancellationToken) =>
                Task.FromResult(TestDocuments.Create(identifier, TestDocuments.Text("source", "content", pageNumber: 1))));
            CapturingWriter writer = new();
            using IngestionPipeline<string> pipeline = new(
                reader,
                new Chunkers.SectionChunker(new(TiktokenTokenizer.CreateForModel("gpt-4"))
                {
                    MaxTokensPerChunk = 100,
                }),
                writer);

            List<IngestionResult> results = [];
            await foreach (IngestionResult ingestionResult in pipeline.ProcessAsync([source]))
            {
                results.Add(ingestionResult);
            }

            IngestionResult result = Assert.Single(results);

            Assert.True(result.Succeeded);
            IngestionChunk<string> chunk = Assert.Single(writer.Chunks);
            Assert.Equal("content", chunk.Content);
            Assert.Equal([new DocumentNodeId("source")], chunk.SourceNodeIds);
            Assert.Equal([1], chunk.PageNumbers);
        }
        finally
        {
            source.Delete();
        }
    }

    private sealed class CapturingWriter : IngestionChunkWriter<string>
    {
        public List<IngestionChunk<string>> Chunks { get; } = [];

        public override async Task WriteAsync(IAsyncEnumerable<IngestionChunk<string>> chunks, CancellationToken cancellationToken = default)
        {
            await foreach (IngestionChunk<string> chunk in chunks.WithCancellation(cancellationToken))
            {
                Chunks.Add(chunk);
            }
        }
    }
}
