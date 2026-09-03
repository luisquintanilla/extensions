// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DataIngestion.Chunkers;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Microsoft.ML.Tokenizers;
using Xunit;

namespace Microsoft.Extensions.DataIngestion;

public class DocumentExtractionReaderTests
{
    [Fact]
    public async Task PreservesSharedSemanticsWhileLeavingEvidenceAndMarkdownOnExtractionEnvelope()
    {
        object raw = new();
        DocumentText title = new(new("title"), "*Report*", DocumentTextRole.Heading, 1, pageReferences: [new(1)]);
        DocumentText code = new(new("code"), "Console.WriteLine(`literal`);", DocumentTextRole.Code, language: "csharp", pageReferences: [new(1)]);
        DocumentTable table = CreateTable();
        DocumentImage image = new(new("image"), new byte[] { 1, 2, 3, 4 }, "image/png", pageReferences: [new(2)]);
        DocumentExtractionEvidence evidence = new(code.Id)
        {
            Confidence = 0.91,
            BoundingRegion = DocumentBoundingRegion.FromRectangle(1, 0, 0, 10, 10),
            RawRepresentation = raw,
        };
        DocumentExtractionResult extractionResult = new(
        [
            new DocumentPage(
                1,
                new Document([title, code]),
                markdown: "# Provider report\r\n```csharp\r\nprovider\r\n```",
                evidence: [evidence]),
            new DocumentPage(2, new Document([table, image]), markdown: null),
        ]);
        using StubClient client = new(extractionResult);
        DocumentExtractionReader reader = new(client);

        IngestionDocument ingestion = await reader.ReadAsync(new MemoryStream([1]), "fixture", "application/pdf");

        Assert.Same(extractionResult.Document, ingestion.Document);
        Assert.Equal("*Report*\n\nConsole.WriteLine(`literal`);\n\nRegion\tRevenue\t\n\tQ1\tQ2", ingestion.Document.Text);
        Assert.DoesNotContain("Provider report", ingestion.Document.Text);
        Assert.Equal([1, 2], ingestion.Document.Nodes.SelectMany(node => node.PageReferences).Select(reference => reference.PageNumber).Distinct());
        Assert.Null(typeof(DocumentNode).GetProperty("Confidence"));
        Assert.Same(raw, evidence.RawRepresentation);
        Assert.Equal("# Provider report\r\n```csharp\r\nprovider\r\n```", extractionResult.Pages[0].Markdown);
    }

    [Fact]
    public async Task SharedTreeFlowsDirectlyThroughChunkingWithTypedProvenance()
    {
        DocumentExtractionResult extractionResult = new(
        [
            new DocumentPage(
                1,
                new Document(
                [
                    new DocumentContainer(
                        new("section"),
                        DocumentContainerRole.Section,
                        [
                            new DocumentText(new("heading"), "Report", DocumentTextRole.Heading, 1, pageReferences: [new(1)]),
                            new DocumentText(new("body"), "literal body", pageReferences: [new(1)]),
                        ]),
                ])),
        ]);
        using StubClient client = new(extractionResult);
        IngestionDocument ingestion = await new DocumentExtractionReader(client)
            .ReadAsync(new MemoryStream([1]), "fixture", "application/pdf");
        SectionChunker chunker = new(new(TiktokenTokenizer.CreateForModel("gpt-4")) { MaxTokensPerChunk = 100 });

        IngestionChunk<string> chunk = Assert.Single(await chunker.ProcessAsync(ingestion).ToListAsync());

        Assert.Equal("Report\nliteral body", chunk.Content);
        Assert.Equal([1], chunk.PageNumbers);
        Assert.Equal(["body", "heading"], chunk.SourceNodeIds.Select(id => id.Value).OrderBy(id => id));
    }

    [Fact]
    public void DependencyDirectionKeepsCoreMediIndependentFromExtraction()
    {
        string[] references = typeof(IngestionPipeline<>).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name!).ToArray();

        Assert.DoesNotContain("Microsoft.Extensions.DocumentExtraction.Abstractions", references);
        Assert.Contains("Microsoft.Extensions.Documents.Abstractions", references);
    }

    private static DocumentTable CreateTable() =>
        new(
            new("table"),
            2,
            3,
            [
                new DocumentTableCell(0, 0, [new DocumentText(new("region"), "Region")], rowSpan: 2, role: DocumentTableCellRole.RowHeader),
                new DocumentTableCell(0, 1, [new DocumentText(new("revenue"), "Revenue")], columnSpan: 2, role: DocumentTableCellRole.ColumnHeader),
                new DocumentTableCell(1, 1, [new DocumentText(new("q1"), "Q1")]),
                new DocumentTableCell(1, 2, [new DocumentText(new("q2"), "Q2")]),
            ],
            pageReferences: [new(2)]);

    private sealed class StubClient : IDocumentExtractionClient
    {
        private readonly DocumentExtractionResult _result;

        public StubClient(DocumentExtractionResult result)
        {
            _result = result;
        }

        public Task<DocumentExtractionResult> ExtractAsync(
            Stream document,
            string mediaType,
            DocumentExtractionOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);

        public IAsyncEnumerable<DocumentExtractionPageResult> ExtractPagesAsync(
            Stream document,
            string mediaType,
            DocumentExtractionOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
