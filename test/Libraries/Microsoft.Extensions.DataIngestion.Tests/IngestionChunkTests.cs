// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Tests;

public class IngestionChunkTests
{
    [Fact]
    public void ConstructorNormalizesPageNumbers()
    {
        int[] pageNumbers = [2, 1, 2];
        IngestionChunk<string> chunk = new(
            "content",
            new IngestionDocument("document"),
            context: null,
            pageNumbers);
        pageNumbers[0] = -1;

        Assert.Equal([1, 2], chunk.PageNumbers);
        Assert.Throws<NotSupportedException>(
            () => ((IList<int>)chunk.PageNumbers)[0] = -1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ConstructorRejectsInvalidPageNumbers(int pageNumber)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            "pageNumbers",
            () => new IngestionChunk<string>(
                "content",
                new IngestionDocument("document"),
                context: null,
                pageNumbers: [pageNumber]));

        Assert.Contains("positive one-based", exception.Message, StringComparison.Ordinal);
    }
}
