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

/// <summary>Represents the structured result of an OCR / document-parsing request.</summary>
/// <remarks>
/// The result normalizes the page envelopes, shared semantic document, and extraction evidence common to every engine
/// while preserving everything provider-specific via
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
        DocumentPage[] orderedPages = Throw.IfNull(pages).ToArray();
        if (Array.Exists(orderedPages, static page => page is null))
        {
            Throw.ArgumentException(nameof(pages), "Pages cannot contain null entries.");
        }

        Array.Sort(orderedPages, static (left, right) => left.PageNumber.CompareTo(right.PageNumber));
        for (int index = 1; index < orderedPages.Length; index++)
        {
            if (orderedPages[index - 1].PageNumber == orderedPages[index].PageNumber)
            {
                Throw.ArgumentException(nameof(pages), $"Page number {orderedPages[index].PageNumber} is duplicated.");
            }
        }

        Pages = new ReadOnlyCollection<DocumentPage>(orderedPages);
        Document = new(Pages.SelectMany(static page => page.Document.Children).ToArray());

        Dictionary<DocumentNodeId, DocumentExtractionEvidence> evidenceByNodeId = [];
        foreach (DocumentPage page in Pages)
        {
            foreach (DocumentExtractionEvidence evidence in page.Evidence)
            {
                if (evidenceByNodeId.ContainsKey(evidence.NodeId))
                {
                    Throw.ArgumentException(nameof(pages), $"Evidence for node '{evidence.NodeId}' is duplicated.");
                }

                evidenceByNodeId.Add(evidence.NodeId, evidence);
            }
        }

        Evidence = new ReadOnlyDictionary<DocumentNodeId, DocumentExtractionEvidence>(evidenceByNodeId);
    }

    /// <summary>Gets the per-page structured content and extraction evidence.</summary>
    public IReadOnlyList<DocumentPage> Pages { get; }

    /// <summary>Gets the merged canonical semantic document.</summary>
    public Document Document { get; }

    /// <summary>Gets extraction evidence keyed by the stable identifier of the corresponding document node.</summary>
    /// <remarks>
    /// This index is extraction-owned. It preserves provider geometry, confidence, raw representations, and additional
    /// properties without adding those concerns to the shared semantic document tree.
    /// </remarks>
    [JsonIgnore]
    public IReadOnlyDictionary<DocumentNodeId, DocumentExtractionEvidence> Evidence { get; }

    /// <summary>Gets the full-document text projected from <see cref="Document"/>.</summary>
    /// <remarks>
    /// This type intentionally does not aggregate page <see cref="DocumentPage.Markdown"/> fragments. Such fragments
    /// are not necessarily a complete provider-supplied document rendering.
    /// </remarks>
    public string Text => Document.Text;

    /// <summary>Attempts to find extraction evidence for a document node.</summary>
    /// <param name="nodeId">The stable identifier of the document node.</param>
    /// <param name="evidence">The evidence associated with <paramref name="nodeId"/>, when present.</param>
    /// <returns><see langword="true"/> when evidence was found; otherwise, <see langword="false"/>.</returns>
    public bool TryGetEvidence(DocumentNodeId nodeId, [NotNullWhen(true)] out DocumentExtractionEvidence? evidence) =>
        Evidence.TryGetValue(nodeId, out evidence);

    /// <summary>Gets or sets usage details associated with the request.</summary>
    public DocumentExtractionUsage? Usage { get; set; }

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
