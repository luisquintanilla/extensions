// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Processors.Tests;

public class ClassificationEnricherTests
{
    private static readonly IngestionDocument _document = TestDocuments.Create("test");

    [Fact]
    public void ThrowsOnNullOptions()
    {
        Assert.Throws<ArgumentNullException>("options", () => new ClassificationEnricher(null!, predefinedClasses: ["some"]));
    }

    [Fact]
    public void ThrowsOnEmptyPredefinedClasses()
    {
        Assert.Throws<ArgumentException>("predefinedClasses", () => new ClassificationEnricher(new(new TestChatClient()), predefinedClasses: []));
    }

    [Fact]
    public void ThrowsOnDuplicatePredefinedClasses()
    {
        Assert.Throws<ArgumentException>("predefinedClasses", () => new ClassificationEnricher(new(new TestChatClient()), predefinedClasses: ["same", "same"]));
    }

    [Fact]
    public void ThrowsOnPredefinedClassesContainingFallback()
    {
        Assert.Throws<ArgumentException>("predefinedClasses", () => new ClassificationEnricher(new(new TestChatClient()), predefinedClasses: ["same", "Unknown"]));
    }

    [Fact]
    public void ThrowsOnFallbackInPredefinedClasses()
    {
        Assert.Throws<ArgumentException>("predefinedClasses", () => new ClassificationEnricher(new(new TestChatClient()), predefinedClasses: ["some"], fallbackClass: "some"));
    }

    [Fact]
    public async Task ThrowsOnNullChunks()
    {
        using TestChatClient chatClient = new();
        ClassificationEnricher sut = new(new(chatClient), predefinedClasses: ["some"]);

        await Assert.ThrowsAsync<ArgumentNullException>("chunks", async () =>
        {
            await foreach (var _ in sut.ProcessAsync(null!))
            {
                // No-op
            }
        });
    }

    [Fact]
    public async Task CanClassify()
    {
        int counter = 0;
        string[] classes = ["AI", "Animals", "UFO"];
        using TestChatClient chatClient = new()
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) =>
            {
                Assert.Equal(0, counter++);
                var materializedMessages = messages.ToArray();

                Assert.Equal(2, materializedMessages.Length);
                Assert.Equal(ChatRole.System, materializedMessages[0].Role);
                Assert.Equal(ChatRole.User, materializedMessages[1].Role);

                string response = JsonSerializer.Serialize(new Envelope<string[]> { data = classes });
                return Task.FromResult(new ChatResponse(new[]
                {
                    new ChatMessage(ChatRole.Assistant, response)
                }));
            }
        };
        ClassificationEnricher sut = new(new(chatClient), ["AI", "Animals", "Sports"], fallbackClass: "UFO");

        IReadOnlyList<IngestionChunk> got = await sut.ProcessAsync(CreateChunks().ToAsyncEnumerable()).ToListAsync();

        Assert.Equal(3, got.Count);
        Assert.Equal("AI", got[0].Metadata[ClassificationEnricher.MetadataKey]);
        Assert.Equal("Animals", got[1].Metadata[ClassificationEnricher.MetadataKey]);
        Assert.Equal("UFO", got[2].Metadata[ClassificationEnricher.MetadataKey]);
    }

    [Fact]
    public async Task FailureDoesNotStopTheProcessing()
    {
        FakeLogCollector collector = new();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)));
        using TestChatClient chatClient = new()
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) => Task.FromException<ChatResponse>(new ExpectedException())
        };

        ClassificationEnricher sut = new(new(chatClient) { LoggerFactory = loggerFactory }, ["AI", "Other"]);
        List<IngestionChunk> chunks = CreateChunks();

        IReadOnlyList<IngestionChunk> got = await sut.ProcessAsync(chunks.ToAsyncEnumerable()).ToListAsync();

        Assert.Equal(chunks.Count, got.Count);
        Assert.All(chunks, chunk => Assert.False(chunk.HasMetadata));
        Assert.Equal(1, collector.Count); // with batching, only one log entry is expected
        Assert.Equal(LogLevel.Error, collector.LatestRecord.Level);
        Assert.IsType<ExpectedException>(collector.LatestRecord.Exception);
    }

    private static List<IngestionChunk> CreateChunks() =>
    [
        TestChunkFactory.CreateChunk(".NET developers need to integrate and interact with a growing variety of artificial intelligence (AI) services in their apps. " +
            "The Microsoft.Extensions.AI libraries provide a unified approach for representing generative AI components, and enable seamless" +
            " integration and interoperability with various AI services.", _document),
        TestChunkFactory.CreateChunk("Rabbits are small mammals in the family Leporidae of the order Lagomorpha (along with the hare and the pika)." +
            "They are herbivorous animals and are known for their long ears, large hind legs, and short fluffy tails.", _document),
        TestChunkFactory.CreateChunk("This text does not belong to any category.", _document),
    ];

    [Fact]
    public async Task ProcessAsync_BatchesRequestsAtConfiguredSize()
    {
        List<string[]> requestBatches = [];
        string[][] responseBatches =
        [
            ["AI", "Animals"],
            ["Unknown"],
        ];
        using TestChatClient chatClient = new()
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) =>
            {
                ChatMessage[] request = messages.ToArray();
                Assert.Equal(ChatRole.System, request[0].Role);
                Assert.Equal(ChatRole.User, request[1].Role);
                requestBatches.Add(request[1].Contents.Cast<TextContent>().Select(content => content.Text).ToArray());
                return Task.FromResult(CreateResponse(responseBatches[requestBatches.Count - 1]));
            }
        };
        ClassificationEnricher sut = new(
            new EnricherOptions(chatClient) { BatchSize = 2 },
            ["AI", "Animals"]);
        List<IngestionChunk> chunks = CreateChunks();

        IReadOnlyList<IngestionChunk> result = await sut.ProcessAsync(chunks.ToAsyncEnumerable()).ToListAsync();

        Assert.Equal([2, 1], requestBatches.Select(batch => batch.Length));
        Assert.Equal(
            chunks.Select(chunk => Assert.IsType<TextContent>(chunk.Content).Text),
            requestBatches.SelectMany(batch => batch));
        Assert.Equal(["AI", "Animals", "Unknown"], result.Select(chunk => chunk.Metadata[ClassificationEnricher.MetadataKey]));
        Assert.Equal(chunks, result);
    }

    [Fact]
    public async Task ProcessAsync_ResultCountMismatch_UsesDocumentedBestEffortBehavior()
    {
        FakeLogCollector collector = new();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)));
        int callCount = 0;
        using TestChatClient chatClient = new()
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) =>
            {
                callCount++;
                return Task.FromResult(CreateResponse("AI", "Animals"));
            }
        };
        ClassificationEnricher sut = new(
            new EnricherOptions(chatClient) { BatchSize = 3, LoggerFactory = loggerFactory },
            ["AI", "Animals"]);
        List<IngestionChunk> chunks = CreateChunks();

        IReadOnlyList<IngestionChunk> result = await sut.ProcessAsync(chunks.ToAsyncEnumerable()).ToListAsync();

        Assert.Equal(1, callCount);
        Assert.Equal(chunks, result);
        Assert.All(result, chunk => Assert.False(chunk.HasMetadata));
        Assert.Equal(1, collector.Count);
        Assert.Equal(7, collector.LatestRecord.Id.Id);
        Assert.Equal(LogLevel.Error, collector.LatestRecord.Level);
        Assert.Equal("The AI chat service returned 2 instead of 3 results.", collector.LatestRecord.Message);
        Assert.Null(collector.LatestRecord.Exception);
    }

    [Fact]
    public async Task ProcessAsync_CancellationAtChatBoundary_CharacterizesBestEffortBehavior()
    {
        FakeLogCollector collector = new();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)));
        using CancellationTokenSource cancellationSource = new();
        CancellationToken observedToken = default;
        int callCount = 0;
        using TestChatClient chatClient = new()
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) =>
            {
                callCount++;
                observedToken = cancellationToken;
                cancellationSource.Cancel();
                return Task.FromCanceled<ChatResponse>(cancellationToken);
            }
        };
        ClassificationEnricher sut = new(
            new EnricherOptions(chatClient) { BatchSize = 3, LoggerFactory = loggerFactory },
            ["AI", "Animals"]);
        List<IngestionChunk> chunks = CreateChunks();

        IReadOnlyList<IngestionChunk> result =
            await sut.ProcessAsync(chunks.ToAsyncEnumerable(), cancellationSource.Token).ToListAsync();

        Assert.Equal(1, callCount);
        Assert.Equal(cancellationSource.Token, observedToken);
        Assert.True(observedToken.IsCancellationRequested);
        Assert.Equal(chunks, result);
        Assert.All(result, chunk => Assert.False(chunk.HasMetadata));
        Assert.Equal(1, collector.Count);
        Assert.Equal(8, collector.LatestRecord.Id.Id);
        Assert.Equal(LogLevel.Error, collector.LatestRecord.Level);
        Assert.IsAssignableFrom<OperationCanceledException>(collector.LatestRecord.Exception);
    }

    private static ChatResponse CreateResponse(params string[] results) =>
        new(
        [
            new ChatMessage(
                ChatRole.Assistant,
                JsonSerializer.Serialize(new Envelope<string[]> { data = results })),
        ]);
}
