// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Microsoft.Extensions.Documents;

/// <summary>Represents one immutable node in a semantic document tree.</summary>
public abstract class DocumentNode
{
    /// <summary>Initializes a new instance of the <see cref="DocumentNode"/> class.</summary>
    protected DocumentNode(
        DocumentNodeId id,
        IEnumerable<DocumentPageReference>? pageReferences = null,
        IEnumerable<DocumentNodeId>? sourceNodeIds = null)
    {
        Id = id;
        PageReferences = CopyDistinct(pageReferences, static reference => reference.PageNumber);
        SourceNodeIds = CopyDistinct(sourceNodeIds ?? new[] { id }, static sourceId => sourceId);

        if (SourceNodeIds.Count == 0)
        {
            throw new ArgumentException("A node must retain at least one source node identifier.", nameof(sourceNodeIds));
        }
    }

    /// <summary>Gets the stable identifier of this node.</summary>
    public DocumentNodeId Id { get; }

    /// <summary>Gets the physical source pages associated with this node.</summary>
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
