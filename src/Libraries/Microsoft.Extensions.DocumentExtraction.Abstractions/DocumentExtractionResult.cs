// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DocumentExtraction;

/// <summary>Represents the structured result of an OCR / document-parsing request.</summary>
/// <remarks>
/// The result normalizes the content common to every engine (text, pages, tables, bounding
/// regions) while preserving everything provider-specific via
/// <see cref="RawRepresentation"/> and <see cref="AdditionalProperties"/>, mirroring how
/// <c>ChatResponse</c> normalizes the common surface and preserves the raw.
/// </remarks>
[Experimental(DiagnosticIds.Experiments.DocumentExtraction, UrlFormat = DiagnosticIds.UrlFormat)]
public class DocumentExtractionResult
{
    /// <summary>Initializes a new instance of the <see cref="DocumentExtractionResult"/> class.</summary>
    /// <param name="pages">The per-page structured content.</param>
    /// <exception cref="System.ArgumentNullException"><paramref name="pages"/> is <see langword="null"/>.</exception>
    public DocumentExtractionResult(IReadOnlyList<DocumentPage> pages)
    {
        Pages = new ReadOnlyCollection<DocumentPage>(Throw.IfNull(pages).ToArray());
        Document = new(Pages.SelectMany(static page => page.Document.Children));
    }

    /// <summary>Gets the per-page structured content (text, tables, blocks).</summary>
    public IReadOnlyList<DocumentPage> Pages { get; }

    /// <summary>Gets the merged canonical semantic document.</summary>
    /// <remarks>
    /// For a materialized extraction this is formed by concatenating page-fragment roots in page order. Stable node
    /// identifiers must therefore be unique across all fragments. This deterministic merge does not infer a logical
    /// container spanning pages; providers that have a complete logical hierarchy should return it as page references
    /// on nodes rather than physical page containers.
    /// </remarks>
    public Document Document { get; }

    /// <summary>Gets the full-document text projected from <see cref="Document"/>.</summary>
    /// <remarks>
    /// This type intentionally does not aggregate page <see cref="DocumentPage.Markdown"/> fragments. Such fragments
    /// are not necessarily a complete provider-supplied document rendering.
    /// </remarks>
    public string Text => Document.Text;

    /// <summary>Gets or sets the provider-native object underlying this result.</summary>
    /// <remarks>
    /// The escape hatch for provider richness that does not map onto the normalized surface, mirroring
    /// <c>ChatResponse.RawRepresentation</c>. Nothing is lost.
    /// </remarks>
    [JsonIgnore]
    public object? RawRepresentation { get; set; }

    /// <summary>Gets or sets any additional properties associated with the result.</summary>
    public AdditionalPropertiesDictionary? AdditionalProperties { get; set; }
}
