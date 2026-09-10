// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Documents;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion;

/// <summary>Adds ingestion identity and context to a shared semantic <see cref="Documents.Document"/>.</summary>
public sealed class IngestionDocument
{
    private Dictionary<string, object?>? _metadata;

    /// <summary>Initializes a new instance of the <see cref="IngestionDocument"/> class.</summary>
    public IngestionDocument(string identifier, Document document)
    {
        Identifier = Throw.IfNullOrEmpty(identifier);
        Document = Throw.IfNull(document);
    }

    /// <summary>Gets the unique ingestion identifier.</summary>
    public string Identifier { get; }

    /// <summary>Gets the immutable canonical semantic document.</summary>
    public Document Document { get; }

    /// <summary>Gets a value indicating whether ingestion context metadata has been added.</summary>
    public bool HasMetadata => _metadata?.Count > 0;

    /// <summary>Gets mutable ingestion-only context metadata.</summary>
    public IDictionary<string, object?> Metadata => _metadata ??= [];
}
