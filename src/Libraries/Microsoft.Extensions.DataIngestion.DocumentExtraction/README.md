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
  -> Microsoft.Extensions.DataIngestion.Abstractions
  -> Microsoft.Extensions.DocumentExtraction.Abstractions
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
| Empty text block | Omitted because MEDI text elements require non-empty content |
| Structured table | Typed table cells retain indexes, spans, roles, and recursively mapped nested content |
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

This implementation is the minimal-change fallback evaluated against the separate neutral-tree
alternative, not an automatic architecture recommendation.

| Measure | Explicit bridge on current generic MEDI | Neutral semantic tree spike |
|---|---:|---:|
| Changed production C# lines | +1,082 / -41 | 2,416 spike lines |
| Changed test C# lines | +858 / -1 | Executable runner assertions |
| New public types | 5 | 32 public types, replacing 10 MEDI content types |
| Added public API entries | 32 | 32 public types |
| New core dependency edge | None | Shared model required coordinated producer and MEDI adoption |
| Consumer-owned mapper/chunker/writer stack | None | None |
| Existing authored Markdown constructors | Preserved | Replaced by the shared hierarchy |
| Generic chunk/writer direction | Preserved | Preview 2 `AIContent`-shaped evidence was not ported |

Line counts are measured against common base `1fec8651d88b19ae855c39239e75645c548e5dde`
and exclude project metadata, API baselines, and documentation.

The corpus covers literal text versus Markdown, headings, collision-safe code fences, nested and
spanned tables, cell roles, captioned and captionless images, empty and partial element collections,
Markdown-only policy, multiple pages, chunk provenance, text writing, and a separate generic binary
pipeline. Unsupported elements and contentless images fail with source-page context.

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
- The portable page encoding preserves provenance but does not provide numeric range filtering.
- A public mapper abstraction is deferred until a second mapping policy demonstrates the need.
