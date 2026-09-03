# Microsoft.Extensions.DataIngestion.DocumentExtraction

Connects `IDocumentExtractionClient` output to Microsoft.Extensions.DataIngestion through an explicit,
deterministic mapping. The extraction and ingestion models remain distinct because they own different
invariants.

## Usage

```csharp
using Microsoft.Extensions.DataIngestion;

IngestionDocumentReader reader = new DocumentExtractionReader(extractionClient);
IngestionDocument document =
    await reader.ReadAsync(stream, "document-id", "application/pdf");
```

The default mapping requires canonical extraction elements. A page containing only provider Markdown
fails rather than silently treating markup as literal text. Applications that intentionally want to
retain that source-format output can opt in:

```csharp
IngestionDocumentReader reader = new DocumentExtractionReader(
    extractionClient,
    new()
    {
        MarkdownOnlyPagePolicy = MarkdownOnlyPagePolicy.PreserveAsMarkdown,
    });
```

In that mode the exact provider value uses MEDI's existing Markdown construction path. It is never
parsed, copied into a literal text node, or used when canonical elements are present.
