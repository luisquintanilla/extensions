// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.Documents;

/// <summary>Represents an image in a document.</summary>
public sealed class DocumentImage : DocumentNode
{
    /// <summary>Initializes a new instance of the <see cref="DocumentImage"/> class.</summary>
    [JsonConstructor]
    public DocumentImage(
        DocumentNodeId id,
        ReadOnlyMemory<byte> content = default,
        string? mediaType = null,
        Uri? source = null,
        string? description = null,
        IReadOnlyList<DocumentPageReference>? pageReferences = null,
        IReadOnlyList<DocumentNodeId>? sourceNodeIds = null)
        : base(id, pageReferences, sourceNodeIds)
    {
        if (content.IsEmpty && source is null && string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("An image must provide binary content, a source URI, or a description.", nameof(content));
        }

        if (!content.IsEmpty && string.IsNullOrWhiteSpace(mediaType))
        {
            throw new ArgumentException("Binary image content requires a media type.", nameof(mediaType));
        }

        Content = new ReadOnlyMemory<byte>(content.ToArray());
        MediaType = string.IsNullOrWhiteSpace(mediaType) ? null : mediaType;
        Source = source;
        Description = string.IsNullOrWhiteSpace(description) ? null : description;
    }

    /// <summary>Gets the image bytes.</summary>
    public ReadOnlyMemory<byte> Content { get; }

    /// <summary>Gets the media type of <see cref="Content"/>, when known.</summary>
    public string? MediaType { get; }

    /// <summary>Gets the source URI, when known.</summary>
    public Uri? Source { get; }

    /// <summary>Gets the literal image description or alternative text, when known.</summary>
    public string? Description { get; }
}
