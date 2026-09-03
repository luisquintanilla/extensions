// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Documents;

namespace Microsoft.Extensions.DocumentExtraction;

internal static class TestDocument
{
    public static DocumentPage Page(int pageNumber, string text) =>
        new(
            pageNumber,
            new Document(
            [
                new DocumentText(
                    new($"page-{pageNumber}-text"),
                    text,
                    pageReferences: [new(pageNumber)]),
            ]));
}
