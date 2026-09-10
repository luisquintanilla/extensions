// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Writers.Tests;

public sealed class VectorStoreWriterBehaviorTests
{
    [Fact]
    public async Task WriteAsync_BatchesUpsertsAtConfiguredBatchSize()
    {
        using RecordingVectorStoreCollection collection = new();
        using VectorStoreWriter<IngestionChunkVectorRecord> writer = new(
            collection,
            new VectorStoreWriterOptions
            {
                BatchTokenCount = 3,
                IncrementalIngestion = false,
            });
        IngestionDocument document = TestDocuments.Create("batch-document");
        IngestionChunk[] chunks = Enumerable.Range(1, 7)
            .Select(index => CreateChunk($"chunk-{index}", document))
            .ToArray();

        await writer.WriteAsync(chunks.ToAsyncEnumerable());

        Assert.Equal([3, 3, 1], collection.CompletedUpsertBatches.Select(batch => batch.Count));
        Assert.Equal(
            chunks.Select(chunk => ((TextContent)chunk.Content).Text),
            collection.CompletedUpsertBatches.SelectMany(batch => batch).Select(GetText));
        Assert.Equal(7, collection.Records.Count);
        Assert.Equal(
            ["ensure", "upsert:chunk-1,chunk-2,chunk-3", "upsert:chunk-4,chunk-5,chunk-6", "upsert:chunk-7"],
            collection.Operations);
    }

    [Fact]
    public async Task WriteAsync_BatchFailure_PreservesCompletedCallOrderAndSkipsLaterMutation()
    {
        IngestionDocument document = TestDocuments.Create("failure-document");
        using RecordingVectorStoreCollection collection = new() { FailOnUpsertCall = 2 };
        collection.AddExisting(CreateRecord("old-chunk", document.Identifier));
        using VectorStoreWriter<IngestionChunkVectorRecord> writer = new(
            collection,
            new VectorStoreWriterOptions
            {
                BatchTokenCount = 2,
                IncrementalIngestion = true,
            });
        IngestionChunk[] chunks = Enumerable.Range(1, 5)
            .Select(index => CreateChunk($"new-{index}", document))
            .ToArray();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => writer.WriteAsync(chunks.ToAsyncEnumerable()));

        Assert.Equal("Injected failure on upsert call 2.", exception.Message);
        Assert.Single(collection.CompletedUpsertBatches);
        Assert.Equal(["new-1", "new-2"], collection.CompletedUpsertBatches[0].Select(GetText));
        Assert.Equal([2, 2], collection.UpsertAttempts.Select(batch => batch.Count));
        Assert.Equal(["old-chunk", "new-1", "new-2"], collection.Records.Select(GetText));
        Assert.Equal(
            ["ensure", "get", "upsert:new-1,new-2", "upsert-failed:new-3,new-4"],
            collection.Operations);
        Assert.DoesNotContain(collection.Operations, operation => operation.StartsWith("delete:", StringComparison.Ordinal));
        Assert.DoesNotContain(collection.UpsertAttempts.SelectMany(batch => batch), record => GetText(record) == "new-5");
    }

    [Fact]
    public async Task WriteAsync_CancellationBetweenBatches_StopsBeforeIncrementalDelete()
    {
        using CancellationTokenSource cancellationSource = new();
        IngestionDocument document = TestDocuments.Create("cancellation-document");
        using RecordingVectorStoreCollection collection = new()
        {
            CancelOnUpsertCall = 1,
            CancellationSource = cancellationSource,
        };
        collection.AddExisting(CreateRecord("old-chunk", document.Identifier));
        using VectorStoreWriter<IngestionChunkVectorRecord> writer = new(
            collection,
            new VectorStoreWriterOptions
            {
                BatchTokenCount = 2,
                IncrementalIngestion = true,
            });
        IngestionChunk[] chunks = Enumerable.Range(1, 5)
            .Select(index => CreateChunk($"new-{index}", document))
            .ToArray();

        OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => writer.WriteAsync(WithCancellationChecks(chunks), cancellationSource.Token));

        Assert.Equal(cancellationSource.Token, exception.CancellationToken);
        Assert.True(cancellationSource.IsCancellationRequested);
        Assert.Single(collection.UpsertAttempts);
        Assert.Single(collection.CompletedUpsertBatches);
        Assert.Equal(["new-1", "new-2"], collection.CompletedUpsertBatches[0].Select(GetText));
        Assert.Equal(cancellationSource.Token, collection.UpsertCancellationTokens[0]);
        Assert.Equal(["old-chunk", "new-1", "new-2"], collection.Records.Select(GetText));
        Assert.Equal(["ensure", "get", "upsert:new-1,new-2"], collection.Operations);
    }

    [Fact]
    public async Task WriteAsync_IncrementalReplacementDeletesOldRecordsOnlyAfterSuccessfulUpsert()
    {
        IngestionDocument document = TestDocuments.Create("replacement-document");
        using RecordingVectorStoreCollection collection = new();
        collection.AddExisting(CreateRecord("old-1", document.Identifier));
        collection.AddExisting(CreateRecord("old-2", document.Identifier));
        using VectorStoreWriter<IngestionChunkVectorRecord> writer = new(
            collection,
            new VectorStoreWriterOptions
            {
                BatchTokenCount = 10,
                IncrementalIngestion = true,
            });
        IngestionChunk[] replacement =
        [
            CreateChunk("replacement-1", document),
            CreateChunk("replacement-2", document),
        ];

        await writer.WriteAsync(replacement.ToAsyncEnumerable());

        Assert.Equal(
            ["ensure", "get", "upsert:replacement-1,replacement-2", "delete:old-1,old-2"],
            collection.Operations);
        Assert.Equal(["replacement-1", "replacement-2"], collection.Records.Select(GetText));
        Assert.Single(collection.CompletedUpsertBatches);
        Assert.Equal(2, collection.DeletedKeys.Count);
    }

    [Fact]
    public async Task WriteAsync_MultipleDocumentsDoesNotMutateCollection()
    {
        using RecordingVectorStoreCollection collection = new();
        collection.AddExisting(CreateRecord("existing", "existing-document"));
        using VectorStoreWriter<IngestionChunkVectorRecord> writer = new(
            collection,
            new VectorStoreWriterOptions { IncrementalIngestion = false });
        IngestionChunk[] chunks =
        [
            CreateChunk("first", TestDocuments.Create("document-1")),
            CreateChunk("second", TestDocuments.Create("document-2")),
        ];

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WriteAsync(chunks.ToAsyncEnumerable()));

        Assert.Empty(collection.UpsertAttempts);
        Assert.Empty(collection.DeletedKeys);
        Assert.Equal(["existing"], collection.Records.Select(GetText));
        Assert.Equal(["ensure"], collection.Operations);
    }

    [Fact]
    public void EmbeddingProperty_ReturnsOriginalAIContent()
    {
        TextContent textContent = new("original text");
        DataContent dataContent = new(new byte[] { 4, 2, 1 }, "application/octet-stream");
        IngestionChunkVectorRecord record = new() { Content = textContent };

        Assert.Same(textContent, record.Embedding);
        Assert.Contains("original text", record.SerializedContent, StringComparison.Ordinal);

        record.Content = dataContent;

        Assert.Same(dataContent, record.Embedding);
        Assert.DoesNotContain("original text", record.SerializedContent, StringComparison.Ordinal);
        Assert.Contains("application/octet-stream", record.SerializedContent, StringComparison.Ordinal);
    }

    private static IngestionChunk CreateChunk(string text, IngestionDocument document)
        => new(new TextContent(text), document, tokenCount: 1);

    private static IngestionChunkVectorRecord CreateRecord(string text, string documentId)
        => new()
        {
            Key = Guid.NewGuid(),
            Content = new TextContent(text),
            DocumentId = documentId,
        };

    private static string GetText(IngestionChunkVectorRecord record)
        => Assert.IsType<TextContent>(record.Content).Text;

    private static async IAsyncEnumerable<IngestionChunk> WithCancellationChecks(
        IEnumerable<IngestionChunk> chunks,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (IngestionChunk chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return chunk;
            await Task.Yield();
        }
    }

    private sealed class RecordingVectorStoreCollection : VectorStoreCollection<Guid, IngestionChunkVectorRecord>
    {
        private readonly List<IngestionChunkVectorRecord> _records = [];

        public override string Name => "recording";

        public IReadOnlyList<IngestionChunkVectorRecord> Records => _records;

        public List<IReadOnlyList<IngestionChunkVectorRecord>> UpsertAttempts { get; } = [];

        public List<IReadOnlyList<IngestionChunkVectorRecord>> CompletedUpsertBatches { get; } = [];

        public List<CancellationToken> UpsertCancellationTokens { get; } = [];

        public List<Guid> DeletedKeys { get; } = [];

        public List<string> Operations { get; } = [];

        public int? FailOnUpsertCall { get; set; }

        public int? CancelOnUpsertCall { get; set; }

        public CancellationTokenSource? CancellationSource { get; set; }

        public void AddExisting(IngestionChunkVectorRecord record) => _records.Add(record);

        public override Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public override Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Operations.Add("ensure");
            return Task.CompletedTask;
        }

        public override Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _records.Clear();
            return Task.CompletedTask;
        }

        public override Task<IngestionChunkVectorRecord?> GetAsync(
            Guid key,
            RecordRetrievalOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IngestionChunkVectorRecord?>(
                _records.SingleOrDefault(record => record.Key == key));
        }

        public override async IAsyncEnumerable<IngestionChunkVectorRecord> GetAsync(
            Expression<Func<IngestionChunkVectorRecord, bool>> filter,
            int top,
            FilteredRecordRetrievalOptions<IngestionChunkVectorRecord>? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Operations.Add("get");
            IEnumerable<IngestionChunkVectorRecord> records = _records
                .Where(filter.Compile())
                .Skip(options?.Skip ?? 0)
                .Take(top)
                .ToArray();

            foreach (IngestionChunkVectorRecord record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return record;
                await Task.Yield();
            }
        }

        public override Task DeleteAsync(Guid key, CancellationToken cancellationToken = default)
            => DeleteAsync([key], cancellationToken);

        public override Task DeleteAsync(IEnumerable<Guid> keys, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Guid[] materializedKeys = keys.ToArray();
            DeletedKeys.AddRange(materializedKeys);
            string[] deletedTexts = _records
                .Where(record => materializedKeys.Contains(record.Key))
                .Select(GetText)
                .ToArray();
            Operations.Add($"delete:{string.Join(",", deletedTexts)}");
            _records.RemoveAll(record => materializedKeys.Contains(record.Key));
            return Task.CompletedTask;
        }

        public override Task UpsertAsync(IngestionChunkVectorRecord record, CancellationToken cancellationToken = default)
            => UpsertAsync([record], cancellationToken);

        public override Task UpsertAsync(
            IEnumerable<IngestionChunkVectorRecord> records,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IngestionChunkVectorRecord[] batch = records.ToArray();
            UpsertAttempts.Add(batch);
            UpsertCancellationTokens.Add(cancellationToken);
            int callNumber = UpsertAttempts.Count;
            string batchDescription = string.Join(",", batch.Select(GetText));

            if (FailOnUpsertCall == callNumber)
            {
                Operations.Add($"upsert-failed:{batchDescription}");
                throw new InvalidOperationException($"Injected failure on upsert call {callNumber}.");
            }

            foreach (IngestionChunkVectorRecord record in batch)
            {
                if (record.Key == Guid.Empty)
                {
                    record.Key = Guid.NewGuid();
                }

                _records.RemoveAll(existing => existing.Key == record.Key);
                _records.Add(record);
            }

            CompletedUpsertBatches.Add(batch);
            Operations.Add($"upsert:{batchDescription}");

            if (CancelOnUpsertCall == callNumber)
            {
                CancellationSource!.Cancel();
            }

            return Task.CompletedTask;
        }

        public override async IAsyncEnumerable<VectorSearchResult<IngestionChunkVectorRecord>> SearchAsync<TInput>(
            TInput searchValue,
            int top,
            VectorSearchOptions<IngestionChunkVectorRecord>? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }

        public override object? GetService(Type serviceType, object? serviceKey = null) => null;
    }
}
