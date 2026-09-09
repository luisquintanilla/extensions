# Preview 2 neutral shared document tree comparison

> **DRAFT / DO NOT MERGE.** This branch is an architecture comparison built on the Preview 2 common base, not a landing-ready proposal.

## Exact ancestry

- Authoritative Preview 2: `e124c123afeeda2f271f3b99a70eb3cfe187a471`
- Architecture-neutral extraction base: `f6ba2df16275bfc5eaf50aeb9327e2ec34ee8129`
- Superseded generic experiment presentation: `a7f00e8052a4789d1ca5e67e4c6b9c5552b07ab6`
- Superseded generic validated implementation: `f8aa67620d6174850fcd6f2ceb62096a33488ccc`

The current implementation descends from `f6ba2df1`, whose parent is `e124c123`. Historical generic commits were not rebased or cherry-picked.

## Ownership

`Microsoft.Extensions.Documents.Abstractions` owns one closed immutable semantic tree. Document Extraction owns operations, page envelopes, Markdown, usage/progress, geometry, confidence, evidence, properties, and raw provider state. MEDI owns ingestion identity/context, processors, non-generic chunkers/chunks/writers, pipeline orchestration, typed vector records, and persistence.

Preview 2 contracts remain non-generic:

```csharp
public class IngestionChunk
{
    public AIContent Content { get; }
    public IngestionDocument Document { get; }
    public int TokenCount { get; }
    public IReadOnlyList<DocumentNodeId> SourceNodeIds { get; }
    public IReadOnlyList<int> PageNumbers { get; }
}

public abstract class IngestionChunker;
public abstract class IngestionChunkProcessor;
public abstract class IngestionChunkWriter;
public sealed class IngestionPipeline;
public class VectorStoreWriter<TRecord>
    where TRecord : IngestionChunkVectorRecord, new();
```

`AIContent` remains the chunk boundary. Built-in semantic chunkers emit `TextContent`; callers may create non-text chunks such as `DataContent`. `TokenCount` remains required and positive.

## Typed record persistence

`IngestionChunkVectorRecord` keeps polymorphic `AIContent` serialization and `Embedding => Content`, so configured VectorData providers generate embeddings during upsert. It adds nullable `SerializedPageNumbers` with storage name `pagenumbers` and a typed `PageNumbers` view. Source-node IDs remain typed on chunks but are not persisted by the default record.

## Critical MEDI deviations

Every changed MEDI file relative to `f6ba2df1` is required by the neutral architecture:

| File | Justification |
|---|---|
| `DataIngestion.Abstractions/IngestionDocument.cs` | Thin identity/context wrapper over shared `Document` |
| `DataIngestion.Abstractions/IngestionChunk.cs` | Typed source/page provenance while preserving `AIContent` and `TokenCount` |
| `DataIngestion.Abstractions/IngestionDocumentElement.cs` | Removed competing semantic hierarchy |
| `DataIngestion/DocumentNodeExtensions.cs` | Shared-tree traversal and provenance helpers |
| `DataIngestion/IngestionDocumentElementExtensions.cs` | Removed obsolete MEDI element helper |
| `DataIngestion/Chunkers/ElementsChunker.cs` | Shared nodes, exact text projection, table provenance |
| `DataIngestion/Chunkers/DocumentTokenChunker.cs` | Recursive projection-range provenance |
| `DataIngestion/Chunkers/HeaderChunker.cs` | Shared heading roles |
| `DataIngestion/Chunkers/SectionChunker.cs` | Shared logical sections |
| `DataIngestion/Chunkers/SemanticSimilarityChunker.cs` | Shared nodes while retaining embedding-based grouping |
| `DataIngestion/Processors/ImageAlternativeTextEnricher.cs` | Explicit immutable tree rewrite |
| `DataIngestion/Writers/IngestionChunkVectorRecord.cs` | Typed page persistence without replacing automatic embeddings |
| `DataIngestion/Writers/VectorStoreWriter.cs` | Copies typed chunk pages into the record |
| `DataIngestion.Markdig/MarkdownParser.cs` | Authored producer for shared semantics |

Project references add only the neutral package. Non-generic pipeline contracts, `VectorStoreExtensions`, embedding generation, batch limits, incremental ingestion, and typed custom-record extensibility remain Preview 2 behavior.

## Exercised behavior

Deterministic tests cover closed hierarchy, stable IDs, ordered recursive projection, polymorphic serialization, dimension-independent table validation, page/evidence validation, immutable image enrichment, recursive token/source provenance, authored Markdown, non-generic `TextContent` chunks, required token counts, non-text `DataContent`, polymorphic record round trips, provider-driven embeddings, page persistence, and extraction-to-retrieval composition.

Provider integrations are compile-only. No live-provider, quality, performance, or merge-readiness claim is made.

## Open decisions

1. Should MEDI provide public immutable-tree rewrite helpers for processor authors?
2. Should default records persist `SourceNodeIds` in addition to pages?
3. What schema-version/evolution policy should govern serialized documents?
4. Does streamed extraction need a provider hook for cross-page logical hierarchy?
5. How should existing vector collections migrate to nullable `pagenumbers`?
