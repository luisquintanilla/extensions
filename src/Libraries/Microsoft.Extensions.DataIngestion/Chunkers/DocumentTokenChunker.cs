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

                IReadOnlyList<(DocumentNode Node, int Start, int End)> elementSourceSegments =
                    GetProjectionSourceSegments(element, elementContent);
                int processedCharacters = 0;
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
                            AddIntersectingSourceSegments(
                                sourceSegments,
                                elementSourceSegments,
                                processedCharacters,
                                index,
                                start);
                        }
                    }
                    processedCharacters += index;
                    yield return FinalizeChunk();

                    contentToProcess = contentToProcess.Slice(index);
                    contentToProcessTokenCount = _tokenizer.CountTokens(contentToProcess.Span, considerNormalization: false);
                }

                if (!contentToProcess.IsEmpty)
                {
                    int remainderStart = stringBuilder.Length;
                    _ = stringBuilder.Append(contentToProcess);
                    AddIntersectingSourceSegments(
                        sourceSegments,
                        elementSourceSegments,
                        processedCharacters,
                        contentToProcess.Length,
                        remainderStart);
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

        private static void AddIntersectingSourceSegments(
            List<(DocumentNode Node, int Start, int End)> destination,
            IReadOnlyList<(DocumentNode Node, int Start, int End)> source,
            int sourceStart,
            int sourceLength,
            int destinationStart)
        {
            int sourceEnd = sourceStart + sourceLength;
            foreach ((DocumentNode node, int start, int end) in source)
            {
                int intersectionStart = Math.Max(start, sourceStart);
                int intersectionEnd = Math.Min(end, sourceEnd);
                if (intersectionStart < intersectionEnd)
                {
                    destination.Add((
                        node,
                        destinationStart + intersectionStart - sourceStart,
                        destinationStart + intersectionEnd - sourceStart));
                }
            }
        }

        private static IReadOnlyList<(DocumentNode Node, int Start, int End)> GetProjectionSourceSegments(
            DocumentNode element,
            string content)
        {
            List<(DocumentNode Node, int Start, int End)> segments = [];
            AddNodeProjectionSegments(element, content, offset: 0, segments);
            return segments;
        }

        private static void AddNodeProjectionSegments(
            DocumentNode node,
            string projection,
            int offset,
            List<(DocumentNode Node, int Start, int End)> segments)
        {
            if (projection.Length == 0)
            {
                return;
            }

            segments.Add((node, offset, offset + projection.Length));
            switch (node)
            {
                case DocumentContainer container:
                    AddSequenceProjectionSegments(container.Children, offset, segments);
                    break;
                case DocumentTable table:
                    AddTableProjectionSegments(table, offset, segments);
                    break;
                case DocumentTableCell cell:
                    AddSequenceProjectionSegments(cell.Content, offset, segments);
                    break;
            }
        }

        private static void AddSequenceProjectionSegments(
            IEnumerable<DocumentNode> nodes,
            int offset,
            List<(DocumentNode Node, int Start, int End)> segments)
        {
            bool hasPreviousText = false;
            foreach (DocumentNode node in nodes)
            {
                string projection = DocumentTextProjection.GetText(node);
                if (projection.Length == 0)
                {
                    continue;
                }

                if (hasPreviousText)
                {
                    offset += 2;
                }

                AddNodeProjectionSegments(node, projection, offset, segments);
                offset += projection.Length;
                hasPreviousText = true;
            }
        }

        private static void AddTableProjectionSegments(
            DocumentTable table,
            int offset,
            List<(DocumentNode Node, int Start, int End)> segments)
        {
            IGrouping<int, DocumentTableCell>[] rows = table.Cells
                .GroupBy(static cell => cell.RowIndex)
                .OrderBy(static row => row.Key)
                .ToArray();
            for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
            {
                List<(string Text, DocumentTableCell? Cell)> columns = [];
                foreach (DocumentTableCell cell in rows[rowIndex].OrderBy(static cell => cell.ColumnIndex))
                {
                    while (columns.Count < cell.ColumnIndex)
                    {
                        columns.Add((string.Empty, null));
                    }

                    columns.Add((DocumentTextProjection.GetText(cell.Content), cell));
                    for (int span = 1; span < cell.ColumnSpan; span++)
                    {
                        columns.Add((string.Empty, null));
                    }
                }

                for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
                {
                    (string cellText, DocumentTableCell? cell) = columns[columnIndex];
                    if (cell is not null && cellText.Length > 0)
                    {
                        AddNodeProjectionSegments(cell, cellText, offset, segments);
                    }

                    offset += cellText.Length;
                    if (columnIndex < columns.Count - 1)
                    {
                        offset++;
                    }
                }

                if (rowIndex < rows.Length - 1)
                {
                    offset++;
                }
            }
        }

    }
}
