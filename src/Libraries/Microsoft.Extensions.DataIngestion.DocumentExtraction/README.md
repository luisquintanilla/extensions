# Microsoft.Extensions.DataIngestion.DocumentExtraction

This package composes `IDocumentExtractionClient` with MEDI through `DocumentExtractionReader`.

It references both abstractions packages. Core MEDI does not reference Document Extraction, and no semantic mapping is required because both use `Microsoft.Extensions.Documents.Abstractions`.

```csharp
IngestionDocumentReader reader = new DocumentExtractionReader(extractionClient);
IngestionDocument document = await reader.ReadAsync(source, "report.pdf", "application/pdf");

IngestionChunker chunker = new SectionChunker(
    new IngestionChunkerOptions(TiktokenTokenizer.CreateForModel("gpt-4"))
    {
        MaxTokensPerChunk = 800,
    });

VectorStoreCollection<Guid, IngestionChunkVectorRecord> collection =
    vectorStore.GetIngestionRecordCollection<IngestionChunkVectorRecord>(
        "chunks", dimensionCount: 1536);
using VectorStoreWriter<IngestionChunkVectorRecord> writer = new(collection);
await writer.WriteAsync(chunker.ProcessAsync(document));
```

`DocumentExtractionReader` returns an `IngestionDocument` that uses the exact shared `result.Document` instance. It also hands off the extraction-owned result through `document.TryGetExtractionResult(...)`, so downstream processors can look up evidence by node identity without copying geometry into the neutral tree or vector records. Provider Markdown, evidence, geometry, confidence, usage, progress, and raw state remain extraction-owned.
