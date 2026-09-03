// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Extensions.DocumentExtraction;

public class DocumentExtractionEvidenceTests
{
    [Fact]
    public void EvidenceIsKeyedBySharedNodeIdAndDoesNotLeakIntoNode()
    {
        DocumentText node = new(new("paragraph"), "literal");
        object raw = new();
        DocumentExtractionEvidence evidence = new(node.Id)
        {
            BoundingRegion = DocumentBoundingRegion.FromRectangle(1, 10, 20, 110, 220),
            Confidence = 0.75,
            RawRepresentation = raw,
            AdditionalProperties = new() { ["language"] = "en" },
        };
        DocumentPage page = new(1, new Document([node]), evidence: [evidence]);

        Assert.Equal(node.Id, Assert.Single(page.Evidence).NodeId);
        Assert.Same(raw, evidence.RawRepresentation);
        Assert.Null(typeof(DocumentNode).GetProperty("Confidence"));
        Assert.Null(typeof(DocumentNode).GetProperty("BoundingRegion"));
        Assert.Null(typeof(DocumentNode).GetProperty("RawRepresentation"));
        Assert.Null(typeof(DocumentNode).GetProperty("AdditionalProperties"));
    }
}
