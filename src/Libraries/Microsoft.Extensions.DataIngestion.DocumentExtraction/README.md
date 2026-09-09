# Microsoft.Extensions.DataIngestion.DocumentExtraction

Connects `IDocumentExtractionClient` to Preview 2 MEDI through an explicit deterministic mapping.
The extraction and ingestion models remain distinct because they own different invariants.

## Common path

```csharp
IngestionDocumentReader reader = new DocumentExtractionReader(extractionClient);
IngestionDocument document = await reader.ReadAsync(
    stream,
    "document-id",
    "application/pdf");

IngestionChunker chunker = new SectionChunker(new(
    TiktokenTokenizer.CreateForModel("gpt-4o"))
{
    MaxTokensPerChunk = 256,
    OverlapTokens = 0,
});

IAsyncEnumerable<IngestionChunk> chunks = chunker.ProcessAsync(document);
await writer.WriteAsync(chunks);
```

Chunks remain Preview 2's non-generic `IngestionChunk` values with `AIContent` and required
`TokenCount`. The stock `VectorStoreWriter<TRecord>` persists polymorphic content and leaves
embedding generation to the configured VectorData provider during upsert.

## Dependency direction

```text
Microsoft.Extensions.DataIngestion.DocumentExtraction
  +---> Microsoft.Extensions.DataIngestion.Abstractions
  +---> Microsoft.Extensions.DocumentExtraction.Abstractions
```

Neither core abstraction depends on the other. No neutral shared-document package is introduced.

## Markdown policy

Canonical extraction elements take precedence over provider Markdown. Markdown-only pages fail by
default:

```csharp
IngestionDocumentReader reader = new DocumentExtractionReader(extractionClient);
// ReadAsync throws when a page has Markdown but no normalized Elements.
```

Applications can explicitly preserve exact provider Markdown:

```csharp
IngestionDocumentReader reader = new DocumentExtractionReader(
    extractionClient,
    new()
    {
        MarkdownOnlyPagePolicy = MarkdownOnlyPagePolicy.PreserveAsMarkdown,
    });
```

The preserved value uses MEDI's existing Markdown construction path. It is never parsed, copied to
`Text`, or used when canonical `Elements` exist.

## Mapping and loss policy

| Extraction fact | MEDI behavior |
|---|---|
| Page and one-based page number | One `IngestionDocumentSection`; page copied to section and mapped elements |
| Canonical ordered `Elements` | Mapped in order and take precedence over page Markdown |
| Title and paragraph blocks | Literal header and paragraph elements |
| Code block | Typed `IngestionDocumentCodeBlock` with collision-safe Markdown fences |
| Unknown block kind | Content maps to literal prose; provider kind is intentionally dropped |
| Empty text block | Omitted; if all elements are omitted, page Markdown still follows the configured policy |
| Structured table | Cell indexes, spans, roles, and defensively copied nested elements are retained |
| Nested table | Retained and projected through structured string chunking |
| Markdown-only table | Exact `MarkdownRepresentation` uses the existing Markdown table path |
| Top-level image with bytes and media type | `IngestionDocumentImage` and a zero-token `DataContent` chunk |
| Caption-only image | Text-described image; caption participates in string chunking |
| Image bytes without media type | Bytes remain mapped, but stock chunking cannot construct `DataContent` and emits no chunk without another description |
| Nested image | Bytes remain in the cell model and caption participates in table projection; recursive binary traversal is required to emit bytes |
| Image with neither bytes nor caption | Fails with page context |
| Chunk page provenance | Distinct sorted pages on `IngestionChunk.PageNumbers` |
| Default vector persistence | `IngestionChunkVectorRecord.PageNumbers` backed by nullable provider-portable `SerializedPageNumbers` |
| Geometry, dimensions, coordinates, confidence | Intentionally not mapped |
| Usage, raw provider objects, `AdditionalProperties` | Intentionally not mapped or persisted |

## Preview 2 compatibility

- Preview 2's original `IngestionChunk(AIContent, IngestionDocument, int, string?)` constructor
  remains available with the same CLR signature.
- `IngestionChunker`, `IngestionChunkProcessor`, `IngestionChunkWriter`, and `IngestionPipeline`
  remain non-generic.
- `VectorStoreWriter<TRecord>` still requires `TRecord : IngestionChunkVectorRecord, new()`.
- `Content` remains polymorphic `AIContent`; `TokenCount` still drives writer batching.
- Existing authored-Markdown constructors retain their behavior.
- Literal factories, typed code, structured cells, images, and page provenance are additive.
- The default vector record gains `SerializedPageNumbers`; existing collections may require
  provider-specific migration or recreation.

## Critical MEDI file accounting

The architecture-neutral base keeps the authoritative Preview 2 MEDI files identical to
`e124c123afeeda2f271f3b99a70eb3cfe187a471`. This bridge changes only these MEDI files:

| File | Bridge-specific reason |
|---|---|
| `DataIngestion.Abstractions/IngestionChunk.cs` | Typed page provenance and honest zero token count for non-text `AIContent` |
| `DataIngestion.Abstractions/IngestionDocumentElement.cs` | Literal text, typed code, structured cells, and captionless image construction |
| `DataIngestion.Abstractions/MarkdownProjection.cs` | Deterministic literal/code/table projections |
| `DataIngestion/Chunkers/DocumentTokenChunker.cs` | Exact contributing pages and non-text image chunks while preserving structure-flattening semantics |
| `DataIngestion/Chunkers/ElementsChunker.cs` | Page-aware text/code/table chunks and `DataContent` image chunks |
| `DataIngestion/Chunkers/HeaderChunker.cs` | Literal heading context and contributing heading pages |
| `DataIngestion/Chunkers/SectionChunker.cs` | Literal section context and contributing context pages |
| `DataIngestion/IngestionDocumentElementExtensions.cs` | Distinguish authored Markdown, literal text, code, and image semantic content |
| `DataIngestion/Writers/IngestionChunkVectorRecord.cs` | Typed page view plus provider-portable serialized storage |
| `DataIngestion/Writers/VectorStoreWriter.cs` | Copy typed chunk pages to the typed record before provider embedding/upsert |

The non-generic abstraction contracts and `VectorStoreExtensions` embedding schema remain unchanged.

## Evaluated code and package pin

- Preview 2 common base: `f6ba2df16275bfc5eaf50aeb9327e2ec34ee8129`
- Authoritative Preview 2 parent: `e124c123afeeda2f271f3b99a70eb3cfe187a471`
- Evaluated bridge code: `c1913907f05148370a84824b669d73249bb502e4`
- Package version: `10.8.0-preview2bridge.c191390`

A coherent six-package feed was built from that exact code head. Every package identifies
`https://github.com/luisquintanilla/extensions.git` and commit `c1913907f05148370a84824b669d73249bb502e4`.

| Package | SHA-256 |
|---|---|
| `Microsoft.Extensions.AI` | `a8192d63fa45ad84cfb018107c8431290e1aee6f7cd8454c1fac4302c3f085ad` |
| `Microsoft.Extensions.AI.Abstractions` | `13ec6febf70c77f7352e736b6e54e469706be435895271fb05fa0a91b6e3fecb` |
| `Microsoft.Extensions.DataIngestion` | `569c315c3f8fc5d80140db53fb5f13046d6535967d61f4061f6029cbc73caa81` |
| `Microsoft.Extensions.DataIngestion.Abstractions` | `06a201a6687b5abfb3e593f1557614e2071cba7cecdb2d3d5d2383459d61acff` |
| `Microsoft.Extensions.DataIngestion.DocumentExtraction` | `904f50db70912c45230e55c52446e3dc776d3a4eb79eaff11345c859d516f95a` |
| `Microsoft.Extensions.DocumentExtraction.Abstractions` | `79dc4282564a82a2a21c6347d9a964d2e05be49308d11644e27a47152ae58c2c` |

## Same-base bridge metrics

These counts describe the evaluated implementation at `c1913907f05148370a84824b669d73249bb502e4`;
they are not quality or performance scores.

| Measure | Delta from `f6ba2df16275bfc5eaf50aeb9327e2ec34ee8129` |
|---|---:|
| Commits | 4 |
| Changed files | 28 |
| Total | +2,229 / -42 |
| All `src/` | +1,348 / -40 |
| All `test/` | +881 / -2 |
| Production C# | +1,304 / -39 |
| Test C# | +858 / -2 |
| Top-level public source type declarations | +5 / -0 |

## Validation and limitations

- Filtered 100-project build: zero warnings and errors.
- Author-run test matrix: 886 passed and 44 expected MarkItDown skips across `net462`, `net8.0`,
  `net9.0`, and `net10.0`.
- `net8.0` and `net9.0` required clean per-TFM rebuilds after stale output folders omitted
  `xunit.abstractions.dll`; the rebuilt tests all passed.
- The base Document Extraction suites contribute 39 abstraction and 30 implementation tests per TFM.
- The bridge contributes 8 tests per TFM; MEDI contributes 147 tests on modern TFMs and 137 on
  `net462`.
- Real-provider projects are compile-only consumer gates. No live-provider, quality, performance, or
  merge-readiness claim is made here.
- `DocumentTokenChunker` remains intentionally structure-flattening for code.
- Serialized page storage does not provide numeric page-range filtering.
- A public mapper seam remains deferred until a second mapping policy demonstrates the need.
- Paired neutral comparison is pending its Preview 2 head and must not reuse historical generic-main
  metrics.
