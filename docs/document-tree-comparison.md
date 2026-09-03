# Neutral shared document tree comparison

> **DO NOT MERGE:** This is the preferred strategic comparison for Adam's architecture decision, not a merge candidate.

## Dependency direction

```mermaid
flowchart TD
    Documents[Microsoft.Extensions.Documents.Abstractions]
    Extraction[Microsoft.Extensions.DocumentExtraction.Abstractions]
    Ingestion[Microsoft.Extensions.DataIngestion.Abstractions]
    Medi[Microsoft.Extensions.DataIngestion]
    Integration[Microsoft.Extensions.DataIngestion.DocumentExtraction]

    Extraction --> Documents
    Ingestion --> Documents
    Medi --> Ingestion
    Medi --> Documents
    Integration --> Extraction
    Integration --> Ingestion
```

The shared package contains only the semantic value and a `System.Text.Json` compatibility reference for older target frameworks. It does not reference Document Extraction, Data Ingestion, VectorData, or AI. Core MEDI does not reference Document Extraction; the dedicated integration package owns client-to-reader composition.

## Semantic and evidence boundary

`Document` is one immutable ordered tree. Stable `DocumentNodeId` values identify text, containers, tables, cell content, and images. Logical sections, lists, list items, and quotes remain containers. `DocumentPageReference` annotations preserve physical provenance without introducing page containers or multi-parent traversal.

Extraction geometry, confidence, raw provider objects, and provider properties live in `DocumentExtractionEvidence`, keyed by shared node ID. Page dimensions, coordinate frames, exact provider Markdown, and streaming envelopes remain extraction concerns.

`Document.Text` and `DocumentTextProjection` are the only plain-text projection. They use fixed `\n` separators. There is no independently supplied text authority. Exact provider Markdown remains nullable on `DocumentPage`; it is neither parsed into the tree nor synthesized from it, and results do not aggregate it.

Streaming reduction sorts page fragments and concatenates their root nodes. IDs must be unique across fragments. It deliberately does not infer a logical container spanning pages.

## Compatibility implications

`IngestionDocument` remains only as a thin ingestion identity/context wrapper around `Document`. The previous MEDI element hierarchy was removed rather than retained as a competing authority. Existing MEDI readers and processors must construct or immutably rewrite shared nodes. Existing Markdown-first element constructors are source-breaking preview changes.

The finite node union is closed to external derivation and has explicit stable `System.Text.Json` `$type` discriminators. This restores the serializable exchange contract that extraction elements previously provided, while making future schema evolution a deliberate cross-package compatibility commitment.

## Measured comparison

Both alternatives are measured against common base `1fec8651d88b19ae855c39239e75645c548e5dde`. The shared-tree figures use head `e743fa91c8c8158226ece422a3e064693ddcb17e`; bridge figures use head `c4e31ad04b16996aed67bd75319cbf4fc4fc7d4e`.

| Measure | Bridge | Shared tree |
|---|---:|---:|
| All `src/` lines | +1,289/-52 | +2,079/-1,650 |
| All `test/` lines | +1,031/-1 | +941/-2,067 |
| Production C# (`src/**/*.cs`) | +1,148/-51 | +1,562/-1,339 |
| Test C# (`test/**/*.cs`) | +1,007/-1 | +912/-2,067 |
| New public types | 5 | 14 |
| Shared package public signatures | 0 | 49 across 13 types |
| Integration package public signatures | 2 | 2 |
| MEDI semantic types replaced | 0 | 7; `IngestionDocument` retained only as context |
| Neutral-package product dependencies | N/A | 0 |
| Consumer-owned mapping | 0 | 0 |

The shared direction does not have fewer total domain types: extraction operations/evidence, ingestion contexts/processors/chunks/writers, and the neutral semantic value remain separate concepts.

## Exercised contracts

The focused corpus covers literal text versus Markdown, headings and code language, logical lists and quotes, nested and spanned tables, captioned and captionless images, empty and Markdown-only extraction pages, multiple pages, typed page provenance, authored Markdown production, chunking, source-node provenance, pipeline writing, evidence isolation, and independent use of the neutral package.

## Open decisions

1. Whether the larger coordinated breaking change is justified versus the smaller bridge.
2. Whether immutable processor rewrite ergonomics need first-class helpers.
3. Whether the serialized contract needs an explicit schema-version marker and evolution policy.
4. Whether streamed page fragments need an explicit provider hook for reconstructing cross-page logical containers.
5. Whether binary-image chunk production belongs in core MEDI or in an image-specific chunker.
