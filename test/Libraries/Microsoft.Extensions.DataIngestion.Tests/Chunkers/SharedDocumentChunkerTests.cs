// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Microsoft.ML.Tokenizers;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Chunkers.Tests;

public class SharedDocumentChunkerTests
{
    private static readonly Tokenizer _tokenizer = TiktokenTokenizer.CreateForModel("gpt-4");

    [Fact]
    public async Task SectionChunkerProducesNonGenericTextContentWithTokenCountAndProvenance()
    {
        IngestionDocument document = TestDocuments.Create(
            "id",
            TestDocuments.Section(
                "section",
                TestDocuments.Text("heading", "Report", DocumentTextRole.Heading, 1, 1),
                TestDocuments.Text("body", "literal body", pageNumber: 2)));

        IngestionChunk chunk = Assert.Single(await new SectionChunker(new(_tokenizer) { MaxTokensPerChunk = 100 }).ProcessAsync(document).ToListAsync());

        Assert.Equal("Report\nliteral body", Assert.IsType<TextContent>(chunk.Content).Text);
        Assert.True(chunk.TokenCount > 0);
        Assert.Equal(["body", "heading"], chunk.SourceNodeIds.Select(id => id.Value).OrderBy(id => id));
        Assert.Equal([1, 2], chunk.PageNumbers);
    }

    [Fact]
    public async Task TokenChunkerAttributesNestedTextContinuationsExactly()
    {
        DocumentTable table = new(
            new("table"),
            1,
            1,
            [
                new DocumentTableCell(
                    new("cell"),
                    0,
                    0,
                    [
                        TestDocuments.Text("alpha", "alpha alpha alpha alpha alpha", pageNumber: 1),
                        TestDocuments.Text("omega", "omega omega omega omega omega", pageNumber: 2),
                    ]),
            ]);
        IngestionDocument document = TestDocuments.Create("table", table);

        var chunks = await new DocumentTokenChunker(new(_tokenizer) { MaxTokensPerChunk = 4, OverlapTokens = 0 }).ProcessAsync(document).ToListAsync();

        Assert.Contains(chunks, chunk => chunk.SourceNodeIds.Contains(new("alpha")) && !chunk.SourceNodeIds.Contains(new("omega")) && chunk.PageNumbers.SequenceEqual([1]));
        Assert.Contains(chunks, chunk => chunk.SourceNodeIds.Contains(new("omega")) && !chunk.SourceNodeIds.Contains(new("alpha")) && chunk.PageNumbers.SequenceEqual([2]));
        Assert.All(chunks, chunk => Assert.IsType<TextContent>(chunk.Content));
        Assert.All(chunks, chunk => Assert.True(chunk.TokenCount > 0));
    }
}
