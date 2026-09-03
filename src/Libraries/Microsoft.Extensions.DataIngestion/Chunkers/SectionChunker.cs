// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Documents;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Extensions.DataIngestion.Chunkers;

/// <summary>Treats each semantic section as a separate chunking unit.</summary>
public sealed class SectionChunker : IngestionChunker<string>
{
    private readonly ElementsChunker _elementsChunker;

    /// <summary>Initializes a new instance of the <see cref="SectionChunker"/> class.</summary>
    public SectionChunker(IngestionChunkerOptions options)
    {
        _elementsChunker = new(options);
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<IngestionChunk<string>> ProcessAsync(
        IngestionDocument document,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _ = Throw.IfNull(document);
        foreach (DocumentNode root in document.Document.Children)
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<IngestionChunk<string>> chunks = [];
            if (root is DocumentContainer { Role: DocumentContainerRole.Section } section)
            {
                Process(document, section, chunks);
            }
            else
            {
                chunks.AddRange(_elementsChunker.Process(document, string.Empty, [root]));
            }

            foreach (IngestionChunk<string> chunk in chunks)
            {
                yield return chunk;
            }
        }
    }

    private void Process(
        IngestionDocument document,
        DocumentContainer section,
        List<IngestionChunk<string>> chunks,
        IReadOnlyList<DocumentNode>? parentContextNodes = null)
    {
        List<DocumentNode> elements = [];
        List<DocumentNode> contextNodes = parentContextNodes is null ? [] : [.. parentContextNodes];

        for (int index = 0; index < section.Children.Count; index++)
        {
            switch (section.Children[index])
            {
                case DocumentText { Role: DocumentTextRole.Heading } heading when index == 0:
                    contextNodes.Add(heading);
                    break;
                case DocumentContainer { Role: DocumentContainerRole.Section } nested:
                    Commit();
                    Process(document, nested, chunks, contextNodes);
                    break;
                default:
                    elements.Add(section.Children[index]);
                    break;
            }
        }

        Commit();

        void Commit()
        {
            if (elements.Count == 0)
            {
                return;
            }

            string context = string.Join(" ", contextNodes.ConvertAll(static node => ((DocumentText)node).Text));
            chunks.AddRange(_elementsChunker.Process(document, context, elements, contextNodes));
            elements.Clear();
        }
    }
}
