// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion;

/// <summary>
/// Represents the base record type used by <see cref="VectorStoreWriter{TRecord}"/> to store ingested chunks in a vector store.
/// </summary>
/// <remarks>
/// When the vector dimension count is not known at compile time,
/// use the <see cref="VectorStoreExtensions.GetIngestionRecordCollection{TRecord}(VectorStore, string, int, string?, string?)"/>
/// helper to create a <see cref="VectorStoreCollection{TKey, TRecord}"/> and pass it to the <see cref="VectorStoreWriter{TRecord}"/> constructor.
/// When the vector dimension count is known at compile time, derive from this class and add
/// the <see cref="VectorStoreVectorAttribute"/> to the <see cref="Embedding"/> property.
/// </remarks>
public class IngestionChunkVectorRecord
{
    private AIContent? _content;
    private IReadOnlyList<int>? _pageNumbers;
    private string? _serializedPageNumbers;

    /// <summary>
    /// Gets or sets the unique key for this record.
    /// </summary>
    [VectorStoreKey]
    public virtual Guid Key { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the document from which this chunk was extracted.
    /// </summary>
    [VectorStoreData]
    public virtual string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the JSON-serialized content of the chunk, used for storage and retrieval.
    /// </summary>
    [VectorStoreData]
    public virtual string? SerializedContent { get; set; }

    private static readonly JsonTypeInfo<AIContent> _aiContentTypeInfo =
        (JsonTypeInfo<AIContent>)AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(AIContent));

    /// <summary>
    /// Gets or sets the content of the chunk.
    /// </summary>
    public virtual AIContent? Content
    {
        get
        {
            if (_content is not null)
            {
                return _content;
            }

            if (string.IsNullOrEmpty(SerializedContent))
            {
                return null;
            }

            return _content = JsonSerializer.Deserialize(SerializedContent!, _aiContentTypeInfo);
        }
        set
        {
            _content = value;
            SerializedContent = value is not null ? JsonSerializer.Serialize(value, _aiContentTypeInfo) : null;
        }
    }

    /// <summary>
    /// Gets or sets additional context for the chunk.
    /// </summary>
    [VectorStoreData]
    public virtual string? Context { get; set; }

    /// <summary>
    /// Gets or sets the serialized one-based source page numbers that contributed to the chunk.
    /// </summary>
    [VectorStoreData]
    public virtual string? SerializedPageNumbers
    {
        get => _serializedPageNumbers;
        set
        {
            _serializedPageNumbers = value;
            _pageNumbers = null;
        }
    }

    /// <summary>
    /// Gets or sets the distinct one-based source page numbers that contributed to the chunk.
    /// </summary>
    /// <remarks>
    /// The values are stored through <see cref="SerializedPageNumbers"/> because vector providers
    /// do not consistently support integer-array data properties.
    /// </remarks>
    public virtual IReadOnlyList<int> PageNumbers
    {
        get
        {
            if (_pageNumbers is not null)
            {
                return _pageNumbers;
            }

            string? serializedPageNumbers = SerializedPageNumbers;
            return _pageNumbers = string.IsNullOrEmpty(serializedPageNumbers)
                ? Array.Empty<int>()
#pragma warning disable EA0009 // The persisted representation is intentionally a simple provider-portable string.
                : NormalizePageNumbers(serializedPageNumbers!
                    .Split(',')
                    .Select(value => int.Parse(value, CultureInfo.InvariantCulture)));
#pragma warning restore EA0009
        }
        set
        {
            _pageNumbers = NormalizePageNumbers(value ?? []);
            _serializedPageNumbers = _pageNumbers.Count == 0
                ? null
#pragma warning disable LA0002 // Avoid adding a shared-text dependency solely for this persisted representation.
                : string.Join(
                    ",",
                    _pageNumbers.Select(value => value.ToString(CultureInfo.InvariantCulture)));
#pragma warning restore LA0002
        }
    }

    /// <summary>
    /// Gets the embedding value for this record.
    /// </summary>
    /// <remarks>
    /// By default, returns the <see cref="Content"/> value. The vector store's embedding generator
    /// will convert this to a vector. Override this property in derived classes to add
    /// the <see cref="VectorStoreVectorAttribute"/> with the appropriate dimension count.
    /// </remarks>
    public virtual AIContent? Embedding => Content;

    private static ReadOnlyCollection<int> NormalizePageNumbers(IEnumerable<int> pageNumbers)
    {
        int[] normalized = pageNumbers.Distinct().OrderBy(pageNumber => pageNumber).ToArray();
        if (Array.Exists(normalized, pageNumber => pageNumber <= 0))
        {
            Throw.ArgumentOutOfRangeException(
                nameof(pageNumbers),
                "Page numbers must contain only positive one-based values.");
        }

        return Array.AsReadOnly(normalized);
    }
}
