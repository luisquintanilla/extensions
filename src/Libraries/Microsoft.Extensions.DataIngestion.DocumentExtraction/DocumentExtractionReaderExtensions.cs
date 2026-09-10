// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion;

/// <summary>Provides access to extraction-owned state handed off by <see cref="DocumentExtractionReader"/>.</summary>
public static class DocumentExtractionReaderExtensions
{
    /// <summary>Attempts to retrieve the extraction-owned result handed off by <see cref="DocumentExtractionReader"/>.</summary>
    /// <param name="document">The ingestion document.</param>
    /// <param name="result">The extraction result, when the document was produced by this reader.</param>
    /// <returns><see langword="true"/> when an extraction result is available; otherwise, <see langword="false"/>.</returns>
    public static bool TryGetExtractionResult(this IngestionDocument document, out DocumentExtractionResult? result)
    {
        _ = Throw.IfNull(document);

        if (document.Metadata.TryGetValue(DocumentExtractionReader.ExtractionResultMetadataKey, out object? value) &&
            value is DocumentExtractionResult extractionResult)
        {
            result = extractionResult;
            return true;
        }

        result = null;
        return false;
    }
}
