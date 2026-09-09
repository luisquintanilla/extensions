// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Tests;

public class IngestionChunkTests
{
    [Fact]
    public void Constructor_SetsTokenCountProperty()
    {
        IngestionDocument document = new("test");
        IngestionChunk chunk = new(new TextContent("test content"), document, 42);

        Assert.Equal(42, chunk.TokenCount);
    }

    [Fact]
    public void Constructor_ThrowsWhenTokenCountIsNegative()
    {
        IngestionDocument document = new("test");

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new IngestionChunk(new TextContent("test content"), document, -1));

        Assert.Equal("tokenCount", exception.ParamName);
    }

    [Fact]
    public void Constructor_AllowsZeroTokenCount()
    {
        IngestionDocument document = new("test");
        IngestionChunk chunk = new(new DataContent(new byte[] { 1 }, "image/png"), document, 0);

        Assert.Equal(0, chunk.TokenCount);
    }

    [Fact]
    public void Constructor_ThrowsWhenTextTokenCountIsZero()
    {
        IngestionDocument document = new("test");

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new IngestionChunk(new TextContent("test content"), document, 0));

        Assert.Equal("tokenCount", exception.ParamName);
    }

    [Fact]
    public void Constructor_NormalizesPageNumbers()
    {
        IngestionDocument document = new("test");
        int[] pageNumbers = [2, 1, 2];
        IngestionChunk chunk = new(
            new TextContent("test content"),
            document,
            2,
            context: null,
            pageNumbers: pageNumbers);
        pageNumbers[0] = -1;

        Assert.Equal([1, 2], chunk.PageNumbers);
        Assert.Throws<NotSupportedException>(
            () => ((IList<int>)chunk.PageNumbers)[0] = -1);
    }

    [Fact]
    public void Constructor_ThrowsWhenPageNumberIsNotPositive()
    {
        IngestionDocument document = new("test");

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new IngestionChunk(
                new TextContent("test content"),
                document,
                2,
                context: null,
                pageNumbers: [0]));

        Assert.Equal("pageNumbers", exception.ParamName);
    }
}
