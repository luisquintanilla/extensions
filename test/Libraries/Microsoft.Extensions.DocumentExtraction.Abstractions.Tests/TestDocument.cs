// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Documents;

namespace Microsoft.Extensions.DocumentExtraction;

internal static class TestDocument
{
    public static Document Create(string text, int pageNumber = 1) =>
        new([new DocumentText(new($"page-{pageNumber}-text"), text, pageReferences: [new(pageNumber)])]);

    public static DocumentPage Page(int pageNumber, string text, string? markdown = null) =>
        new(pageNumber, Create(text, pageNumber), markdown);

    public static DocumentPage EmptyPage(int pageNumber) => new(pageNumber, new Document([]));
}
