using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Documents;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace AIChatWeb_CSharp.Web.Services.Ingestion;

internal sealed class PdfPigReader : IngestionDocumentReader
{
    public override Task<IngestionDocument> ReadAsync(Stream source, string identifier, string mediaType, CancellationToken cancellationToken = default)
    {
        using var pdf = PdfDocument.Open(source);
        var pages = new List<DocumentNode>();
        foreach (var page in pdf.GetPages())
        {
            pages.Add(GetPageSection(page));
        }
        return Task.FromResult(new IngestionDocument(identifier, new Document(pages)));
    }

    private static DocumentContainer GetPageSection(Page pdfPage)
    {
        var letters = pdfPage.Letters;
        var words = NearestNeighbourWordExtractor.Instance.GetWords(letters);
        var blocks = new List<DocumentNode>();
        var blockIndex = 0;
        foreach (var textBlock in DocstrumBoundingBoxes.Instance.GetBlocks(words))
        {
            blocks.Add(new DocumentText(
                new($"page-{pdfPage.Number}-block-{blockIndex++}"),
                textBlock.Text,
                pageReferences: [new(pdfPage.Number)]));
        }

        return new DocumentContainer(
            new($"page-{pdfPage.Number}"),
            DocumentContainerRole.Section,
            blocks,
            pageReferences: [new(pdfPage.Number)]);
    }
}
