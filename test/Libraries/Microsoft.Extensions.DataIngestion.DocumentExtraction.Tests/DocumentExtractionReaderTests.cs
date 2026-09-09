// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DocumentExtraction;
using Xunit;

namespace Microsoft.Extensions.DataIngestion;

public class DocumentExtractionReaderTests
{
    [Fact]
    public async Task MapsCanonicalElementsAndDropsExtractionEvidence()
    {
        DocumentExtractionResult result = DocumentExtractionBridgeFixture.Create();
        TestDocumentExtractionClient client = CreateClient(result, out Func<DocumentExtractionOptions?> getOptions);
        DocumentExtractionOptions extractionOptions = new() { ModelId = "model-before-reader" };
        DocumentExtractionReader reader = new(
            client,
            new() { ExtractionOptions = extractionOptions });
        extractionOptions.ModelId = "mutated-after-reader";

        IngestionDocument document = await reader.ReadAsync(
            new MemoryStream([1, 2, 3]),
            "technical-review.pdf",
            "application/pdf");

        Assert.Equal("model-before-reader", getOptions()!.ModelId);
        Assert.Equal([1, 2], document.Sections.Select(section => section.PageNumber));
        Assert.Equal(6, document.Sections[0].Elements.Count);
        Assert.Equal(2, document.Sections[1].Elements.Count);

        IngestionDocumentHeader title = Assert.IsType<IngestionDocumentHeader>(
            document.Sections[0].Elements[0]);
        Assert.Equal("Quarterly *Report*", title.Text);
        Assert.Equal(@"Quarterly \*Report\*", title.GetMarkdown());

        IngestionDocumentCodeBlock code = Assert.IsType<IngestionDocumentCodeBlock>(
            document.Sections[0].Elements[2]);
        Assert.StartsWith("````", code.GetMarkdown(), StringComparison.Ordinal);

        IngestionDocumentTable table = Assert.IsType<IngestionDocumentTable>(
            document.Sections[0].Elements[3]);
        IngestionDocumentTableCell rowHeader = table.StructuredCells!.Single(
            cell => cell.RowIndex == 0 && cell.ColumnIndex == 0);
        Assert.Equal(2, rowHeader.RowSpan);
        Assert.Equal("rowHeader", rowHeader.Kind);
        Assert.Equal(3, rowHeader.Elements.Count);
        IngestionDocumentTable nestedTable = Assert.IsType<IngestionDocumentTable>(
            rowHeader.Elements[1]);
        Assert.Equal(
            "Nested value",
            nestedTable.StructuredCells![0].Elements[0].Text);
        IngestionDocumentImage nestedImage = Assert.IsType<IngestionDocumentImage>(
            rowHeader.Elements[2]);
        Assert.Equal(3, nestedImage.Content!.Value.Length);
        Assert.Equal("Nested chart", nestedImage.AlternativeText);

        IngestionDocumentImage topLevelImage = Assert.IsType<IngestionDocumentImage>(
            document.Sections[0].Elements[4]);
        Assert.Equal(4, topLevelImage.Content!.Value.Length);
        Assert.Equal("image/png", topLevelImage.MediaType);

        Assert.All(document.Sections[0].Elements, element => Assert.Equal(1, element.PageNumber));
        Assert.All(document.Sections[1].Elements, element => Assert.Equal(2, element.PageNumber));
        Assert.All(document.EnumerateContent(), element => Assert.False(element.HasMetadata));
        Assert.DoesNotContain(
            DocumentExtractionBridgeFixture.ProviderMarkdown,
            string.Join("\n", document.EnumerateContent().Select(element => element.GetMarkdown())),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MarkdownOnlyPageRequiresExplicitPolicy()
    {
        const string Markdown = "## Supplement\n\nExact **provider Markdown**.";
        DocumentExtractionResult result = new([new DocumentPage(4, [], Markdown)]);

        DocumentExtractionReader strictReader = new(CreateClient(result, out _));
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => strictReader.ReadAsync(
                new MemoryStream([1]),
                "document",
                "application/pdf"));
        Assert.Contains("page 4", exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(MarkdownOnlyPagePolicy.PreserveAsMarkdown), exception.Message, StringComparison.Ordinal);

        DocumentExtractionReader preservingReader = new(
            CreateClient(result, out _),
            new() { MarkdownOnlyPagePolicy = MarkdownOnlyPagePolicy.PreserveAsMarkdown });
        IngestionDocument document = await preservingReader.ReadAsync(
            new MemoryStream([1]),
            "document",
            "application/pdf");

        IngestionDocumentParagraph paragraph = Assert.IsType<IngestionDocumentParagraph>(
            Assert.Single(document.Sections[0].Elements));
        Assert.Null(paragraph.Text);
        Assert.Equal(Markdown, paragraph.GetMarkdown());
        Assert.Equal(4, paragraph.PageNumber);
    }

    [Fact]
    public async Task ContentlessImageFailsWithPageContext()
    {
        DocumentExtractionReader reader = new(
            CreateClient(
                new([new DocumentPage(3, [new DocumentImage()])]),
                out _));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.ReadAsync(
                new MemoryStream([1]),
                "document",
                "application/pdf"));

        Assert.Contains("page 3", exception.Message, StringComparison.Ordinal);
        Assert.Contains("neither non-empty content nor a caption", exception.Message, StringComparison.Ordinal);
    }

    private static TestDocumentExtractionClient CreateClient(
        DocumentExtractionResult result,
        out Func<DocumentExtractionOptions?> getOptions)
    {
        DocumentExtractionOptions? capturedOptions = null;
        getOptions = () => capturedOptions;
        return new()
        {
            ExtractAsyncCallback = (_, _, options, _) =>
            {
                capturedOptions = options;
                return Task.FromResult(result);
            },
        };
    }
}
