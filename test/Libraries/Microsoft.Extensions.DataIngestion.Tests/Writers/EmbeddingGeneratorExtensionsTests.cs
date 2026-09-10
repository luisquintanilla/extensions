// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Writers.Tests;

public sealed class EmbeddingGeneratorExtensionsTests
{
    [Fact]
    public async Task Adapter_ConvertsTextContentInputToString()
    {
        using RecordingStringEmbeddingGenerator inner = new();
        IEmbeddingGenerator<TextContent, Embedding<float>> adapter = inner.AsTextContentEmbeddingGenerator();
        TextContent[] input = [new("first value"), new("second value"), new("third value")];

        GeneratedEmbeddings<Embedding<float>> result = await adapter.GenerateAsync(input);

        Assert.Equal(["first value", "second value", "third value"], inner.ObservedInputs);
        Assert.Equal(1, inner.GenerateCallCount);
        Assert.Same(inner.Result, result);
    }

    [Fact]
    public async Task Adapter_ForwardsEmbeddingGenerationOptionsAndCancellationToken()
    {
        using RecordingStringEmbeddingGenerator inner = new();
        using CancellationTokenSource cancellationSource = new();
        EmbeddingGenerationOptions options = new()
        {
            Dimensions = 3,
            ModelId = "test-model",
        };
        IEmbeddingGenerator<TextContent, Embedding<float>> adapter = inner.AsTextContentEmbeddingGenerator();

        GeneratedEmbeddings<Embedding<float>> result = await adapter.GenerateAsync(
            [new TextContent("one"), new TextContent("two")],
            options,
            cancellationSource.Token);

        Assert.Same(options, inner.ObservedOptions);
        Assert.Equal(cancellationSource.Token, inner.ObservedCancellationToken);
        Assert.Equal(["one", "two"], inner.ObservedInputs);
        Assert.Same(inner.Result, result);
    }

    [Fact]
    public async Task Adapter_ReturnsInnerEmbeddingsWithoutReordering()
    {
        Embedding<float> first = new(new float[] { 1, 10 });
        Embedding<float> second = new(new float[] { 2, 20 });
        Embedding<float> third = new(new float[] { 3, 30 });
        GeneratedEmbeddings<Embedding<float>> expected = new([first, second, third])
        {
            Usage = new UsageDetails { InputTokenCount = 17 },
        };
        using RecordingStringEmbeddingGenerator inner = new() { Result = expected };
        IEmbeddingGenerator<TextContent, Embedding<float>> adapter = inner.AsTextContentEmbeddingGenerator();

        GeneratedEmbeddings<Embedding<float>> actual = await adapter.GenerateAsync(
            [new TextContent("first"), new TextContent("second"), new TextContent("third")]);

        Assert.Same(expected, actual);
        Assert.Same(first, actual[0]);
        Assert.Same(second, actual[1]);
        Assert.Same(third, actual[2]);
        Assert.Equal(17, actual.Usage!.InputTokenCount);
        Assert.Equal(["first", "second", "third"], inner.ObservedInputs);
    }

    [Fact]
    public void GetService_DelegatesToInnerGenerator()
    {
        object serviceKey = new();
        TimeZoneInfo expectedService = TimeZoneInfo.Utc;
        using RecordingStringEmbeddingGenerator inner = new()
        {
            Service = expectedService,
        };
        IEmbeddingGenerator<TextContent, Embedding<float>> adapter = inner.AsTextContentEmbeddingGenerator();

        object? actual = adapter.GetService(typeof(TimeZoneInfo), serviceKey);

        Assert.Same(expectedService, actual);
        Assert.Equal(1, inner.GetServiceCallCount);
        Assert.Equal(typeof(TimeZoneInfo), inner.ObservedServiceType);
        Assert.Same(serviceKey, inner.ObservedServiceKey);
    }

    [Fact]
    public void GetService_CompatibleUnkeyedRequestReturnsAdapterWithoutDelegating()
    {
        using RecordingStringEmbeddingGenerator inner = new();
        IEmbeddingGenerator<TextContent, Embedding<float>> adapter = inner.AsTextContentEmbeddingGenerator();

        object? service = adapter.GetService(typeof(IEmbeddingGenerator<TextContent, Embedding<float>>));

        Assert.Same(adapter, service);
        Assert.Equal(0, inner.GetServiceCallCount);
    }

    [Fact]
    public void Dispose_DisposesInnerGeneratorExactlyOnce()
    {
        using RecordingStringEmbeddingGenerator inner = new();
        IEmbeddingGenerator<TextContent, Embedding<float>> adapter = inner.AsTextContentEmbeddingGenerator();

        adapter.Dispose();

        Assert.Equal(1, inner.DisposeCallCount);
        Assert.Equal(0, inner.GenerateCallCount);
    }

    [Fact]
    public void AsTextContentEmbeddingGenerator_NullGenerator_Throws()
    {
        IEmbeddingGenerator<string, Embedding<float>> generator = null!;

        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => generator.AsTextContentEmbeddingGenerator());

        Assert.Equal("stringGenerator", exception.ParamName);
    }

    [Fact]
    public async Task GenerateAsync_NullValues_Throws()
    {
        using RecordingStringEmbeddingGenerator inner = new();
        IEmbeddingGenerator<TextContent, Embedding<float>> adapter = inner.AsTextContentEmbeddingGenerator();

        ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => adapter.GenerateAsync(null!));

        Assert.Equal("values", exception.ParamName);
        Assert.Equal(0, inner.GenerateCallCount);
    }

    [Fact]
    public void GetService_NullServiceType_Throws()
    {
        using RecordingStringEmbeddingGenerator inner = new();
        IEmbeddingGenerator<TextContent, Embedding<float>> adapter = inner.AsTextContentEmbeddingGenerator();

        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => adapter.GetService(null!));

        Assert.Equal("serviceType", exception.ParamName);
        Assert.Equal(0, inner.GetServiceCallCount);
    }

    private sealed class RecordingStringEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public GeneratedEmbeddings<Embedding<float>> Result { get; set; } = new(
        [
            new Embedding<float>(new float[] { 1, 2, 3 }),
            new Embedding<float>(new float[] { 4, 5, 6 }),
            new Embedding<float>(new float[] { 7, 8, 9 }),
        ]);

        public IReadOnlyList<string> ObservedInputs { get; private set; } = [];

        public EmbeddingGenerationOptions? ObservedOptions { get; private set; }

        public CancellationToken ObservedCancellationToken { get; private set; }

        public Type? ObservedServiceType { get; private set; }

        public object? ObservedServiceKey { get; private set; }

        public object? Service { get; set; }

        public int GenerateCallCount { get; private set; }

        public int GetServiceCallCount { get; private set; }

        public int DisposeCallCount { get; private set; }

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            GenerateCallCount++;
            ObservedInputs = values.ToArray();
            ObservedOptions = options;
            ObservedCancellationToken = cancellationToken;
            return Task.FromResult(Result);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            GetServiceCallCount++;
            ObservedServiceType = serviceType;
            ObservedServiceKey = serviceKey;
            return Service;
        }

        public void Dispose() => DisposeCallCount++;
    }
}
