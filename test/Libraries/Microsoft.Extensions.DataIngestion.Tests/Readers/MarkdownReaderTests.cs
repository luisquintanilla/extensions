// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Readers.Tests;

public class MarkdownReaderTests
{
    [Fact]
    public async Task ProducesSharedTreeForAuthoredMarkdown()
    {
        const string Markdown = """
            # *Report*

            Use `VectorStoreWriter<TRecord>` with [links](https://example.com).

            > quoted text

            - first
            - second

            ```csharp
            Console.WriteLine("literal");
            ```

            | Region | Q1 | Q2 |
            | --- | --- | --- |
            | West | 1 | 2 |

            ![chart](data:image/png;base64,AQIDBA==)
            """;
        using MemoryStream source = new(Encoding.UTF8.GetBytes(Markdown));

        IngestionDocument result = await new MarkdownReader().ReadAsync(source, "authored", "text/markdown");

        Assert.Equal("authored", result.Identifier);
        Assert.Contains(result.Document.Nodes.OfType<DocumentText>(), text => text.Role == DocumentTextRole.Heading && text.Text == "Report");
        Assert.Contains(result.Document.Nodes.OfType<DocumentText>(), text => text.Role == DocumentTextRole.Code && text.Language == "csharp");
        Assert.Contains(result.Document.Nodes.OfType<DocumentContainer>(), container => container.Role == DocumentContainerRole.Quote);
        Assert.Contains(result.Document.Nodes.OfType<DocumentContainer>(), container => container.Role == DocumentContainerRole.List);
        Assert.Single(result.Document.Nodes.OfType<DocumentTable>());
        DocumentImage image = Assert.Single(result.Document.Nodes.OfType<DocumentImage>());
        Assert.Equal("chart", image.Description);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, image.Content.ToArray());
        Assert.DoesNotContain("# ", result.Document.Text);
        Assert.Contains("Report", result.Document.Text);
    }

    [Fact]
    public async Task RejectsMixedModalityParagraphInsteadOfDroppingContent()
    {
        using MemoryStream source = new(Encoding.UTF8.GetBytes("prefix ![chart](chart.png) suffix"));

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            new MarkdownReader().ReadAsync(source, "mixed", "text/markdown"));
    }

    [Fact]
    public async Task SupportsFormattedImageDescriptions()
    {
        using MemoryStream source = new(Encoding.UTF8.GetBytes("![*formatted chart*](chart.png)"));

        IngestionDocument result = await new MarkdownReader().ReadAsync(source, "image", "text/markdown");

        Assert.Equal("formatted chart", Assert.Single(result.Document.Nodes.OfType<DocumentImage>()).Description);
    }

    [Fact]
    public async Task SkipsMarkItDownStyleBlankHeaderRow()
    {
        const string Markdown = """
            |  |  |
            | --- | --- |
            | A | B |
            """;
        using MemoryStream source = new(Encoding.UTF8.GetBytes(Markdown));

        IngestionDocument result = await new MarkdownReader().ReadAsync(source, "table", "text/markdown");
        DocumentTable table = Assert.Single(result.Document.Nodes.OfType<DocumentTable>());

        Assert.Equal(1, table.RowCount);
        Assert.Equal("A\tB", DocumentTextProjection.GetText(table));
    }
}
