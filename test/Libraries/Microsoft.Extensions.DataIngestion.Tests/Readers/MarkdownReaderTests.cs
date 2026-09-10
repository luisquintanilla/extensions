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
    public async Task ProducesSharedAuthoredTree()
    {
        const string Markdown = """
            # Report

            literal `code`

            ```csharp
            Console.WriteLine("hello");
            ```

            | Header | Value |
            | --- | --- |
            | A | B |

            ![chart](data:image/png;base64,AQIDBA==)
            """;
        using MemoryStream source = new(Encoding.UTF8.GetBytes(Markdown));

        IngestionDocument result = await new MarkdownReader().ReadAsync(source, "authored", "text/markdown");

        Assert.Contains(result.Document.Nodes.OfType<DocumentText>(), text => text.Role == DocumentTextRole.Heading);
        Assert.Contains(result.Document.Nodes.OfType<DocumentText>(), text => text.Role == DocumentTextRole.Code && text.Language == "csharp");
        Assert.Single(result.Document.Nodes.OfType<DocumentTable>());
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, Assert.Single(result.Document.Nodes.OfType<DocumentImage>()).Content.ToArray());
        Assert.DoesNotContain("# ", result.Document.Text);
    }

    [Fact]
    public async Task RejectsMixedModalityParagraph()
    {
        using MemoryStream source = new(Encoding.UTF8.GetBytes("prefix ![chart](chart.png) suffix"));
        await Assert.ThrowsAsync<NotSupportedException>(() => new MarkdownReader().ReadAsync(source, "mixed", "text/markdown"));
    }
}
