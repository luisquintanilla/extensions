// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Extensions.DataIngestion;

public class IngestionDocumentTests
{
    [Fact]
    public void IsThinContextWrapperAroundSharedDocument()
    {
        Document semanticDocument = new([TestDocuments.Text("p", "literal")]);
        IngestionDocument ingestionDocument = new("id", semanticDocument);

        Assert.Equal("id", ingestionDocument.Identifier);
        Assert.Same(semanticDocument, ingestionDocument.Document);
        Assert.Null(typeof(IngestionDocument).GetProperty("Sections"));
        Assert.DoesNotContain(
            typeof(IngestionDocument).Assembly.GetTypes(),
            type => type.Name.StartsWith("IngestionDocument", StringComparison.Ordinal) &&
                type.Name is not nameof(IngestionDocument) &&
                type.Name is not nameof(IngestionDocumentProcessor) &&
                type.Name is not nameof(IngestionDocumentReader));
    }

    [Fact]
    public void RequiresIdentifierAndDocument()
    {
        Assert.Throws<ArgumentException>("identifier", () => new IngestionDocument("", new Document([])));
        Assert.Throws<ArgumentNullException>("document", () => new IngestionDocument("id", null!));
    }
}
