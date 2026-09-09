// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Extensions.DocumentExtraction;

/// <summary>Represents a table extracted from a document.</summary>
/// <remarks>
/// Cells are the structured representation used when projecting page <see cref="DocumentPage.Text"/>.
/// <see cref="MarkdownRepresentation"/> preserves an exact provider-supplied table rendering when one is available,
/// but it is never parsed or used as a text fallback. On the markdown-only path <see cref="RowCount"/> and
/// <see cref="ColumnCount"/> may be 0 because the structure was not enumerated.
/// </remarks>
[Experimental(DiagnosticIds.Experiments.DocumentExtraction, UrlFormat = DiagnosticIds.UrlFormat)]
public class DocumentTable : DocumentElement
{
    /// <summary>Initializes a new instance of the <see cref="DocumentTable"/> class.</summary>
    /// <param name="rowCount">The number of rows in the table.</param>
    /// <param name="columnCount">The number of columns in the table.</param>
    /// <param name="cells">The structured cells, or <see langword="null"/> when only markdown is available.</param>
    /// <param name="markdownRepresentation">The exact provider-supplied markdown or HTML representation, when available.</param>
    public DocumentTable(
        int rowCount,
        int columnCount,
        IReadOnlyList<DocumentTableCell>? cells = null,
        string? markdownRepresentation = null)
    {
        RowCount = rowCount;
        ColumnCount = columnCount;
        Cells = cells;
        MarkdownRepresentation = markdownRepresentation;
    }

    /// <summary>Gets the number of rows in the table.</summary>
    public int RowCount { get; }

    /// <summary>Gets the number of columns in the table.</summary>
    public int ColumnCount { get; }

    /// <summary>Gets the structured cells, or <see langword="null"/> when the engine only returned markdown.</summary>
    public IReadOnlyList<DocumentTableCell>? Cells { get; }

    /// <summary>Gets the exact provider-supplied markdown or HTML table rendering, when available.</summary>
    public string? MarkdownRepresentation { get; }
}
