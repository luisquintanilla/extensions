// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Extensions.DataIngestion;

public class IngestionDocumentTests
{
    [Fact]
    public void IsThinContextOverSharedDocument()
    {
        Document semantic = new([TestDocuments.Text("p", "literal")]);
        IngestionDocument ingestion = new("id", semantic);

        Assert.Same(semantic, ingestion.Document);
        Assert.Equal("id", ingestion.Identifier);
        Assert.Null(typeof(IngestionDocument).GetProperty("Sections"));
        ingestion.Metadata["tenant"] = "contoso";
        Assert.True(ingestion.HasMetadata);
    }

    [Fact]
    public void RequiresIdentityAndDocument()
    {
        Assert.Throws<ArgumentException>("identifier", () => new IngestionDocument("", new Document([])));
        Assert.Throws<ArgumentNullException>("document", () => new IngestionDocument("id", null!));
    }
}
