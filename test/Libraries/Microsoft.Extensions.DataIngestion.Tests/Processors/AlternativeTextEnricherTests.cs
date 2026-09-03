// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Extensions.DataIngestion.Processors.Tests;

public class AlternativeTextEnricherTests
{
    [Fact]
    public async Task ReturnsRewrittenImmutableDocumentWithGeneratedDescription()
    {
        using TestChatClient chatClient = new()
        {
            GetResponseAsyncCallback = (messages, options, cancellationToken) =>
                Task.FromResult(new ChatResponse(
                [
                    new ChatMessage(
                        ChatRole.Assistant,
                        JsonSerializer.Serialize(new Envelope<string[]> { data = ["generated"] })),
                ])),
        };
        DocumentImage original = new(new("image"), new byte[] { 1, 2, 3 }, "image/png");
        IngestionDocument input = TestDocuments.Create("id", original);

        IngestionDocument result = await new ImageAlternativeTextEnricher(new(chatClient)).ProcessAsync(input);

        Assert.Null(original.Description);
        DocumentImage rewritten = Assert.Single(result.Document.Nodes.OfType<DocumentImage>());
        Assert.Equal(original.Id, rewritten.Id);
        Assert.Equal("generated", rewritten.Description);
    }
}
