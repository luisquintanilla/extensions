// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Documents;
using Microsoft.ML.Tokenizers;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion.Chunkers;

internal sealed class ElementsChunker
{
    private readonly Tokenizer _tokenizer;
    private readonly int _maxTokensPerChunk;
    private readonly StringBuilder _currentChunk;

    internal ElementsChunker(IngestionChunkerOptions options)
    {
        _ = Throw.IfNull(options);
        _tokenizer = options.Tokenizer;
        _maxTokensPerChunk = options.MaxTokensPerChunk;
        _currentChunk = new(capacity: _maxTokensPerChunk);
    }

    internal IEnumerable<IngestionChunk<string>> Process(
        IngestionDocument document,
        string context,
        IReadOnlyList<DocumentNode> elements,
        IReadOnlyList<DocumentNode>? contextNodes = null)
    {
        List<IngestionChunk<string>> chunks = [];
        List<DocumentNode> contributingNodes = contextNodes is null ? [] : [.. contextNodes];
        int contextTokenCount = CountTokens(context.AsSpan());
        int totalTokenCount = contextTokenCount;

        if (contextTokenCount >= _maxTokensPerChunk)
        {
            ThrowTokenCountExceeded();
        }

        _currentChunk.Append(context);

        foreach (DocumentNode element in elements)
        {
            string? semanticContent = element.GetSemanticContent();
            if (string.IsNullOrEmpty(semanticContent))
            {
                continue;
            }

            int elementTokenCount = CountTokens(semanticContent.AsSpan());
            if (elementTokenCount + totalTokenCount <= _maxTokensPerChunk)
            {
                totalTokenCount += elementTokenCount;
                AppendNewLineAndSpan(_currentChunk, semanticContent.AsSpan());
                contributingNodes.Add(element);
            }
            else if (element is DocumentTable table)
            {
                if (totalTokenCount > contextTokenCount)
                {
                    Commit();
                }

                List<DocumentNode> tableNodes = [.. contributingNodes, .. GetTableNodes(table)];
                foreach (string tableChunk in GetTableChunks(table))
                {
                    string content = context.Length == 0 ? tableChunk : context + "\n" + tableChunk;
                    if (CountTokens(content.AsSpan()) > _maxTokensPerChunk)
                    {
                        ThrowTokenCountExceeded();
                    }

                    chunks.Add(new(
                        content,
                        document,
                        context,
                        tableNodes.GetSourceNodeIds(),
                        tableNodes.GetPageNumbers()));
                }
            }
            else
            {
                ReadOnlySpan<char> remainingContent = semanticContent.AsSpan();
                while (!remainingContent.IsEmpty)
                {
                    int index = _tokenizer.GetIndexByTokenCount(
                        remainingContent,
                        _maxTokensPerChunk - totalTokenCount,
                        out string? _,
                        out int tokenCount,
                        considerNormalization: false);

                    if (index > 0)
                    {
                        int newLineIndex = remainingContent.Slice(0, index).LastIndexOf('\n');
                        if (newLineIndex > 0)
                        {
                            index = newLineIndex + 1;
                            tokenCount = CountTokens(remainingContent.Slice(0, index));
                        }

                        totalTokenCount += tokenCount;
                        AppendNewLineAndSpan(_currentChunk, remainingContent.Slice(0, index));
                        if (!contributingNodes.Contains(element))
                        {
                            contributingNodes.Add(element);
                        }

                        remainingContent = remainingContent.Slice(index);
                    }
                    else if (totalTokenCount == contextTokenCount)
                    {
                        ThrowTokenCountExceeded();
                    }

                    if (!remainingContent.IsEmpty)
                    {
                        Commit();
                    }
                }
            }

            if (totalTokenCount == _maxTokensPerChunk)
            {
                Commit();
            }
        }

        if (totalTokenCount > contextTokenCount)
        {
            AddChunk();
        }

        _currentChunk.Clear();
        return chunks;

        void AddChunk() =>
            chunks.Add(new(
                _currentChunk.ToString(),
                document,
                context,
                contributingNodes.GetSourceNodeIds(),
                contributingNodes.GetPageNumbers()));

        void Commit()
        {
            AddChunk();
            _currentChunk.Remove(context.Length, _currentChunk.Length - context.Length);
            totalTokenCount = contextTokenCount;
            contributingNodes.Clear();
            if (contextNodes is not null)
            {
                contributingNodes.AddRange(contextNodes);
            }
        }
    }

    private static void AppendNewLineAndSpan(StringBuilder builder, ReadOnlySpan<char> chars)
    {
        if (builder.Length > 0)
        {
            _ = builder.Append('\n');
        }

#if NET
        _ = builder.Append(chars);
#else
        _ = builder.Append(chars.ToString());
#endif
    }

    private static IEnumerable<DocumentNode> GetTableNodes(DocumentTable table)
    {
        yield return table;
        foreach (DocumentTableCell cell in table.Cells)
        {
            yield return cell;
            foreach (DocumentNode node in cell.Content)
            {
                yield return node;
            }
        }
    }

    private static IEnumerable<string> GetTableChunks(DocumentTable table)
    {
        IGrouping<int, DocumentTableCell>[] rows = table.Cells
            .GroupBy(static cell => cell.RowIndex)
            .OrderBy(static row => row.Key)
            .ToArray();
        if (rows.Length == 0)
        {
            yield break;
        }

        string header = GetTableRow(rows[0]);
        if (rows.Length == 1)
        {
            yield return header;
            yield break;
        }

        for (int rowIndex = 1; rowIndex < rows.Length; rowIndex++)
        {
            yield return header + "\n" + GetTableRow(rows[rowIndex]);
        }
    }

    private static string GetTableRow(IEnumerable<DocumentTableCell> cells)
    {
        List<string> columns = [];
        foreach (DocumentTableCell cell in cells.OrderBy(static cell => cell.ColumnIndex))
        {
            while (columns.Count < cell.ColumnIndex)
            {
                columns.Add(string.Empty);
            }

            columns.Add(DocumentTextProjection.GetText(cell.Content));
            for (int span = 1; span < cell.ColumnSpan; span++)
            {
                columns.Add(string.Empty);
            }
        }

        return string.Join("\t", columns);
    }

    private static void ThrowTokenCountExceeded() =>
        throw new InvalidOperationException("Can't fit in the current chunk. Consider increasing max tokens per chunk.");

    private int CountTokens(ReadOnlySpan<char> input) => _tokenizer.CountTokens(input, considerNormalization: false);
}
