// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
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
    public void Text_JoinsPerPageText()
    {
        var result = new DocumentExtractionResult(
        [
            new DocumentPage(1, [new DocumentBlock("page one")]),
            new DocumentPage(2, [new DocumentBlock("page two")]),
        ]);

        Assert.Equal("page one\n\npage two", result.Text);
        Assert.Equal(2, result.Pages.Count);
    }

    [Fact]
    public void PageMarkdown_IsNullableAndPreservedExactlyWithoutBecomingText()
    {
        const string Markdown = " \n# Provider heading\r\n\r\n*exact*  \n";
        DocumentPage pageWithoutMarkdown = new(1, []);
        DocumentPage markdownOnlyPage = new(2, [], Markdown);

        Assert.Null(pageWithoutMarkdown.Markdown);
        Assert.Equal(Markdown, markdownOnlyPage.Markdown);
        Assert.Equal(string.Empty, markdownOnlyPage.Text);
    }

    [Fact]
    public void PageText_RecursesInReadingOrderWithDeterministicTableSeparators()
    {
        DocumentTableCell spanningCell = new(
            0,
            0,
            [new DocumentBlock("A"), new DocumentImage { Caption = "caption" }])
        {
            RowSpan = 2,
            ColumnSpan = 2,
        };
        DocumentTableCell nestedCell = new(
            0,
            2,
            [
                new DocumentTable(
                    1,
                    1,
                    [new DocumentTableCell(0, 0, [new DocumentBlock("B")])]),
            ]);
        DocumentTableCell finalCell = new(1, 2, [new DocumentBlock("C")]);
        DocumentTable table = new(
            2,
            3,
            [finalCell, nestedCell, spanningCell],
            markdownRepresentation: "| ignored provider rendering |");
        DocumentPage page = new(
            1,
            [
                new DocumentBlock("Intro"),
                table,
                new DocumentImage { Content = new byte[] { 1, 2, 3 }, MediaType = "image/png" },
                new DocumentBlock("Outro"),
            ]);

        Assert.Equal("Intro\n\nA\n\ncaption\t\tB\n\t\tC\n\nOutro", page.Text);
    }

    [Fact]
    public void PageText_UsesOnlyTextBearingStructuredContent()
    {
        DocumentPage page = new(
            1,
            [
                new DocumentImage { Content = new byte[] { 1 }, MediaType = "image/png" },
                new DocumentTable(0, 0, cells: null, markdownRepresentation: "| markdown only |"),
                new DocumentTable(1, 1, [new DocumentTableCell(0, 0, [])]),
            ],
            markdown: "provider page markdown");

        Assert.Equal(string.Empty, page.Text);
    }

    [Fact]
    public void TableText_PreservesEmptyCellColumnPosition()
    {
        DocumentPage page = new(
            1,
            [
                new DocumentTable(
                    1,
                    3,
                    [
                        new DocumentTableCell(0, 0, [new DocumentBlock("A")]),
                        new DocumentTableCell(0, 1, []),
                        new DocumentTableCell(0, 2, [new DocumentBlock("C")]),
                    ]),
            ]);

        Assert.Equal("A\t\tC", page.Text);
    }

    [Fact]
    public void ResultText_PreservesPageBoundariesWithoutAggregatingMarkdown()
    {
        DocumentExtractionResult result = new(
        [
            new DocumentPage(1, [new DocumentBlock("one")], "# one"),
            new DocumentPage(2, [], "# two"),
            new DocumentPage(3, [new DocumentBlock("three")], "# three"),
        ]);

        Assert.Equal("one\n\n\n\nthree", result.Text);
        Assert.Null(typeof(DocumentExtractionResult).GetProperty("Markdown"));
    }

    [Fact]
    public void ApiShape_HasSingleStructuredTextAuthorityAndBclImageContent()
    {
        Assert.False(typeof(DocumentPage).GetProperty(nameof(DocumentPage.Elements))!.CanWrite);
        Assert.False(typeof(DocumentPage).GetProperty(nameof(DocumentPage.Text))!.CanWrite);
        Assert.False(typeof(DocumentPage).GetProperty(nameof(DocumentPage.Markdown))!.CanWrite);
        Assert.Null(typeof(DocumentTableCell).GetProperty("Content"));
        Assert.Null(typeof(DocumentExtractionResult).GetProperty("Usage"));
        Assert.Null(typeof(DocumentExtractionPageResult).GetProperty("Usage"));
        Assert.Null(typeof(DocumentExtractionPageResult).GetProperty("PagesProcessed"));
        Assert.Equal(typeof(ReadOnlyMemory<byte>?), typeof(DocumentImage).GetProperty(nameof(DocumentImage.Content))!.PropertyType);
        Assert.Equal(typeof(string), typeof(DocumentImage).GetProperty(nameof(DocumentImage.MediaType))!.PropertyType);
    }

    [Fact]
    public void StructuredConstructors_RejectNullElements()
    {
        Assert.Throws<ArgumentNullException>("elements", () => new DocumentPage(1, null!));
        Assert.Throws<ArgumentNullException>("elements", () => new DocumentTableCell(0, 0, null!));
    }
}
