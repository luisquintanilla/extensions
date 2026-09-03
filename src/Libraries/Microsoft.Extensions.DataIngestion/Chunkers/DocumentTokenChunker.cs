// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Documents;
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
    public sealed class DocumentTokenChunker : IngestionChunker<string>
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
        public override async IAsyncEnumerable<IngestionChunk<string>> ProcessAsync(IngestionDocument document, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = Throw.IfNull(document);

            int stringBuilderTokenCount = 0;
            StringBuilder stringBuilder = new();
            List<(DocumentNode Node, int Start, int End)> sourceSegments = [];
            foreach (DocumentNode element in document.Document.EnumerateContent())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? elementContent = element.GetSemanticContent();
                if (string.IsNullOrEmpty(elementContent))
                {
                    continue;
                }

                int contentToProcessTokenCount = _tokenizer.CountTokens(elementContent!, considerNormalization: false);
                ReadOnlyMemory<char> contentToProcess = elementContent.AsMemory();
                while (stringBuilderTokenCount + contentToProcessTokenCount >= _maxTokensPerChunk)
                {
                    int index = _tokenizer.GetIndexByTokenCount(
                        text: contentToProcess.Span,
                        maxTokenCount: _maxTokensPerChunk - stringBuilderTokenCount,
                        out string? _,
                        out int _,
                        considerNormalization: false);

                    unsafe
                    {
                        fixed (char* ptr = &MemoryMarshal.GetReference(contentToProcess.Span))
                        {
                            int start = stringBuilder.Length;
                            _ = stringBuilder.Append(ptr, index);
                            sourceSegments.Add((element, start, stringBuilder.Length));
                        }
                    }
                    yield return FinalizeChunk();

                    contentToProcess = contentToProcess.Slice(index);
                    contentToProcessTokenCount = _tokenizer.CountTokens(contentToProcess.Span, considerNormalization: false);
                }

                if (!contentToProcess.IsEmpty)
                {
                    int remainderStart = stringBuilder.Length;
                    _ = stringBuilder.Append(contentToProcess);
                    sourceSegments.Add((element, remainderStart, stringBuilder.Length));
                }

                stringBuilderTokenCount += contentToProcessTokenCount;
            }

            if (stringBuilder.Length > 0)
            {
                yield return FinalizeChunk();
            }
            yield break;

            IngestionChunk<string> FinalizeChunk()
            {
                DocumentNode[] sourceNodes = sourceSegments.Select(static segment => segment.Node).Distinct().ToArray();
                IngestionChunk<string> chunk = new IngestionChunk<string>(
                    content: stringBuilder.ToString(),
                    document: document,
                    context: string.Empty,
                    sourceNodeIds: sourceNodes.GetSourceNodeIds(),
                    pageNumbers: sourceNodes.GetPageNumbers());
                _ = stringBuilder.Clear();
                stringBuilderTokenCount = 0;

                if (_chunkOverlap > 0)
                {
                    int index = _tokenizer.GetIndexByTokenCountFromEnd(
                        text: chunk.Content,
                        maxTokenCount: _chunkOverlap,
                        out string? _,
                        out stringBuilderTokenCount,
                        considerNormalization: false);

                    ReadOnlySpan<char> overlapContent = chunk.Content.AsSpan().Slice(index);
                    sourceSegments = sourceSegments
                        .Where(segment => segment.End > index)
                        .Select(segment => (
                            segment.Node,
                            Start: Math.Max(0, segment.Start - index),
                            End: segment.End - index))
                        .ToList();
                    unsafe
                    {
                        fixed (char* ptr = &MemoryMarshal.GetReference(overlapContent))
                        {
                            _ = stringBuilder.Append(ptr, overlapContent.Length);
                        }
                    }
                }
                else
                {
                    sourceSegments.Clear();
                }

                return chunk;
            }
        }

    }
}
