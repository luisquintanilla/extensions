// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion;

/// <summary>
/// Represents a chunk of content extracted from an <see cref="IngestionDocument"/>.
/// </summary>
/// <typeparam name="T">The type of the content.</typeparam>
[DebuggerDisplay("Content = {Content}")]
public sealed class IngestionChunk<T>
{
    private Dictionary<string, object>? _metadata;

    /// <summary>
    /// Initializes a new instance of the <see cref="IngestionChunk{T}"/> class.
    /// </summary>
    /// <param name="content">The content of the chunk.</param>
    /// <param name="document">The document from which this chunk was extracted.</param>
    /// <param name="context">Additional context for the chunk.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="content"/> or <paramref name="document"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="content"/> is a string that is empty or contains only white-space characters.
    /// </exception>
    public IngestionChunk(T content, IngestionDocument document, string? context = null)
        : this(content, document, context, [])
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IngestionChunk{T}"/> class with source-page provenance.
    /// </summary>
    /// <param name="content">The content of the chunk.</param>
    /// <param name="document">The document from which this chunk was extracted.</param>
    /// <param name="context">Additional context for the chunk.</param>
    /// <param name="pageNumbers">The one-based source page numbers that contributed to the chunk.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="content"/>, <paramref name="document"/>, or <paramref name="pageNumbers"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="content"/> is a string that is empty or contains only white-space characters.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="pageNumbers"/> contains a value that is not positive.
    /// </exception>
    public IngestionChunk(
        T content,
        IngestionDocument document,
        string? context,
        IReadOnlyList<int> pageNumbers)
    {
        if (typeof(T) == typeof(string))
        {
            Content = (T)(object)Throw.IfNullOrEmpty((string)(object)content!);
        }
        else
        {
            Content = Throw.IfNull(content);
        }

        Document = Throw.IfNull(document);
        Context = context;

        int[] normalizedPageNumbers = Throw.IfNull(pageNumbers)
            .Distinct()
            .OrderBy(static pageNumber => pageNumber)
            .ToArray();
        if (Array.Exists(normalizedPageNumbers, static pageNumber => pageNumber <= 0))
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
    public T Content { get; }

    /// <summary>
    /// Gets the document from which this chunk was extracted.
    /// </summary>
    public IngestionDocument Document { get; }

    /// <summary>
    /// Gets additional context for the chunk.
    /// </summary>
    public string? Context { get; }

    /// <summary>
    /// Gets the distinct one-based source page numbers that contributed to this chunk.
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
