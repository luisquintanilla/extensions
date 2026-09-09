// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Chunkers.Tests
{
    public abstract class DocumentTokenChunkerTests : DocumentChunkerTests
    {
        [Fact]
        public async Task SingleChunkText()
        {
            string text = "This is a short document that fits within a single chunk.";
            IngestionDocument doc = new IngestionDocument("singleChunkDoc");
            doc.Sections.Add(new IngestionDocumentSection
            {
                Elements =
                {
                    new IngestionDocumentParagraph(text)
                }
            });

            IngestionChunker chunker = CreateDocumentChunker();
            IReadOnlyList<IngestionChunk> chunks = await chunker.ProcessAsync(doc).ToListAsync();

            IngestionChunk chunk = Assert.Single(chunks);
            Assert.Equal(text, GetText(chunk), ignoreLineEndingDifferences: true);
        }

        [Fact]
        public async Task CollectsPagesAcrossElements()
        {
            IngestionDocument doc = new("pages");
            IngestionDocumentParagraph pageOne = new("page one") { PageNumber = 1 };
            IngestionDocumentParagraph pageTwo = new(" page two") { PageNumber = 2 };
            doc.Sections.Add(new IngestionDocumentSection
            {
                Elements = { pageOne, pageTwo }
            });

            IngestionChunk chunk = Assert.Single(await CreateDocumentChunker()
                .ProcessAsync(doc)
                .ToListAsync());

            Assert.Equal([1, 2], chunk.PageNumbers);
        }

        [Fact]
        public async Task EmitsCaptionlessBinaryImage()
        {
            IngestionDocument doc = new("image");
            IngestionDocumentImage image = IngestionDocumentImage.FromContent(
                new byte[] { 1, 2, 3 },
                "image/png");
            image.PageNumber = 3;
            doc.Sections.Add(new IngestionDocumentSection
            {
                Elements = { image }
            });

            IngestionChunk chunk = Assert.Single(await CreateDocumentChunker()
                .ProcessAsync(doc)
                .ToListAsync());

            Assert.IsType<DataContent>(chunk.Content);
            Assert.Equal(0, chunk.TokenCount);
            Assert.Equal([3], chunk.PageNumbers);
        }

        [Fact]
        public async Task ExactBoundaryDoesNotClaimNextElementPage()
        {
            IngestionDocument doc = new("boundary-pages");
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
                overlapTokens: 0)
                .ProcessAsync(doc)
                .ToListAsync();

            Assert.Equal([1], chunks[0].PageNumbers);
            Assert.Equal([2], chunks[^1].PageNumbers);
        }
    }
}
