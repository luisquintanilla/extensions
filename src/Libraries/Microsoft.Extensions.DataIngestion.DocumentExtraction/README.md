# Microsoft.Extensions.DataIngestion.DocumentExtraction

Connects `IDocumentExtractionClient` to Preview 2 MEDI through an explicit deterministic mapping.
The extraction and ingestion models remain distinct.

```csharp
IngestionDocumentReader reader = new DocumentExtractionReader(extractionClient);
IngestionDocument document =
    await reader.ReadAsync(stream, "document-id", "application/pdf");
```

Canonical extraction elements take precedence over provider Markdown. Markdown-only pages fail by
default; `MarkdownOnlyPagePolicy.PreserveAsMarkdown` explicitly preserves the provider value through
MEDI's existing Markdown path without copying it to `Text`.

The integration package references the Data Ingestion and Document Extraction abstractions in
parallel. Neither core abstraction depends on the other.
