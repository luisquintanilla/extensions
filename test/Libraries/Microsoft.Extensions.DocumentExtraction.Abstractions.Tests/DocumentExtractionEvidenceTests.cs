// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Extensions.DocumentExtraction;

public class DocumentExtractionEvidenceTests
{
    [Fact]
    public void EvidenceIsExtractionOwnedAndValidated()
    {
        DocumentText node = new(new("paragraph"), "literal");
        object raw = new();
        DocumentExtractionEvidence evidence = new(node.Id)
        {
            Confidence = 0.75,
            BoundingRegion = DocumentBoundingRegion.FromRectangle(1, 10, 20, 110, 220),
            RawRepresentation = raw,
        };
        Document document = new([node]);
        DocumentPage page = new(1, document, evidence: [evidence]);

        Assert.Same(raw, Assert.Single(page.Evidence).RawRepresentation);
        Assert.Null(typeof(DocumentNode).GetProperty("Confidence"));
        Assert.Throws<ArgumentException>("evidence", () => new DocumentPage(1, document, evidence: [new(new("missing"))]));
        Assert.Throws<ArgumentException>("evidence", () => new DocumentPage(1, document, evidence: [new(node.Id), new(node.Id)]));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ConfidenceMustBeNormalized(double confidence)
    {
        DocumentExtractionEvidence evidence = new(new("node"));

        Assert.Throws<ArgumentOutOfRangeException>("value", () => evidence.Confidence = confidence);
    }
}
