// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using Xunit;

namespace Microsoft.Extensions.Documents;

public class DocumentTests
{
    [Fact]
    public void TreePreservesStructureAndProjectsDeterministicText()
    {
        Document document = new(
        [
            new DocumentContainer(
                new("section"),
                DocumentContainerRole.Section,
                [
                    new DocumentText(new("heading"), "*Report*", DocumentTextRole.Heading, level: 1),
                    new DocumentContainer(
                        new("list"),
                        DocumentContainerRole.List,
                        [
                            new DocumentContainer(
                                new("item"),
                                DocumentContainerRole.ListItem,
                                [new DocumentText(new("paragraph"), "Use `code` literally.")]),
                        ]),
                    new DocumentTable(
                        new("table"),
                        2,
                        3,
                        [
                            new DocumentTableCell(new("cell-region"), 0, 0, [new DocumentText(new("region"), "Region")], rowSpan: 2, role: DocumentTableCellRole.RowHeader),
                            new DocumentTableCell(new("cell-revenue"), 0, 1, [new DocumentText(new("revenue"), "Revenue")], columnSpan: 2, role: DocumentTableCellRole.ColumnHeader),
                            new DocumentTableCell(new("cell-q1"), 1, 1, [new DocumentText(new("q1"), "Q1")]),
                            new DocumentTableCell(new("cell-q2"), 1, 2, [new DocumentText(new("q2"), "Q2")]),
                        ]),
                    new DocumentImage(new("image"), new byte[] { 1, 2, 3 }, "image/png", description: "Chart"),
                ]),
        ]);

        Assert.Equal("*Report*\n\nUse `code` literally.\n\nRegion\tRevenue\t\n\tQ1\tQ2\n\nChart", document.Text);
        Assert.Equal("Q1", Assert.IsType<DocumentText>(document.GetNode(new("q1"))).Text);
        Assert.Equal(15, document.Nodes.Count);
    }

    [Fact]
    public void PageReferencesAndSourceIdsAreTypedAndImmutable()
    {
        DocumentText node = new(
            new("derived"),
            "cross page",
            pageReferences: [new(2), new(1), new(2)],
            sourceNodeIds: [new("source-1"), new("source-2")]);
        Document document = new([node]);

        Assert.Equal([2, 1], node.PageReferences.Select(reference => reference.PageNumber));
        Assert.Equal(["source-1", "source-2"], node.SourceNodeIds.Select(id => id.Value));
        Assert.Equal("cross page", document.Text);
    }

    [Fact]
    public void DuplicateNodeIdsAreRejectedAcrossTableContent()
    {
        DocumentTable table = new(
            new("table"),
            1,
            1,
            [new DocumentTableCell(new("cell"), 0, 0, [new DocumentText(new("duplicate"), "cell")])]);

        Assert.Throws<ArgumentException>("children", () =>
            new Document([new DocumentText(new("duplicate"), "root"), table]));
    }

    [Fact]
    public void TableCellsSupportNestedSemanticContentAndSpans()
    {
        DocumentTable nested = new(
            new("nested"),
            1,
            1,
            [new DocumentTableCell(new("nested-cell"), 0, 0, [new DocumentText(new("nested-text"), "inside")])]);
        DocumentTable outer = new(
            new("outer"),
            2,
            2,
            [
                new DocumentTableCell(new("outer-cell-00"), 0, 0, [nested], rowSpan: 2),
                new DocumentTableCell(new("outer-cell-01"), 0, 1, [new DocumentText(new("header"), "value")], role: DocumentTableCellRole.ColumnHeader),
                new DocumentTableCell(new("outer-cell-11"), 1, 1, [new DocumentText(new("cell"), "tail")]),
            ]);

        Document document = new([outer]);

        Assert.Equal("inside\tvalue\n\ttail", document.Text);
        Assert.Same(nested, document.GetNode(new("nested")));
    }

    [Fact]
    public void BinaryImageIsDefensivelyCopiedAndCaptionless()
    {
        byte[] bytes = [1, 2, 3];
        DocumentImage image = new(new("image"), bytes, "image/png");
        bytes[0] = 9;
        byte[] returned = image.Content.ToArray();
        returned[1] = 9;

        Assert.Equal([1, 2, 3], image.Content.ToArray());
        Assert.Equal(string.Empty, new Document([image]).Text);
    }

    [Fact]
    public void RejectsDefaultIdentifiersAndOverflowingCells()
    {
        Assert.Throws<ArgumentException>("id", () => new DocumentText(default, "text"));
        Assert.Throws<ArgumentException>("pageReferences", () =>
            new DocumentText(new("text"), "text", pageReferences: [default]));
        Assert.Throws<ArgumentException>("sourceNodeIds", () =>
            new DocumentText(new("text"), "text", sourceNodeIds: [default]));
        Assert.Throws<ArgumentException>("Cells", () =>
            new DocumentTable(
                new("table"),
                1,
                1,
                [new DocumentTableCell(new("cell"), int.MaxValue, 0, [])]));
    }

    [Fact]
    public void CellsAreNodesAndTraversalUsesRowColumnOrder()
    {
        DocumentTableCell second = new(new("second"), 0, 1, [new DocumentText(new("b"), "B")]);
        DocumentTableCell first = new(new("first"), 0, 0, [new DocumentText(new("a"), "A")]);
        Document document = new([new DocumentTable(new("table"), 1, 2, [second, first])]);

        Assert.Equal(["table", "first", "a", "second", "b"], document.Nodes.Select(node => node.Id.Value));
    }

    [Fact]
    public void NeutralPackageHasNoProductDependencies()
    {
        string[] references = typeof(Document).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name!).ToArray();

        Assert.DoesNotContain("Microsoft.Extensions.DataIngestion", references);
        Assert.DoesNotContain("Microsoft.Extensions.DocumentExtraction.Abstractions", references);
        Assert.DoesNotContain("Microsoft.Extensions.VectorData.Abstractions", references);
        Assert.DoesNotContain("Microsoft.Extensions.AI.Abstractions", references);
    }
}
