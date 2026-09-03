// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Microsoft.Extensions.Documents;

/// <summary>Represents one immutable, ordered semantic document tree.</summary>
public sealed class Document
{
    private readonly IReadOnlyDictionary<DocumentNodeId, DocumentNode> _nodesById;

    /// <summary>Initializes a new instance of the <see cref="Document"/> class.</summary>
    /// <param name="children">The root nodes in logical reading order.</param>
    public Document(IEnumerable<DocumentNode> children)
    {
        Children = DocumentNode.Copy(children ?? throw new ArgumentNullException(nameof(children)));
        Nodes = new ReadOnlyCollection<DocumentNode>(Enumerate(Children).ToArray());

        Dictionary<DocumentNodeId, DocumentNode> nodesById = new();
        foreach (DocumentNode node in Nodes)
        {
            if (nodesById.ContainsKey(node.Id))
            {
                throw new ArgumentException($"The node identifier '{node.Id}' is duplicated.", nameof(children));
            }

            nodesById.Add(node.Id, node);
        }

        _nodesById = new ReadOnlyDictionary<DocumentNodeId, DocumentNode>(nodesById);
    }

    /// <summary>Gets the root nodes in logical reading order.</summary>
    public IReadOnlyList<DocumentNode> Children { get; }

    /// <summary>Gets every node in depth-first reading order, including table-cell content.</summary>
    public IReadOnlyList<DocumentNode> Nodes { get; }

    /// <summary>Gets the deterministic plain-text projection of the canonical tree.</summary>
    public string Text => DocumentTextProjection.GetText(Children);

    /// <summary>Gets a node by its stable identifier.</summary>
    public DocumentNode GetNode(DocumentNodeId id) =>
        _nodesById.TryGetValue(id, out DocumentNode? node)
            ? node
            : throw new KeyNotFoundException($"No document node has the identifier '{id}'.");

    private static IEnumerable<DocumentNode> Enumerate(IEnumerable<DocumentNode> nodes)
    {
        foreach (DocumentNode node in nodes)
        {
            yield return node;
            foreach (DocumentNode nested in Enumerate(node.GetNestedNodes()))
            {
                yield return nested;
            }
        }
    }
}
