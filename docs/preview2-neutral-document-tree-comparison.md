# Preview 2 neutral shared document tree comparison

> **DRAFT / DO NOT MERGE.** This branch is an architecture comparison built on the Preview 2 common base, not a landing-ready proposal.

## Exact ancestry

- Authoritative Preview 2: `e124c123afeeda2f271f3b99a70eb3cfe187a471`
- Architecture-neutral extraction base: `f6ba2df16275bfc5eaf50aeb9327e2ec34ee8129`
- Evaluated Preview 2 neutral implementation: `704a3e44ef4d7b053748780549fc2c8e929a444b`
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

## Same-base implementation comparison

Both implementations are measured from `f6ba2df16275bfc5eaf50aeb9327e2ec34ee8129`.

| Measure | Bridge `c1913907` | Neutral `704a3e44` |
|---|---:|---:|
| Commits | 4 | 9 |
| Changed files | 28 | 95 |
| Total lines | +2,229/-42 | +3,590/-4,234 |
| All `src/` lines | +1,348/-40 | +2,753/-1,788 |
| All `test/` lines | +881/-2 | +712/-2,446 |
| Production C# | +1,304/-39 | +1,667/-1,482 |
| Test C# | +858/-2 | +680/-2,446 |
| Top-level public source declarations | +5/-0 | +16/-14 |

The paired bridge is PR #1, evaluated at `c1913907f05148370a84824b669d73249bb502e4`; its live presentation head is `a3033e0aa1aa25e4b5e360d73d500101e6b9af71`. Its verified consumer is iocrclient-demo PR #1 at `aa55dfe6a6d297b7a3edaf9107007cefcc9f09f6`.

These measurements compare implementation cost, not quality or performance.

## Author-run validation

- Generated 111-project filtered solution.
- Release build and all focused tests passed with 0 errors and 387 repeated analyzer warnings.
- Tests: 149 on each of `net10.0`, `net9.0`, and `net8.0`; 137 on `net462`; 584 executions, 0 skips.
- AI Chat template snapshot cases: 5 passed.
- ApiChief baselines reproduce exactly for all five changed/new public packages.
- Package dependency inspection confirms the neutral package references only `System.Text.Json`; core MEDI does not reference Document Extraction.
- Two focused code-review passes completed; confidence validation and canonical token separators were fixed.

## Package/feed attestation

Evaluated source: `704a3e44ef4d7b053748780549fc2c8e929a444b`
Package version: `10.8.0-preview2neutral.704a3e4`

```text
2b6002fc142dace6a5b08a1bc845eb544d08523c4f75d60c6384a36255e8f7b0  Microsoft.Extensions.DataIngestion.10.8.0-preview2neutral.704a3e4.nupkg
6b8a88bb5f52121b05022c834de890669f8a8327a54bafa148df063675cf2f4f  Microsoft.Extensions.DataIngestion.Abstractions.10.8.0-preview2neutral.704a3e4.nupkg
c2dd354bf6460b5f1f8b01186b5ff3f0c27ce790a6bb08535e30846250ca5d35  Microsoft.Extensions.DataIngestion.DocumentExtraction.10.8.0-preview2neutral.704a3e4.nupkg
fa54be131cc99b3c870ea9789cde03584967413302e2fa7ac53f9ac6e89b79a1  Microsoft.Extensions.DocumentExtraction.10.8.0-preview2neutral.704a3e4.nupkg
a4347cb50702c82127af83cbcb5852d3148a2429f7920f13b89c0538b67e2b65  Microsoft.Extensions.DocumentExtraction.Abstractions.10.8.0-preview2neutral.704a3e4.nupkg
c94ea97233f9756009012f8f25234974f56c950982d7021b2df22430d4c98f4b  Microsoft.Extensions.Documents.Abstractions.10.8.0-preview2neutral.704a3e4.nupkg
```

The feed is staged as a handoff artifact. Its owner must repack or attest these exact archives before updating the companion consumer.

## Open decisions

1. Should MEDI provide public immutable-tree rewrite helpers for processor authors?
2. Should default records persist `SourceNodeIds` in addition to pages?
3. What schema-version/evolution policy should govern serialized documents?
4. Does streamed extraction need a provider hook for cross-page logical hierarchy?
5. How should existing vector collections migrate to nullable `pagenumbers`?
