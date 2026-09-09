// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.AI;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion;

/// <summary>
/// Represents a chunk of content extracted from an <see cref="IngestionDocument"/>.
/// </summary>
[DebuggerDisplay("Content = {Content}")]
public class IngestionChunk
{
    private Dictionary<string, object>? _metadata;

    /// <summary>
    /// Initializes a new instance of the <see cref="IngestionChunk"/> class.
    /// </summary>
    /// <param name="content">The content of the chunk.</param>
    /// <param name="document">The document from which this chunk was extracted.</param>
    /// <param name="tokenCount">The number of tokens used to represent the chunk.</param>
    /// <param name="context">Additional context for the chunk.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="content"/> or <paramref name="document"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="tokenCount"/> is negative, or is zero for text content.
    /// </exception>
    public IngestionChunk(AIContent content, IngestionDocument document, int tokenCount, string? context = null)
        : this(content, document, tokenCount, context, [])
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IngestionChunk"/> class with source-page provenance.
    /// </summary>
    /// <param name="content">The content of the chunk.</param>
    /// <param name="document">The document from which this chunk was extracted.</param>
    /// <param name="tokenCount">The number of tokens used to represent the chunk.</param>
    /// <param name="context">Additional context for the chunk.</param>
    /// <param name="pageNumbers">The one-based source page numbers that contributed to the chunk.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="content"/>, <paramref name="document"/>, or <paramref name="pageNumbers"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="tokenCount"/> is negative or is zero for text content, or
    /// <paramref name="pageNumbers"/> contains a value that is not positive.
    /// </exception>
    public IngestionChunk(
        AIContent content,
        IngestionDocument document,
        int tokenCount,
        string? context,
        IReadOnlyList<int> pageNumbers)
    {
        Content = Throw.IfNull(content);
        Document = Throw.IfNull(document);
        Context = context;
        TokenCount = content is TextContent
            ? Throw.IfLessThanOrEqual(tokenCount, 0)
            : Throw.IfLessThan(tokenCount, 0);

        int[] normalizedPageNumbers = Throw.IfNull(pageNumbers)
            .Distinct()
            .OrderBy(pageNumber => pageNumber)
            .ToArray();
        if (Array.Exists(normalizedPageNumbers, pageNumber => pageNumber <= 0))
        {
            Throw.ArgumentOutOfRangeException(
                nameof(pageNumbers),
                "Page numbers must contain only positive one-based values.");
        }

        PageNumbers = Array.AsReadOnly(normalizedPageNumbers);
    }

    /// <summary>
    /// Gets the content of the chunk.
    /// </summary>
    public AIContent Content { get; }

    /// <summary>
    /// Gets the document from which this chunk was extracted.
    /// </summary>
    public IngestionDocument Document { get; }

    /// <summary>
    /// Gets additional context for the chunk.
    /// </summary>
    public string? Context { get; }

    /// <summary>
    /// Gets the number of tokens used to represent the chunk.
    /// </summary>
    public int TokenCount { get; }

    /// <summary>
    /// Gets the distinct one-based source page numbers that contributed to the chunk.
    /// </summary>
    public IReadOnlyList<int> PageNumbers { get; }

    /// <summary>
    /// Gets a value indicating whether this chunk has metadata.
    /// </summary>
    public bool HasMetadata => _metadata?.Count > 0;

    /// <summary>
    /// Gets the metadata associated with this chunk.
    /// </summary>
    public IDictionary<string, object> Metadata => _metadata ??= [];
}
