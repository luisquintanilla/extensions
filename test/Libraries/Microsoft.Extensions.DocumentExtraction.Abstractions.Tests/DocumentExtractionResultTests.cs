// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Extensions.DocumentExtraction;

public class DocumentExtractionResultTests
{
    [Fact]
    public void ConstructorSortsPagesAndPreservesUsage()
    {
        DocumentExtractionResult result = new([TestDocument.Page(2, "two"), TestDocument.Page(1, "one")])
        {
            Usage = new() { PagesProcessed = 2 },
        };

        Assert.Equal([1, 2], result.Pages.Select(page => page.PageNumber));
        Assert.Equal("one\n\ntwo", result.Text);
        Assert.Equal(result.Text, result.Document.Text);
        Assert.Equal(2, result.Usage.PagesProcessed);
    }

    [Fact]
    public void MarkdownRemainsExactExtractionState()
    {
        const string Markdown = " \n# Provider heading\r\n\r\n*exact*  \n";
        DocumentPage page = new(1, new Document([]), Markdown);

        Assert.Equal(Markdown, page.Markdown);
        Assert.Equal(string.Empty, page.Text);
        Assert.Null(typeof(DocumentExtractionResult).GetProperty("Markdown"));
    }

    [Fact]
    public void InvalidAndDuplicatePagesAreRejected()
    {
        Assert.Throws<ArgumentNullException>("pages", () => new DocumentExtractionResult(null!));
        Assert.Throws<ArgumentOutOfRangeException>("pageNumber", () => new DocumentPage(0, new Document([])));
        Assert.Throws<ArgumentException>("pages", () => new DocumentExtractionResult([TestDocument.Page(1, "one"), TestDocument.Page(1, "duplicate")]));
    }
}
