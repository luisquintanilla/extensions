// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace Microsoft.Extensions.Documents;

public class DocumentTests
{
    [Fact]
    public void TreeProjectsTraversesAndLooksUpInOrder()
    {
        Document document = CreateDocument();

        Assert.Equal("Report\n\nRegion\tRevenue\t\n\tQ1\tQ2\n\nChart", document.Text);
        Assert.Equal("Q1", Assert.IsType<DocumentText>(document.GetNode(new("q1"))).Text);
        Assert.Equal(["section", "heading", "table", "cell-region", "region", "cell-revenue", "revenue", "cell-q1", "q1", "cell-q2", "q2", "image"], document.Nodes.Select(node => node.Id.Value));
    }

    [Fact]
    public void PolymorphicTreeRoundTrips()
    {
        Document document = CreateDocument();

        string json = JsonSerializer.Serialize(document);
        Document result = JsonSerializer.Deserialize<Document>(json)!;

        Assert.Contains("\"$type\":\"container\"", json);
        Assert.Contains("\"$type\":\"table\"", json);
        Assert.Contains("\"$type\":\"image\"", json);
        Assert.Equal(document.Text, result.Text);
        Assert.Equal(document.Nodes.Select(node => node.Id), result.Nodes.Select(node => node.Id));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Document>(json.Replace("\"$type\":\"text\"", "\"$type\":\"unknown\"")));
    }

    [Fact]
    public void HierarchyIsClosedAndNeutral()
    {
        ConstructorInfo constructor = Assert.Single(typeof(DocumentNode).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.True(constructor.IsFamilyAndAssembly);
        Assert.All(typeof(DocumentNode).Assembly.GetTypes().Where(type => type.IsSubclassOf(typeof(DocumentNode))), type => Assert.True(type.IsSealed));

        string[] references = typeof(Document).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name!).ToArray();
        Assert.DoesNotContain("Microsoft.Extensions.DataIngestion", references);
        Assert.DoesNotContain("Microsoft.Extensions.DocumentExtraction.Abstractions", references);
        Assert.DoesNotContain("Microsoft.Extensions.VectorData.Abstractions", references);
        Assert.DoesNotContain("Microsoft.Extensions.AI.Abstractions", references);
    }

    [Fact]
    public void TableValidationIsDimensionIndependentAndRejectsOverlap()
    {
        Assert.Empty(new DocumentTable(new("sparse"), int.MaxValue, int.MaxValue, []).Cells);
        Assert.Throws<ArgumentException>(() => new DocumentTable(
            new("overlap"),
            2,
            2,
            [
                new DocumentTableCell(new("a"), 0, 0, [], rowSpan: 2),
                new DocumentTableCell(new("b"), 1, 0, []),
            ]));
    }

    private static Document CreateDocument() =>
        new(
        [
            new DocumentContainer(
                new("section"),
                DocumentContainerRole.Section,
                [
                    new DocumentText(new("heading"), "Report", DocumentTextRole.Heading, level: 1, pageReferences: [new(1)]),
                    new DocumentTable(
                        new("table"),
                        2,
                        3,
                        [
                            new DocumentTableCell(new("cell-region"), 0, 0, [new DocumentText(new("region"), "Region")], rowSpan: 2),
                            new DocumentTableCell(new("cell-revenue"), 0, 1, [new DocumentText(new("revenue"), "Revenue")], columnSpan: 2),
                            new DocumentTableCell(new("cell-q1"), 1, 1, [new DocumentText(new("q1"), "Q1")]),
                            new DocumentTableCell(new("cell-q2"), 1, 2, [new DocumentText(new("q2"), "Q2")]),
                        ]),
                    new DocumentImage(new("image"), new byte[] { 1, 2, 3 }, "image/png", description: "Chart", pageReferences: [new(2)]),
                ]),
        ]);

    [Fact]
    public void NestedNeutralTree_ProjectsTextAndTraversesInStableOrder()
    {
        Document document = CreateDocument();

        Assert.Equal("Report\n\nRegion\tRevenue\t\n\tQ1\tQ2\n\nChart", DocumentTextProjection.GetText(document.Children));
        Assert.Equal(
            [
                typeof(DocumentContainer),
                typeof(DocumentText),
                typeof(DocumentTable),
                typeof(DocumentTableCell),
                typeof(DocumentText),
                typeof(DocumentTableCell),
                typeof(DocumentText),
                typeof(DocumentTableCell),
                typeof(DocumentText),
                typeof(DocumentTableCell),
                typeof(DocumentText),
                typeof(DocumentImage),
            ],
            document.Nodes.Select(static node => node.GetType()));
        Assert.Equal(
            ["section", "heading", "table", "cell-region", "region", "cell-revenue", "revenue", "cell-q1", "q1", "cell-q2", "q2", "image"],
            document.Nodes.Select(static node => node.Id.Value));
        Assert.Equal("Revenue", Assert.IsType<DocumentText>(document.GetNode(new("revenue"))).Text);
    }

    [Fact]
    public void DocumentNodeIdAndPageReference_RoundTripWithoutNormalizingProducerAnnotations()
    {
        System.Collections.Generic.List<DocumentPageReference> pages = [new(2), new(2), new(5)];
        System.Collections.Generic.List<DocumentNodeId> sources = [new("scan-2"), new("scan-2"), new("ocr-7")];
        Document document = new(
        [
            new DocumentText(
                new("paragraph"),
                "Retained provenance",
                pageReferences: pages,
                sourceNodeIds: sources),
        ]);

        pages.Add(new(9));
        sources.Clear();
        Document result = JsonSerializer.Deserialize<Document>(JsonSerializer.Serialize(document))!;
        DocumentText node = Assert.IsType<DocumentText>(result.GetNode(new("paragraph")));

        Assert.Equal(new DocumentNodeId("paragraph"), node.Id);
        Assert.Equal([new DocumentPageReference(2), new DocumentPageReference(2), new DocumentPageReference(5)], node.PageReferences);
        Assert.Equal([new DocumentNodeId("scan-2"), new DocumentNodeId("ocr-7")], node.SourceNodeIds);
        Assert.Equal("Retained provenance", node.Text);
        Assert.Equal("paragraph", node.Id.ToString());
    }

    [Fact]
    public void PolymorphicRoundTrip_PreservesEveryClosedNeutralNodeKind()
    {
        Document document = CreateDocument();

        Document result = JsonSerializer.Deserialize<Document>(JsonSerializer.Serialize(document))!;

        DocumentNode[] expectedNodes = document.Nodes.ToArray();
        DocumentNode[] actualNodes = result.Nodes.ToArray();
        Assert.Equal(expectedNodes.Length, actualNodes.Length);
        for (int index = 0; index < expectedNodes.Length; index++)
        {
            DocumentNode expected = expectedNodes[index];
            DocumentNode actual = actualNodes[index];
            Assert.Equal(expected.GetType(), actual.GetType());
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.PageReferences, actual.PageReferences);
            Assert.Equal(expected.SourceNodeIds, actual.SourceNodeIds);
            AssertNodeProperties(expected, actual);
        }

        Assert.Equal(document.Text, result.Text);
    }

    [Fact]
    public void PublicDocumentModel_RemainsClosedAndNeutral()
    {
        Type[] nodeTypes = typeof(DocumentNode).Assembly
            .GetExportedTypes()
            .Where(static type => type.IsSubclassOf(typeof(DocumentNode)))
            .OrderBy(static type => type.Name)
            .ToArray();

        Assert.Equal(
            [
                typeof(DocumentContainer),
                typeof(DocumentImage),
                typeof(DocumentOpaque),
                typeof(DocumentTable),
                typeof(DocumentTableCell),
                typeof(DocumentText),
            ],
            nodeTypes);
        Assert.All(nodeTypes, static type => Assert.True(type.IsSealed));
        Assert.DoesNotContain(typeof(DocumentNode).Assembly.GetTypes(), static type => type.Name.StartsWith("IngestionDocument", StringComparison.Ordinal));
    }

    [Fact]
    public void OpaqueNodeRoundTripsAsVersionedNonTextExtension()
    {
        using JsonDocument payloadDocument = JsonDocument.Parse("""{"providerValue":42,"items":["a","b"]}""");
        DocumentOpaque opaque = new(
            new("extension"),
            "vendor.layout.figure",
            schemaVersion: 3,
            position: 7,
            payloadDocument.RootElement,
            pageReferences: [new(4), new(2)],
            sourceNodeIds: [new("provider-node")]);

        Document document = new([new DocumentText(new("before"), "before"), opaque, new DocumentText(new("after"), "after")]);
        string json = JsonSerializer.Serialize(document);
        Document roundTripped = JsonSerializer.Deserialize<Document>(json)!;
        DocumentOpaque result = Assert.IsType<DocumentOpaque>(roundTripped.GetNode(new("extension")));

        Assert.Contains("\"$type\":\"opaque\"", json);
        Assert.Equal("before\n\nafter", roundTripped.Text);
        Assert.Equal("vendor.layout.figure", result.LogicalKind);
        Assert.Equal(3, result.SchemaVersion);
        Assert.Equal(7, result.Position);
        Assert.Equal(payloadDocument.RootElement.GetRawText(), result.Payload.GetRawText());
        Assert.Equal([new DocumentPageReference(4), new DocumentPageReference(2)], result.PageReferences);
        Assert.Equal([new DocumentNodeId("provider-node")], result.SourceNodeIds);
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Document>(json.Replace("\"$type\":\"opaque\"", "\"$type\":\"unsupported\"")));
    }

    private static void AssertNodeProperties(DocumentNode expected, DocumentNode actual)
    {
        switch (expected)
        {
            case DocumentContainer expectedContainer:
                DocumentContainer actualContainer = Assert.IsType<DocumentContainer>(actual);
                Assert.Equal(expectedContainer.Role, actualContainer.Role);
                Assert.Equal(
                    expectedContainer.Children.Select(static child => child.Id),
                    actualContainer.Children.Select(static child => child.Id));
                break;

            case DocumentText expectedText:
                DocumentText actualText = Assert.IsType<DocumentText>(actual);
                Assert.Equal(expectedText.Text, actualText.Text);
                Assert.Equal(expectedText.Role, actualText.Role);
                Assert.Equal(expectedText.Level, actualText.Level);
                Assert.Equal(expectedText.Language, actualText.Language);
                break;

            case DocumentTable expectedTable:
                DocumentTable actualTable = Assert.IsType<DocumentTable>(actual);
                Assert.Equal(expectedTable.RowCount, actualTable.RowCount);
                Assert.Equal(expectedTable.ColumnCount, actualTable.ColumnCount);
                Assert.Equal(
                    expectedTable.Cells.Select(static cell => cell.Id),
                    actualTable.Cells.Select(static cell => cell.Id));
                break;

            case DocumentTableCell expectedCell:
                DocumentTableCell actualCell = Assert.IsType<DocumentTableCell>(actual);
                Assert.Equal(expectedCell.RowIndex, actualCell.RowIndex);
                Assert.Equal(expectedCell.ColumnIndex, actualCell.ColumnIndex);
                Assert.Equal(expectedCell.RowSpan, actualCell.RowSpan);
                Assert.Equal(expectedCell.ColumnSpan, actualCell.ColumnSpan);
                Assert.Equal(expectedCell.Role, actualCell.Role);
                Assert.Equal(
                    expectedCell.Content.Select(static child => child.Id),
                    actualCell.Content.Select(static child => child.Id));
                break;

            case DocumentImage expectedImage:
                DocumentImage actualImage = Assert.IsType<DocumentImage>(actual);
                Assert.Equal(expectedImage.Content.ToArray(), actualImage.Content.ToArray());
                Assert.Equal(expectedImage.MediaType, actualImage.MediaType);
                Assert.Equal(expectedImage.Source, actualImage.Source);
                Assert.Equal(expectedImage.Description, actualImage.Description);
                break;

            case DocumentOpaque expectedOpaque:
                DocumentOpaque actualOpaque = Assert.IsType<DocumentOpaque>(actual);
                Assert.Equal(expectedOpaque.LogicalKind, actualOpaque.LogicalKind);
                Assert.Equal(expectedOpaque.SchemaVersion, actualOpaque.SchemaVersion);
                Assert.Equal(expectedOpaque.Position, actualOpaque.Position);
                Assert.Equal(expectedOpaque.Payload, actualOpaque.Payload);
                break;

            default:
                throw new InvalidOperationException($"Unexpected document node type: {expected.GetType()}.");
        }
    }
}
