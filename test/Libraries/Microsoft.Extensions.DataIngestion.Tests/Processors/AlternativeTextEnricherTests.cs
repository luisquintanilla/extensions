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
}
