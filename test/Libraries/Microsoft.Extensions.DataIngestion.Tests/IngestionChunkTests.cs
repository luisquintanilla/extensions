// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Tests;

public class IngestionChunkTests
{
    [Fact]
    public void Constructor_SetsTokenCountProperty()
    {
        IngestionDocument document = TestDocuments.Create("test");
        IngestionChunk chunk = new(new TextContent("test content"), document, 42);

        Assert.Equal(42, chunk.TokenCount);
    }

    [Fact]
    public void ConstructorNormalizesTypedProvenance()
    {
        IngestionDocument document = TestDocuments.Create("test");
        IngestionChunk chunk = new(
            new DataContent(new byte[] { 1 }, "image/png"),
            document,
            1,
            sourceNodeIds: [new("image"), new("image")],
            pageNumbers: [2, 1, 2]);

        Assert.IsType<DataContent>(chunk.Content);
        Assert.Equal(["image"], chunk.SourceNodeIds.Select(id => id.Value));
        Assert.Equal([1, 2], chunk.PageNumbers);
    }

    [Fact]
    public void ConstructorRejectsInvalidTypedProvenance()
    {
        IngestionDocument document = TestDocuments.Create("test");

        Assert.Throws<ArgumentException>(
            "sourceNodeIds",
            () => new IngestionChunk(new TextContent("content"), document, 1, sourceNodeIds: [default]));
        Assert.Throws<ArgumentOutOfRangeException>(
            "pageNumbers",
            () => new IngestionChunk(new TextContent("content"), document, 1, pageNumbers: [0]));
    }

    [Fact]
    public void Constructor_ThrowsWhenTokenCountIsNegative()
    {
        IngestionDocument document = TestDocuments.Create("test");

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new IngestionChunk(new TextContent("test content"), document, -1));

        Assert.Equal("tokenCount", exception.ParamName);
    }

    [Fact]
    public void Constructor_ThrowsWhenTokenCountIsZero()
    {
        IngestionDocument document = TestDocuments.Create("test");

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new IngestionChunk(new TextContent("test content"), document, 0));

        Assert.Equal("tokenCount", exception.ParamName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Constructor_AcceptsNonGenericAIContent(bool useBinaryContent)
    {
        AIContent content = useBinaryContent
            ? new DataContent(new byte[] { 0x01, 0x02, 0x03 }, "application/octet-stream")
            : new TextContent("neutral chunk content");
        IngestionDocument document = TestDocuments.Create(
            "document-42",
            TestDocuments.Text("node-7", "neutral chunk content", pageNumber: 4));

        IngestionChunk chunk = new(
            content,
            document,
            3,
            context: "section context",
            sourceNodeIds: [new("node-7")],
            pageNumbers: [4]);

        Assert.Same(content, chunk.Content);
        Assert.Same(document, chunk.Document);
        Assert.Equal(3, chunk.TokenCount);
        Assert.Equal("section context", chunk.Context);
        Assert.Equal(["node-7"], chunk.SourceNodeIds.Select(static id => id.Value));
        Assert.Equal([4], chunk.PageNumbers);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RequiresPositiveTokenCount(int tokenCount)
    {
        IngestionDocument document = TestDocuments.Create("test");

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            nameof(tokenCount),
            () => new IngestionChunk(new TextContent("content"), document, tokenCount));

        Assert.Equal(tokenCount, exception.ActualValue);
    }

    [Fact]
    public void Constructor_PreservesDocumentNodeAndPageProvenance()
    {
        IngestionDocument document = TestDocuments.Create(
            "source-document",
            TestDocuments.Section(
                "section",
                TestDocuments.Text("paragraph", "source text", pageNumber: 3)));

        IngestionChunk chunk = new(
            new TextContent("source text"),
            document,
            2,
            sourceNodeIds: [new("section"), new("paragraph")],
            pageNumbers: [3]);

        Assert.Same(document, chunk.Document);
        Assert.Equal("source-document", chunk.Document.Identifier);
        Assert.Equal(["section", "paragraph"], chunk.SourceNodeIds.Select(static id => id.Value));
        Assert.Equal([3], chunk.PageNumbers);
        DocumentText sourceNode = Assert.IsType<DocumentText>(document.Document.GetNode(new("paragraph")));
        Assert.Equal("source text", sourceNode.Text);
        Assert.Equal([3], sourceNode.PageReferences.Select(static page => page.PageNumber));
    }
}
