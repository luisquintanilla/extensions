// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.Extensions.Documents;

namespace Microsoft.Extensions.DataIngestion;

internal static class DocumentNodeExtensions
{
    internal static IEnumerable<DocumentNode> EnumerateContent(this Document document)
    {
        foreach (DocumentNode child in document.Children)
        {
            foreach (DocumentNode content in EnumerateContent(child))
            {
                yield return content;
            }
        }
    }

    internal static string? GetSemanticContent(this DocumentNode node)
    {
        string text = DocumentTextProjection.GetText(node);
        return text.Length == 0 ? null : text;
    }

    internal static IReadOnlyList<DocumentNodeId> GetSourceNodeIds(this IEnumerable<DocumentNode> nodes)
    {
        HashSet<DocumentNodeId> ids = new();
        foreach (DocumentNode node in nodes)
        {
            foreach (DocumentNodeId id in node.SourceNodeIds)
            {
                _ = ids.Add(id);
            }
        }

        return [.. ids];
    }

    internal static IReadOnlyList<int> GetPageNumbers(this IEnumerable<DocumentNode> nodes)
    {
        SortedSet<int> pages = new();
        foreach (DocumentNode node in nodes)
        {
            foreach (DocumentPageReference reference in node.PageReferences)
            {
                _ = pages.Add(reference.PageNumber);
            }
        }

        return [.. pages];
    }

    private static IEnumerable<DocumentNode> EnumerateContent(DocumentNode node)
    {
        if (node is DocumentContainer container)
        {
            foreach (DocumentNode child in container.Children)
            {
                foreach (DocumentNode content in EnumerateContent(child))
                {
                    yield return content;
                }
            }
        }
        else
        {
            yield return node;
        }
    }
}
