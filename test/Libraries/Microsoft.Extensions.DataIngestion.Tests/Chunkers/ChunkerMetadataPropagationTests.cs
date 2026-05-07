// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ML.Tokenizers;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Chunkers.Tests;

public class ChunkerMetadataPropagationTests
{
    private static IngestionChunker<string> CreateSectionChunker(int maxTokensPerChunk = 2_000)
    {
        var tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");
        return new SectionChunker(new(tokenizer) { MaxTokensPerChunk = maxTokensPerChunk, OverlapTokens = 0 });
    }

    private static IngestionChunker<string> CreateHeaderChunker(int maxTokensPerChunk = 2_000)
    {
        var tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");
        return new HeaderChunker(new(tokenizer) { MaxTokensPerChunk = maxTokensPerChunk, OverlapTokens = 0 });
    }

    private static IngestionChunker<string> CreateDocumentTokenChunker(int maxTokensPerChunk = 2_000)
    {
        var tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");
        return new DocumentTokenChunker(new(tokenizer) { MaxTokensPerChunk = maxTokensPerChunk, OverlapTokens = 0 });
    }

    [Fact]
    public async Task SectionChunker_SingleElementWithMetadata_PropagatesMetadata()
    {
        var paragraph = new IngestionDocumentParagraph("This is a paragraph.");
        paragraph.Metadata["element_type"] = "text";
        paragraph.Metadata["page"] = 1;

        var doc = new IngestionDocument("doc");
        doc.Sections.Add(new IngestionDocumentSection { Elements = { paragraph } });

        var chunker = CreateSectionChunker();
        var chunks = await chunker.ProcessAsync(doc).ToListAsync();

        var chunk = Assert.Single(chunks);
        Assert.True(chunk.HasMetadata);
        Assert.Equal("text", chunk.Metadata["element_type"]);
        Assert.Equal(1, chunk.Metadata["page"]);
    }

    [Fact]
    public async Task SectionChunker_MultipleElementsDifferentKeys_AllKeysAppear()
    {
        var para1 = new IngestionDocumentParagraph("First paragraph.");
        para1.Metadata["element_type"] = "text";

        var para2 = new IngestionDocumentParagraph("Second paragraph.");
        para2.Metadata["confidence"] = 0.95;

        var doc = new IngestionDocument("doc");
        doc.Sections.Add(new IngestionDocumentSection { Elements = { para1, para2 } });

        var chunker = CreateSectionChunker();
        var chunks = await chunker.ProcessAsync(doc).ToListAsync();

        var chunk = Assert.Single(chunks);
        Assert.True(chunk.HasMetadata);
        Assert.Equal("text", chunk.Metadata["element_type"]);
        Assert.Equal(0.95, chunk.Metadata["confidence"]);
    }

    [Fact]
    public async Task SectionChunker_ConflictingKeys_FirstElementWins()
    {
        var para1 = new IngestionDocumentParagraph("First paragraph.");
        para1.Metadata["element_type"] = "table";

        var para2 = new IngestionDocumentParagraph("Second paragraph.");
        para2.Metadata["element_type"] = "text";

        var doc = new IngestionDocument("doc");
        doc.Sections.Add(new IngestionDocumentSection { Elements = { para1, para2 } });

        var chunker = CreateSectionChunker();
        var chunks = await chunker.ProcessAsync(doc).ToListAsync();

        var chunk = Assert.Single(chunks);
        Assert.Equal("table", chunk.Metadata["element_type"]);
    }

    [Fact]
    public async Task SectionChunker_NullMetadataValue_Skipped()
    {
        var paragraph = new IngestionDocumentParagraph("This is a paragraph.");
        paragraph.Metadata["element_type"] = null;
        paragraph.Metadata["valid_key"] = "valid_value";

        var doc = new IngestionDocument("doc");
        doc.Sections.Add(new IngestionDocumentSection { Elements = { paragraph } });

        var chunker = CreateSectionChunker();
        var chunks = await chunker.ProcessAsync(doc).ToListAsync();

        var chunk = Assert.Single(chunks);
        Assert.True(chunk.HasMetadata);
        Assert.False(chunk.Metadata.ContainsKey("element_type"));
        Assert.Equal("valid_value", chunk.Metadata["valid_key"]);
    }

    [Fact]
    public async Task SectionChunker_NoMetadata_ChunkHasNoMetadata()
    {
        var doc = new IngestionDocument("doc");
        doc.Sections.Add(new IngestionDocumentSection
        {
            Elements =
            {
                new IngestionDocumentParagraph("No metadata here."),
                new IngestionDocumentParagraph("Also no metadata.")
            }
        });

        var chunker = CreateSectionChunker();
        var chunks = await chunker.ProcessAsync(doc).ToListAsync();

        var chunk = Assert.Single(chunks);
        Assert.False(chunk.HasMetadata);
    }

    [Fact]
    public async Task SectionChunker_ElementSplitAcrossChunks_FirstChunkGetsMetadata()
    {
        // Create a large paragraph that exceeds the token limit and forces a split
        string longText = string.Join(" ", Enumerable.Repeat("word", 600));
        var paragraph = new IngestionDocumentParagraph(longText);
        paragraph.Metadata["element_type"] = "body";

        var doc = new IngestionDocument("doc");
        doc.Sections.Add(new IngestionDocumentSection { Elements = { paragraph } });

        var chunker = CreateSectionChunker(maxTokensPerChunk: 200);
        var chunks = await chunker.ProcessAsync(doc).ToListAsync();

        Assert.True(chunks.Count > 1);

        // First chunk gets the metadata
        Assert.True(chunks[0].HasMetadata);
        Assert.Equal("body", chunks[0].Metadata["element_type"]);

        // Subsequent chunks from the same element do NOT get metadata (accumulator was cleared on commit)
        Assert.False(chunks[1].HasMetadata);
    }

    [Fact]
    public async Task SectionChunker_TwoSectionsWithMetadata_IndependentMetadataPerSection()
    {
        var para1 = new IngestionDocumentParagraph("First section paragraph.");
        para1.Metadata["section"] = "intro";

        var para2 = new IngestionDocumentParagraph("Second section paragraph.");
        para2.Metadata["section"] = "conclusion";

        var doc = new IngestionDocument("doc");
        doc.Sections.Add(new IngestionDocumentSection { Elements = { para1 } });
        doc.Sections.Add(new IngestionDocumentSection { Elements = { para2 } });

        var chunker = CreateSectionChunker();
        var chunks = await chunker.ProcessAsync(doc).ToListAsync();

        Assert.Equal(2, chunks.Count);
        Assert.Equal("intro", chunks[0].Metadata["section"]);
        Assert.Equal("conclusion", chunks[1].Metadata["section"]);
    }

    [Fact]
    public async Task HeaderChunker_PropagatesMetadata()
    {
        var header = new IngestionDocumentHeader("# Title") { Level = 1 };
        var para = new IngestionDocumentParagraph("Body text.");
        para.Metadata["element_type"] = "text";
        para.Metadata["page"] = 3;

        var doc = new IngestionDocument("doc");
        doc.Sections.Add(new IngestionDocumentSection { Elements = { header, para } });

        var chunker = CreateHeaderChunker();
        var chunks = await chunker.ProcessAsync(doc).ToListAsync();

        var chunk = Assert.Single(chunks);
        Assert.True(chunk.HasMetadata);
        Assert.Equal("text", chunk.Metadata["element_type"]);
        Assert.Equal(3, chunk.Metadata["page"]);
    }

    [Fact]
    public async Task DocumentTokenChunker_SingleElementWithMetadata_PropagatesMetadata()
    {
        var paragraph = new IngestionDocumentParagraph("This is a paragraph.");
        paragraph.Metadata["element_type"] = "text";

        var doc = new IngestionDocument("doc");
        doc.Sections.Add(new IngestionDocumentSection { Elements = { paragraph } });

        var chunker = CreateDocumentTokenChunker();
        var chunks = await chunker.ProcessAsync(doc).ToListAsync();

        var chunk = Assert.Single(chunks);
        Assert.True(chunk.HasMetadata);
        Assert.Equal("text", chunk.Metadata["element_type"]);
    }

    [Fact]
    public async Task DocumentTokenChunker_MultipleElements_AccumulatesMetadata()
    {
        var para1 = new IngestionDocumentParagraph("First paragraph.");
        para1.Metadata["element_type"] = "text";

        var para2 = new IngestionDocumentParagraph("Second paragraph.");
        para2.Metadata["confidence"] = 0.9;

        var doc = new IngestionDocument("doc");
        doc.Sections.Add(new IngestionDocumentSection { Elements = { para1, para2 } });

        var chunker = CreateDocumentTokenChunker();
        var chunks = await chunker.ProcessAsync(doc).ToListAsync();

        var chunk = Assert.Single(chunks);
        Assert.True(chunk.HasMetadata);
        Assert.Equal("text", chunk.Metadata["element_type"]);
        Assert.Equal(0.9, chunk.Metadata["confidence"]);
    }

    [Fact]
    public async Task DocumentTokenChunker_ConflictingKeys_FirstElementWins()
    {
        var para1 = new IngestionDocumentParagraph("First paragraph.");
        para1.Metadata["element_type"] = "table";

        var para2 = new IngestionDocumentParagraph("Second paragraph.");
        para2.Metadata["element_type"] = "text";

        var doc = new IngestionDocument("doc");
        doc.Sections.Add(new IngestionDocumentSection { Elements = { para1, para2 } });

        var chunker = CreateDocumentTokenChunker();
        var chunks = await chunker.ProcessAsync(doc).ToListAsync();

        var chunk = Assert.Single(chunks);
        Assert.Equal("table", chunk.Metadata["element_type"]);
    }

    [Fact]
    public async Task DocumentTokenChunker_ElementSplitAcrossChunks_FirstChunkGetsMetadata()
    {
        string longText = string.Join(" ", Enumerable.Repeat("word", 600));
        var paragraph = new IngestionDocumentParagraph(longText);
        paragraph.Metadata["element_type"] = "body";

        var doc = new IngestionDocument("doc");
        doc.Sections.Add(new IngestionDocumentSection { Elements = { paragraph } });

        var chunker = CreateDocumentTokenChunker(maxTokensPerChunk: 200);
        var chunks = await chunker.ProcessAsync(doc).ToListAsync();

        Assert.True(chunks.Count > 1);

        // First chunk gets the metadata
        Assert.True(chunks[0].HasMetadata);
        Assert.Equal("body", chunks[0].Metadata["element_type"]);

        // Subsequent chunks from the same element do NOT get metadata (cleared on finalize)
        Assert.False(chunks[1].HasMetadata);
    }

    [Fact]
    public async Task DocumentTokenChunker_NoMetadata_ChunkHasNoMetadata()
    {
        var doc = new IngestionDocument("doc");
        doc.Sections.Add(new IngestionDocumentSection
        {
            Elements =
            {
                new IngestionDocumentParagraph("No metadata here.")
            }
        });

        var chunker = CreateDocumentTokenChunker();
        var chunks = await chunker.ProcessAsync(doc).ToListAsync();

        var chunk = Assert.Single(chunks);
        Assert.False(chunk.HasMetadata);
    }

    [Fact]
    public async Task SectionChunker_TableWithMetadata_PropagatesMetadata()
    {
        var cells = new IngestionDocumentElement?[2, 2]
        {
            { new IngestionDocumentParagraph("Header1"), new IngestionDocumentParagraph("Header2") },
            { new IngestionDocumentParagraph("Value1"), new IngestionDocumentParagraph("Value2") }
        };
        var table = new IngestionDocumentTable("| Header1 | Header2 |\n| --- | --- |\n| Value1 | Value2 |", cells);
        table.Metadata["element_type"] = "table";

        var doc = new IngestionDocument("doc");
        doc.Sections.Add(new IngestionDocumentSection { Elements = { table } });

        var chunker = CreateSectionChunker();
        var chunks = await chunker.ProcessAsync(doc).ToListAsync();

        var chunk = Assert.Single(chunks);
        Assert.True(chunk.HasMetadata);
        Assert.Equal("table", chunk.Metadata["element_type"]);
    }
}
