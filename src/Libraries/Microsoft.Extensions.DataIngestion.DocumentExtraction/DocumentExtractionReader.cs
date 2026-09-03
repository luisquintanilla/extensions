// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion;

/// <summary>
/// Reads documents by using an <see cref="IDocumentExtractionClient"/> and explicitly mapping its
/// normalized output into an <see cref="IngestionDocument"/>.
/// </summary>
public sealed class DocumentExtractionReader : IngestionDocumentReader
{
    private readonly IDocumentExtractionClient _client;
    private readonly DocumentExtractionOptions? _extractionOptions;
    private readonly MarkdownOnlyPagePolicy _markdownOnlyPagePolicy;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentExtractionReader"/> class.
    /// </summary>
    /// <param name="client">The document extraction client.</param>
    /// <param name="options">The reader options.</param>
    public DocumentExtractionReader(
        IDocumentExtractionClient client,
        DocumentExtractionReaderOptions? options = null)
    {
        _client = Throw.IfNull(client);
        options ??= new();
        _extractionOptions = options.ExtractionOptions?.Clone();
        _markdownOnlyPagePolicy = options.MarkdownOnlyPagePolicy;

        if (_markdownOnlyPagePolicy is not MarkdownOnlyPagePolicy.RequireElements
            and not MarkdownOnlyPagePolicy.PreserveAsMarkdown)
        {
            Throw.ArgumentOutOfRangeException(
                nameof(options),
                $"Unsupported Markdown-only page policy '{_markdownOnlyPagePolicy}'.");
        }
    }

    /// <inheritdoc/>
    public override async Task<IngestionDocument> ReadAsync(
        Stream source,
        string identifier,
        string mediaType,
        CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(source);
        _ = Throw.IfNullOrEmpty(identifier);
        _ = Throw.IfNullOrEmpty(mediaType);

        DocumentExtractionResult result = await _client
            .ExtractAsync(
                source,
                mediaType,
                _extractionOptions?.Clone(),
                cancellationToken)
            .ConfigureAwait(false);

        return DefaultDocumentExtractionMapper.Map(result, identifier, _markdownOnlyPagePolicy);
    }
}
