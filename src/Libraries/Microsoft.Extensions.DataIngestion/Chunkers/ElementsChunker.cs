// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.ML.Tokenizers;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion.Chunkers;

#pragma warning disable IDE0058 // Expression value is never used
#pragma warning disable SA1204 // Static members should appear before non-static members

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

        // Token count != character count, but StringBuilder will grow as needed.
        _currentChunk = new(capacity: _maxTokensPerChunk);
    }

    // Goals:
    // 1. Create chunks that do not exceed _maxTokensPerChunk when tokenized.
    // 2. Maintain context in each chunk.
    // 3. If a single IngestionDocumentElement exceeds _maxTokensPerChunk, it should be split intelligently (e.g., paragraphs can be split into sentences, tables into rows).
    internal IEnumerable<IngestionChunk<string>> Process(
        IngestionDocument document,
        string context,
        List<IngestionDocumentElement> elements,
        IReadOnlyList<int>? contextPageNumbers = null)
    {
        // Not using yield return here as we use ref structs.
        List<IngestionChunk<string>> chunks = [];

        int contextTokenCount = CountTokens(context.AsSpan());
        int totalTokenCount = contextTokenCount;
        HashSet<int> currentPageNumbers = contextPageNumbers is null
            ? []
            : [.. contextPageNumbers];

        // If the context itself exceeds the max tokens per chunk, we can't do anything.
        if (contextTokenCount >= _maxTokensPerChunk)
        {
            ThrowTokenCountExceeded();
        }

        _currentChunk.Append(context);

        for (int elementIndex = 0; elementIndex < elements.Count; elementIndex++)
        {
            IngestionDocumentElement element = elements[elementIndex];

            if (element is IngestionDocumentCodeBlock codeBlock)
            {
                Commit();
                chunks.AddRange(CreateCodeChunks(document, context, contextPageNumbers, codeBlock));
                continue;
            }

            string? semanticContent = element.GetSemanticContent();

            if (string.IsNullOrEmpty(semanticContent))
            {
                continue; // An image can come with Markdown, but no AlternativeText or Text.
            }

            int elementTokenCount = CountTokens(semanticContent.AsSpan());
            if (elementTokenCount + totalTokenCount <= _maxTokensPerChunk)
            {
                AddPageNumbers(element, currentPageNumbers);
                totalTokenCount += elementTokenCount;
                AppendNewLineAndSpan(_currentChunk, semanticContent.AsSpan());
            }
            else if (element is IngestionDocumentTable { StructuredCells: null } table)
            {
                AddPageNumbers(table, currentPageNumbers);
                ValueStringBuilder tableBuilder = new(initialCapacity: 8000);

                try
                {
                    AddMarkdownTableRow(table, rowIndex: 0, ref tableBuilder);
                    AddMarkdownTableSeparatorRow(columnCount: table.Cells.GetLength(1), ref tableBuilder);

                    int headerLength = tableBuilder.Length;
                    int headerTokenCount = CountTokens(tableBuilder.AsSpan());

                    // We can't respect the limit if context and header themselves use more tokens.
                    if (contextTokenCount + headerTokenCount >= _maxTokensPerChunk)
                    {
                        ThrowTokenCountExceeded();
                    }

                    if (headerTokenCount + totalTokenCount >= _maxTokensPerChunk)
                    {
                        // We can't add the header row, so commit what we have accumulated so far.
                        Commit();
                        AddPageNumbers(table, currentPageNumbers);
                    }

                    totalTokenCount += headerTokenCount;
                    int tableLength = headerLength;

                    int rowCount = table.Cells.GetLength(0);
                    for (int rowIndex = 1; rowIndex < rowCount; rowIndex++)
                    {
                        AddMarkdownTableRow(table, rowIndex, ref tableBuilder);

                        int lastRowTokens = CountTokens(tableBuilder.AsSpan(tableLength));

                        // Appending this row would exceed the limit.
                        if (totalTokenCount + lastRowTokens > _maxTokensPerChunk)
                        {
                            // We append the table as long as it's not just the header.
                            if (rowIndex != 1)
                            {
                                AppendNewLineAndSpan(_currentChunk, tableBuilder.AsSpan(0, tableLength - Environment.NewLine.Length));
                            }

                            // And commit the table we built so far.
                            Commit();
                            AddPageNumbers(table, currentPageNumbers);

                            // Erase previous rows and keep only the header.
                            tableBuilder.Length = headerLength;
                            tableLength = headerLength;
                            totalTokenCount += headerTokenCount;

                            if (totalTokenCount + lastRowTokens > _maxTokensPerChunk)
                            {
                                // This row is simply too big even for a fresh chunk:
                                ThrowTokenCountExceeded();
                            }

                            AddMarkdownTableRow(table, rowIndex, ref tableBuilder);
                        }

                        tableLength = tableBuilder.Length;
                        totalTokenCount += lastRowTokens;
                    }

                    AppendNewLineAndSpan(_currentChunk, tableBuilder.AsSpan(0, tableLength - Environment.NewLine.Length));
                }
                finally
                {
                    tableBuilder.Dispose();
                }
            }
            else
            {
                ReadOnlySpan<char> remainingContent = semanticContent.AsSpan();

                while (!remainingContent.IsEmpty)
                {
                    int index = _tokenizer.GetIndexByTokenCount(
                        text: remainingContent,
                        maxTokenCount: _maxTokensPerChunk - totalTokenCount,
                        out string? normalizedText,
                        out int tokenCount,
                        considerNormalization: false); // We don't normalize, just append as-is to keep original content.

                    // some tokens fit
                    if (index > 0)
                    {
                        // We could try to split by sentences or other delimiters, but it's complicated.
                        // For simplicity, we will just split at the last new line that fits.
                        // Our promise is not to go over the max token count, not to create perfect chunks.
                        int newLineIndex = remainingContent.Slice(0, index).LastIndexOf('\n');
                        if (newLineIndex > 0)
                        {
                            index = newLineIndex + 1; // We want to include the new line character (works for "\r\n" as well).
                            tokenCount = CountTokens(remainingContent.Slice(0, index));
                        }

                        AddPageNumbers(element, currentPageNumbers);
                        totalTokenCount += tokenCount;
                        ReadOnlySpan<char> spanToAppend = remainingContent.Slice(0, index);
                        AppendNewLineAndSpan(_currentChunk, spanToAppend);
                        remainingContent = remainingContent.Slice(index);
                    }
                    else if (totalTokenCount == contextTokenCount)
                    {
                        // We are at the beginning of a chunk, and even a single token does not fit.
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

        Commit();
        _currentChunk.Clear();

        return chunks;

        void Commit()
        {
            if (totalTokenCount <= contextTokenCount)
            {
                return;
            }

            chunks.Add(new(
                _currentChunk.ToString(),
                document,
                context,
                [.. currentPageNumbers.OrderBy(static pageNumber => pageNumber)]));

            // We keep the context in the current chunk as it's the same for all elements.
            _currentChunk.Remove(
                startIndex: context.Length,
                length: _currentChunk.Length - context.Length);
            totalTokenCount = contextTokenCount;
            currentPageNumbers.Clear();
            if (contextPageNumbers is not null)
            {
                currentPageNumbers.UnionWith(contextPageNumbers);
            }
        }

    }

    private IEnumerable<IngestionChunk<string>> CreateCodeChunks(
        IngestionDocument document,
        string context,
        IReadOnlyList<int>? contextPageNumbers,
        IngestionDocumentCodeBlock codeBlock)
    {
        string code = codeBlock.Text!;
        int longestBacktickRun = GetLongestBacktickRun(code);
        string fence = new('`', Math.Max(3, longestBacktickRun + 1));
        HashSet<int> pageNumbers = contextPageNumbers is null
            ? []
            : [.. contextPageNumbers];
        AddPageNumbers(codeBlock, pageNumbers);

        ReadOnlyMemory<char> remaining = code.AsMemory();
        while (!remaining.IsEmpty)
        {
            int segmentLength = GetSegmentLength(
                remaining.Span,
                value => RenderCode(context, fence, value));
            string segment = remaining.Slice(0, segmentLength).ToString();
            yield return new(
                RenderCode(context, fence, segment),
                document,
                context,
                [.. pageNumbers.OrderBy(static pageNumber => pageNumber)]);
            remaining = remaining.Slice(segmentLength);
        }
    }

    private int GetSegmentLength(ReadOnlySpan<char> remaining, Func<string, string> render)
    {
        int tokenBudget = _maxTokensPerChunk;

        while (tokenBudget > 0)
        {
            int index = _tokenizer.GetIndexByTokenCount(
                remaining,
                tokenBudget,
                out _,
                out _,
                considerNormalization: false);
            if (index == 0)
            {
                tokenBudget--;
                continue;
            }

            if (index < remaining.Length)
            {
                int newLineIndex = remaining.Slice(0, index).LastIndexOf('\n');
                if (newLineIndex > 0)
                {
                    index = newLineIndex + 1;
                }
            }

            string candidate = remaining.Slice(0, index).ToString();
            int renderedTokenCount = CountTokens(render(candidate).AsSpan());
            if (renderedTokenCount <= _maxTokensPerChunk)
            {
                return index;
            }

            tokenBudget -= Math.Max(1, renderedTokenCount - _maxTokensPerChunk);
        }

        ThrowTokenCountExceeded();
        return 0;
    }

    private static string RenderCode(string context, string fence, string code)
    {
        StringBuilder builder = new();
        if (!string.IsNullOrEmpty(context))
        {
            builder.AppendLine(context);
        }

        builder.AppendLine(fence);
        builder.Append(code);
#pragma warning disable EA0003 // The character overload is unavailable on legacy target frameworks.
        if (!code.EndsWith("\n", StringComparison.Ordinal))
#pragma warning restore EA0003
        {
            builder.AppendLine();
        }

        builder.Append(fence);
        return builder.ToString();
    }

    private static int GetLongestBacktickRun(string value)
    {
        int longest = 0;
        int current = 0;
        foreach (char character in value)
        {
            if (character == '`')
            {
                longest = Math.Max(longest, ++current);
            }
            else
            {
                current = 0;
            }
        }

        return longest;
    }

    private static void ThrowTokenCountExceeded()
        => throw new InvalidOperationException("Can't fit in the current chunk. Consider increasing max tokens per chunk.");

    private static void AddPageNumbers(
        IngestionDocumentElement element,
        HashSet<int> pageNumbers)
    {
        if (element.PageNumber is int pageNumber)
        {
            _ = pageNumbers.Add(pageNumber);
        }

        switch (element)
        {
            case IngestionDocumentSection section:
                foreach (IngestionDocumentElement nested in section.Elements)
                {
                    AddPageNumbers(nested, pageNumbers);
                }
                break;

            case IngestionDocumentTable { StructuredCells: not null } table:
                foreach (IngestionDocumentTableCell cell in table.StructuredCells)
                {
                    foreach (IngestionDocumentElement nested in cell.Elements)
                    {
                        AddPageNumbers(nested, pageNumbers);
                    }
                }
                break;

            case IngestionDocumentTable table:
                foreach (IngestionDocumentElement? nested in table.Cells)
                {
                    if (nested is not null)
                    {
                        AddPageNumbers(nested, pageNumbers);
                    }
                }
                break;
        }
    }

    private static void AppendNewLineAndSpan(StringBuilder stringBuilder, ReadOnlySpan<char> chars)
    {
        // Don't start an empty chunk (no context provided) with a new line.
        if (stringBuilder.Length > 0)
        {
            stringBuilder.AppendLine();
        }

#if NET
        stringBuilder.Append(chars);
#else
        stringBuilder.Append(chars.ToString());
#endif
    }

    private static void AddMarkdownTableRow(IngestionDocumentTable table, int rowIndex, ref ValueStringBuilder vsb)
    {
        for (int columnIndex = 0; columnIndex < table.Cells.GetLength(1); columnIndex++)
        {
            vsb.Append('|');
            vsb.Append(' ');
            string? cellContent = table.Cells[rowIndex, columnIndex] switch
            {
                null => null,
                IngestionDocumentImage img => img.AlternativeText ?? img.Text,
                IngestionDocumentElement other => other.GetMarkdown()
            };
            vsb.Append(cellContent);
            vsb.Append(' ');
        }

        vsb.Append('|');
        vsb.Append(Environment.NewLine);
    }

    private static void AddMarkdownTableSeparatorRow(int columnCount, ref ValueStringBuilder vsb)
    {
        const int DashCount = 3; // The dash count does not need to match the header length.
        for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            vsb.Append('|');
            vsb.Append(' ');
            vsb.Append('-', DashCount);
            vsb.Append(' ');
        }

        vsb.Append('|');
        vsb.Append(Environment.NewLine);
    }

    private int CountTokens(ReadOnlySpan<char> input)
        => _tokenizer.CountTokens(input, considerNormalization: false);
}

#pragma warning restore SA1204 // Static members should appear before non-static members
