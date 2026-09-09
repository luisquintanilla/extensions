// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
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
    /// <paramref name="tokenCount"/> is negative.
    /// </exception>
    public IngestionChunk(
        AIContent content,
        IngestionDocument document,
        int tokenCount,
        string? context = null,
        IEnumerable<DocumentNodeId>? sourceNodeIds = null,
        IEnumerable<int>? pageNumbers = null)
    {
        Content = Throw.IfNull(content);
        Document = Throw.IfNull(document);
        Context = context;
        TokenCount = Throw.IfLessThanOrEqual(tokenCount, 0);
        DocumentNodeId[] normalizedSourceNodeIds = sourceNodeIds?.Distinct().ToArray() ?? [];
        if (normalizedSourceNodeIds.Any(static sourceNodeId => sourceNodeId == default))
        {
            Throw.ArgumentException(nameof(sourceNodeIds), "Source node identifiers cannot be empty.");
        }

        int[] normalizedPageNumbers = pageNumbers?.Distinct().OrderBy(static pageNumber => pageNumber).ToArray() ?? [];
        if (normalizedPageNumbers.Any(static pageNumber => pageNumber <= 0))
        {
            Throw.ArgumentOutOfRangeException(nameof(pageNumbers), "Page numbers must be positive and one-based.");
        }

        SourceNodeIds = normalizedSourceNodeIds;
        PageNumbers = normalizedPageNumbers;
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

    /// <summary>Gets semantic source node identifiers contributing to this chunk.</summary>
    public IReadOnlyList<DocumentNodeId> SourceNodeIds { get; }

    /// <summary>Gets physical source page numbers contributing to this chunk.</summary>
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
