// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
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

/// <summary>Represents one page of structured OCR output.</summary>
[Experimental(DiagnosticIds.Experiments.DocumentExtraction, UrlFormat = DiagnosticIds.UrlFormat)]
public class DocumentPage
{
    /// <summary>Initializes a new instance of the <see cref="DocumentPage"/> class.</summary>
    /// <param name="pageNumber">The one-based page number.</param>
    /// <param name="document">The semantic document fragment extracted from this page.</param>
    /// <param name="markdown">The exact provider-supplied Markdown for the page, when available.</param>
    /// <param name="evidence">Extraction evidence keyed by node identifier.</param>
    /// <exception cref="System.ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    [JsonConstructor]
    public DocumentPage(
        int pageNumber,
        Document document,
        string? markdown = null,
        IReadOnlyList<DocumentExtractionEvidence>? evidence = null)
    {
        if (pageNumber <= 0)
        {
            Throw.ArgumentOutOfRangeException(nameof(pageNumber), "Page numbers must be positive and one-based.");
        }

        PageNumber = pageNumber;
        Document = Throw.IfNull(document);
        Markdown = markdown;

        DocumentExtractionEvidence[] evidenceCopy = evidence?.ToArray() ?? [];
        HashSet<DocumentNodeId> knownNodeIds = new(Document.Nodes.Select(static node => node.Id));
        HashSet<DocumentNodeId> evidenceNodeIds = [];
        foreach (DocumentExtractionEvidence item in evidenceCopy)
        {
            if (item is null)
            {
                Throw.ArgumentException(nameof(evidence), "Extraction evidence cannot contain null entries.");
            }

            if (!knownNodeIds.Contains(item.NodeId))
            {
                Throw.ArgumentException(nameof(evidence), $"Evidence references unknown document node '{item.NodeId}'.");
            }

            if (!evidenceNodeIds.Add(item.NodeId))
            {
                Throw.ArgumentException(nameof(evidence), $"Evidence for document node '{item.NodeId}' is duplicated.");
            }
        }

        Evidence = new ReadOnlyCollection<DocumentExtractionEvidence>(evidenceCopy);
    }

    /// <summary>Gets the one-based page number.</summary>
    public int PageNumber { get; }

    /// <summary>Gets the plain text deterministically projected from <see cref="Document"/>.</summary>
    /// <remarks>
    /// Provider-supplied <see cref="Markdown"/> is never parsed or used as a fallback. A page with no text-bearing
    /// elements has an empty value even when <see cref="Markdown"/> is present.
    /// </remarks>
    public string Text => Document.Text;

    /// <summary>Gets the canonical semantic document fragment extracted from this page.</summary>
    /// <remarks>
    /// The shared tree preserves logical hierarchy while typed page references preserve physical provenance.
    /// The full page text is available directly on <see cref="Text"/>, so reading-order consumers do not need
    /// geometry math.
    /// </remarks>
    public Document Document { get; }

    /// <summary>Gets extraction evidence keyed by stable semantic node identifier.</summary>
    public IReadOnlyList<DocumentExtractionEvidence> Evidence { get; }

    /// <summary>Gets the exact provider-supplied formatted Markdown for this page, when available.</summary>
    /// <remarks>This value is preserved as supplied and is never synthesized from or parsed into <see cref="Document"/>.</remarks>
    public string? Markdown { get; }

    /// <summary>Gets or sets the page dimensions (width and height), expressed in <see cref="CoordinateUnit"/>, when the engine provides them.</summary>
    /// <remarks>
    /// Together with <see cref="CoordinateUnit"/> and <see cref="CoordinateOrigin"/>, the dimensions let a consumer
    /// interpret or normalize the geometry (<see cref="DocumentBoundingBox"/> / <see cref="DocumentPoint"/>) on this page with
    /// engine-agnostic code. For example, dividing a coordinate by the corresponding dimension yields a page-relative
    /// [0, 1] value regardless of the native unit.
    /// </remarks>
    public DocumentPageDimensions? Dimensions { get; set; }

    /// <summary>Gets or sets the unit in which this page's geometry coordinates are expressed, when known.</summary>
    /// <remarks>
    /// Reported per page: engines can emit different units for different pages of one document (for example, a batch
    /// mixing image and PDF inputs). Applies to every <see cref="DocumentBoundingRegion"/> on the page and to
    /// <see cref="Dimensions"/>. When <see langword="null"/>, the geometry should be treated as an opaque,
    /// provider-specific coordinate space.
    /// </remarks>
    public DocumentCoordinateUnit? CoordinateUnit { get; set; }

    /// <summary>Gets or sets the origin corner and axis direction of this page's geometry coordinates, when known.</summary>
    public DocumentCoordinateOrigin? CoordinateOrigin { get; set; }

    /// <summary>Gets or sets the provider-native object underlying this page.</summary>
    /// <remarks>
    /// If an <see cref="DocumentPage"/> is created to represent an underlying object from another object model, this
    /// property can store that original object. This can be useful for debugging or for enabling a consumer to
    /// access the underlying object model if needed. Because the page node rides through
    /// <see cref="DocumentExtractionPageResultExtensions.ToDocumentExtractionResult"/> reduction, provider-native page data set here survives
    /// into <see cref="DocumentExtractionResult.Pages"/>.
    /// </remarks>
    [JsonIgnore]
    public object? RawRepresentation { get; set; }

    /// <summary>Gets or sets any additional properties associated with the page.</summary>
    public AdditionalPropertiesDictionary? AdditionalProperties { get; set; }
}
