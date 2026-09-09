// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Extensions.DocumentExtraction;

public class DocumentExtractionPageResultExtensionsTests
{
    [Fact]
    public void ReductionSortsPagesAndPreservesUsageAndProperties()
    {
        DocumentExtractionPageResult[] updates =
        [
            new(TestDocument.Page(2, "two")) { AdditionalProperties = new() { ["b"] = "2" } },
            new(TestDocument.Page(1, "one", "# one"))
            {
                Usage = new() { PagesProcessed = 2 },
                AdditionalProperties = new() { ["a"] = "1" },
            },
        ];

        DocumentExtractionResult result = updates.ToDocumentExtractionResult();

        Assert.Equal("one\n\ntwo", result.Text);
        Assert.Equal(2, result.Usage!.PagesProcessed);
        Assert.Equal("1", result.AdditionalProperties!["a"]);
        Assert.Equal("2", result.AdditionalProperties["b"]);
    }

    [Fact]
    public async Task AsyncReductionMatchesMaterializedRules()
    {
        DocumentExtractionResult result = await YieldAsync(
        [
            new(TestDocument.Page(2, "two")),
            new(TestDocument.Page(1, "one")),
        ]).ToDocumentExtractionResultAsync();

        Assert.Equal("one\n\ntwo", result.Text);
    }

    [Fact]
    public void DuplicatePagesAreRejected()
    {
        DocumentExtractionPageResult[] updates =
        [
            new(TestDocument.Page(1, "one")),
            new(TestDocument.Page(1, "duplicate")),
        ];

        Assert.Throws<ArgumentException>("pages", updates.ToDocumentExtractionResult);
    }

    private static async IAsyncEnumerable<DocumentExtractionPageResult> YieldAsync(IEnumerable<DocumentExtractionPageResult> updates)
    {
        foreach (DocumentExtractionPageResult update in updates)
        {
            await Task.Yield();
            yield return update;
        }
    }
}
