// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.DataIngestion.Chunkers;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion;

/// <summary>
/// Splits documents into chunks based on headers and their corresponding levels, preserving the header context.
/// </summary>
public sealed class HeaderChunker : IngestionChunker<string>
{
    private const int MaxHeaderLevel = 10;
    private readonly ElementsChunker _elementsChunker;

    /// <summary>
    /// Initializes a new instance of the <see cref="HeaderChunker"/> class.
    /// </summary>
    /// <param name="options">The options for the chunker.</param>
    public HeaderChunker(IngestionChunkerOptions options)
    {
        _elementsChunker = new(options);
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<IngestionChunk<string>> ProcessAsync(IngestionDocument document,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(document);

        List<IngestionDocumentElement> elements = [];
        string?[] headers = new string?[MaxHeaderLevel + 1];
        int?[] headerPageNumbers = new int?[MaxHeaderLevel + 1];

        foreach (IngestionDocumentElement element in document.EnumerateContent())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (element is IngestionDocumentHeader header)
            {
                foreach (var chunk in SplitIntoChunks(document, headers, headerPageNumbers, elements))
                {
                    yield return chunk;
                }

                int headerLevel = header.Level.GetValueOrDefault();
                headers[headerLevel] = header.GetSemanticContent();
                headerPageNumbers[headerLevel] = header.PageNumber;
                headers.AsSpan(headerLevel + 1).Clear(); // clear all lower level headers
                headerPageNumbers.AsSpan(headerLevel + 1).Clear();

                continue; // don't add headers to the elements list, they are part of the context
            }

            elements.Add(element);
        }

        // take care of any remaining paragraphs
        foreach (var chunk in SplitIntoChunks(document, headers, headerPageNumbers, elements))
        {
            yield return chunk;
        }
    }

    private IEnumerable<IngestionChunk<string>> SplitIntoChunks(
        IngestionDocument document,
        string?[] headers,
        int?[] headerPageNumbers,
        List<IngestionDocumentElement> elements)
    {
        if (elements.Count > 0)
        {
            string chunkHeader = string.Join(" ", headers.Where(h => !string.IsNullOrEmpty(h)));
            int[] contextPageNumbers = headerPageNumbers
                .Where(static pageNumber => pageNumber.HasValue)
                .Select(static pageNumber => pageNumber!.Value)
                .Distinct()
                .ToArray();

            foreach (var chunk in _elementsChunker.Process(document, chunkHeader, elements, contextPageNumbers))
            {
                yield return chunk;
            }

            elements.Clear();
        }
    }
}
