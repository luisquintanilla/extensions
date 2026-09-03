// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.Documents;

/// <summary>Describes the logical role of a document container.</summary>
public enum DocumentContainerRole
{
    /// <summary>A general logical grouping.</summary>
    Section,
    /// <summary>A list.</summary>
    List,
    /// <summary>An item within a list.</summary>
    ListItem,
    /// <summary>A quotation.</summary>
    Quote,
}

/// <summary>Represents an ordered logical container in a document.</summary>
public sealed class DocumentContainer : DocumentNode
{
    /// <summary>Initializes a new instance of the <see cref="DocumentContainer"/> class.</summary>
    [JsonConstructor]
    public DocumentContainer(
        DocumentNodeId id,
        DocumentContainerRole role,
        IReadOnlyList<DocumentNode> children,
        IReadOnlyList<DocumentPageReference>? pageReferences = null,
        IReadOnlyList<DocumentNodeId>? sourceNodeIds = null)
        : base(id, pageReferences, sourceNodeIds)
    {
        Children = Copy(children ?? throw new System.ArgumentNullException(nameof(children)));
        Role = role;
    }

    /// <summary>Gets the container role.</summary>
    public DocumentContainerRole Role { get; }

    /// <summary>Gets the child nodes in logical reading order.</summary>
    public IReadOnlyList<DocumentNode> Children { get; }

    internal override IEnumerable<DocumentNode> GetNestedNodes() => Children;
}
