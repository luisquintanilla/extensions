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

`DocumentExtractionReader` returns `new IngestionDocument(identifier, result.Document)`, preserving shared document identity. Provider Markdown, evidence, geometry, confidence, usage, progress, and raw state remain extraction-owned.
