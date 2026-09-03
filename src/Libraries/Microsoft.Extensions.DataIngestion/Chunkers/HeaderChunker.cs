// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Documents;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion;

/// <summary>Splits documents at headings while preserving heading context.</summary>
public sealed class HeaderChunker : IngestionChunker<string>
{
    private const int MaxHeaderLevel = 10;
    private readonly Chunkers.ElementsChunker _elementsChunker;

    /// <summary>Initializes a new instance of the <see cref="HeaderChunker"/> class.</summary>
    public HeaderChunker(IngestionChunkerOptions options)
    {
        _elementsChunker = new(options);
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<IngestionChunk<string>> ProcessAsync(
        IngestionDocument document,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(document);
        List<DocumentNode> elements = [];
        DocumentText?[] headers = new DocumentText?[MaxHeaderLevel + 1];

        foreach (DocumentNode element in document.Document.EnumerateContent())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (element is DocumentText { Role: DocumentTextRole.Heading } header)
            {
                foreach (IngestionChunk<string> chunk in SplitIntoChunks(document, headers, elements))
                {
                    yield return chunk;
                }

                int level = System.Math.Min(header.Level.GetValueOrDefault(1), MaxHeaderLevel);
                headers[level] = header;
                headers.AsSpan(level + 1).Clear();
            }
            else
            {
                elements.Add(element);
            }
        }

        foreach (IngestionChunk<string> chunk in SplitIntoChunks(document, headers, elements))
        {
            yield return chunk;
        }
    }

    private IEnumerable<IngestionChunk<string>> SplitIntoChunks(
        IngestionDocument document,
        DocumentText?[] headers,
        List<DocumentNode> elements)
    {
        if (elements.Count == 0)
        {
            yield break;
        }

        DocumentNode[] contextNodes = headers.Where(static header => header is not null).Cast<DocumentNode>().ToArray();
        string context = string.Join(" ", contextNodes.Cast<DocumentText>().Select(static header => header.Text));
        foreach (IngestionChunk<string> chunk in _elementsChunker.Process(document, context, elements, contextNodes))
        {
            yield return chunk;
        }

        elements.Clear();
    }
}
