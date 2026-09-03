// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion;

#pragma warning disable SA1402 // File may only contain a single type

/// <summary>
/// Represents an element within an <see cref="IngestionDocument"/>.
/// </summary>
[DebuggerDisplay("Type = {GetType().Name}, Markdown = {GetMarkdown()}")]
public abstract class IngestionDocumentElement
{
#pragma warning disable IDE1006 // Naming Styles
    private protected string? _markdown;
#pragma warning restore IDE1006 // Naming Styles

    /// <summary>
    /// Initializes a new instance of the <see cref="IngestionDocumentElement"/> class.
    /// </summary>
    /// <param name="markdown">The markdown representation of the element.</param>
    /// <exception cref="ArgumentNullException"><paramref name="markdown"/> is <see langword="null"/> or empty.</exception>
    private protected IngestionDocumentElement(string markdown)
    {
        _markdown = string.IsNullOrEmpty(markdown) ? throw new ArgumentNullException(nameof(markdown)) : markdown;
    }

    private protected IngestionDocumentElement()
    {
    }

    private Dictionary<string, object?>? _metadata;

    /// <summary>
    /// Gets or sets the textual content of the element.
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Text"/> is the literal source content rather than
    /// a text projection of explicit Markdown.
    /// </summary>
    public bool IsLiteralText { get; private protected set; }

    /// <summary>
    /// Gets the markdown representation of the element.
    /// </summary>
    /// <returns>The markdown representation.</returns>
    public virtual string GetMarkdown()
        => _markdown ?? MarkdownProjection.EscapeLiteral(Text ?? string.Empty);

    /// <summary>
    /// Gets or sets the page number where this element appears.
    /// </summary>
    public int? PageNumber { get; set; }

    /// <summary>
    /// Gets a value indicating whether this element has metadata.
    /// </summary>
    public bool HasMetadata => _metadata?.Count > 0;

    /// <summary>
    /// Gets the metadata associated with this element.
    /// </summary>
    public IDictionary<string, object?> Metadata => _metadata ??= [];
}

/// <summary>
/// A section can be just a page or a logical grouping of elements in a document.
/// </summary>
public sealed class IngestionDocumentSection : IngestionDocumentElement
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IngestionDocumentSection"/> class.
    /// </summary>
    /// <param name="markdown">The markdown representation of the section.</param>
    public IngestionDocumentSection(string markdown)
        : base(markdown)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IngestionDocumentSection"/> class.
    /// </summary>
    public IngestionDocumentSection()
    {
    }

    /// <summary>
    /// Gets the elements within this section.
    /// </summary>
    public IList<IngestionDocumentElement> Elements { get; } = [];

    /// <inheritdoc/>
    public override string GetMarkdown()
        => string.Join(Environment.NewLine, Elements.Select(e => e.GetMarkdown()));
}

/// <summary>
/// Represents a paragraph in a document.
/// </summary>
public sealed class IngestionDocumentParagraph : IngestionDocumentElement
{
    private IngestionDocumentParagraph()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IngestionDocumentParagraph"/> class.
    /// </summary>
    /// <param name="markdown">The markdown representation of the paragraph.</param>
    public IngestionDocumentParagraph(string markdown)
        : base(markdown)
    {
    }

    /// <summary>
    /// Creates a paragraph from literal text rather than Markdown.
    /// </summary>
    /// <param name="text">The literal paragraph text.</param>
    /// <returns>A paragraph whose semantic content is <paramref name="text"/>.</returns>
    public static IngestionDocumentParagraph FromText(string text)
        => new()
        {
            Text = Throw.IfNullOrEmpty(text),
            IsLiteralText = true,
        };
}

/// <summary>
/// Represents a header in a document.
/// </summary>
public sealed class IngestionDocumentHeader : IngestionDocumentElement
{
    private IngestionDocumentHeader()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IngestionDocumentHeader"/> class.
    /// </summary>
    /// <param name="markdown">The markdown representation of the header.</param>
    public IngestionDocumentHeader(string markdown)
        : base(markdown)
    {
    }

    /// <summary>
    /// Creates a header from literal text rather than Markdown.
    /// </summary>
    /// <param name="text">The literal header text.</param>
    /// <param name="level">The optional header level.</param>
    /// <returns>A header whose semantic content is <paramref name="text"/>.</returns>
    public static IngestionDocumentHeader FromText(string text, int? level = null)
        => new()
        {
            Text = Throw.IfNullOrEmpty(text),
            IsLiteralText = true,
            Level = level,
        };

    /// <summary>
    /// Gets or sets the level of the header.
    /// </summary>
    public int? Level
    {
        get => field;
        set
        {
            if (value.HasValue)
            {
                field = Throw.IfOutOfRange(value.Value, min: 1, max: 10, nameof(value));
            }
            else
            {
                field = null;
            }
        }
    }
}

/// <summary>
/// Represents a footer in a document.
/// </summary>
public sealed class IngestionDocumentFooter : IngestionDocumentElement
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IngestionDocumentFooter"/> class.
    /// </summary>
    /// <param name="markdown">The markdown representation of the footer.</param>
    public IngestionDocumentFooter(string markdown)
        : base(markdown)
    {
    }
}

/// <summary>
/// Represents a source-code block in a document.
/// </summary>
public sealed class IngestionDocumentCodeBlock : IngestionDocumentElement
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IngestionDocumentCodeBlock"/> class.
    /// </summary>
    /// <param name="code">The literal source code.</param>
    public IngestionDocumentCodeBlock(string code)
    {
        Text = Throw.IfNullOrEmpty(code);
        IsLiteralText = true;
    }

    /// <inheritdoc/>
    public override string GetMarkdown() => MarkdownProjection.CreateCodeFence(Text!);
}

/// <summary>
/// Represents a positioned cell in an ingestion table.
/// </summary>
public sealed class IngestionDocumentTableCell
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IngestionDocumentTableCell"/> class.
    /// </summary>
    /// <param name="rowIndex">The zero-based row index.</param>
    /// <param name="columnIndex">The zero-based column index.</param>
    /// <param name="elements">The nested cell content in reading order.</param>
    /// <param name="rowSpan">The number of rows spanned by the cell.</param>
    /// <param name="columnSpan">The number of columns spanned by the cell.</param>
    /// <param name="kind">The source cell role, such as a row or column header.</param>
    public IngestionDocumentTableCell(
        int rowIndex,
        int columnIndex,
        IEnumerable<IngestionDocumentElement> elements,
        int rowSpan = 1,
        int columnSpan = 1,
        string? kind = null)
    {
        RowIndex = Throw.IfLessThan(rowIndex, 0);
        ColumnIndex = Throw.IfLessThan(columnIndex, 0);
        RowSpan = Throw.IfLessThan(rowSpan, 1);
        ColumnSpan = Throw.IfLessThan(columnSpan, 1);
        Kind = kind;
        Elements = Throw.IfNull(elements).ToArray();

        for (int i = 0; i < Elements.Count; i++)
        {
            if (Elements[i] is null)
            {
                Throw.ArgumentException(nameof(elements), "Cell elements must not contain null values.");
            }
        }
    }

    /// <summary>Gets the zero-based row index.</summary>
    public int RowIndex { get; }

    /// <summary>Gets the zero-based column index.</summary>
    public int ColumnIndex { get; }

    /// <summary>Gets the nested cell content in reading order.</summary>
    public IReadOnlyList<IngestionDocumentElement> Elements { get; }

    /// <summary>Gets the number of rows spanned by the cell.</summary>
    public int RowSpan { get; }

    /// <summary>Gets the number of columns spanned by the cell.</summary>
    public int ColumnSpan { get; }

    /// <summary>Gets the source cell role, such as a row or column header.</summary>
    public string? Kind { get; }
}

/// <summary>
/// Represents a table in a document.
/// </summary>
public sealed class IngestionDocumentTable : IngestionDocumentElement
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IngestionDocumentTable"/> class.
    /// </summary>
    /// <param name="markdown">The markdown representation of the table.</param>
    /// <param name="cells">The cells of the table.</param>
    /// <exception cref="ArgumentNullException"><paramref name="cells"/> is <see langword="null"/>.</exception>
#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional
#pragma warning disable S3967 // Multidimensional arrays should not be used
    public IngestionDocumentTable(string markdown, IngestionDocumentElement?[,] cells)
        : base(markdown)
    {
        Cells = Throw.IfNull(cells);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IngestionDocumentTable"/> class from structured cells.
    /// </summary>
    /// <param name="rowCount">The number of rows in the table.</param>
    /// <param name="columnCount">The number of columns in the table.</param>
    /// <param name="cells">The positioned table cells.</param>
    public IngestionDocumentTable(
        int rowCount,
        int columnCount,
        IEnumerable<IngestionDocumentTableCell> cells)
    {
        rowCount = Throw.IfLessThan(rowCount, 1);
        columnCount = Throw.IfLessThan(columnCount, 1);
        StructuredCells = Throw.IfNull(cells).ToArray();
        Cells = CreateCellGrid(rowCount, columnCount, StructuredCells);
    }

    /// <summary>
    /// Gets the positioned cells, including nested content, spans, and roles.
    /// </summary>
    /// <remarks>This value is <see langword="null"/> for tables created from explicit Markdown.</remarks>
    public IReadOnlyList<IngestionDocumentTableCell>? StructuredCells { get; }

    /// <summary>
    /// Gets the cells of the table.
    /// Each table can be represented as a two-dimensional array of cell contents, with the first row being the headers.
    /// </summary>
    /// <remarks>
    /// <para>This information is useful when chunking large tables that exceed token count limit.</para>
    /// <para>Null represents an empty cell (<see cref="IngestionDocumentElement.GetMarkdown()"/> can't return an empty string).</para>
    /// </remarks>
#pragma warning disable CA1819 // Properties should not return arrays
    public IngestionDocumentElement?[,] Cells { get; }
#pragma warning restore CA1819 // Properties should not return arrays
#pragma warning restore S3967 // Multidimensional arrays should not be used
#pragma warning restore CA1814 // Prefer jagged arrays over multidimensional

    /// <inheritdoc/>
    public override string GetMarkdown()
        => _markdown ?? MarkdownProjection.CreateTable(this);

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional
#pragma warning disable S3967 // Multidimensional arrays should not be used
    private static IngestionDocumentElement?[,] CreateCellGrid(
        int rowCount,
        int columnCount,
        IReadOnlyList<IngestionDocumentTableCell> cells)
    {
        IngestionDocumentElement?[,] grid = new IngestionDocumentElement?[rowCount, columnCount];
        bool[,] occupied = new bool[rowCount, columnCount];

        foreach (IngestionDocumentTableCell cell in cells)
        {
            if (cell.RowIndex + cell.RowSpan > rowCount
                || cell.ColumnIndex + cell.ColumnSpan > columnCount)
            {
                Throw.ArgumentOutOfRangeException(
                    nameof(cells),
                    $"Cell at ({cell.RowIndex}, {cell.ColumnIndex}) exceeds the table bounds.");
            }

            for (int row = cell.RowIndex; row < cell.RowIndex + cell.RowSpan; row++)
            {
                for (int column = cell.ColumnIndex; column < cell.ColumnIndex + cell.ColumnSpan; column++)
                {
                    if (occupied[row, column])
                    {
                        Throw.ArgumentException(
                            nameof(cells),
                            $"Cell at ({cell.RowIndex}, {cell.ColumnIndex}) overlaps another cell.");
                    }

                    occupied[row, column] = true;
                }
            }

            grid[cell.RowIndex, cell.ColumnIndex] = CreateCellElement(cell.Elements);
        }

        return grid;
    }
#pragma warning restore S3967 // Multidimensional arrays should not be used
#pragma warning restore CA1814 // Prefer jagged arrays over multidimensional

    private static IngestionDocumentElement? CreateCellElement(IReadOnlyList<IngestionDocumentElement> elements)
    {
        if (elements.Count == 0)
        {
            return null;
        }

        if (elements.Count == 1)
        {
            return elements[0];
        }

        IngestionDocumentSection section = new();
        foreach (IngestionDocumentElement element in elements)
        {
            section.Elements.Add(element);
        }

        return section;
    }
}

/// <summary>
/// Represents an image in a document.
/// </summary>
public sealed class IngestionDocumentImage : IngestionDocumentElement
{
    private IngestionDocumentImage()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IngestionDocumentImage"/> class.
    /// </summary>
    /// <param name="markdown">The markdown representation of the image.</param>
    public IngestionDocumentImage(string markdown)
        : base(markdown)
    {
    }

    /// <summary>
    /// Creates an image from binary content without inventing a Markdown source.
    /// </summary>
    /// <param name="content">The image bytes.</param>
    /// <param name="mediaType">The image media type, when known.</param>
    /// <param name="alternativeText">Optional alternative text.</param>
    /// <returns>An image containing the supplied binary content.</returns>
    public static IngestionDocumentImage FromContent(
        ReadOnlyMemory<byte> content,
        string? mediaType = null,
        string? alternativeText = null)
    {
        if (content.IsEmpty)
        {
            Throw.ArgumentException(nameof(content), "Image content must not be empty.");
        }

        if (mediaType is not null)
        {
            _ = Throw.IfNullOrWhitespace(mediaType);
        }

        return new()
        {
            Content = content,
            MediaType = mediaType,
            AlternativeText = alternativeText,
        };
    }

    /// <summary>
    /// Creates an image from literal alternative text when binary content is unavailable.
    /// </summary>
    /// <param name="alternativeText">The literal image description.</param>
    /// <returns>An image containing the supplied alternative text.</returns>
    public static IngestionDocumentImage FromText(string alternativeText)
        => new()
        {
            AlternativeText = Throw.IfNullOrEmpty(alternativeText),
        };

    /// <summary>
    /// Gets or sets the binary content of the image.
    /// </summary>
    public ReadOnlyMemory<byte>? Content { get; set; }

    /// <summary>
    /// Gets or sets the media type of the image.
    /// </summary>
    public string? MediaType { get; set; }

    /// <summary>
    /// Gets or sets the alternative text for the image.
    /// </summary>
    /// <remarks>
    /// Alternative text is a brief, descriptive text that explains the content, context, or function of an image when the image cannot be displayed or accessed.
    /// This property can be used when generating the embedding for the image that is part of larger chunk.
    /// </remarks>
    public string? AlternativeText { get; set; }

    /// <inheritdoc/>
    public override string GetMarkdown()
        => _markdown ?? MarkdownProjection.EscapeLiteral(AlternativeText ?? Text ?? string.Empty);
}

#pragma warning restore SA1402 // File may only contain a single type
