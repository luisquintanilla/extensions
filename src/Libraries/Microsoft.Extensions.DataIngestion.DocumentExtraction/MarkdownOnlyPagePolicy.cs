// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.DataIngestion;

/// <summary>
/// Specifies how <see cref="DocumentExtractionReader"/> maps provider Markdown when a page has no
/// normalized document elements.
/// </summary>
public enum MarkdownOnlyPagePolicy
{
    /// <summary>Require normalized elements and fail rather than reinterpret provider Markdown.</summary>
    RequireElements,

    /// <summary>Preserve provider Markdown through MEDI's explicit Markdown construction path.</summary>
    PreserveAsMarkdown,
}
