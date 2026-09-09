// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ML.Tokenizers;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Chunkers.Tests
{
    public class OverlapTokenChunkerTests : DocumentTokenChunkerTests
    {
        protected override IngestionChunker CreateDocumentChunker(int maxTokensPerChunk = 2_000, int overlapTokens = 500)
        {
            var tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");
            return new DocumentTokenChunker(new(tokenizer) { MaxTokensPerChunk = maxTokensPerChunk, OverlapTokens = overlapTokens });
        }

        [Fact]
        public async Task TokenChunking_WithOverlap()
        {
            string text = "The quick brown fox jumps over the lazy dog";
            var tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");
            int chunkSize = 4;  // Small chunk size to demonstrate overlap
            int chunkOverlap = 1;

            var chunker = new DocumentTokenChunker(new(tokenizer) { MaxTokensPerChunk = chunkSize, OverlapTokens = chunkOverlap });
            IngestionDocument doc = new IngestionDocument("overlapExample");
            doc.Sections.Add(new IngestionDocumentSection
            {
                Elements =
                {
                    new IngestionDocumentParagraph(text)
                }
            });

            IReadOnlyList<IngestionChunk> chunks = await chunker.ProcessAsync(doc).ToListAsync();
            Assert.Equal(3, chunks.Count);
            Assert.Equal("The quick brown fox", GetText(chunks[0]), ignoreLineEndingDifferences: true);
            Assert.Equal(" fox jumps over the", GetText(chunks[1]), ignoreLineEndingDifferences: true);
            Assert.Equal(" the lazy dog", GetText(chunks[2]), ignoreLineEndingDifferences: true);

            Assert.True(tokenizer.CountTokens(GetText(chunks.Last())) <= chunkSize);

            for (int i = 0; i < chunks.Count - 1; i++)
            {
                var currentChunk = chunks[i];
                var nextChunk = chunks[i + 1];

                var currentWords = GetText(currentChunk).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var nextWords = GetText(nextChunk).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                bool hasOverlap = currentWords.Intersect(nextWords).Any();
                Assert.True(hasOverlap, $"Chunks {i} and {i + 1} should have overlapping content");
            }

            Assert.NotEmpty(string.Concat(chunks.Select(c => GetText(c))));
        }

        [Fact]
        public async Task OverlapRetainsPagesFromBothSourceRanges()
        {
            IngestionDocument doc = new("overlap-pages");
            doc.Sections.Add(new IngestionDocumentSection
            {
                Elements =
                {
                    new IngestionDocumentParagraph("one two three four") { PageNumber = 1 },
                    new IngestionDocumentParagraph(" five six seven eight") { PageNumber = 2 },
                }
            });

            IReadOnlyList<IngestionChunk> chunks = await CreateDocumentChunker(
                maxTokensPerChunk: 4,
                overlapTokens: 1)
                .ProcessAsync(doc)
                .ToListAsync();

            Assert.Contains(chunks, chunk => chunk.PageNumbers.SequenceEqual([1, 2]));
        }

        [Fact]
        public async Task BinaryImageBreaksTextOverlap()
        {
            IngestionDocument doc = new("overlap-image");
            IngestionDocumentImage image = IngestionDocumentImage.FromContent(
                new byte[] { 1, 2, 3 },
                "image/png");
            image.PageNumber = 2;
            doc.Sections.Add(new IngestionDocumentSection
            {
                Elements =
                {
                    new IngestionDocumentParagraph("one two three four") { PageNumber = 1 },
                    image,
                }
            });

            IReadOnlyList<IngestionChunk> chunks = await CreateDocumentChunker(
                maxTokensPerChunk: 8,
                overlapTokens: 2)
                .ProcessAsync(doc)
                .ToListAsync();

            Assert.Collection(
                chunks,
                text =>
                {
                    Assert.IsType<Microsoft.Extensions.AI.TextContent>(text.Content);
                    Assert.Equal([1], text.PageNumbers);
                },
                binary =>
                {
                    Assert.IsType<Microsoft.Extensions.AI.DataContent>(binary.Content);
                    Assert.Equal([2], binary.PageNumbers);
                });
        }
    }
}
