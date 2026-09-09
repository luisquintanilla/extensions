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
}
