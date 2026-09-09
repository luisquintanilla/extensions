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
}
