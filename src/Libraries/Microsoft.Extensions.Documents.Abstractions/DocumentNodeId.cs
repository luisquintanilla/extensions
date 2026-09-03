// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.Documents;

/// <summary>Identifies a node within a <see cref="Document"/>.</summary>
public readonly struct DocumentNodeId : IEquatable<DocumentNodeId>
{
    /// <summary>Initializes a new instance of the <see cref="DocumentNodeId"/> struct.</summary>
    /// <param name="value">The stable identifier value.</param>
    [JsonConstructor]
    public DocumentNodeId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A node identifier cannot be null, empty, or whitespace.", nameof(value));
        }

        Value = value;
    }

    /// <summary>Gets the identifier value.</summary>
    public string Value { get; }

    /// <inheritdoc/>
    public bool Equals(DocumentNodeId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is DocumentNodeId other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Value is null ? 0 : StringComparer.Ordinal.GetHashCode(Value);

    /// <inheritdoc/>
    public override string ToString() => Value ?? string.Empty;

    /// <summary>Determines whether two identifiers are equal.</summary>
    public static bool operator ==(DocumentNodeId left, DocumentNodeId right) => left.Equals(right);

    /// <summary>Determines whether two identifiers are not equal.</summary>
    public static bool operator !=(DocumentNodeId left, DocumentNodeId right) => !left.Equals(right);
}
