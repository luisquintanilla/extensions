// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

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
    [JsonConstructor]
    public DocumentTableCell(
        DocumentNodeId id,
        int rowIndex,
        int columnIndex,
        IReadOnlyList<DocumentNode> content,
        int rowSpan = 1,
        int columnSpan = 1,
        DocumentTableCellRole role = DocumentTableCellRole.Content,
        IReadOnlyList<DocumentPageReference>? pageReferences = null,
        IReadOnlyList<DocumentNodeId>? sourceNodeIds = null)
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
    [JsonConstructor]
    public DocumentTable(
        DocumentNodeId id,
        int rowCount,
        int columnCount,
        IReadOnlyList<DocumentTableCell> cells,
        IReadOnlyList<DocumentPageReference>? pageReferences = null,
        IReadOnlyList<DocumentNodeId>? sourceNodeIds = null)
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
        for (int cellIndex = 0; cellIndex < Cells.Count; cellIndex++)
        {
            DocumentTableCell cell = Cells[cellIndex];
            if (cell.RowIndex >= RowCount ||
                cell.ColumnIndex >= ColumnCount ||
                cell.RowSpan > RowCount - cell.RowIndex ||
                cell.ColumnSpan > ColumnCount - cell.ColumnIndex)
            {
                throw new ArgumentException($"Cell [{cell.RowIndex}, {cell.ColumnIndex}] exceeds the table bounds.", nameof(Cells));
            }

            for (int previousIndex = 0; previousIndex < cellIndex; previousIndex++)
            {
                DocumentTableCell previous = Cells[previousIndex];
                bool rowsOverlap =
                    cell.RowIndex < previous.RowIndex + previous.RowSpan &&
                    previous.RowIndex < cell.RowIndex + cell.RowSpan;
                bool columnsOverlap =
                    cell.ColumnIndex < previous.ColumnIndex + previous.ColumnSpan &&
                    previous.ColumnIndex < cell.ColumnIndex + cell.ColumnSpan;
                if (rowsOverlap && columnsOverlap)
                {
                    throw new ArgumentException($"Cell [{cell.RowIndex}, {cell.ColumnIndex}] overlaps another cell.", nameof(Cells));
                }
            }
        }
    }
}
