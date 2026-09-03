// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion;

/// <summary>Reads source content through an <see cref="IDocumentExtractionClient"/> for a MEDI pipeline.</summary>
/// <remarks>
/// This integration owns operation composition only. The extracted semantic value already uses the shared document
/// tree, so no content-model mapping or duplicate semantic hierarchy is involved.
/// </remarks>
public sealed class DocumentExtractionReader : IngestionDocumentReader
{
    private readonly IDocumentExtractionClient _client;
    private readonly DocumentExtractionOptions? _options;

    /// <summary>Initializes a new instance of the <see cref="DocumentExtractionReader"/> class.</summary>
    /// <param name="client">The extraction client.</param>
    /// <param name="options">Optional extraction options.</param>
    public DocumentExtractionReader(IDocumentExtractionClient client, DocumentExtractionOptions? options = null)
    {
        _client = Throw.IfNull(client);
        _options = options?.Clone();
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
            .ExtractAsync(source, mediaType, _options?.Clone(), cancellationToken)
            .ConfigureAwait(false);

        return new IngestionDocument(identifier, result.Document);
    }
}
