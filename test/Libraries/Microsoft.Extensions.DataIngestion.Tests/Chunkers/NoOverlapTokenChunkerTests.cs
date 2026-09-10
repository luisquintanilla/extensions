// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Chunkers.Tests;

public class NoOverlapTokenChunkerTests : DocumentTokenChunkerTests
{
    [Fact]
    public async Task ProcessAsync_NoOverlap_TwoChunksHaveNoRepeatedTokens()
    {
        const string Text = "The quick brown fox jumps over";
        IngestionDocument document = TestDocuments.Create(
            "two-chunks",
            TestDocuments.Text("paragraph", Text, pageNumber: 2));

        IReadOnlyList<IngestionChunk> chunks = await CreateDocumentChunker(maxTokensPerChunk: 4).ProcessAsync(document).ToListAsync();

        Assert.Equal(2, chunks.Count);
        ChunkerTestAssertions.Equal(chunks[0], document, "The quick brown fox", string.Empty, ["paragraph"], [2], Tokenizer);
        ChunkerTestAssertions.Equal(chunks[1], document, " jumps over", string.Empty, ["paragraph"], [2], Tokenizer);
        Assert.Equal(Text, string.Concat(chunks.Select(GetText)));
        Assert.Equal("fox", GetText(chunks[0]).Split(' ').Last());
        Assert.Equal("jumps", GetText(chunks[1]).Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries).First());
    }

    [Fact]
    public async Task ProcessAsync_NoOverlap_ManyChunksPreserveAllProjectedText()
    {
        const string Text = "The quick brown fox jumps over the lazy dog today";
        IngestionDocument document = TestDocuments.Create(
            "many-chunks",
            TestDocuments.Text("paragraph", Text, pageNumber: 7));

        IReadOnlyList<IngestionChunk> chunks = await CreateDocumentChunker(maxTokensPerChunk: 3).ProcessAsync(document).ToListAsync();

        Assert.Equal(4, chunks.Count);
        ChunkerTestAssertions.Equal(chunks[0], document, "The quick brown", string.Empty, ["paragraph"], [7], Tokenizer);
        ChunkerTestAssertions.Equal(chunks[1], document, " fox jumps over", string.Empty, ["paragraph"], [7], Tokenizer);
        ChunkerTestAssertions.Equal(chunks[2], document, " the lazy dog", string.Empty, ["paragraph"], [7], Tokenizer);
        ChunkerTestAssertions.Equal(chunks[3], document, " today", string.Empty, ["paragraph"], [7], Tokenizer);
        Assert.Equal(document.Document.Text, string.Concat(chunks.Select(GetText)));
        for (int i = 1; i < chunks.Count; i++)
        {
            Assert.NotEqual(GetText(chunks[i - 1]), GetText(chunks[i]));
        }
    }
}
