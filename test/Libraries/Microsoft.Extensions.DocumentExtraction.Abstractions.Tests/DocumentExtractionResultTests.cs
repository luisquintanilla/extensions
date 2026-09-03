// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Extensions.DocumentExtraction;

public class DocumentExtractionResultTests
{
    [Fact]
    public void Constructor_NullPages_Throws()
    {
        Assert.Throws<ArgumentNullException>("pages", () => new DocumentExtractionResult(null!));
    }

    [Fact]
    public void ResultMergesPageFragmentsIntoCanonicalDocument()
    {
        DocumentExtractionResult result = new(
        [
            TestDocument.Page(1, "page one"),
            TestDocument.Page(2, "page two"),
        ]);

        Assert.Equal("page one\n\npage two", result.Text);
        Assert.Equal(result.Text, result.Document.Text);
        Assert.Equal(2, result.Pages.Count);
    }

    [Fact]
    public void PageMarkdown_IsNullableAndPreservedExactlyWithoutBecomingText()
    {
        const string Markdown = " \n# Provider heading\r\n\r\n*exact*  \n";
        DocumentPage pageWithoutMarkdown = TestDocument.EmptyPage(1);
        DocumentPage markdownOnlyPage = new(2, new Document([]), Markdown);

        Assert.Null(pageWithoutMarkdown.Markdown);
        Assert.Equal(Markdown, markdownOnlyPage.Markdown);
        Assert.Equal(string.Empty, markdownOnlyPage.Text);
    }

    [Fact]
    public void ResultDoesNotAggregateProviderMarkdown()
    {
        DocumentExtractionResult result = new(
        [
            new DocumentPage(1, TestDocument.Create("one", 1), "# one"),
            new DocumentPage(2, new Document([]), "# two"),
            new DocumentPage(3, TestDocument.Create("three", 3), "# three"),
        ]);

        Assert.Equal("one\n\nthree", result.Text);
        Assert.Null(typeof(DocumentExtractionResult).GetProperty("Markdown"));
    }

    [Fact]
    public void StreamingMergeRejectsDuplicateNodeIds()
    {
        DocumentPage first = new(1, new Document([new DocumentText(new("same"), "one")]));
        DocumentPage second = new(2, new Document([new DocumentText(new("same"), "two")]));

        Assert.Throws<ArgumentException>("children", () => new DocumentExtractionResult([first, second]));
    }

    [Fact]
    public void PagesAreDefensivelySnapshotted()
    {
        List<DocumentPage> pages = [TestDocument.Page(1, "one")];
        DocumentExtractionResult result = new(pages);

        pages.Add(TestDocument.Page(2, "two"));

        Assert.Single(result.Pages);
        Assert.Equal("one", result.Text);
    }

    [Fact]
    public void ApiShapeHasSingleSemanticAuthority()
    {
        Assert.False(typeof(DocumentPage).GetProperty(nameof(DocumentPage.Document))!.CanWrite);
        Assert.False(typeof(DocumentPage).GetProperty(nameof(DocumentPage.Text))!.CanWrite);
        Assert.False(typeof(DocumentPage).GetProperty(nameof(DocumentPage.Markdown))!.CanWrite);
        Assert.False(typeof(DocumentExtractionResult).GetProperty(nameof(DocumentExtractionResult.Document))!.CanWrite);
        Assert.Null(typeof(DocumentExtractionResult).GetProperty("Usage"));
        Assert.Null(typeof(DocumentExtractionPageResult).GetProperty("Usage"));
        Assert.Null(typeof(DocumentExtractionPageResult).GetProperty("PagesProcessed"));
    }
}
