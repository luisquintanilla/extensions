// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.VectorData.InMemory;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion.Chunkers;
using Microsoft.Extensions.VectorData;
using Microsoft.ML.Tokenizers;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Tests;

#pragma warning disable CA2000 // The pipeline owns its writer; disposable seam fakes intentionally verify that other seams are not owned.

public class IngestionPipelineTests
{
    [Fact]
    public async Task PreservesNonGenericChunkContractThroughWriting()
    {
        FileInfo source = new(Path.GetTempFileName());
        try
        {
            TestReader reader = new((stream, identifier, mediaType, cancellationToken) =>
                Task.FromResult(TestDocuments.Create(identifier, TestDocuments.Text("node", "content", pageNumber: 1))));
            CapturingWriter writer = new();
            using IngestionPipeline pipeline = new(
                reader,
                new SectionChunker(new(TiktokenTokenizer.CreateForModel("gpt-4")) { MaxTokensPerChunk = 100 }),
                writer);

            await foreach (IngestionResult result in pipeline.ProcessAsync([source]))
            {
                Assert.True(result.Succeeded);
            }

            IngestionChunk chunk = Assert.Single(writer.Chunks);
            Assert.IsType<TextContent>(chunk.Content);
            Assert.True(chunk.TokenCount > 0);
            Assert.Equal([1], chunk.PageNumbers);
        }
        finally
        {
            source.Delete();
        }
    }

    private sealed class CapturingWriter : IngestionChunkWriter
    {
        public List<IngestionChunk> Chunks { get; } = [];

        public override async Task WriteAsync(IAsyncEnumerable<IngestionChunk> chunks, CancellationToken cancellationToken = default)
        {
            await foreach (IngestionChunk chunk in chunks.WithCancellation(cancellationToken))
            {
                Chunks.Add(chunk);
            }
        }
    }

    [Fact]
    public async Task RunAsync_InvokesNonGenericSeamsInConfiguredOrder()
    {
        string directory = CreateTemporaryDirectory();
        FileInfo source = CreateFile(directory, "ordered.txt");
        try
        {
            List<string> calls = [];
            IngestionDocument readDocument = TestDocuments.Create("read-document");
            IngestionDocument firstProcessedDocument = TestDocuments.Create("first-processed-document");
            IngestionDocument secondProcessedDocument = TestDocuments.Create("second-processed-document");
            IngestionChunk chunked = new(new TextContent("chunked"), secondProcessedDocument, 2);
            IngestionChunk firstProcessedChunk = new(new TextContent("first processed"), secondProcessedDocument, 3);
            IngestionChunk secondProcessedChunk = new(new TextContent("second processed"), secondProcessedDocument, 4);

            RecordingReader reader = new(calls, (_, _) => Task.FromResult(readDocument));
            RecordingDocumentProcessor firstDocumentProcessor = new(
                "document-processor-1",
                calls,
                (document, _) =>
                {
                    Assert.Same(readDocument, document);
                    return Task.FromResult(firstProcessedDocument);
                });
            RecordingDocumentProcessor secondDocumentProcessor = new(
                "document-processor-2",
                calls,
                (document, _) =>
                {
                    Assert.Same(firstProcessedDocument, document);
                    return Task.FromResult(secondProcessedDocument);
                });
            RecordingChunker chunker = new("chunker", calls, document =>
            {
                Assert.Same(secondProcessedDocument, document);
                return [chunked];
            });
            RecordingChunkProcessor firstChunkProcessor = new(
                "chunk-processor-1",
                calls,
                chunk =>
                {
                    Assert.Same(chunked, chunk);
                    return firstProcessedChunk;
                });
            RecordingChunkProcessor secondChunkProcessor = new(
                "chunk-processor-2",
                calls,
                chunk =>
                {
                    Assert.Same(firstProcessedChunk, chunk);
                    return secondProcessedChunk;
                });
            RecordingWriter writer = new(calls);

            using IngestionPipeline pipeline = new(reader, chunker, writer);
            pipeline.DocumentProcessors.Add(firstDocumentProcessor);
            pipeline.DocumentProcessors.Add(secondDocumentProcessor);
            pipeline.ChunkProcessors.Add(firstChunkProcessor);
            pipeline.ChunkProcessors.Add(secondChunkProcessor);

            IngestionResult result = Assert.Single(await pipeline.ProcessAsync([source]).ToListAsync());

            Assert.True(result.Succeeded);
            Assert.Same(secondProcessedDocument, result.Document);
            Assert.Equal("second-processed-document", result.DocumentId);
            Assert.Equal(
                [
                    "reader:ordered.txt",
                    "document-processor-1:read-document",
                    "document-processor-2:first-processed-document",
                    "chunker:second-processed-document",
                    "chunk-processor-1:second-processed-document",
                    "chunk-processor-2:second-processed-document",
                    "writer:second-processed-document",
                ],
                calls);
            Assert.Same(readDocument, Assert.Single(firstDocumentProcessor.Inputs));
            Assert.Same(firstProcessedDocument, Assert.Single(secondDocumentProcessor.Inputs));
            Assert.Same(secondProcessedDocument, Assert.Single(chunker.Inputs));
            Assert.Same(chunked, Assert.Single(firstChunkProcessor.Inputs));
            Assert.Same(firstProcessedChunk, Assert.Single(secondChunkProcessor.Inputs));
            Assert.Same(secondProcessedChunk, Assert.Single(Assert.Single(writer.Batches)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_PreservesMixedTextContentAndDataContent()
    {
        string directory = CreateTemporaryDirectory();
        FileInfo source = CreateFile(directory, "mixed.bin");
        try
        {
            IngestionDocument document = TestDocuments.Create("mixed-document");
            TextContent text = new("alpha text");
            DataContent data = new(new byte[] { 0x10, 0x20, 0x30 }, "application/octet-stream");
            IngestionChunk textChunk = new(text, document, 3);
            IngestionChunk dataChunk = new(data, document, 5);
            List<string> calls = [];
            RecordingReader reader = new(calls, (_, _) => Task.FromResult(document));
            RecordingChunker chunker = new("chunker", calls, _ => [textChunk, dataChunk]);
            RecordingChunkProcessor processor = new("chunk-processor", calls, static chunk => chunk);
            RecordingWriter writer = new(calls);

            using IngestionPipeline pipeline = new(reader, chunker, writer);
            pipeline.ChunkProcessors.Add(processor);

            IngestionResult result = Assert.Single(await pipeline.ProcessAsync([source]).ToListAsync());

            Assert.True(result.Succeeded);
            IReadOnlyList<IngestionChunk> written = Assert.Single(writer.Batches);
            Assert.Equal(2, written.Count);
            Assert.Same(textChunk, written[0]);
            Assert.Same(dataChunk, written[1]);
            Assert.Same(text, Assert.IsType<TextContent>(written[0].Content));
            DataContent writtenData = Assert.IsType<DataContent>(written[1].Content);
            Assert.Same(data, writtenData);
            Assert.Equal(new byte[] { 0x10, 0x20, 0x30 }, writtenData.Data.ToArray());
            Assert.Equal("application/octet-stream", writtenData.MediaType);
            Assert.Equal([3, 5], written.Select(static chunk => chunk.TokenCount));
            Assert.Equal(
                [
                    "reader:mixed.bin",
                    "chunker:mixed-document",
                    "chunk-processor:mixed-document",
                    "writer:mixed-document",
                    "chunker:mixed-document",
                    "chunk-processor:mixed-document",
                    "writer:mixed-document",
                ],
                calls);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_WithEmbeddingProvider_PersistsProviderGeneratedEmbedding()
    {
        string directory = CreateTemporaryDirectory();
        FileInfo source = CreateFile(directory, "embedded.txt");
        try
        {
            using CancellationTokenSource cancellationSource = new();
            IngestionDocument document = TestDocuments.Create("embedded-document");
            TextContent alpha = new("alpha");
            TextContent beta = new("beta");
            IngestionChunk alphaChunk = new(alpha, document, 1);
            IngestionChunk betaChunk = new(beta, document, 1);
            List<string> calls = [];
            RecordingReader reader = new(calls, (_, _) => Task.FromResult(document));
            RecordingChunker chunker = new("chunker", calls, _ => [alphaChunk, betaChunk]);
            using RecordingEmbeddingGenerator embeddingGenerator = new();
            using VectorStore vectorStore = new InMemoryVectorStore(new() { EmbeddingGenerator = embeddingGenerator });
            VectorStoreCollection<Guid, IngestionChunkVectorRecord> collection =
                vectorStore.GetIngestionRecordCollection("pipeline-embeddings", RecordingEmbeddingGenerator.DimensionCount);
            VectorStoreWriter<IngestionChunkVectorRecord> writer = new(collection);

            using (IngestionPipeline pipeline = new(reader, chunker, writer))
            {
                IngestionResult result = Assert.Single(
                    await pipeline.ProcessAsync([source], cancellationSource.Token).ToListAsync());
                Assert.True(result.Succeeded);
                Assert.Same(document, result.Document);
            }

            IReadOnlyList<AIContent> embeddingInputs = Assert.Single(embeddingGenerator.Calls);
            Assert.Equal(2, embeddingInputs.Count);
            Assert.Same(alpha, embeddingInputs[0]);
            Assert.Same(beta, embeddingInputs[1]);
            Assert.Equal(cancellationSource.Token, Assert.Single(embeddingGenerator.Tokens));

            VectorSearchResult<IngestionChunkVectorRecord> alphaMatch = await collection
                .SearchAsync(new ReadOnlyMemory<float>([1, 0, 0, 0]), top: 1)
                .SingleAsync();
            VectorSearchResult<IngestionChunkVectorRecord> betaMatch = await collection
                .SearchAsync(new ReadOnlyMemory<float>([0, 1, 0, 0]), top: 1)
                .SingleAsync();

            Assert.Equal("alpha", Assert.IsType<TextContent>(alphaMatch.Record.Content).Text);
            Assert.Equal("beta", Assert.IsType<TextContent>(betaMatch.Record.Content).Text);
            Assert.Equal("embedded-document", alphaMatch.Record.DocumentId);
            Assert.Equal("embedded-document", betaMatch.Record.DocumentId);
            Assert.NotEqual(alphaMatch.Record.Key, betaMatch.Record.Key);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_MultipleFiles_IsolatesPerFileFailureAndPreservesResultOrder()
    {
        string directory = CreateTemporaryDirectory();
        FileInfo first = CreateFile(directory, "first.txt");
        FileInfo failing = CreateFile(directory, "failing.txt");
        FileInfo third = CreateFile(directory, "third.txt");
        try
        {
            InvalidDataException expectedFailure = new("cannot read failing.txt");
            List<string> calls = [];
            RecordingReader reader = new(calls, (identifier, _) =>
            {
                if (Path.GetFileName(identifier) == failing.Name)
                {
                    throw expectedFailure;
                }

                return Task.FromResult(TestDocuments.Create($"doc-{Path.GetFileName(identifier)}"));
            });
            RecordingChunker chunker = new(
                "chunker",
                calls,
                document => [new IngestionChunk(new TextContent(document.Identifier), document, 2)]);
            RecordingWriter writer = new(calls);

            using IngestionPipeline pipeline = new(reader, chunker, writer);
            List<IngestionResult> results = await pipeline.ProcessAsync([first, failing, third]).ToListAsync();

            Assert.Equal(3, results.Count);
            Assert.Collection(
                results,
                result =>
                {
                    Assert.True(result.Succeeded);
                    Assert.Equal("doc-first.txt", result.DocumentId);
                    Assert.NotNull(result.Document);
                },
                result =>
                {
                    Assert.False(result.Succeeded);
                    Assert.Equal(failing.FullName, result.DocumentId);
                    Assert.Null(result.Document);
                    Assert.Same(expectedFailure, result.Exception);
                },
                result =>
                {
                    Assert.True(result.Succeeded);
                    Assert.Equal("doc-third.txt", result.DocumentId);
                    Assert.NotNull(result.Document);
                });
            Assert.Equal(3, reader.Identifiers.Count);
            Assert.Equal([first.FullName, failing.FullName, third.FullName], reader.Identifiers);
            Assert.Equal(2, writer.WriteCount);
            Assert.Equal(
                ["doc-first.txt", "doc-third.txt"],
                writer.Batches.Select(static batch => Assert.Single(batch).Document.Identifier));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_DirectoryEnumeratesExpectedLocalFiles()
    {
        string directory = CreateTemporaryDirectory();
        string nestedDirectory = Directory.CreateDirectory(Path.Combine(directory, "nested")).FullName;
        FileInfo alpha = CreateFile(directory, "alpha.txt");
        FileInfo beta = CreateFile(directory, "beta.txt");
        FileInfo nested = CreateFile(nestedDirectory, "nested.txt");
        _ = CreateFile(directory, "excluded.bin");
        try
        {
            List<string> calls = [];
            RecordingReader reader = new(
                calls,
                (identifier, _) => Task.FromResult(TestDocuments.Create(Path.GetFileName(identifier))));
            RecordingChunker chunker = new(
                "chunker",
                calls,
                document => [new IngestionChunk(new TextContent(document.Identifier), document, 1)]);
            RecordingWriter writer = new(calls);
            using IngestionPipeline pipeline = new(reader, chunker, writer);

            List<IngestionResult> results = await pipeline
                .ProcessAsync(new DirectoryInfo(directory), "*.txt", SearchOption.AllDirectories)
                .ToListAsync();

            string[] expectedPaths = [alpha.FullName, beta.FullName, nested.FullName];
            Assert.Equal(3, results.Count);
            Assert.Equal(
                expectedPaths.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase),
                reader.Identifiers.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase));
            Assert.Equal(
                reader.Identifiers.Select(Path.GetFileName),
                results.Select(static result => result.DocumentId));
            Assert.All(results, static result => Assert.True(result.Succeeded));
            Assert.Equal(3, writer.WriteCount);
            Assert.DoesNotContain(reader.Identifiers, static path => path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_SingletonEnumerableAndFileListUseEquivalentStageComposition()
    {
        string directory = CreateTemporaryDirectory();
        FileInfo singleton = CreateFile(directory, "singleton.txt");
        FileInfo first = CreateFile(directory, "list-first.txt");
        FileInfo second = CreateFile(directory, "list-second.txt");
        try
        {
            List<string> calls = [];
            RecordingReader reader = new(
                calls,
                (identifier, _) => Task.FromResult(TestDocuments.Create(Path.GetFileName(identifier))));
            RecordingDocumentProcessor documentProcessor = new(
                "document-processor",
                calls,
                static (document, _) => Task.FromResult(document));
            RecordingChunker chunker = new(
                "chunker",
                calls,
                document => [new IngestionChunk(new TextContent(document.Identifier), document, 1)]);
            RecordingChunkProcessor chunkProcessor = new("chunk-processor", calls, static chunk => chunk);
            RecordingWriter writer = new(calls);
            using IngestionPipeline pipeline = new(reader, chunker, writer);
            pipeline.DocumentProcessors.Add(documentProcessor);
            pipeline.ChunkProcessors.Add(chunkProcessor);

            IngestionResult singletonResult = Assert.Single(
                await pipeline.ProcessAsync(new[] { singleton }).ToListAsync());
            List<IngestionResult> listResults = await pipeline.ProcessAsync(new List<FileInfo> { first, second }).ToListAsync();

            Assert.True(singletonResult.Succeeded);
            Assert.Equal(["list-first.txt", "list-second.txt"], listResults.Select(static result => result.DocumentId));
            Assert.All(listResults, static result => Assert.True(result.Succeeded));
            Assert.Equal(
                ExpectedComposition(singleton.Name, first.Name, second.Name),
                calls);
            Assert.Equal(3, reader.Identifiers.Count);
            Assert.Equal(3, documentProcessor.Inputs.Count);
            Assert.Equal(3, chunker.Inputs.Count);
            Assert.Equal(3, chunkProcessor.Inputs.Count);
            Assert.Equal(3, writer.WriteCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ForwardsCancellationTokenToEveryReachedStage()
    {
        string directory = CreateTemporaryDirectory();
        FileInfo source = CreateFile(directory, "tokens.txt");
        try
        {
            using CancellationTokenSource cancellationSource = new();
            CancellationToken expectedToken = cancellationSource.Token;
            List<string> calls = [];
            IngestionDocument document = TestDocuments.Create("token-document");
            RecordingReader reader = new(calls, (_, _) => Task.FromResult(document));
            RecordingDocumentProcessor firstDocumentProcessor = new(
                "document-processor-1",
                calls,
                static (input, _) => Task.FromResult(input));
            RecordingDocumentProcessor secondDocumentProcessor = new(
                "document-processor-2",
                calls,
                static (input, _) => Task.FromResult(input));
            RecordingChunker chunker = new(
                "chunker",
                calls,
                input => [new IngestionChunk(new TextContent("token content"), input, 2)]);
            RecordingChunkProcessor firstChunkProcessor = new("chunk-processor-1", calls, static chunk => chunk);
            RecordingChunkProcessor secondChunkProcessor = new("chunk-processor-2", calls, static chunk => chunk);
            RecordingWriter writer = new(calls);
            using IngestionPipeline pipeline = new(reader, chunker, writer);
            pipeline.DocumentProcessors.Add(firstDocumentProcessor);
            pipeline.DocumentProcessors.Add(secondDocumentProcessor);
            pipeline.ChunkProcessors.Add(firstChunkProcessor);
            pipeline.ChunkProcessors.Add(secondChunkProcessor);

            IngestionResult result = Assert.Single(
                await pipeline.ProcessAsync([source], expectedToken).ToListAsync());

            Assert.True(result.Succeeded);
            Assert.Equal(expectedToken, Assert.Single(reader.Tokens));
            Assert.Equal(expectedToken, Assert.Single(firstDocumentProcessor.Tokens));
            Assert.Equal(expectedToken, Assert.Single(secondDocumentProcessor.Tokens));
            Assert.Equal(expectedToken, Assert.Single(chunker.Tokens));
            Assert.Equal(expectedToken, Assert.Single(firstChunkProcessor.Tokens));
            Assert.Equal(expectedToken, Assert.Single(secondChunkProcessor.Tokens));
            Assert.Equal(expectedToken, Assert.Single(writer.Tokens));
            Assert.Equal(
                [
                    "reader:tokens.txt",
                    "document-processor-1:token-document",
                    "document-processor-2:token-document",
                    "chunker:token-document",
                    "chunk-processor-1:token-document",
                    "chunk-processor-2:token-document",
                    "writer:token-document",
                ],
                calls);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_PreCanceledToken_IsCapturedAsFailedResultAndSuppressesLaterStages()
    {
        string directory = CreateTemporaryDirectory();
        FileInfo source = CreateFile(directory, "pre-canceled.txt");
        try
        {
            using CancellationTokenSource cancellationSource = new();
            cancellationSource.Cancel();
            List<string> calls = [];
            RecordingReader reader = new(calls, (identifier, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(TestDocuments.Create(identifier));
            });
            RecordingDocumentProcessor documentProcessor = new(
                "document-processor",
                calls,
                static (document, _) => Task.FromResult(document));
            RecordingChunker chunker = new(
                "chunker",
                calls,
                document => [new IngestionChunk(new TextContent("unreached"), document, 1)]);
            RecordingChunkProcessor chunkProcessor = new("chunk-processor", calls, static chunk => chunk);
            RecordingWriter writer = new(calls);
            using IngestionPipeline pipeline = new(reader, chunker, writer);
            pipeline.DocumentProcessors.Add(documentProcessor);
            pipeline.ChunkProcessors.Add(chunkProcessor);

            IngestionResult result = Assert.Single(
                await pipeline.ProcessAsync([source], cancellationSource.Token).ToListAsync());

            Assert.False(result.Succeeded);
            Assert.Equal(source.FullName, result.DocumentId);
            Assert.Null(result.Document);
            Assert.IsType<OperationCanceledException>(result.Exception);
            Assert.Equal(cancellationSource.Token, Assert.Single(reader.Tokens));
            Assert.Equal(["reader:pre-canceled.txt"], calls);
            Assert.Empty(documentProcessor.Inputs);
            Assert.Empty(chunker.Inputs);
            Assert.Empty(chunkProcessor.Inputs);
            Assert.Equal(0, writer.WriteCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_MidStreamCancellation_IsCapturedPerFileAndNextFileIsAttempted()
    {
        string directory = CreateTemporaryDirectory();
        FileInfo first = CreateFile(directory, "cancel-first.txt");
        FileInfo second = CreateFile(directory, "cancel-second.txt");
        try
        {
            using CancellationTokenSource cancellationSource = new();
            List<string> calls = [];
            RecordingReader reader = new(calls, (identifier, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(TestDocuments.Create(Path.GetFileName(identifier)));
            });
            RecordingChunker chunker = new(
                "chunker",
                calls,
                document =>
                [
                    new IngestionChunk(new TextContent("first chunk"), document, 2),
                    new IngestionChunk(new TextContent("second chunk"), document, 2),
                ]);
            RecordingChunkProcessor cancelingProcessor = new(
                "canceling-processor",
                calls,
                static chunk => chunk,
                afterYield: count =>
                {
                    if (count == 1)
                    {
                        cancellationSource.Cancel();
                    }
                });
            RecordingWriter writer = new(calls);
            using IngestionPipeline pipeline = new(reader, chunker, writer);
            pipeline.ChunkProcessors.Add(cancelingProcessor);

            List<IngestionResult> results = await pipeline
                .ProcessAsync([first, second], cancellationSource.Token)
                .ToListAsync();

            Assert.True(cancellationSource.IsCancellationRequested);
            Assert.Equal(2, results.Count);
            Assert.All(results, static result => Assert.False(result.Succeeded));
            Assert.Equal("cancel-first.txt", results[0].DocumentId);
            Assert.NotNull(results[0].Document);
            Assert.IsType<OperationCanceledException>(results[0].Exception);
            Assert.Equal(second.FullName, results[1].DocumentId);
            Assert.Null(results[1].Document);
            Assert.IsType<OperationCanceledException>(results[1].Exception);
            Assert.Equal([first.FullName, second.FullName], reader.Identifiers);
            Assert.All(reader.Tokens, token => Assert.Equal(cancellationSource.Token, token));
            Assert.Single(chunker.Inputs);
            Assert.Single(cancelingProcessor.Inputs);
            Assert.Equal(1, writer.WriteCount);
            Assert.Single(Assert.Single(writer.Batches));
            Assert.Contains("writer:cancel-first.txt", calls);
            Assert.DoesNotContain("chunker:cancel-second.txt", calls);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_SuccessActivityHasExpectedUnsetStatusAndTags()
    {
        string directory = CreateTemporaryDirectory();
        FileInfo source = CreateFile(directory, "activity-success.txt");
        try
        {
            string sourceName = $"IngestionPipelineTests.Success.{Guid.NewGuid():N}";
            List<Activity> activities = [];
            using ActivityListener listener = CreateActivityListener(sourceName, activities);
            List<string> calls = [];
            IngestionDocument original = TestDocuments.Create("original-id");
            IngestionDocument transformed = TestDocuments.Create("transformed-id");
            RecordingReader reader = new(calls, (_, _) => Task.FromResult(original));
            RecordingDocumentProcessor processor = new(
                "document-processor",
                calls,
                (_, _) => Task.FromResult(transformed));
            RecordingChunker chunker = new(
                "chunker",
                calls,
                document => [new IngestionChunk(new TextContent("activity"), document, 1)]);
            RecordingWriter writer = new(calls);
            using IngestionPipeline pipeline = new(
                reader,
                chunker,
                writer,
                new IngestionPipelineOptions { ActivitySourceName = sourceName });
            pipeline.DocumentProcessors.Add(processor);

            IngestionResult result = Assert.Single(await pipeline.ProcessAsync([source]).ToListAsync());

            Assert.True(result.Succeeded);
            Activity processFiles = Assert.Single(activities, static activity => activity.OperationName == "ProcessFiles");
            Activity processFile = Assert.Single(activities, static activity => activity.OperationName == "ProcessFile");
            Assert.Equal(ActivityStatusCode.Unset, processFiles.Status);
            Assert.Equal(ActivityStatusCode.Unset, processFile.Status);
            Assert.Equal(1, processFiles.GetTagItem("rag.file.count"));
            Assert.Equal(source.FullName, processFile.GetTagItem("rag.file.path"));
            Assert.Equal("transformed-id", processFile.GetTagItem("rag.document.id"));
            Assert.Null(processFile.GetTagItem("error.type"));
            Assert.Equal(processFiles.SpanId.ToString(), processFile.ParentSpanId.ToString());
            Assert.Equal(processFiles.TraceId.ToString(), processFile.TraceId.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_FailureActivityHasExpectedErrorStatusAndFileTags()
    {
        string directory = CreateTemporaryDirectory();
        FileInfo source = CreateFile(directory, "activity-failure.txt");
        try
        {
            string sourceName = $"IngestionPipelineTests.Failure.{Guid.NewGuid():N}";
            List<Activity> activities = [];
            using ActivityListener listener = CreateActivityListener(sourceName, activities);
            InvalidOperationException expectedFailure = new("processor rejected document");
            List<string> calls = [];
            IngestionDocument document = TestDocuments.Create("failed-document");
            RecordingReader reader = new(calls, (_, _) => Task.FromResult(document));
            RecordingDocumentProcessor processor = new(
                "document-processor",
                calls,
                (_, _) => throw expectedFailure);
            RecordingChunker chunker = new(
                "chunker",
                calls,
                input => [new IngestionChunk(new TextContent("unreached"), input, 1)]);
            RecordingWriter writer = new(calls);
            using IngestionPipeline pipeline = new(
                reader,
                chunker,
                writer,
                new IngestionPipelineOptions { ActivitySourceName = sourceName });
            pipeline.DocumentProcessors.Add(processor);

            IngestionResult result = Assert.Single(await pipeline.ProcessAsync([source]).ToListAsync());

            Assert.False(result.Succeeded);
            Assert.Same(expectedFailure, result.Exception);
            Assert.Same(document, result.Document);
            Activity processFiles = Assert.Single(activities, static activity => activity.OperationName == "ProcessFiles");
            Activity processFile = Assert.Single(activities, static activity => activity.OperationName == "ProcessFile");
            Assert.Equal(ActivityStatusCode.Unset, processFiles.Status);
            Assert.Equal(ActivityStatusCode.Error, processFile.Status);
            Assert.Equal(expectedFailure.Message, processFile.StatusDescription);
            Assert.Equal(typeof(InvalidOperationException).FullName, processFile.GetTagItem("error.type"));
            Assert.Equal(source.FullName, processFile.GetTagItem("rag.file.path"));
            Assert.Equal("failed-document", processFile.GetTagItem("rag.document.id"));
            Assert.Equal(processFiles.SpanId, processFile.ParentSpanId);
            Assert.Empty(chunker.Inputs);
            Assert.Equal(0, writer.WriteCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Dispose_DisposesOwnedWriterExactlyOnceAndDoesNotDisposeOtherSeams()
    {
        List<string> calls = [];
        RecordingReader reader = new(calls, (identifier, _) => Task.FromResult(TestDocuments.Create(identifier)));
        RecordingDocumentProcessor documentProcessor = new(
            "document-processor",
            calls,
            static (document, _) => Task.FromResult(document));
        RecordingChunker chunker = new("chunker", calls, static _ => []);
        RecordingChunkProcessor chunkProcessor = new("chunk-processor", calls, static chunk => chunk);
        RecordingWriter writer = new(calls);
        IngestionPipeline pipeline = new(reader, chunker, writer);
        pipeline.DocumentProcessors.Add(documentProcessor);
        pipeline.ChunkProcessors.Add(chunkProcessor);

        pipeline.Dispose();

        Assert.Equal(1, writer.DisposeCount);
        Assert.Equal(0, reader.DisposeCount);
        Assert.Equal(0, documentProcessor.DisposeCount);
        Assert.Equal(0, chunker.DisposeCount);
        Assert.Equal(0, chunkProcessor.DisposeCount);
        Assert.Empty(calls);
    }

    [Fact]
    public async Task Dispose_AfterPartialFailureStillDisposesOwnedWriterExactlyOnce()
    {
        string directory = CreateTemporaryDirectory();
        FileInfo source = CreateFile(directory, "partial-failure.txt");
        try
        {
            InvalidOperationException expectedFailure = new("write failed after first chunk");
            List<string> calls = [];
            IngestionDocument document = TestDocuments.Create("partial-document");
            RecordingReader reader = new(calls, (_, _) => Task.FromResult(document));
            RecordingDocumentProcessor documentProcessor = new(
                "document-processor",
                calls,
                static (input, _) => Task.FromResult(input));
            RecordingChunker chunker = new(
                "chunker",
                calls,
                input =>
                [
                    new IngestionChunk(new TextContent("written before failure"), input, 3),
                    new IngestionChunk(new TextContent("never written"), input, 2),
                ]);
            RecordingChunkProcessor chunkProcessor = new("chunk-processor", calls, static chunk => chunk);
            RecordingWriter writer = new(calls, throwAfterChunkCount: 1, writeException: expectedFailure);
            IngestionPipeline pipeline = new(reader, chunker, writer);
            pipeline.DocumentProcessors.Add(documentProcessor);
            pipeline.ChunkProcessors.Add(chunkProcessor);

            IngestionResult result;
            try
            {
                result = Assert.Single(await pipeline.ProcessAsync([source]).ToListAsync());
            }
            finally
            {
                pipeline.Dispose();
            }

            Assert.False(result.Succeeded);
            Assert.Same(expectedFailure, result.Exception);
            Assert.Same(document, result.Document);
            Assert.Single(Assert.Single(writer.Batches));
            Assert.Equal(0, writer.CompletedWriteCount);
            Assert.Equal(1, writer.DisposeCount);
            Assert.Equal(0, reader.DisposeCount);
            Assert.Equal(0, documentProcessor.DisposeCount);
            Assert.Equal(0, chunker.DisposeCount);
            Assert.Equal(0, chunkProcessor.DisposeCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"IngestionPipelineTests-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(path);
        return path;
    }

    private static FileInfo CreateFile(string directory, string fileName)
    {
        string path = Path.Combine(directory, fileName);
        File.WriteAllText(path, $"local content for {fileName}");
        return new FileInfo(path);
    }

    private static string[] ExpectedComposition(params string[] fileNames) =>
        fileNames.SelectMany(static fileName => new[]
        {
            $"reader:{fileName}",
            $"document-processor:{fileName}",
            $"chunker:{fileName}",
            $"chunk-processor:{fileName}",
            $"writer:{fileName}",
        }).ToArray();

    private static ActivityListener CreateActivityListener(string sourceName, List<Activity> activities)
    {
        ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private sealed class RecordingReader : IngestionDocumentReader, IDisposable
    {
        private readonly List<string> _calls;
        private readonly Func<string, CancellationToken, Task<IngestionDocument>> _read;

        public RecordingReader(
            List<string> calls,
            Func<string, CancellationToken, Task<IngestionDocument>> read)
        {
            _calls = calls;
            _read = read;
        }

        public List<string> Identifiers { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public int DisposeCount { get; private set; }

        public override Task<IngestionDocument> ReadAsync(
            Stream source,
            string identifier,
            string mediaType,
            CancellationToken cancellationToken = default)
        {
            Identifiers.Add(identifier);
            Tokens.Add(cancellationToken);
            _calls.Add($"reader:{Path.GetFileName(identifier)}");
            return _read(identifier, cancellationToken);
        }

        public void Dispose() => DisposeCount++;
    }

    private sealed class RecordingDocumentProcessor : IngestionDocumentProcessor, IDisposable
    {
        private readonly string _name;
        private readonly List<string> _calls;
        private readonly Func<IngestionDocument, CancellationToken, Task<IngestionDocument>> _process;

        public RecordingDocumentProcessor(
            string name,
            List<string> calls,
            Func<IngestionDocument, CancellationToken, Task<IngestionDocument>> process)
        {
            _name = name;
            _calls = calls;
            _process = process;
        }

        public List<IngestionDocument> Inputs { get; } = [];

        public List<IngestionDocument> Outputs { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public int DisposeCount { get; private set; }

        public override async Task<IngestionDocument> ProcessAsync(
            IngestionDocument document,
            CancellationToken cancellationToken = default)
        {
            Inputs.Add(document);
            Tokens.Add(cancellationToken);
            _calls.Add($"{_name}:{document.Identifier}");
            IngestionDocument output = await _process(document, cancellationToken);
            Outputs.Add(output);
            return output;
        }

        public void Dispose() => DisposeCount++;
    }

    private sealed class RecordingChunker : IngestionChunker, IDisposable
    {
        private readonly string _name;
        private readonly List<string> _calls;
        private readonly Func<IngestionDocument, IReadOnlyList<IngestionChunk>> _chunk;

        public RecordingChunker(
            string name,
            List<string> calls,
            Func<IngestionDocument, IReadOnlyList<IngestionChunk>> chunk)
        {
            _name = name;
            _calls = calls;
            _chunk = chunk;
        }

        public List<IngestionDocument> Inputs { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public int DisposeCount { get; private set; }

        public override IAsyncEnumerable<IngestionChunk> ProcessAsync(
            IngestionDocument document,
            CancellationToken cancellationToken = default)
        {
            Inputs.Add(document);
            Tokens.Add(cancellationToken);
            return EnumerateAsync(_chunk(document), cancellationToken);
        }

        private async IAsyncEnumerable<IngestionChunk> EnumerateAsync(
            IReadOnlyList<IngestionChunk> chunks,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (IngestionChunk chunk in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _calls.Add($"{_name}:{chunk.Document.Identifier}");
                yield return chunk;
            }

            await Task.CompletedTask;
        }

        public void Dispose() => DisposeCount++;
    }

    private sealed class RecordingChunkProcessor : IngestionChunkProcessor, IDisposable
    {
        private readonly string _name;
        private readonly List<string> _calls;
        private readonly Func<IngestionChunk, IngestionChunk> _process;
        private readonly Action<int>? _afterYield;

        public RecordingChunkProcessor(
            string name,
            List<string> calls,
            Func<IngestionChunk, IngestionChunk> process,
            Action<int>? afterYield = null)
        {
            _name = name;
            _calls = calls;
            _process = process;
            _afterYield = afterYield;
        }

        public List<IngestionChunk> Inputs { get; } = [];

        public List<IngestionChunk> Outputs { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public int DisposeCount { get; private set; }

        public override IAsyncEnumerable<IngestionChunk> ProcessAsync(
            IAsyncEnumerable<IngestionChunk> chunks,
            CancellationToken cancellationToken = default)
        {
            Tokens.Add(cancellationToken);
            return ProcessCoreAsync(chunks, cancellationToken);
        }

        private async IAsyncEnumerable<IngestionChunk> ProcessCoreAsync(
            IAsyncEnumerable<IngestionChunk> chunks,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            int count = 0;
            await foreach (IngestionChunk chunk in chunks.WithCancellation(cancellationToken))
            {
                Inputs.Add(chunk);
                IngestionChunk output = _process(chunk);
                Outputs.Add(output);
                _calls.Add($"{_name}:{output.Document.Identifier}");
                yield return output;

                count++;
                _afterYield?.Invoke(count);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        public void Dispose() => DisposeCount++;
    }

    private sealed class RecordingWriter : IngestionChunkWriter
    {
        private readonly List<string> _calls;
        private readonly int? _throwAfterChunkCount;
        private readonly Exception? _writeException;

        public RecordingWriter(
            List<string> calls,
            int? throwAfterChunkCount = null,
            Exception? writeException = null)
        {
            _calls = calls;
            _throwAfterChunkCount = throwAfterChunkCount;
            _writeException = writeException;
        }

        public List<IReadOnlyList<IngestionChunk>> Batches { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public int WriteCount { get; private set; }

        public int CompletedWriteCount { get; private set; }

        public int DisposeCount { get; private set; }

        public override async Task WriteAsync(
            IAsyncEnumerable<IngestionChunk> chunks,
            CancellationToken cancellationToken = default)
        {
            WriteCount++;
            Tokens.Add(cancellationToken);
            List<IngestionChunk> batch = [];
            Batches.Add(batch);

            await foreach (IngestionChunk chunk in chunks.WithCancellation(cancellationToken))
            {
                batch.Add(chunk);
                _calls.Add($"writer:{chunk.Document.Identifier}");
                if (batch.Count == _throwAfterChunkCount)
                {
                    throw _writeException ?? new InvalidOperationException("Recording writer failure.");
                }
            }

            CompletedWriteCount++;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
            }

            base.Dispose(disposing);
        }
    }

    private sealed class RecordingEmbeddingGenerator : IEmbeddingGenerator<AIContent, Embedding<float>>
    {
        public const int DimensionCount = 4;

        public List<IReadOnlyList<AIContent>> Calls { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<AIContent> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            List<AIContent> inputs = values.ToList();
            Calls.Add(inputs);
            Tokens.Add(cancellationToken);

            List<Embedding<float>> embeddings = inputs.Select(static value =>
            {
                string text = Assert.IsType<TextContent>(value).Text;
                return text switch
                {
                    "alpha" => new Embedding<float>(new float[] { 1, 0, 0, 0 }),
                    "beta" => new Embedding<float>(new float[] { 0, 1, 0, 0 }),
                    _ => throw new InvalidOperationException($"Unexpected embedding input '{text}'."),
                };
            }).ToList();

            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
