// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Extensions.DocumentExtraction;

/// <summary>Associates extraction-specific evidence with one node in the shared semantic document.</summary>
/// <remarks>
/// Geometry, confidence, provider objects, and provider properties describe how an extraction engine produced a
/// semantic node. They are deliberately kept out of <see cref="DocumentNode"/> so authored and extracted documents
/// share the same content contract.
/// </remarks>
[Experimental(DiagnosticIds.Experiments.DocumentExtraction, UrlFormat = DiagnosticIds.UrlFormat)]
public sealed class DocumentExtractionEvidence
{
    /// <summary>Initializes a new instance of the <see cref="DocumentExtractionEvidence"/> class.</summary>
    /// <param name="nodeId">The shared semantic node identifier.</param>
    public DocumentExtractionEvidence(DocumentNodeId nodeId)
    {
        NodeId = nodeId;
    }

    /// <summary>Gets the shared semantic node identifier.</summary>
    public DocumentNodeId NodeId { get; }

    /// <summary>Gets or sets the region occupied by the node, when provided by the engine.</summary>
    public DocumentBoundingRegion? BoundingRegion { get; set; }

    /// <summary>Gets or sets the confidence in the range [0, 1], when available.</summary>
    public double? Confidence { get; set; }

    /// <summary>Gets or sets the provider-native object underlying the node.</summary>
    [JsonIgnore]
    public object? RawRepresentation { get; set; }

    /// <summary>Gets or sets additional provider-specific properties associated with the node.</summary>
    public AdditionalPropertiesDictionary? AdditionalProperties { get; set; }
}
