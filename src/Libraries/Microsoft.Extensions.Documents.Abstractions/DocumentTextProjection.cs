// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Extensions.Documents;

/// <summary>Provides the deterministic plain-text projection for semantic document nodes.</summary>
public static class DocumentTextProjection
{
    private const string BlockSeparator = "\n\n";
    private const string ColumnSeparator = "\t";
    private const string RowSeparator = "\n";

    /// <summary>Projects nodes to plain text in reading order.</summary>
    public static string GetText(IEnumerable<DocumentNode> nodes) =>
        string.Join(BlockSeparator, nodes.Select(GetText).Where(static text => text.Length > 0));

    /// <summary>Projects one node to plain text.</summary>
    public static string GetText(DocumentNode node)
    {
        if (node is null)
        {
            throw new System.ArgumentNullException(nameof(node));
        }

        return node switch
        {
            DocumentText text => text.Text,
            DocumentImage image => image.Description ?? string.Empty,
            DocumentContainer container => GetText(container.Children),
            DocumentTable table => GetTableText(table),
            _ => string.Empty,
        };
    }

    private static string GetTableText(DocumentTable table)
    {
        if (table.Cells.Count == 0)
        {
            return string.Empty;
        }

        List<string> rows = new();
        bool hasText = false;
        foreach (IGrouping<int, DocumentTableCell> row in table.Cells
            .OrderBy(static cell => cell.RowIndex)
            .ThenBy(static cell => cell.ColumnIndex)
            .GroupBy(static cell => cell.RowIndex))
        {
            List<string> columns = new();
            foreach (DocumentTableCell cell in row)
            {
                while (columns.Count < cell.ColumnIndex)
                {
                    columns.Add(string.Empty);
                }

                string cellText = GetText(cell.Content);
                columns.Add(cellText);
                hasText |= cellText.Length > 0;

                for (int span = 1; span < cell.ColumnSpan; span++)
                {
                    columns.Add(string.Empty);
                }
            }

            rows.Add(string.Join(ColumnSeparator, columns));
        }

        return hasText ? string.Join(RowSeparator, rows) : string.Empty;
    }
}
