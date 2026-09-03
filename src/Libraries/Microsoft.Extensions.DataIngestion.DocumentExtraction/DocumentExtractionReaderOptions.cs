// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DocumentExtraction;

namespace Microsoft.Extensions.DataIngestion;

/// <summary>
/// Configures <see cref="DocumentExtractionReader"/>.
/// </summary>
public sealed class DocumentExtractionReaderOptions
{
    /// <summary>
    /// Gets or sets the options passed to the document extraction client.
    /// </summary>
    public DocumentExtractionOptions? ExtractionOptions { get; set; }

    /// <summary>
    /// Gets or sets the policy for pages that contain provider Markdown but no normalized elements.
    /// </summary>
    public MarkdownOnlyPagePolicy MarkdownOnlyPagePolicy { get; set; } = MarkdownOnlyPagePolicy.RequireElements;
}
