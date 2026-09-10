// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion;

/// <summary>Reads source content through an <see cref="IDocumentExtractionClient"/> for a MEDI pipeline.</summary>
public sealed class DocumentExtractionReader : IngestionDocumentReader
{
    /// <summary>
    /// The metadata key used by <see cref="DocumentExtractionReaderExtensions.TryGetExtractionResult(IngestionDocument, out DocumentExtractionResult?)"/> to
    /// hand the extraction-owned result to downstream processors without copying provider geometry into the shared tree.
    /// </summary>
    public const string ExtractionResultMetadataKey = "Microsoft.Extensions.DataIngestion.DocumentExtraction.Result";

    private readonly IDocumentExtractionClient _client;
    private readonly DocumentExtractionOptions? _options;

    /// <summary>Initializes a new instance of the <see cref="DocumentExtractionReader"/> class.</summary>
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
        IngestionDocument ingestionDocument = new(identifier, result.Document);
        ingestionDocument.Metadata[ExtractionResultMetadataKey] = result;
        return ingestionDocument;
    }
}
