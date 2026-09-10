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

    [Fact]
    public void EvidenceIsIndexedByMergedDocumentNodeIdentity()
    {
        DocumentText first = new(new("first"), "one");
        DocumentText second = new(new("second"), "two");
        object raw = new();
        DocumentExtractionResult result = new(
        [
            new DocumentPage(2, new Document([second]), evidence: [new(second.Id) { RawRepresentation = raw }]),
            new DocumentPage(1, new Document([first]), evidence: [new(first.Id) { Confidence = 0.8 }]),
        ]);

        Assert.True(result.TryGetEvidence(second.Id, out DocumentExtractionEvidence? evidence));
        Assert.Same(raw, evidence!.RawRepresentation);
        Assert.Equal([first.Id, second.Id], result.Evidence.Keys.OrderBy(static id => id.Value));
        Assert.False(result.TryGetEvidence(new("missing"), out _));
        Assert.Null(typeof(DocumentNode).GetProperty("BoundingRegion"));
    }

    [Fact]
    public void PageAndNodeEvidencePreserveProviderGeometryAndProperties()
    {
        DocumentText node = new(new("geometry"), "located");
        object pageRaw = new();
        object nodeRaw = new();
        DocumentExtractionEvidence evidence = new(node.Id)
        {
            BoundingRegion = DocumentBoundingRegion.FromRectangle(3, 10, 20, 110, 220),
            Confidence = 0.75,
            RawRepresentation = nodeRaw,
            AdditionalProperties = new() { ["providerNode"] = "value" },
        };
        DocumentPage page = new(3, new Document([node]), evidence: [evidence])
        {
            Dimensions = new(612, 792),
            CoordinateUnit = DocumentCoordinateUnit.Point,
            CoordinateOrigin = DocumentCoordinateOrigin.BottomLeft,
            RawRepresentation = pageRaw,
            AdditionalProperties = new() { ["providerPage"] = "value" },
        };

        DocumentExtractionResult result = new([page]);

        Assert.Equal(new DocumentPageDimensions(612, 792), result.Pages[0].Dimensions);
        Assert.Equal(DocumentCoordinateUnit.Point, result.Pages[0].CoordinateUnit);
        Assert.Equal(DocumentCoordinateOrigin.BottomLeft, result.Pages[0].CoordinateOrigin);
        Assert.Same(pageRaw, result.Pages[0].RawRepresentation);
        Assert.Equal("value", result.Pages[0].AdditionalProperties!["providerPage"]);
        Assert.True(result.TryGetEvidence(node.Id, out DocumentExtractionEvidence? indexed));
        Assert.Equal(0.75, indexed!.Confidence);
        Assert.Equal(3, indexed.BoundingRegion!.PageNumber);
        Assert.Same(nodeRaw, indexed.RawRepresentation);
        Assert.Equal("value", indexed.AdditionalProperties!["providerNode"]);
    }
}
