// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Extensions.DataIngestion;

#pragma warning disable IDE0058 // Expression value is never used
#pragma warning disable CA1307 // These literal replacements are ordinal on all target frameworks.

internal static class MarkdownProjection
{
    internal static string EscapeLiteral(string value)
    {
        StringBuilder builder = new(value.Length);
        foreach (char character in value)
        {
            if (character is '\\' or '`' or '*' or '_' or '{' or '}' or '[' or ']'
                or '<' or '>' or '(' or ')' or '#' or '+' or '-' or '.' or '!' or '|')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    internal static string CreateCodeFence(string code)
    {
        int longestRun = 0;
        int currentRun = 0;
        foreach (char character in code)
        {
            if (character == '`')
            {
                longestRun = Math.Max(longestRun, ++currentRun);
            }
            else
            {
                currentRun = 0;
            }
        }

        string fence = new('`', Math.Max(3, longestRun + 1));
        StringBuilder builder = new();
        builder.AppendLine(fence);
        builder.Append(code);
#pragma warning disable EA0003 // The character overload is unavailable on legacy target frameworks.
        if (!code.EndsWith("\n", StringComparison.Ordinal))
#pragma warning restore EA0003
        {
            builder.AppendLine();
        }

        builder.Append(fence);
        return builder.ToString();
    }

    internal static string CreateTable(IngestionDocumentTable table)
    {
        StringBuilder builder = new();
#pragma warning disable S3967 // The existing table API requires a rectangular array.
        IngestionDocumentElement?[,] cellGrid = table.CellGrid;
#pragma warning restore S3967
        int columnCount = cellGrid.GetLength(1);
        HashSet<long> covered = [];
        Dictionary<long, IngestionDocumentTableCell> anchors = [];
        foreach (IngestionDocumentTableCell cell in table.StructuredCells ?? [])
        {
            anchors[GetCellKey(cell.RowIndex, cell.ColumnIndex, columnCount)] = cell;
            for (int row = cell.RowIndex; row < cell.RowIndex + cell.RowSpan; row++)
            {
                for (int column = cell.ColumnIndex; column < cell.ColumnIndex + cell.ColumnSpan; column++)
                {
                    covered.Add(GetCellKey(row, column, columnCount));
                }
            }
        }

        builder.AppendLine("<table>");
        for (int row = 0; row < cellGrid.GetLength(0); row++)
        {
            builder.AppendLine("  <tr>");
            for (int column = 0; column < cellGrid.GetLength(1); column++)
            {
                long cellKey = GetCellKey(row, column, columnCount);
                if (anchors.TryGetValue(cellKey, out IngestionDocumentTableCell? cell)
                    && cell is not null)
                {
                    string tag = cell.Kind?.Contains("header", StringComparison.OrdinalIgnoreCase) is true
                        ? "th"
                        : "td";
                    builder.Append("    <");
                    builder.Append(tag);
                    if (cell.RowSpan > 1)
                    {
                        builder.Append(" rowspan=\"");
                        builder.Append(cell.RowSpan);
                        builder.Append('"');
                    }

                    if (cell.ColumnSpan > 1)
                    {
                        builder.Append(" colspan=\"");
                        builder.Append(cell.ColumnSpan);
                        builder.Append('"');
                    }

                    if (!string.IsNullOrEmpty(cell.Kind))
                    {
                        builder.Append(" data-kind=\"");
                        builder.Append(EscapeHtml(cell.Kind!));
                        builder.Append('"');
                    }

                    builder.Append('>');
                    AppendCellContent(builder, cell.Elements);
                    builder.Append("</");
                    builder.Append(tag);
                    builder.AppendLine(">");
                }
                else if (!covered.Contains(cellKey))
                {
                    builder.AppendLine("    <td></td>");
                }
            }

            builder.AppendLine("  </tr>");
        }

        builder.Append("</table>");
        return builder.ToString();
    }

    private static long GetCellKey(int row, int column, int columnCount)
        => ((long)row * columnCount) + column;

    private static void AppendCellContent(StringBuilder builder, IReadOnlyList<IngestionDocumentElement> elements)
    {
        for (int i = 0; i < elements.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
            }

            IngestionDocumentElement element = elements[i];
            if (element is IngestionDocumentTable table)
            {
                builder.Append(table.GetMarkdown());
            }
            else
            {
                builder.Append(EscapeHtml(
                    element is IngestionDocumentImage image
                        ? image.AlternativeText ?? image.Text ?? string.Empty
                        : element.Text ?? element.GetMarkdown()));
            }
        }
    }

    private static string EscapeHtml(string value)
        => value.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
}

#pragma warning restore IDE0058 // Expression value is never used
#pragma warning restore CA1307 // Specify StringComparison for clarity
