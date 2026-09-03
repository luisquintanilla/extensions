// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Extensions.Documents;

/// <summary>Describes the semantic role of a table cell.</summary>
public enum DocumentTableCellRole
{
    /// <summary>A content cell.</summary>
    Content,
    /// <summary>A column header.</summary>
    ColumnHeader,
    /// <summary>A row header.</summary>
    RowHeader,
}

/// <summary>Represents a structured table cell with ordered nested content.</summary>
public sealed class DocumentTableCell : DocumentNode
{
    /// <summary>Initializes a new instance of the <see cref="DocumentTableCell"/> class.</summary>
    public DocumentTableCell(
        DocumentNodeId id,
        int rowIndex,
        int columnIndex,
        IEnumerable<DocumentNode> content,
        int rowSpan = 1,
        int columnSpan = 1,
        DocumentTableCellRole role = DocumentTableCellRole.Content,
        IEnumerable<DocumentPageReference>? pageReferences = null,
        IEnumerable<DocumentNodeId>? sourceNodeIds = null)
        : base(id, pageReferences, sourceNodeIds)
    {
        if (rowIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rowIndex));
        }

        if (columnIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(columnIndex));
        }

        if (rowSpan <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rowSpan));
        }

        if (columnSpan <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(columnSpan));
        }

        RowIndex = rowIndex;
        ColumnIndex = columnIndex;
        RowSpan = rowSpan;
        ColumnSpan = columnSpan;
        Role = role;
        Content = DocumentNode.Copy(content ?? throw new ArgumentNullException(nameof(content)));
    }

    /// <summary>Gets the zero-based row index.</summary>
    public int RowIndex { get; }

    /// <summary>Gets the zero-based column index.</summary>
    public int ColumnIndex { get; }

    /// <summary>Gets the number of rows spanned.</summary>
    public int RowSpan { get; }

    /// <summary>Gets the number of columns spanned.</summary>
    public int ColumnSpan { get; }

    /// <summary>Gets the cell role.</summary>
    public DocumentTableCellRole Role { get; }

    /// <summary>Gets the cell content in reading order.</summary>
    public IReadOnlyList<DocumentNode> Content { get; }

    internal override IEnumerable<DocumentNode> GetNestedNodes() => Content;
}

/// <summary>Represents a structured table in a document.</summary>
public sealed class DocumentTable : DocumentNode
{
    /// <summary>Initializes a new instance of the <see cref="DocumentTable"/> class.</summary>
    public DocumentTable(
        DocumentNodeId id,
        int rowCount,
        int columnCount,
        IEnumerable<DocumentTableCell> cells,
        IEnumerable<DocumentPageReference>? pageReferences = null,
        IEnumerable<DocumentNodeId>? sourceNodeIds = null)
        : base(id, pageReferences, sourceNodeIds)
    {
        if (rowCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rowCount));
        }

        if (columnCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(columnCount));
        }

        RowCount = rowCount;
        ColumnCount = columnCount;
        Cells = Copy((cells ?? throw new ArgumentNullException(nameof(cells)))
            .OrderBy(static cell => cell.RowIndex)
            .ThenBy(static cell => cell.ColumnIndex));
        ValidateCells();
    }

    /// <summary>Gets the number of rows.</summary>
    public int RowCount { get; }

    /// <summary>Gets the number of columns.</summary>
    public int ColumnCount { get; }

    /// <summary>Gets the structured cells.</summary>
    public IReadOnlyList<DocumentTableCell> Cells { get; }

    internal override IEnumerable<DocumentNode> GetNestedNodes()
    {
        foreach (DocumentTableCell cell in Cells)
        {
            yield return cell;
        }
    }

    private void ValidateCells()
    {
        bool[,] occupied = new bool[RowCount, ColumnCount];
        foreach (DocumentTableCell cell in Cells)
        {
            if (cell.RowIndex >= RowCount ||
                cell.ColumnIndex >= ColumnCount ||
                cell.RowSpan > RowCount - cell.RowIndex ||
                cell.ColumnSpan > ColumnCount - cell.ColumnIndex)
            {
                throw new ArgumentException($"Cell [{cell.RowIndex}, {cell.ColumnIndex}] exceeds the table bounds.", nameof(Cells));
            }

            for (int row = cell.RowIndex; row < cell.RowIndex + cell.RowSpan; row++)
            {
                for (int column = cell.ColumnIndex; column < cell.ColumnIndex + cell.ColumnSpan; column++)
                {
                    if (occupied[row, column])
                    {
                        throw new ArgumentException($"Cell [{cell.RowIndex}, {cell.ColumnIndex}] overlaps another cell.", nameof(Cells));
                    }

                    occupied[row, column] = true;
                }
            }
        }
    }
}
