// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Extensions.DocumentExtraction;

internal static class DocumentTextProjection
{
    private const string ElementSeparator = "\n\n";
    private const string TableColumnSeparator = "\t";
    private const string TableRowSeparator = "\n";

    public static string GetText(IReadOnlyList<DocumentElement> elements) =>
        string.Join(
            ElementSeparator,
            elements.Select(GetElementText).Where(static text => text.Length > 0));

    private static string GetElementText(DocumentElement element) =>
        element switch
        {
            DocumentBlock block => block.Text,
            DocumentImage image => image.Caption ?? string.Empty,
            DocumentTable table => GetTableText(table),
            _ => string.Empty,
        };

    private static string GetTableText(DocumentTable table)
    {
        if (table.Cells is null)
        {
            return string.Empty;
        }

        List<string> rows = [];
        bool hasText = false;

        foreach (IGrouping<int, DocumentTableCell> row in table.Cells
            .OrderBy(static cell => cell.RowIndex)
            .ThenBy(static cell => cell.ColumnIndex)
            .GroupBy(static cell => cell.RowIndex))
        {
            List<string> columns = [];
            foreach (DocumentTableCell cell in row)
            {
                while (columns.Count < cell.ColumnIndex)
                {
                    columns.Add(string.Empty);
                }

                string cellText = GetText(cell.Elements);
                columns.Add(cellText);
                hasText |= cellText.Length > 0;

                for (int span = 1; span < cell.ColumnSpan; span++)
                {
                    columns.Add(string.Empty);
                }
            }

            rows.Add(string.Join(TableColumnSeparator, columns));
        }

        return hasText ? string.Join(TableRowSeparator, rows) : string.Empty;
    }
}
