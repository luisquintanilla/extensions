// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.DataIngestion;

/// <summary>
/// Specifies how <see cref="DocumentExtractionReader"/> maps a page that contains provider Markdown
/// but no normalized document elements.
/// </summary>
public enum MarkdownOnlyPagePolicy
{
    /// <summary>
    /// Require normalized elements and fail rather than silently reinterpret provider Markdown.
    /// </summary>
    RequireElements,

    /// <summary>
    /// Preserve the provider Markdown through MEDI's explicit Markdown construction path.
    /// </summary>
    PreserveAsMarkdown,
}
