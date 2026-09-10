// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Processors.Tests;

public class AlternativeTextEnricherTests
{
    [Fact]
    public async Task ReturnsImmutableRewriteWithStableIdentity()
    {
        using TestChatClient client = new()
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) =>
                Task.FromResult(new ChatResponse(
                [
                    new ChatMessage(ChatRole.Assistant, JsonSerializer.Serialize(new Envelope<string[]> { data = ["generated"] })),
                ])),
        };
        DocumentImage image = new(new("image"), new byte[] { 1 }, "image/png");
        IngestionDocument input = TestDocuments.Create("id", image);

        IngestionDocument result = await new ImageAlternativeTextEnricher(new(client)).ProcessAsync(input);

        Assert.Null(image.Description);
        DocumentImage rewritten = Assert.Single(result.Document.Nodes.OfType<DocumentImage>());
        Assert.Equal(image.Id, rewritten.Id);
        Assert.Equal("generated", rewritten.Description);
    }

    [Fact]
    public void Constructor_NullDependenciesThrow()
    {
        Assert.Throws<ArgumentNullException>("options", () => new ImageAlternativeTextEnricher(null!));
    }

    [Fact]
    public async Task ProcessAsync_NullDocumentThrows()
    {
        using TestChatClient client = new();
        ImageAlternativeTextEnricher sut = new(new(client));

        await Assert.ThrowsAsync<ArgumentNullException>("document", () => sut.ProcessAsync(null!));
    }

    [Fact]
    public async Task ProcessAsync_SelectsOnlyImagesWithoutAlternativeText()
    {
        int callCount = 0;
        DocumentImage selected = new(new("selected"), new byte[] { 1, 2 }, "image/png");
        DocumentImage described = new(new("described"), new byte[] { 3, 4 }, "image/jpeg", description: "existing description");
        IngestionDocument input = TestDocuments.Create("images", selected, described);
        using TestChatClient client = new()
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) =>
            {
                callCount++;
                DataContent payload = Assert.IsType<DataContent>(Assert.Single(messages.Last().Contents));
                Assert.Equal(new byte[] { 1, 2 }, payload.Data.ToArray());
                return Task.FromResult(CreateResponse("generated description"));
            },
        };

        IngestionDocument result = await new ImageAlternativeTextEnricher(new(client)).ProcessAsync(input);

        Assert.Equal(1, callCount);
        Assert.NotSame(input, result);
        Assert.Equal(["selected", "described"], result.Document.Children.Select(node => node.Id.Value));
        DocumentImage rewritten = Assert.IsType<DocumentImage>(result.Document.Children[0]);
        Assert.NotSame(selected, rewritten);
        Assert.Equal("generated description", rewritten.Description);
        Assert.Same(described, result.Document.Children[1]);
        Assert.Equal("existing description", described.Description);
        Assert.Null(selected.Description);
        Assert.Same(selected, input.Document.Children[0]);
        Assert.Same(described, input.Document.Children[1]);
    }

    [Fact]
    public async Task ProcessAsync_BuildsExpectedImagePayloadAndPrompt()
    {
        int callCount = 0;
        DocumentImage first = new(new("first"), new byte[] { 0x01, 0x02, 0x03 }, "image/png");
        DocumentImage second = new(new("second"), new byte[] { 0xA0, 0xB0 }, "image/webp");
        IngestionDocument input = TestDocuments.Create("payload", first, second);
        using TestChatClient client = new()
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) =>
            {
                callCount++;
                ChatMessage[] request = messages.ToArray();
                Assert.Equal(2, request.Length);
                Assert.Equal(ChatRole.System, request[0].Role);
                Assert.Equal(
                    "For each image, write detailed alternative text with fewer than 50 words.",
                    request[0].Text);
                Assert.Equal(ChatRole.User, request[1].Role);
                Assert.Equal(2, request[1].Contents.Count);

                DataContent firstPayload = Assert.IsType<DataContent>(request[1].Contents[0]);
                Assert.Equal("image/png", firstPayload.MediaType);
                Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, firstPayload.Data.ToArray());
                DataContent secondPayload = Assert.IsType<DataContent>(request[1].Contents[1]);
                Assert.Equal("image/webp", secondPayload.MediaType);
                Assert.Equal(new byte[] { 0xA0, 0xB0 }, secondPayload.Data.ToArray());
                return Task.FromResult(CreateResponse("first alternative", "second alternative"));
            },
        };

        IngestionDocument result = await new ImageAlternativeTextEnricher(new(client)).ProcessAsync(input);

        Assert.Equal(1, callCount);
        Assert.Equal(
            ["first alternative", "second alternative"],
            result.Document.Nodes.OfType<DocumentImage>().Select(image => image.Description));
        Assert.All([first, second], image => Assert.Null(image.Description));
        Assert.NotSame(input, result);
        Assert.Same(first, input.Document.Children[0]);
        Assert.Same(second, input.Document.Children[1]);
    }

    [Fact]
    public async Task ProcessAsync_BatchesSelectedImagesAtConfiguredSize()
    {
        List<int[]> requestBatches = [];
        DocumentImage[] images = Enumerable.Range(1, 5)
            .Select(index => new DocumentImage(new($"image-{index}"), new byte[] { (byte)index, 0xFF }, "image/png"))
            .ToArray();
        IngestionDocument input = TestDocuments.Create("batched-images", images);
        using TestChatClient client = new()
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) =>
            {
                int[] batch = messages.Last().Contents
                    .Cast<DataContent>()
                    .Select(content => (int)content.Data.Span[0])
                    .ToArray();
                requestBatches.Add(batch);
                return Task.FromResult(CreateResponse(batch.Select(index => $"alternative-{index}").ToArray()));
            },
        };
        ImageAlternativeTextEnricher sut = new(new EnricherOptions(client) { BatchSize = 2 });

        IngestionDocument result = await sut.ProcessAsync(input);

        Assert.Equal(3, requestBatches.Count);
        Assert.Equal([1, 2], requestBatches[0]);
        Assert.Equal([3, 4], requestBatches[1]);
        Assert.Equal([5], requestBatches[2]);
        Assert.Equal(
            ["alternative-1", "alternative-2", "alternative-3", "alternative-4", "alternative-5"],
            result.Document.Nodes.OfType<DocumentImage>().Select(image => image.Description));
        Assert.All(images, image => Assert.Null(image.Description));
        Assert.NotSame(input, result);
        Assert.Equal(images, input.Document.Children);
    }

    [Fact]
    public async Task ProcessAsync_RewritesNestedContainerAndTableCellImages()
    {
        DocumentText rootText = TestDocuments.Text("root-text", "root text");
        DocumentImage rootImage = new(new("root-image"), new byte[] { 1 }, "image/png");
        DocumentText nestedText = TestDocuments.Text("nested-text", "nested text");
        DocumentImage nestedImage = new(new("nested-image"), new byte[] { 2 }, "image/png");
        DocumentContainer container = new(
            new("container"),
            DocumentContainerRole.Section,
            [nestedText, nestedImage]);
        DocumentText cellText = TestDocuments.Text("cell-text", "cell text");
        DocumentImage cellImage = new(new("cell-image"), new byte[] { 3 }, "image/png");
        DocumentTableCell cell = new(new("cell"), 0, 0, [cellText, cellImage]);
        DocumentTable table = new(new("table"), 1, 1, [cell]);
        IngestionDocument input = TestDocuments.Create("nested", rootText, rootImage, container, table);
        int[]? observedPayloadOrder = null;
        using TestChatClient client = new()
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) =>
            {
                observedPayloadOrder = messages.Last().Contents
                    .Cast<DataContent>()
                    .Select(content => (int)content.Data.Span[0])
                    .ToArray();
                return Task.FromResult(CreateResponse("root alternative", "nested alternative", "cell alternative"));
            },
        };

        IngestionDocument result = await new ImageAlternativeTextEnricher(new(client)).ProcessAsync(input);

        Assert.NotNull(observedPayloadOrder);
        Assert.Equal([1, 2, 3], observedPayloadOrder);
        Assert.Equal(["root-text", "root-image", "container", "table"], result.Document.Children.Select(node => node.Id.Value));
        Assert.Same(rootText, result.Document.Children[0]);
        Assert.NotSame(rootImage, result.Document.Children[1]);

        DocumentContainer rewrittenContainer = Assert.IsType<DocumentContainer>(result.Document.Children[2]);
        Assert.NotSame(container, rewrittenContainer);
        Assert.Equal(["nested-text", "nested-image"], rewrittenContainer.Children.Select(node => node.Id.Value));
        Assert.Same(nestedText, rewrittenContainer.Children[0]);

        DocumentTable rewrittenTable = Assert.IsType<DocumentTable>(result.Document.Children[3]);
        Assert.NotSame(table, rewrittenTable);
        DocumentTableCell rewrittenCell = Assert.Single(rewrittenTable.Cells);
        Assert.NotSame(cell, rewrittenCell);
        Assert.Equal(["cell-text", "cell-image"], rewrittenCell.Content.Select(node => node.Id.Value));
        Assert.Same(cellText, rewrittenCell.Content[0]);

        Assert.Equal(
            ["root alternative", "nested alternative", "cell alternative"],
            result.Document.Nodes.OfType<DocumentImage>().Select(image => image.Description));
        Assert.All([rootImage, nestedImage, cellImage], image => Assert.Null(image.Description));
        Assert.Same(rootImage, input.Document.GetNode(rootImage.Id));
        Assert.Same(nestedImage, input.Document.GetNode(nestedImage.Id));
        Assert.Same(cellImage, input.Document.GetNode(cellImage.Id));
        Assert.NotSame(input, result);
    }

    [Fact]
    public async Task ProcessAsync_SkipsImagesWithAlternativeTextOrUnsupportedPayload()
    {
        int callCount = 0;
        DocumentImage describedBinary = new(
            new("described-binary"),
            new byte[] { 1 },
            "image/png",
            description: "already accessible");
        DocumentImage sourceOnly = new(
            new("source-only"),
            source: new Uri("https://example.invalid/image.png"));
        DocumentImage descriptionOnly = new(
            new("description-only"),
            description: "literal alternative");
        IngestionDocument input = TestDocuments.Create("skipped", describedBinary, sourceOnly, descriptionOnly);
        using TestChatClient client = new()
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) =>
            {
                callCount++;
                return Task.FromResult(CreateResponse("unexpected"));
            },
        };

        IngestionDocument result = await new ImageAlternativeTextEnricher(new(client)).ProcessAsync(input);

        Assert.Equal(0, callCount);
        Assert.Same(input, result);
        Assert.Same(input.Document, result.Document);
        Assert.Equal(
            ["already accessible", null, "literal alternative"],
            result.Document.Nodes.OfType<DocumentImage>().Select(image => image.Description));
        Assert.Same(describedBinary, result.Document.Children[0]);
        Assert.Same(sourceOnly, result.Document.Children[1]);
        Assert.Same(descriptionOnly, result.Document.Children[2]);
    }

    [Fact]
    public async Task ProcessAsync_PreservesDocumentIdentityMetadataNodeIdsAndPages()
    {
        Uri source = new("https://example.invalid/original-image.png");
        DocumentImage image = new(
            new("image-id"),
            new byte[] { 7, 8, 9 },
            "image/png",
            source,
            pageReferences: [new(2), new(4)],
            sourceNodeIds: [new("ocr-image"), new("crop-image")]);
        IngestionDocument input = TestDocuments.Create("stable-document-id", image);
        object owner = new();
        input.Metadata["owner"] = owner;
        input.Metadata["revision"] = 42;
        using TestChatClient client = new()
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) =>
                Task.FromResult(CreateResponse("preserved image")),
        };

        IngestionDocument result = await new ImageAlternativeTextEnricher(new(client)).ProcessAsync(input);

        Assert.Equal("stable-document-id", result.Identifier);
        Assert.NotSame(input, result);
        Assert.NotSame(input.Document, result.Document);
        Assert.NotSame(input.Metadata, result.Metadata);
        Assert.Equal(2, result.Metadata.Count);
        Assert.Same(owner, result.Metadata["owner"]);
        Assert.Equal(42, result.Metadata["revision"]);

        DocumentImage rewritten = Assert.IsType<DocumentImage>(Assert.Single(result.Document.Children));
        Assert.Equal(new DocumentNodeId("image-id"), rewritten.Id);
        Assert.Equal([new DocumentPageReference(2), new DocumentPageReference(4)], rewritten.PageReferences);
        Assert.Equal([new DocumentNodeId("ocr-image"), new DocumentNodeId("crop-image")], rewritten.SourceNodeIds);
        Assert.Equal(source, rewritten.Source);
        Assert.Equal("image/png", rewritten.MediaType);
        Assert.Equal(new byte[] { 7, 8, 9 }, rewritten.Content.ToArray());
        Assert.Equal("preserved image", rewritten.Description);

        Assert.Null(image.Description);
        Assert.Same(image, input.Document.GetNode(image.Id));
        Assert.Equal([new DocumentPageReference(2), new DocumentPageReference(4)], image.PageReferences);
        Assert.Equal([new DocumentNodeId("ocr-image"), new DocumentNodeId("crop-image")], image.SourceNodeIds);
    }

    [Fact]
    public async Task ProcessAsync_ResultCountMismatch_AppliesDocumentedBestEffortBehavior()
    {
        FakeLogCollector collector = new();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)));
        int callCount = 0;
        DocumentImage first = new(new("first"), new byte[] { 1 }, "image/png");
        DocumentImage second = new(new("second"), new byte[] { 2 }, "image/png");
        IngestionDocument input = TestDocuments.Create("mismatch", first, second);
        using TestChatClient client = new()
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) =>
            {
                callCount++;
                return Task.FromResult(CreateResponse("only one result"));
            },
        };
        ImageAlternativeTextEnricher sut = new(new EnricherOptions(client)
        {
            BatchSize = 2,
            LoggerFactory = loggerFactory,
        });

        IngestionDocument result = await sut.ProcessAsync(input);

        Assert.Equal(1, callCount);
        Assert.Same(input, result);
        Assert.Same(input.Document, result.Document);
        Assert.All(result.Document.Nodes.OfType<DocumentImage>(), image => Assert.Null(image.Description));
        Assert.Same(first, result.Document.Children[0]);
        Assert.Same(second, result.Document.Children[1]);
        Assert.Equal(1, collector.Count);
        Assert.Equal(7, collector.LatestRecord.Id.Id);
        Assert.Equal(LogLevel.Error, collector.LatestRecord.Level);
        Assert.Equal("The AI chat service returned 1 instead of 2 results.", collector.LatestRecord.Message);
        Assert.Null(collector.LatestRecord.Exception);
    }

    [Fact]
    public async Task ProcessAsync_ChatFailure_LogsAndReturnsBestEffortDocument()
    {
        FakeLogCollector collector = new();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)));
        int callCount = 0;
        DocumentImage first = new(new("first"), new byte[] { 1 }, "image/png");
        DocumentImage second = new(new("second"), new byte[] { 2 }, "image/png");
        IngestionDocument input = TestDocuments.Create("failure", first, second);
        using TestChatClient client = new()
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) =>
            {
                callCount++;
                return callCount == 1
                    ? Task.FromResult(CreateResponse("first alternative"))
                    : Task.FromException<ChatResponse>(new ExpectedException());
            },
        };
        ImageAlternativeTextEnricher sut = new(new EnricherOptions(client)
        {
            BatchSize = 1,
            LoggerFactory = loggerFactory,
        });

        IngestionDocument result = await sut.ProcessAsync(input);

        Assert.Equal(2, callCount);
        Assert.NotSame(input, result);
        Assert.Equal(
            ["first alternative", null],
            result.Document.Nodes.OfType<DocumentImage>().Select(image => image.Description));
        Assert.NotSame(first, result.Document.Children[0]);
        Assert.Same(second, result.Document.Children[1]);
        Assert.Null(first.Description);
        Assert.Null(second.Description);
        Assert.Same(first, input.Document.Children[0]);
        Assert.Same(second, input.Document.Children[1]);
        Assert.Equal(1, collector.Count);
        Assert.Equal(8, collector.LatestRecord.Id.Id);
        Assert.Equal(LogLevel.Error, collector.LatestRecord.Level);
        Assert.Equal("Unexpected enricher failure.", collector.LatestRecord.Message);
        Assert.IsType<ExpectedException>(collector.LatestRecord.Exception);
    }

    [Fact]
    public async Task ProcessAsync_Cancellation_ForwardsTokenAndCharacterizesCatchBehavior()
    {
        FakeLogCollector collector = new();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)));
        using CancellationTokenSource cancellationSource = new();
        List<CancellationToken> observedTokens = [];
        int callCount = 0;
        DocumentImage first = new(new("first"), new byte[] { 1 }, "image/png");
        DocumentImage second = new(new("second"), new byte[] { 2 }, "image/png");
        IngestionDocument input = TestDocuments.Create("cancellation", first, second);
        using TestChatClient client = new()
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) =>
            {
                callCount++;
                observedTokens.Add(cancellationToken);
                if (callCount == 1)
                {
                    return Task.FromResult(CreateResponse("first alternative"));
                }

                cancellationSource.Cancel();
                return Task.FromCanceled<ChatResponse>(cancellationToken);
            },
        };
        ImageAlternativeTextEnricher sut = new(new EnricherOptions(client)
        {
            BatchSize = 1,
            LoggerFactory = loggerFactory,
        });

        IngestionDocument result = await sut.ProcessAsync(input, cancellationSource.Token);

        Assert.Equal(2, callCount);
        Assert.Equal([cancellationSource.Token, cancellationSource.Token], observedTokens);
        Assert.All(observedTokens, token => Assert.True(token.IsCancellationRequested));
        Assert.True(cancellationSource.IsCancellationRequested);
        Assert.NotSame(input, result);
        Assert.Equal(
            ["first alternative", null],
            result.Document.Nodes.OfType<DocumentImage>().Select(image => image.Description));
        Assert.NotSame(first, result.Document.Children[0]);
        Assert.Same(second, result.Document.Children[1]);
        Assert.Null(first.Description);
        Assert.Null(second.Description);
        Assert.Same(first, input.Document.Children[0]);
        Assert.Same(second, input.Document.Children[1]);
        Assert.Equal(1, collector.Count);
        Assert.Equal(8, collector.LatestRecord.Id.Id);
        Assert.Equal(LogLevel.Error, collector.LatestRecord.Level);
        Assert.IsAssignableFrom<OperationCanceledException>(collector.LatestRecord.Exception);
    }

    private static ChatResponse CreateResponse(params string[] descriptions) =>
        new(
        [
            new ChatMessage(
                ChatRole.Assistant,
                JsonSerializer.Serialize(new Envelope<string[]> { data = descriptions })),
        ]);
}
