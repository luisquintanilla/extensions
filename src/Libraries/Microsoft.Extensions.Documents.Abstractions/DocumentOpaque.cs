// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.Documents;

/// <summary>
/// Represents provider-specific semantic content that is not part of the closed shared node union.
/// </summary>
/// <remarks>
/// <para>
/// This is an explicit versioned envelope. Consumers that do not understand <see cref="LogicalKind"/> must retain
/// the node as opaque content instead of interpreting it as ordinary text.
/// </para>
/// <para>
/// The node identifier, position, page references, source-node references, and JSON payload are preserved so a
/// producer can round-trip an extension without coupling the shared abstraction to its schema.
/// </para>
/// </remarks>
public sealed class DocumentOpaque : DocumentNode
{
    /// <summary>Initializes a new instance of the <see cref="DocumentOpaque"/> class.</summary>
    /// <param name="id">The stable identifier of the node.</param>
    /// <param name="logicalKind">The stable producer-defined logical kind.</param>
    /// <param name="schemaVersion">The positive schema version for <paramref name="logicalKind"/>.</param>
    /// <param name="position">The zero-based producer-supplied position of the node in its logical parent.</param>
    /// <param name="payload">The opaque JSON payload.</param>
    /// <param name="pageReferences">Optional producer- or processor-supplied page references.</param>
    /// <param name="sourceNodeIds">Optional source-node identifiers from which this node was derived.</param>
    [JsonConstructor]
    public DocumentOpaque(
        DocumentNodeId id,
        string logicalKind,
        int schemaVersion,
        int position,
        JsonElement payload,
        IReadOnlyList<DocumentPageReference>? pageReferences = null,
        IReadOnlyList<DocumentNodeId>? sourceNodeIds = null)
        : base(id, pageReferences, sourceNodeIds)
    {
        if (string.IsNullOrWhiteSpace(logicalKind))
        {
            throw new ArgumentException("A logical kind cannot be null, empty, or whitespace.", nameof(logicalKind));
        }

        if (schemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), "A schema version must be positive.");
        }

        if (position < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "A node position cannot be negative.");
        }

        if (payload.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException("An opaque node payload must be a defined JSON value.", nameof(payload));
        }

        LogicalKind = logicalKind;
        SchemaVersion = schemaVersion;
        Position = position;
        Payload = payload.Clone();
    }

    /// <summary>Gets the stable producer-defined logical kind.</summary>
    public string LogicalKind { get; }

    /// <summary>Gets the schema version for <see cref="LogicalKind"/>.</summary>
    public int SchemaVersion { get; }

    /// <summary>Gets the zero-based producer-supplied position in the logical parent.</summary>
    public int Position { get; }

    /// <summary>Gets the opaque JSON payload.</summary>
    public JsonElement Payload { get; }
}
