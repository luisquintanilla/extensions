// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.Documents;

/// <summary>Represents one immutable node in a semantic document tree.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(DocumentContainer), typeDiscriminator: "container")]
[JsonDerivedType(typeof(DocumentText), typeDiscriminator: "text")]
[JsonDerivedType(typeof(DocumentTable), typeDiscriminator: "table")]
[JsonDerivedType(typeof(DocumentTableCell), typeDiscriminator: "tableCell")]
[JsonDerivedType(typeof(DocumentImage), typeDiscriminator: "image")]
[JsonDerivedType(typeof(DocumentOpaque), typeDiscriminator: "opaque")]
public abstract class DocumentNode
{
    private protected DocumentNode(
        DocumentNodeId id,
        IReadOnlyList<DocumentPageReference>? pageReferences = null,
        IReadOnlyList<DocumentNodeId>? sourceNodeIds = null)
    {
        if (id == default)
        {
            throw new ArgumentException("A node identifier cannot be empty.", nameof(id));
        }

        Id = id;
        // Page references are producer- or processor-supplied annotations. Preserve their
        // order and multiplicity; consumers that need aggregation own that policy.
        PageReferences = Copy(pageReferences);
        SourceNodeIds = CopyDistinct(sourceNodeIds ?? new[] { id }, static sourceId => sourceId);

        if (PageReferences.Any(static reference => reference.PageNumber <= 0))
        {
            throw new ArgumentException("Page references must be positive and one-based.", nameof(pageReferences));
        }

        if (SourceNodeIds.Count == 0 || SourceNodeIds.Any(static sourceId => sourceId == default))
        {
            throw new ArgumentException("A node must retain at least one non-empty source node identifier.", nameof(sourceNodeIds));
        }
    }

    /// <summary>Gets the stable identifier of this node.</summary>
    public DocumentNodeId Id { get; }

    /// <summary>Gets the producer- or processor-supplied physical source-page references, in supplied order.</summary>
    /// <remarks>
    /// The shared document contract does not infer, sort, or deduplicate page references. Consumers that need a
    /// normalized page set must apply that policy outside this abstraction.
    /// </remarks>
    public IReadOnlyList<DocumentPageReference> PageReferences { get; }

    /// <summary>Gets the source node identifiers from which this node was derived.</summary>
    public IReadOnlyList<DocumentNodeId> SourceNodeIds { get; }

    internal virtual IEnumerable<DocumentNode> GetNestedNodes()
    {
        yield break;
    }

    internal static IReadOnlyList<T> Copy<T>(IEnumerable<T>? values)
    {
        T[] copy = values?.ToArray() ?? Array.Empty<T>();
        return new ReadOnlyCollection<T>(copy);
    }

    private static IReadOnlyList<T> CopyDistinct<T, TKey>(IEnumerable<T>? values, Func<T, TKey> keySelector)
    {
        T[] copy = values?.GroupBy(keySelector).Select(static group => group.First()).ToArray() ?? Array.Empty<T>();
        return new ReadOnlyCollection<T>(copy);
    }
}
