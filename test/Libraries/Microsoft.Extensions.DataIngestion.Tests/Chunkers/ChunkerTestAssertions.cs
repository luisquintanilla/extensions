// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.AI;
using Microsoft.ML.Tokenizers;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Chunkers.Tests;

internal static class ChunkerTestAssertions
{
    internal static void Equal(
        IngestionChunk actual,
        IngestionDocument expectedDocument,
        string expectedText,
        string? expectedContext,
        IReadOnlyList<string> expectedSourceNodeIds,
        IReadOnlyList<int> expectedPageNumbers,
        Tokenizer tokenizer)
    {
        Assert.Same(expectedDocument, actual.Document);
        AIContent content = actual.Content;
        TextContent textContent = Assert.IsType<TextContent>(content);
        Assert.Equal(expectedText, textContent.Text);
        Assert.True(actual.TokenCount > 0);
        Assert.Equal(tokenizer.CountTokens(expectedText, considerNormalization: false), actual.TokenCount);
        Assert.Equal(expectedContext, actual.Context);
        Assert.Equal(
            expectedSourceNodeIds.OrderBy(static id => id),
            actual.SourceNodeIds.Select(static id => id.Value).OrderBy(static id => id));
        Assert.Equal(expectedPageNumbers, actual.PageNumbers);
    }
}
