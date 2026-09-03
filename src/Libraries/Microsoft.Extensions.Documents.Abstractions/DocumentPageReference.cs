// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.Documents;

/// <summary>Associates semantic content with a physical source page without changing its logical hierarchy.</summary>
public readonly struct DocumentPageReference : IEquatable<DocumentPageReference>
{
    /// <summary>Initializes a new instance of the <see cref="DocumentPageReference"/> struct.</summary>
    /// <param name="pageNumber">The positive, one-based page number.</param>
    [JsonConstructor]
    public DocumentPageReference(int pageNumber)
    {
        if (pageNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber), "Page numbers must be positive and one-based.");
        }

        PageNumber = pageNumber;
    }

    /// <summary>Gets the one-based page number.</summary>
    public int PageNumber { get; }

    /// <inheritdoc/>
    public bool Equals(DocumentPageReference other) => PageNumber == other.PageNumber;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is DocumentPageReference other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => PageNumber;

    /// <summary>Determines whether two references are equal.</summary>
    public static bool operator ==(DocumentPageReference left, DocumentPageReference right) => left.Equals(right);

    /// <summary>Determines whether two references are not equal.</summary>
    public static bool operator !=(DocumentPageReference left, DocumentPageReference right) => !left.Equals(right);
}
