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
using Microsoft.Extensions.Documents;
using Microsoft.ML.Tokenizers;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion.Chunkers;

/// <summary>Splits the canonical document projection into overlapping token chunks.</summary>
public sealed class DocumentTokenChunker : IngestionChunker
{
    private readonly Tokenizer _tokenizer;
    private readonly int _maxTokensPerChunk;
    private readonly int _chunkOverlap;

    /// <summary>Initializes a new instance of the <see cref="DocumentTokenChunker"/> class.</summary>
    public DocumentTokenChunker(IngestionChunkerOptions options)
    {
        _ = Throw.IfNull(options);
        _tokenizer = options.Tokenizer;
        _maxTokensPerChunk = options.MaxTokensPerChunk;
        _chunkOverlap = options.OverlapTokens;
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<IngestionChunk> ProcessAsync(
        IngestionDocument document,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(document);
        int builderTokenCount = 0;
        StringBuilder builder = new();
        List<(DocumentNode Node, int Start, int End)> sourceSegments = [];
        bool hasPreviousContent = false;

        foreach (DocumentNode element in document.Document.EnumerateContent())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? elementContent = element.GetSemanticContent();
            if (string.IsNullOrEmpty(elementContent))
            {
                continue;
            }

            const string NodeSeparator = "\n\n";
            int prefixLength = hasPreviousContent ? NodeSeparator.Length : 0;
            string contentWithSeparator = hasPreviousContent ? NodeSeparator + elementContent : elementContent!;
            List<(DocumentNode Node, int Start, int End)> elementSegments =
                GetProjectionSourceSegments(element, elementContent!)
                    .Select(segment => (segment.Node, segment.Start + prefixLength, segment.End + prefixLength))
                    .ToList();
            if (prefixLength > 0)
            {
                elementSegments.Add((element, 0, prefixLength));
            }

            int processedCharacters = 0;
            int remainingTokenCount = _tokenizer.CountTokens(contentWithSeparator, considerNormalization: false);
            ReadOnlyMemory<char> remaining = contentWithSeparator.AsMemory();
            while (builderTokenCount + remainingTokenCount >= _maxTokensPerChunk)
            {
                int index = _tokenizer.GetIndexByTokenCount(
                    remaining.Span,
                    _maxTokensPerChunk - builderTokenCount,
                    out string? _,
                    out int addedTokenCount,
                    considerNormalization: false);

                unsafe
                {
                    fixed (char* pointer = &MemoryMarshal.GetReference(remaining.Span))
                    {
                        int start = builder.Length;
                        _ = builder.Append(pointer, index);
                        AddIntersectingSegments(sourceSegments, elementSegments, processedCharacters, index, start);
                    }
                }

                builderTokenCount += addedTokenCount;
                processedCharacters += index;
                yield return FinalizeChunk();
                remaining = remaining.Slice(index);
                remainingTokenCount = _tokenizer.CountTokens(remaining.Span, considerNormalization: false);
            }

            if (!remaining.IsEmpty)
            {
                int start = builder.Length;
                _ = builder.Append(remaining);
                AddIntersectingSegments(sourceSegments, elementSegments, processedCharacters, remaining.Length, start);
            }

            builderTokenCount += remainingTokenCount;
            hasPreviousContent = true;
        }

        if (builder.Length > 0)
        {
            yield return FinalizeChunk();
        }

        IngestionChunk FinalizeChunk()
        {
            DocumentNode[] sources = sourceSegments.Select(static segment => segment.Node).Distinct().ToArray();
            TextContent content = new(builder.ToString());
            IngestionChunk chunk = new(
                content,
                document,
                builderTokenCount,
                string.Empty,
                sources.GetSourceNodeIds(),
                sources.GetPageNumbers());
            _ = builder.Clear();
            builderTokenCount = 0;

            if (_chunkOverlap > 0)
            {
                int index = _tokenizer.GetIndexByTokenCountFromEnd(
                    content.Text,
                    _chunkOverlap,
                    out string? _,
                    out builderTokenCount,
                    considerNormalization: false);
                ReadOnlySpan<char> overlap = content.Text.AsSpan().Slice(index);
                sourceSegments = sourceSegments
                    .Where(segment => segment.End > index)
                    .Select(segment => (segment.Node, Math.Max(0, segment.Start - index), segment.End - index))
                    .ToList();
                unsafe
                {
                    fixed (char* pointer = &MemoryMarshal.GetReference(overlap))
                    {
                        _ = builder.Append(pointer, overlap.Length);
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

    private static void AddIntersectingSegments(
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
        AddNodeSegments(element, content, 0, segments);
        return segments;
    }

    private static void AddNodeSegments(
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
                AddSequenceSegments(container.Children, offset, segments);
                break;
            case DocumentTable table:
                AddTableSegments(table, offset, segments);
                break;
            case DocumentTableCell cell:
                AddSequenceSegments(cell.Content, offset, segments);
                break;
        }
    }

    private static void AddSequenceSegments(
        IEnumerable<DocumentNode> nodes,
        int offset,
        List<(DocumentNode Node, int Start, int End)> segments)
    {
        bool hasPrevious = false;
        foreach (DocumentNode node in nodes)
        {
            string projection = DocumentTextProjection.GetText(node);
            if (projection.Length == 0)
            {
                continue;
            }

            if (hasPrevious)
            {
                offset += 2;
            }

            AddNodeSegments(node, projection, offset, segments);
            offset += projection.Length;
            hasPrevious = true;
        }
    }

    private static void AddTableSegments(
        DocumentTable table,
        int offset,
        List<(DocumentNode Node, int Start, int End)> segments)
    {
        IGrouping<int, DocumentTableCell>[] rows = table.Cells.GroupBy(static cell => cell.RowIndex).ToArray();
        for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            List<(string Text, DocumentTableCell? Cell)> columns = [];
            foreach (DocumentTableCell cell in rows[rowIndex])
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
                    AddNodeSegments(cell, cellText, offset, segments);
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
