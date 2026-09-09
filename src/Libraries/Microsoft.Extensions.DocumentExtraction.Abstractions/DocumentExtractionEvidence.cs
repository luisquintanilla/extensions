// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DocumentExtraction;

/// <summary>Associates extraction-specific evidence with one shared semantic node.</summary>
[Experimental(DiagnosticIds.Experiments.DocumentExtraction, UrlFormat = DiagnosticIds.UrlFormat)]
public sealed class DocumentExtractionEvidence
{
    /// <summary>Initializes a new instance of the <see cref="DocumentExtractionEvidence"/> class.</summary>
    public DocumentExtractionEvidence(DocumentNodeId nodeId)
    {
        if (nodeId == default)
        {
            Throw.ArgumentException(nameof(nodeId), "An evidence node identifier cannot be empty.");
        }

        NodeId = nodeId;
    }

    /// <summary>Gets the shared semantic node identifier.</summary>
    public DocumentNodeId NodeId { get; }

    /// <summary>Gets or sets the region occupied by the node, when provided.</summary>
    public DocumentBoundingRegion? BoundingRegion { get; set; }

    /// <summary>Gets or sets extraction confidence in the range [0, 1], when available.</summary>
    public double? Confidence { get; set; }

    /// <summary>Gets or sets the provider-native object underlying the node.</summary>
    [JsonIgnore]
    public object? RawRepresentation { get; set; }

    /// <summary>Gets or sets additional provider-specific properties.</summary>
    public AdditionalPropertiesDictionary? AdditionalProperties { get; set; }
}
