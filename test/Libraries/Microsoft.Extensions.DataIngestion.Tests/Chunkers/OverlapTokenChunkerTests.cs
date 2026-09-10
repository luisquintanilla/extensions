// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Chunkers.Tests;

public class OverlapTokenChunkerTests : DocumentTokenChunkerTests
{
    [Fact]
    public async Task ProcessAsync_Overlap_RepeatsConfiguredBoundaryTokens()
    {
        IngestionDocument document = TestDocuments.Create(
            "overlap",
            TestDocuments.Text("paragraph", "The quick brown fox jumps over the lazy dog", pageNumber: 6));

        IReadOnlyList<IngestionChunk> chunks = await CreateDocumentChunker(maxTokensPerChunk: 4, overlapTokens: 1)
            .ProcessAsync(document)
            .ToListAsync();

        Assert.Equal(3, chunks.Count);
        ChunkerTestAssertions.Equal(chunks[0], document, "The quick brown fox", string.Empty, ["paragraph"], [6], Tokenizer);
        ChunkerTestAssertions.Equal(chunks[1], document, " fox jumps over the", string.Empty, ["paragraph"], [6], Tokenizer);
        ChunkerTestAssertions.Equal(chunks[2], document, " the lazy dog", string.Empty, ["paragraph"], [6], Tokenizer);
        Assert.EndsWith(" fox", GetText(chunks[0]));
        Assert.StartsWith(" fox", GetText(chunks[1]));
        Assert.EndsWith(" the", GetText(chunks[1]));
        Assert.StartsWith(" the", GetText(chunks[2]));
    }

    [Fact]
    public async Task ProcessAsync_Overlap_DoesNotEmitOverlapOnlyTerminalChunk()
    {
        IngestionDocument document = TestDocuments.Create(
            "terminal-overlap",
            TestDocuments.Text("paragraph", "hello world", pageNumber: 9));

        IngestionChunk chunk = Assert.Single(
            await CreateDocumentChunker(maxTokensPerChunk: 2, overlapTokens: 1).ProcessAsync(document).ToListAsync());

        ChunkerTestAssertions.Equal(chunk, document, "hello world", string.Empty, ["paragraph"], [9], Tokenizer);
        Assert.DoesNotContain("worldworld", GetText(chunk));
    }
}
