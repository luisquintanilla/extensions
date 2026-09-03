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

## Dependency direction

```text
Microsoft.Extensions.DataIngestion.DocumentExtraction
  +---> Microsoft.Extensions.DataIngestion.Abstractions
  +---> Microsoft.Extensions.DocumentExtraction.Abstractions
```

Neither core model depends on the other. The adapter is an explicit transformation boundary, not a
shared-model solution.

## Mapping and loss policy

| Extraction fact | MEDI behavior |
|---|---|
| Page and one-based page number | One `IngestionDocumentSection`; page number is copied to the section and mapped elements |
| Canonical ordered `Elements` | Mapped in order; take precedence over duplicate page Markdown |
| Title block | Literal `IngestionDocumentHeader` |
| Code block | Typed `IngestionDocumentCodeBlock` |
| Paragraph or provider-specific block kind | Literal `IngestionDocumentParagraph`; an unknown kind value is intentionally dropped |
| Empty text block | Omitted because MEDI text elements require non-empty content; if every element is omitted, any page Markdown still goes through the configured Markdown-only policy |
| Structured table | Typed table cells retain indexes, spans, roles, and snapshot recursively mapped nested content |
| Nested table in a cell | Retained as a structured nested table and rendered as nested HTML by built-in string chunking |
| Captioned image in a multi-element cell | Bytes, media type, and caption remain in the nested cell model; the caption contributes to built-in string chunking |
| Nested image binary chunking | Stock document enumeration does not traverse table-cell content, so nested image bytes are not emitted by the built-in top-level binary traversal; a recursive binary chunker is required |
| Table with only `MarkdownRepresentation` | Preserved through the existing explicit Markdown table path |
| Image bytes and optional media type/caption | BCL bytes, media type, and alternative text are copied without creating placeholder Markdown |
| Caption-only image | Preserved as a text-described image |
| Image with neither bytes nor caption | Fails with page context rather than silently disappearing |
| Markdown-only page | `RequireElements` fails by default; `PreserveAsMarkdown` is an explicit opt-in |
| Page Markdown alongside canonical elements | Intentionally not mapped; canonical elements are authoritative |
| Page dimensions, coordinate unit/origin, bounding regions, and confidence | Intentionally not mapped |
| Raw provider objects and `AdditionalProperties` at every extraction level | Intentionally not mapped and never copied into MEDI metadata |
| Extraction request media type and provider options | Used for extraction but not persisted in the ingestion document |
| Chunk page provenance | Distinct sorted page numbers are typed on `IngestionChunk<T>` and persisted by the default writer as a provider-portable comma-separated `pagenumbers` field |

## Comparison evidence

This implementation is the explicit-bridge fallback evaluated against the same-base neutral-tree
PR. The measurements describe change shape and compatibility impact; they are not quality scores.

| Measure | Explicit bridge PR #1 | Neutral shared-tree PR #2 |
|---|---:|---:|
| Head measured | This branch's final reviewed head | `1d82e27402420dcc5f7a93f3dca47050aa6a62c0` |
| Production C# delta | +1,148 / -51 | +1,375 / -1,339 |
| Test C# delta | +1,007 / -1 | +696 / -2,068 |
| Top-level public source type declarations | +5 / -0 | +15 / -14 |
| Shared waist | None; explicit adapter between two models | 13 public types |
| Core dependency shape | No dependency between the two core abstractions | Both domains consume the shared waist |
| Existing authored Markdown constructors | Preserved | Replaced by the shared hierarchy |
| Generic chunk/writer direction | Preserved | Preserved through the shared hierarchy |

Line counts are measured against common base `1fec8651d88b19ae855c39239e75645c548e5dde`
and include C# files only. Public declaration counts include top-level source types, excluding nested
converter helpers. PR #2 values were refreshed from its current head, not copied from the earlier
standalone spike.

The corpus covers literal text versus Markdown, headings, collision-safe code fences, nested and
spanned tables, cell roles, captioned and captionless images, empty and partial element collections,
Markdown-only policy, multiple pages, chunk provenance, text writing, and a separate generic binary
pipeline. A nested table and captioned binary image in a multi-element cell are mapped end to end;
string chunking preserves the nested table and image caption. A limitation test records that nested
image bytes require a recursive binary chunker. Unsupported elements and contentless images fail
with source-page context.

## Compatibility and unresolved contracts

- Existing Markdown constructors and the original three-parameter `IngestionChunk<T>` constructor
  remain available. New literal and structured construction paths are additive.
- The default vector schema gains a nullable `pagenumbers` string column. Existing provider
  collections may require recreation or a provider-specific schema migration.
- `DocumentTokenChunker` intentionally remains structure-flattening for code, while element-aware
  chunkers preserve complete fences.
- Current generic pipelines choose one chunk content type. Text chunkers retain captionless images
  in the document but do not emit them; a binary `IngestionChunker<DataContent>` can feed the stock
  generic writer. A built-in mixed-modality contract remains unresolved and this comparison does not
  revive Preview 2's non-generic `AIContent` chunk model.
- Top-level enumeration intentionally does not flatten structured table cells. Consequently, nested
  image captions participate in the table's string projection, while nested image bytes require a
  recursive binary chunker.
- The portable page encoding preserves provenance but does not provide numeric range filtering.
- A public mapper abstraction is deferred until a second mapping policy demonstrates the need.
