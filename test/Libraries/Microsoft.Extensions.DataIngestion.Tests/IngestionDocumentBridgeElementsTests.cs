// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Tests;

public class IngestionDocumentBridgeElementsTests
{
    [Fact]
    public void LiteralAndMarkdownConstructionRemainDistinct()
    {
        const string Value = "Use `code` and [links](https://example.test).";

        IngestionDocumentParagraph literal = IngestionDocumentParagraph.FromText(Value);
        IngestionDocumentParagraph markdown = new(Value);

        Assert.Equal(Value, literal.Text);
        Assert.True(literal.IsLiteralText);
        Assert.Equal(@"Use \`code\` and \[links\]\(https://example\.test\)\.", literal.GetMarkdown());
        Assert.Null(markdown.Text);
        Assert.False(markdown.IsLiteralText);
        Assert.Equal(Value, markdown.GetMarkdown());
    }

    [Fact]
    public void CodeBlockUsesCollisionSafeFence()
    {
        IngestionDocumentCodeBlock code = new("Console.WriteLine(\"```\");");

        Assert.Equal("Console.WriteLine(\"```\");", code.Text);
        Assert.StartsWith("````", code.GetMarkdown(), StringComparison.Ordinal);
        Assert.EndsWith("````", code.GetMarkdown(), StringComparison.Ordinal);
    }

    [Fact]
    public void StructuredTablePreservesNestedContentSpansAndRoles()
    {
        IngestionDocumentTable table = new(
            rowCount: 2,
            columnCount: 2,
            cells:
            [
                new(
                    0,
                    0,
                    [
                        IngestionDocumentParagraph.FromText("Region"),
                        new IngestionDocumentCodeBlock("id"),
                    ],
                    rowSpan: 2,
                    kind: "rowHeader"),
                new(0, 1, [IngestionDocumentParagraph.FromText("Value")], kind: "columnHeader"),
                new(1, 1, [IngestionDocumentParagraph.FromText("42")]),
            ]);

        Assert.Equal(3, table.StructuredCells!.Count);
        Assert.Equal(2, table.StructuredCells[0].Elements.Count);
        Assert.Equal(2, table.StructuredCells[0].RowSpan);
        Assert.Equal("rowHeader", table.StructuredCells[0].Kind);
        Assert.IsType<IngestionDocumentSection>(table.Cells[0, 0]);
        Assert.Null(table.Cells[1, 0]);
        Assert.Contains("rowspan=\"2\"", table.GetMarkdown(), StringComparison.Ordinal);
        Assert.Contains("data-kind=\"rowHeader\"", table.GetMarkdown(), StringComparison.Ordinal);
    }

    [Fact]
    public void StructuredTableRejectsOverlappingCells()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            "cells",
            () => new IngestionDocumentTable(
                2,
                2,
                [
                    new(0, 0, [], rowSpan: 2),
                    new(1, 0, []),
                ]));

        Assert.Contains("overlaps", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StructuredTableSnapshotsCellAndNestedElementCollections()
    {
        IngestionDocumentParagraph original = IngestionDocumentParagraph.FromText("Original");
        List<IngestionDocumentElement> nestedElements = [original];
        IngestionDocumentTableCell originalCell = new(0, 0, nestedElements);
        List<IngestionDocumentTableCell> cells = [originalCell];

        IngestionDocumentTable table = new(1, 1, cells);
        nestedElements[0] = IngestionDocumentParagraph.FromText("Replacement");
        cells[0] = new(0, 0, [IngestionDocumentParagraph.FromText("Replacement")]);

        Assert.Same(originalCell, Assert.Single(table.StructuredCells!));
        Assert.Same(original, Assert.Single(table.StructuredCells![0].Elements));
        Assert.Same(original, table.Cells[0, 0]);
        IngestionDocumentElement?[,] returnedGrid = table.Cells;
        returnedGrid[0, 0] = IngestionDocumentParagraph.FromText("Mutated grid");
        Assert.Same(original, table.Cells[0, 0]);
        Assert.Throws<NotSupportedException>(
            () => ((IList<IngestionDocumentTableCell>)table.StructuredCells!)[0] = cells[0]);
        Assert.Throws<NotSupportedException>(
            () => ((IList<IngestionDocumentElement>)originalCell.Elements)[0] = nestedElements[0]);
    }

    [Fact]
    public void ImagesSupportBinaryAndTextOnlyContentWithoutPlaceholderMarkdown()
    {
        IngestionDocumentImage binary = IngestionDocumentImage.FromContent(
            new byte[] { 1, 2, 3 },
            "image/png");
        IngestionDocumentImage described = IngestionDocumentImage.FromText("Architecture *diagram*");

        Assert.Equal(3, binary.Content!.Value.Length);
        Assert.Equal(string.Empty, binary.GetMarkdown());
        Assert.Null(described.Content);
        Assert.Equal("Architecture *diagram*", described.AlternativeText);
        Assert.Equal(@"Architecture \*diagram\*", described.GetMarkdown());
    }

    [Fact]
    public void BinaryImageRejectsEmptyContent()
        => Assert.Throws<ArgumentException>(
            "content",
            () => IngestionDocumentImage.FromContent(ReadOnlyMemory<byte>.Empty, "image/png"));
}
