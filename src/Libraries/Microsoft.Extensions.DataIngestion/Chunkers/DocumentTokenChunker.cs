// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Extensions.AI;
using Microsoft.ML.Tokenizers;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion.Chunkers
{
    /// <summary>
    /// Processes a document by tokenizing its content and dividing it into overlapping chunks of tokens.
    /// </summary>
    /// <remarks>
    /// <para>This class uses a tokenizer to convert the document's content into tokens and then splits the
    /// tokens into chunks of a specified size, with a configurable overlap between consecutive chunks.</para>
    /// <para>Note that tables may be split mid-row.</para>
    /// </remarks>
    public sealed class DocumentTokenChunker : IngestionChunker
    {
        private readonly Tokenizer _tokenizer;
        private readonly int _maxTokensPerChunk;
        private readonly int _chunkOverlap;

        /// <summary>
        /// Initializes a new instance of the <see cref="DocumentTokenChunker"/> class with the specified options.
        /// </summary>
        /// <param name="options">The options used to configure the chunker, including tokenizer and chunk sizes.</param>
        public DocumentTokenChunker(IngestionChunkerOptions options)
        {
            _ = Throw.IfNull(options);

            _tokenizer = options.Tokenizer;
            _maxTokensPerChunk = options.MaxTokensPerChunk;
            _chunkOverlap = options.OverlapTokens;
        }

        /// <inheritdoc/>
        public override async IAsyncEnumerable<IngestionChunk> ProcessAsync(IngestionDocument document, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = Throw.IfNull(document);

            int stringBuilderTokenCount = 0;
            StringBuilder stringBuilder = new();
            List<ContentRange> contentRanges = [];
            foreach (IngestionDocumentElement element in document.EnumerateContent())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? elementContent = element.GetSemanticContent();
                if (string.IsNullOrEmpty(elementContent))
                {
                    if (element is IngestionDocumentImage
                        {
                            Content: ReadOnlyMemory<byte> imageContent,
                            MediaType: not null
                        } image)
                    {
                        if (stringBuilder.Length > 0)
                        {
                            yield return FinalizeChunk();
                            _ = stringBuilder.Clear();
                            stringBuilderTokenCount = 0;
                            contentRanges.Clear();
                        }

                        yield return new IngestionChunk(
                            new DataContent(imageContent, image.MediaType),
                            document,
                            tokenCount: 0,
                            context: string.Empty,
                            GetPageNumbers(image));
                    }

                    continue;
                }

                int[] elementPageNumbers = GetPageNumbers(element);
                int contentToProcessTokenCount = _tokenizer.CountTokens(elementContent!, considerNormalization: false);
                ReadOnlyMemory<char> contentToProcess = elementContent.AsMemory();
                while (stringBuilderTokenCount + contentToProcessTokenCount >= _maxTokensPerChunk)
                {
                    int index = _tokenizer.GetIndexByTokenCount(
                        text: contentToProcess.Span,
                        maxTokenCount: _maxTokensPerChunk - stringBuilderTokenCount,
                        out string? _,
                        out int addedTokenCount,
                        considerNormalization: false);

                    unsafe
                    {
                        int start = stringBuilder.Length;
                        fixed (char* ptr = &MemoryMarshal.GetReference(contentToProcess.Span))
                        {
                            _ = stringBuilder.Append(ptr, index);
                        }
                        if (index > 0)
                        {
                            contentRanges.Add(new(start, index, elementPageNumbers));
                        }
                    }
                    stringBuilderTokenCount += addedTokenCount;
                    yield return FinalizeChunk();

                    contentToProcess = contentToProcess.Slice(index);
                    contentToProcessTokenCount = _tokenizer.CountTokens(contentToProcess.Span, considerNormalization: false);
                }

                if (!contentToProcess.IsEmpty)
                {
                    int remainingStart = stringBuilder.Length;
                    _ = stringBuilder.Append(contentToProcess);
                    contentRanges.Add(new(remainingStart, contentToProcess.Length, elementPageNumbers));
                    stringBuilderTokenCount += contentToProcessTokenCount;
                }
            }

            if (stringBuilder.Length > 0)
            {
                yield return FinalizeChunk();
            }
            yield break;

            IngestionChunk FinalizeChunk()
            {
                TextContent chunkContent = new(stringBuilder.ToString());
                IngestionChunk chunk = new IngestionChunk(
                    content: chunkContent,
                    document: document,
                    tokenCount: stringBuilderTokenCount,
                    context: string.Empty,
                    pageNumbers: contentRanges
                        .Where(range => range.Length > 0)
                        .SelectMany(range => range.PageNumbers)
                        .Distinct()
                        .OrderBy(pageNumber => pageNumber)
                        .ToArray());
                _ = stringBuilder.Clear();
                stringBuilderTokenCount = 0;

                if (_chunkOverlap > 0)
                {
                    string chunkText = chunkContent.Text;
                    int index = _tokenizer.GetIndexByTokenCountFromEnd(
                        text: chunkText,
                        maxTokenCount: _chunkOverlap,
                        out string? _,
                        out stringBuilderTokenCount,
                        considerNormalization: false);

                    ReadOnlySpan<char> overlapContent = chunkText.AsSpan().Slice(index);
                    List<ContentRange> overlapRanges = [];
                    foreach (ContentRange range in contentRanges)
                    {
                        int overlapStart = Math.Max(range.Start, index);
                        int overlapEnd = Math.Min(range.Start + range.Length, chunkText.Length);
                        if (overlapStart < overlapEnd)
                        {
                            overlapRanges.Add(new(
                                overlapStart - index,
                                overlapEnd - overlapStart,
                                range.PageNumbers));
                        }
                    }

                    unsafe
                    {
                        fixed (char* ptr = &MemoryMarshal.GetReference(overlapContent))
                        {
                            _ = stringBuilder.Append(ptr, overlapContent.Length);
                        }
                    }

                    contentRanges = overlapRanges;
                }
                else
                {
                    contentRanges.Clear();
                }

                return chunk;
            }
        }

        private static int[] GetPageNumbers(IngestionDocumentElement element)
        {
            HashSet<int> pageNumbers = [];
            if (element.PageNumber is int pageNumber)
            {
                _ = pageNumbers.Add(pageNumber);
            }

            if (element is IngestionDocumentTable table
                && table.StructuredCells is { } structuredCells)
            {
                foreach (IngestionDocumentTableCell cell in structuredCells)
                {
                    foreach (IngestionDocumentElement nested in cell.Elements)
                    {
                        AddPageNumbers(nested, pageNumbers);
                    }
                }
            }
            else if (element is IngestionDocumentTable legacyTable)
            {
                foreach (IngestionDocumentElement? cell in legacyTable.Cells)
                {
                    if (cell?.PageNumber is int cellPageNumber)
                    {
                        _ = pageNumbers.Add(cellPageNumber);
                    }
                }
            }

            return [.. pageNumbers.OrderBy(pageNumber => pageNumber)];
        }

        private static void AddPageNumbers(
            IngestionDocumentElement element,
            HashSet<int> pageNumbers)
        {
            if (element.PageNumber is int pageNumber)
            {
                _ = pageNumbers.Add(pageNumber);
            }

            if (element is IngestionDocumentTable { StructuredCells: not null } table)
            {
                foreach (IngestionDocumentTableCell cell in table.StructuredCells)
                {
                    foreach (IngestionDocumentElement nested in cell.Elements)
                    {
                        AddPageNumbers(nested, pageNumbers);
                    }
                }
            }
        }

        private readonly record struct ContentRange(int Start, int Length, int[] PageNumbers);
    }
}
