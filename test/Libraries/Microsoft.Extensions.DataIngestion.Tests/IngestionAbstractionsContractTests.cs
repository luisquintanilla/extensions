// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Tests;

public class IngestionAbstractionsContractTests
{
    [Fact]
    public void AbstractSeams_ExposeNonGenericContracts()
    {
        AssertNonGenericSeam(
            typeof(IngestionDocumentProcessor),
            "ProcessAsync",
            typeof(Task<IngestionDocument>),
            typeof(IngestionDocument),
            typeof(CancellationToken));
        AssertNonGenericSeam(
            typeof(IngestionChunker),
            "ProcessAsync",
            typeof(IAsyncEnumerable<IngestionChunk>),
            typeof(IngestionDocument),
            typeof(CancellationToken));
        AssertNonGenericSeam(
            typeof(IngestionChunkProcessor),
            "ProcessAsync",
            typeof(IAsyncEnumerable<IngestionChunk>),
            typeof(IAsyncEnumerable<IngestionChunk>),
            typeof(CancellationToken));
        AssertNonGenericSeam(
            typeof(IngestionChunkWriter),
            "WriteAsync",
            typeof(Task),
            typeof(IAsyncEnumerable<IngestionChunk>),
            typeof(CancellationToken));
    }

    [Fact]
    public async Task ReaderBridge_ForwardsIdentifierMediaTypeAndCancellationToken()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "local deterministic content");
        using CancellationTokenSource cancellation = new();
        RecordingReader reader = new();

        try
        {
            IngestionDocument document = await reader.ReadAsync(
                new FileInfo(path),
                "logical-document-id",
                "text/x-phase-one",
                cancellation.Token);

            Assert.Equal("logical-document-id", reader.Identifier);
            Assert.Equal("text/x-phase-one", reader.MediaType);
            Assert.Equal(cancellation.Token, reader.CancellationToken);
            Assert.Equal("local deterministic content", reader.Content);
            Assert.Equal("logical-document-id", document.Identifier);
            Assert.False(reader.Source!.CanRead);
            Assert.Throws<ObjectDisposedException>(() => reader.Source.ReadByte());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WriterContract_IsSynchronouslyDisposable()
    {
        RecordingWriter writer = new();
        IDisposable contract = writer;

        contract.Dispose();

        Assert.True(writer.DisposeCalled);
        Assert.True(writer.Disposing);
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(IngestionChunkWriter)));
    }

    private static void AssertNonGenericSeam(Type seamType, string methodName, Type returnType, params Type[] parameterTypes)
    {
        MethodInfo method = Assert.Single(seamType.GetMethods(BindingFlags.Instance | BindingFlags.Public), static method => method.IsAbstract);

        Assert.False(seamType.IsGenericType);
        Assert.True(seamType.IsAbstract);
        Assert.True(method.IsAbstract);
        Assert.False(method.IsGenericMethod);
        Assert.Equal(methodName, method.Name);
        Assert.Equal(returnType, method.ReturnType);
        Assert.Equal(parameterTypes, method.GetParameters().Select(static parameter => parameter.ParameterType));
    }

    private sealed class RecordingReader : IngestionDocumentReader
    {
        public Stream? Source { get; private set; }

        public string? Identifier { get; private set; }

        public string? MediaType { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public string? Content { get; private set; }

        public override async Task<IngestionDocument> ReadAsync(
            Stream source,
            string identifier,
            string mediaType,
            CancellationToken cancellationToken = default)
        {
            Source = source;
            Identifier = identifier;
            MediaType = mediaType;
            CancellationToken = cancellationToken;
            using MemoryStream buffer = new();
            await source.CopyToAsync(buffer, 81920, cancellationToken);
            Content = Encoding.UTF8.GetString(buffer.ToArray());
            return TestDocuments.Create(identifier, TestDocuments.Text("content", Content));
        }
    }

    private sealed class RecordingWriter : IngestionChunkWriter
    {
        public bool DisposeCalled { get; private set; }

        public bool Disposing { get; private set; }

        public override Task WriteAsync(
            IAsyncEnumerable<IngestionChunk> chunks,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        protected override void Dispose(bool disposing)
        {
            DisposeCalled = true;
            Disposing = disposing;
            base.Dispose(disposing);
        }
    }
}
