// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace Microsoft.Extensions.Documents;

/// <summary>Describes the semantic role of literal document text.</summary>
public enum DocumentTextRole
{
    /// <summary>A heading or title.</summary>
    Heading,
    /// <summary>A prose paragraph.</summary>
    Paragraph,
    /// <summary>Source code or other preformatted text.</summary>
    Code,
    /// <summary>A caption.</summary>
    Caption,
    /// <summary>A recurring or logical header.</summary>
    Header,
    /// <summary>A recurring or logical footer.</summary>
    Footer,
}

/// <summary>Represents literal text in a document.</summary>
public sealed class DocumentText : DocumentNode
{
    /// <summary>Initializes a new instance of the <see cref="DocumentText"/> class.</summary>
    public DocumentText(
        DocumentNodeId id,
        string text,
        DocumentTextRole role = DocumentTextRole.Paragraph,
        int? level = null,
        string? language = null,
        IEnumerable<DocumentPageReference>? pageReferences = null,
        IEnumerable<DocumentNodeId>? sourceNodeIds = null)
        : base(id, pageReferences, sourceNodeIds)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        if (level is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(level), "Heading levels must be positive.");
        }

        Level = level;
        Language = string.IsNullOrWhiteSpace(language) ? null : language;
        Role = role;
    }

    /// <summary>Gets the literal text.</summary>
    public string Text { get; }

    /// <summary>Gets the text role.</summary>
    public DocumentTextRole Role { get; }

    /// <summary>Gets the heading level, when known.</summary>
    public int? Level { get; }

    /// <summary>Gets the code language, when known.</summary>
    public string? Language { get; }
}
