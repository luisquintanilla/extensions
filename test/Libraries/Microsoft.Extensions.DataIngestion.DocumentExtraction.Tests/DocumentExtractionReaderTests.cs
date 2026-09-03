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
    public async Task MapsCanonicalElementsAndDropsProviderEvidence()
    {
        DocumentExtractionResult result = DocumentExtractionBridgeFixture.Create();
        TestDocumentExtractionClient client = CreateClient(result);
        DocumentExtractionOptions extractionOptions = new()
        {
            ModelId = "model-before-reader",
        };
        DocumentExtractionReader reader = new(
            client,
            new()
            {
                ExtractionOptions = extractionOptions,
            });
        extractionOptions.ModelId = "mutated-after-reader";

        using MemoryStream source = new([1, 2, 3]);
        IngestionDocument document = await reader.ReadAsync(
            source,
            "technical-review.pdf",
            "application/pdf");

        DocumentExtractionOptions capturedOptions = Assert.IsType<DocumentExtractionOptions>(
            client.GetService(typeof(DocumentExtractionOptions)));
        Assert.Equal("model-before-reader", capturedOptions.ModelId);
        Assert.Equal([1, 2], document.Sections.Select(section => section.PageNumber));
        Assert.Equal(6, document.Sections[0].Elements.Count);
        Assert.Equal(2, document.Sections[1].Elements.Count);

        IngestionDocumentHeader title = Assert.IsType<IngestionDocumentHeader>(
            document.Sections[0].Elements[0]);
        Assert.Equal("Quarterly *Report*", title.Text);
        Assert.Equal(@"Quarterly \*Report\*", title.GetMarkdown());

        IngestionDocumentParagraph paragraph = Assert.IsType<IngestionDocumentParagraph>(
            document.Sections[0].Elements[1]);
        Assert.Contains("`VectorStoreWriter<TRecord>`", paragraph.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(
            DocumentExtractionBridgeFixture.PageOneMarkdown,
            document.Sections[0].Elements.Select(element => element.GetMarkdown()));

        IngestionDocumentCodeBlock code = Assert.IsType<IngestionDocumentCodeBlock>(
            document.Sections[0].Elements[2]);
        Assert.StartsWith("````", code.GetMarkdown(), StringComparison.Ordinal);

        IngestionDocumentTable table = Assert.IsType<IngestionDocumentTable>(
            document.Sections[0].Elements[3]);
        IngestionDocumentTableCell rowHeader = table.StructuredCells!.Single(
            cell => cell.RowIndex == 0 && cell.ColumnIndex == 0);
        Assert.Equal(2, rowHeader.RowSpan);
        Assert.Equal("rowHeader", rowHeader.Kind);
        Assert.Equal(2, rowHeader.Elements.Count);
        Assert.IsType<IngestionDocumentCodeBlock>(rowHeader.Elements[1]);
        Assert.Contains("rowspan=\"2\"", table.GetMarkdown(), StringComparison.Ordinal);

        IngestionDocumentImage image = Assert.IsType<IngestionDocumentImage>(
            document.Sections[0].Elements[4]);
        Assert.Equal(8, image.Content!.Value.Length);
        Assert.Equal("image/png", image.MediaType);
        Assert.Null(image.AlternativeText);
        Assert.Equal(string.Empty, image.GetMarkdown());

        IngestionDocumentParagraph unknown = Assert.IsType<IngestionDocumentParagraph>(
            document.Sections[0].Elements[5]);
        Assert.Equal("Provider-specific note", unknown.Text);

        Assert.All(document.Sections[0].Elements, element => Assert.Equal(1, element.PageNumber));
        Assert.All(document.Sections[1].Elements, element => Assert.Equal(2, element.PageNumber));
        Assert.All(document.EnumerateContent(), element => Assert.False(element.HasMetadata));
        Assert.All(rowHeader.Elements, element =>
        {
            Assert.Equal(1, element.PageNumber);
            Assert.False(element.HasMetadata);
        });
    }

    [Fact]
    public async Task RequiresElementsForMarkdownOnlyPageByDefault()
    {
        DocumentExtractionReader reader = new(CreateClient(new(
        [
            new DocumentPage(4, [], "**provider Markdown**"),
        ])));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.ReadAsync(new MemoryStream([1]), "document", "application/pdf"));

        Assert.Contains("page 4", exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            nameof(MarkdownOnlyPagePolicy.PreserveAsMarkdown),
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplicitPolicyPreservesProviderMarkdownWithoutLiteralizingIt()
    {
        const string Markdown = "**provider Markdown**";
        DocumentExtractionReader reader = new(
            CreateClient(new([new DocumentPage(4, [], Markdown)])),
            new()
            {
                MarkdownOnlyPagePolicy = MarkdownOnlyPagePolicy.PreserveAsMarkdown,
            });

        IngestionDocument document = await reader.ReadAsync(
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
    public async Task EmptyPageMapsToEmptySection()
    {
        DocumentExtractionReader reader = new(
            CreateClient(new([new DocumentPage(1, [])])));

        IngestionDocument document = await reader.ReadAsync(
            new MemoryStream([1]),
            "document",
            "application/pdf");

        Assert.Empty(Assert.Single(document.Sections).Elements);
    }

    [Fact]
    public async Task CanonicalElementsTakePrecedenceOverPartialProviderMarkdown()
    {
        DocumentExtractionReader reader = new(
            CreateClient(new(
            [
                new DocumentPage(
                    1,
                    [new DocumentBlock("Canonical text")],
                    "**duplicate provider Markdown**"),
            ])));

        IngestionDocument document = await reader.ReadAsync(
            new MemoryStream([1]),
            "document",
            "application/pdf");

        IngestionDocumentParagraph paragraph = Assert.IsType<IngestionDocumentParagraph>(
            Assert.Single(document.Sections[0].Elements));
        Assert.Equal("Canonical text", paragraph.Text);
        Assert.DoesNotContain("duplicate", paragraph.GetMarkdown(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MapsMarkdownOnlyTableThroughExplicitMarkdownPath()
    {
        const string Markdown = "| A |\n| - |\n| B |";
        DocumentExtractionReader reader = new(
            CreateClient(new(
            [
                new DocumentPage(
                    1,
                    [new DocumentTable(0, 0, markdownRepresentation: Markdown)]),
            ])));

        IngestionDocument document = await reader.ReadAsync(
            new MemoryStream([1]),
            "document",
            "application/pdf");

        IngestionDocumentTable table = Assert.IsType<IngestionDocumentTable>(
            Assert.Single(document.Sections[0].Elements));
        Assert.Null(table.StructuredCells);
        Assert.Equal(Markdown, table.GetMarkdown());
    }

    [Fact]
    public async Task MapsCaptionedAndCaptionOnlyImages()
    {
        DocumentExtractionReader reader = new(
            CreateClient(new(
            [
                new DocumentPage(
                    1,
                    [
                        new DocumentImage
                        {
                            Content = new byte[] { 1, 2, 3 },
                            MediaType = "image/png",
                            Caption = "Captioned image",
                        },
                        new DocumentImage
                        {
                            Caption = "Description-only image",
                        },
                    ]),
            ])));

        IngestionDocument document = await reader.ReadAsync(
            new MemoryStream([1]),
            "document",
            "application/pdf");
        IngestionDocumentImage[] images = document.EnumerateContent()
            .OfType<IngestionDocumentImage>()
            .ToArray();

        Assert.Equal(3, images[0].Content!.Value.Length);
        Assert.Equal("Captioned image", images[0].AlternativeText);
        Assert.Null(images[1].Content);
        Assert.Equal("Description-only image", images[1].AlternativeText);
    }

    [Fact]
    public async Task FailsExplicitlyForContentlessImage()
    {
        DocumentExtractionReader reader = new(
            CreateClient(new(
            [
                new DocumentPage(3, [new DocumentImage()]),
            ])));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.ReadAsync(new MemoryStream([1]), "document", "application/pdf"));

        Assert.Contains("page 3", exception.Message, StringComparison.Ordinal);
        Assert.Contains("neither non-empty content nor a caption", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailsExplicitlyForUnsupportedElement()
    {
        DocumentExtractionReader reader = new(
            CreateClient(new(
            [
                new DocumentPage(2, [new UnsupportedDocumentElement()]),
            ])));

        NotSupportedException exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => reader.ReadAsync(new MemoryStream([1]), "document", "application/pdf"));

        Assert.Contains("page 2", exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(UnsupportedDocumentElement), exception.Message, StringComparison.Ordinal);
    }

    private static TestDocumentExtractionClient CreateClient(DocumentExtractionResult result)
    {
        DocumentExtractionOptions? capturedOptions = null;
        OptionsServiceProvider services = new(() => capturedOptions);
        TestDocumentExtractionClient client = new()
        {
            Services = services,
            GetServiceCallback = (serviceType, _) => services.GetService(serviceType),
            ExtractAsyncCallback = (_, _, options, _) =>
            {
                capturedOptions = options;
                return Task.FromResult(result);
            },
        };
        return client;
    }

    private sealed class UnsupportedDocumentElement : DocumentElement;

    private sealed class OptionsServiceProvider(Func<DocumentExtractionOptions?> getOptions)
        : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(DocumentExtractionOptions) ? getOptions() : null;
    }
}
