// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Documents;

namespace Microsoft.Extensions.DataIngestion;

internal static class TestDocuments
{
    public static IngestionDocument Create(string identifier, params DocumentNode[] children) =>
        new(identifier, new Document(children));

    public static DocumentText Text(
        string id,
        string text,
        DocumentTextRole role = DocumentTextRole.Paragraph,
        int? level = null,
        int? pageNumber = null) =>
        new(
            new(id),
            text,
            role,
            level,
            pageReferences: pageNumber.HasValue ? [new(pageNumber.Value)] : null);

    public static DocumentContainer Section(string id, params DocumentNode[] children) =>
        new(new(id), DocumentContainerRole.Section, children);
}
