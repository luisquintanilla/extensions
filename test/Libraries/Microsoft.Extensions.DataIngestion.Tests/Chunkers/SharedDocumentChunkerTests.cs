// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Documents;
using Microsoft.ML.Tokenizers;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Chunkers.Tests;

public class SharedDocumentChunkerTests
{
    private static readonly Tokenizer _tokenizer = TiktokenTokenizer.CreateForModel("gpt-4");

    [Fact]
    public async Task SectionChunkerUsesLiteralProjectionAndRetainsProvenance()
    {
        DocumentText heading = TestDocuments.Text("heading", "*Report*", DocumentTextRole.Heading, 1, 1);
        DocumentTable table = new(
            new("table"),
            2,
            2,
            [
                new DocumentTableCell(new("cell-00"), 0, 0, [TestDocuments.Text("h1", "Region", pageNumber: 1)], role: DocumentTableCellRole.ColumnHeader),
                new DocumentTableCell(new("cell-01"), 0, 1, [TestDocuments.Text("h2", "Revenue", pageNumber: 1)], role: DocumentTableCellRole.ColumnHeader),
                new DocumentTableCell(new("cell-10"), 1, 0, [TestDocuments.Text("c1", "West", pageNumber: 2)]),
                new DocumentTableCell(new("cell-11"), 1, 1, [TestDocuments.Text("c2", "$1", pageNumber: 2)]),
            ],
            pageReferences: [new(1), new(2)]);
        IngestionDocument document = TestDocuments.Create(
            "id",
            TestDocuments.Section("section", heading, TestDocuments.Text("paragraph", "Use `literal`.", pageNumber: 1), table));

        IngestionChunk<string> chunk = Assert.Single(await new SectionChunker(new(_tokenizer) { MaxTokensPerChunk = 100 }).ProcessAsync(document).ToListAsync());

        Assert.Equal("*Report*\nUse `literal`.\nRegion\tRevenue\nWest\t$1", chunk.Content);
        Assert.Equal([1, 2], chunk.PageNumbers);
        Assert.Contains(new DocumentNodeId("heading"), chunk.SourceNodeIds);
        Assert.Contains(new DocumentNodeId("table"), chunk.SourceNodeIds);
    }

    [Fact]
    public async Task CaptionlessImageDoesNotCreateTextChunk()
    {
        IngestionDocument document = TestDocuments.Create(
            "image",
            new DocumentImage(new("image-node"), new byte[] { 1 }, "image/png", pageReferences: [new(1)]));

        Assert.Empty(await new SectionChunker(new(_tokenizer) { MaxTokensPerChunk = 100 }).ProcessAsync(document).ToListAsync());
    }

    [Fact]
    public async Task HeaderChunkerUsesHeadingHierarchy()
    {
        IngestionDocument document = TestDocuments.Create(
            "headers",
            TestDocuments.Text("h1", "One", DocumentTextRole.Heading, 1),
            TestDocuments.Text("p1", "alpha"),
            TestDocuments.Text("h2", "Two", DocumentTextRole.Heading, 2),
            TestDocuments.Text("p2", "beta"));

        var chunks = await new HeaderChunker(new(_tokenizer) { MaxTokensPerChunk = 100 }).ProcessAsync(document).ToListAsync();

        Assert.Equal(["One\nalpha", "One Two\nbeta"], chunks.Select(chunk => chunk.Content));
    }

    [Fact]
    public async Task SplitHeaderlessTableDoesNotDuplicateRowsAndKeepsRowProvenance()
    {
        DocumentTable table = new(
            new("table"),
            2,
            1,
            [
                new DocumentTableCell(new("row-1-cell"), 0, 0, [TestDocuments.Text("row-1", "alpha beta gamma", pageNumber: 1)]),
                new DocumentTableCell(new("row-2-cell"), 1, 0, [TestDocuments.Text("row-2", "delta epsilon zeta", pageNumber: 2)]),
            ]);
        IngestionDocument document = TestDocuments.Create("table", table);

        var chunks = await new SectionChunker(new(_tokenizer) { MaxTokensPerChunk = 5 }).ProcessAsync(document).ToListAsync();

        Assert.Equal(["alpha beta gamma", "delta epsilon zeta"], chunks.Select(chunk => chunk.Content));
        Assert.Equal([1], chunks[0].PageNumbers);
        Assert.Equal([2], chunks[1].PageNumbers);
        Assert.DoesNotContain(new DocumentNodeId("row-2"), chunks[0].SourceNodeIds);
    }
}
