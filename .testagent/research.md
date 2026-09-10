# Test Generation Research

## Project Overview
- **Path**: `C:\Dev\copilot-worktrees\extensions\pr-2-luisquintanilla-neutral-document-tree`
- **Language**: C# / .NET
- **Framework**: .NET 10/9/8 plus .NET Framework 4.6.2 on Windows; libraries also target `netstandard2.0`
- **Test Framework**: xUnit 2.9.3 on VSTest (`Microsoft.NET.Test.Sdk` 18.0.1, `xunit.runner.visualstudio` 2.8.2). `global.json` has no native-MTP runner setting, and evaluated MTP bridge properties are empty.
- **Project system**: SDK-style (`Microsoft.NET.Sdk`); package/build policy comes from the Arcade SDK.
- **Dependency format and versions**: central `PackageReference` management through `Directory.Packages.props` and `eng/packages/*.props`. Relevant test packages include Moq 4.18.4, AwesomeAssertions 8.0.2, CommunityToolkit.VectorData.InMemory/SqliteVec 1.0.0-preview.3, and the centrally versioned Microsoft.ML.Tokenizers packages.
- **New-file registration**: implicit SDK `**/*.cs` glob. New tests under the existing test projects need no `<Compile Include>`. Preserve the existing explicit linked helper includes in `Microsoft.Extensions.DataIngestion.Tests.csproj`; SQLite tests are explicitly removed for non-.NETCoreApp targets.
- **Working-tree authority**: tracked status was clean after research. Research performed restore/build discovery only; no production or test source was edited.

## Scope and Strategy
- **Boundary**: the neutral `Document`/`DocumentNode` model and the MEDI document-tree, chunking, ingestion pipeline, enrichment, vector-store writing, persistence, and provider-embedding path. Do not inventory or alter unrelated libraries.
- **Primary source projects**:
  - `src/Libraries/Microsoft.Extensions.Documents.Abstractions/Microsoft.Extensions.Documents.Abstractions.csproj`
  - `src/Libraries/Microsoft.Extensions.DataIngestion.Abstractions/Microsoft.Extensions.DataIngestion.Abstractions.csproj`
  - `src/Libraries/Microsoft.Extensions.DataIngestion/Microsoft.Extensions.DataIngestion.csproj`
- **Primary test projects**:
  - `test/Libraries/Microsoft.Extensions.Documents.Abstractions.Tests/Microsoft.Extensions.Documents.Abstractions.Tests.csproj`
  - `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Microsoft.Extensions.DataIngestion.Tests.csproj`
- **Composition-only supporting test project**: `test/Libraries/Microsoft.Extensions.DataIngestion.DocumentExtraction.Tests/Microsoft.Extensions.DataIngestion.DocumentExtraction.Tests.csproj`; use only to preserve the deterministic extraction → neutral document → chunk → typed writer path.
- **Strategy**: preserve all current tests; port behavior, not source text, from the Preview 2 tests onto `TestDocuments` and immutable `DocumentNode` values. Never restore `IngestionDocumentElement`, `IngestionDocumentSection`, `IngestionDocumentHeader`, `IngestionDocumentParagraph`, `IngestionDocumentImage`, or `IngestionDocumentTable`.
- **Discovery**: the required Roslyn `find-untested-sources` analyzer was invoked exactly once from the repository root. It parsed 1,601 source and 862 test files in 3.9 seconds. Its JSON was prefixed by unexpected `dotnet run` output, so the bounded PowerShell JSON projection failed before retaining the per-file map; it was intentionally not rerun. The exact target pairings below were then confirmed by bounded identifier search and current test discovery. Treat all pairing classifications as static heuristics, not line/branch coverage.

## Mandatory Requirement Checklist
1. non-generic AIContent chunk boundaries
2. required positive token counts
3. non-generic pipeline/chunker/processor/writer contracts
4. typed VectorStoreWriter records
5. batching
6. incremental ingestion
7. cancellation
8. failure behavior
9. enrichment
10. persistence
11. provider-driven embeddings
12. chunkers: DocumentToken with overlap and no-overlap, Header, Section, SemanticSimilarity
13. pipeline: multiple content types, embedding provider, failure, cancellation/activity where applicable
14. image enrichment
15. preserve existing tests; exclude live external-provider/network conformance tests when not deterministic, documenting intentional dispositions

## Dependency Graph
- **Leaf/value types**: `DocumentNodeId`, `DocumentPageReference`; test directly without mocks.
- **Neutral node layer**: `DocumentText`, `DocumentImage`, `DocumentTableCell`, `DocumentTable`, `DocumentContainer`, then `DocumentTextProjection` and `Document`. These use only neutral in-scope values and BCL types; construct real trees rather than mocks.
- **Ingestion abstraction layer**: `IngestionDocument` wraps `Document`; `IngestionChunk` wraps non-generic `AIContent`, `IngestionDocument`, token count, and provenance. `IngestionDocumentReader`, `IngestionDocumentProcessor`, `IngestionChunker`, `IngestionChunkProcessor`, and `IngestionChunkWriter` are non-generic abstract seams.
- **Chunking layer**: `ElementsChunker` and `DocumentNodeExtensions` are internal leaves over the neutral tree/tokenizer. `DocumentTokenChunker`, `HeaderChunker`, `SectionChunker`, and `SemanticSimilarityChunker` depend on them; only semantic chunking needs a fake `IEmbeddingGenerator<TextContent, Embedding<float>>`.
- **Enrichment layer**: `Batching` supports the chat-based chunk enrichers (`ClassificationEnricher`, `KeywordEnricher`, `SentimentEnricher`, `SummaryEnricher`). `ImageAlternativeTextEnricher` is a document processor using `IChatClient` and an immutable tree rewrite. Use `TestChatClient`, not a network provider.
- **Persistence layer**: `IngestionChunkVectorRecord`, `VectorStoreExtensions`, `EmbeddingGeneratorExtensions`, and `VectorStoreWriter<TRecord>` depend on VectorData abstractions. Use local InMemory as the primary fake provider and SQLite only for its existing deterministic provider matrix.
- **Top layer**: `IngestionPipeline` composes reader → document processors → chunker → chunk processors → writer and Activity/logging. Fake each seam so each behavior and cancellation token is observable.

## Build & Test Commands
- **Build (scoped)**: `dotnet build test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Microsoft.Extensions.DataIngestion.Tests.csproj --framework net10.0 --nologo`
- **Test (scoped — fix cycles)**: `dotnet test test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Microsoft.Extensions.DataIngestion.Tests.csproj --framework net10.0 --no-restore --nologo`
- **Test (document model, scoped)**: `dotnet test test/Libraries/Microsoft.Extensions.Documents.Abstractions.Tests/Microsoft.Extensions.Documents.Abstractions.Tests.csproj --framework net10.0 --no-restore --nologo`
- **Test (harness-equivalent — repository/CI)**: `build.cmd -test -configuration Release`
- **CI evidence**: `eng/pipelines/templates/BuildAndTest.yml` runs `.dotnet/dotnet dotnet-coverage collect --settings eng/CodeCoverage.config --output <results>.xml "<buildScript> -test -configuration <config> ..."`. The root `eng/build.proj` traverses all `src/**/*.csproj`, `test/**/*.csproj`, and `bench/**/*.csproj`.
- **Harness discovery check (fast, verified)**: `dotnet test test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Microsoft.Extensions.DataIngestion.Tests.csproj --framework net10.0 --list-tests --nologo`
  - Verified successfully with SDK 10.0.112 and VSTest; 84 net10.0 test cases were listed.
  - The checked-in `global.json` pins SDK 10.0.108, while roll-forward selected installed 10.0.112.
- **All target frameworks**: evaluated test TFMs are `net10.0;net9.0;net8.0;net462`. Run the repository command for final validation; SQLite is intentionally omitted on `net462`.
- **Lint**: no separate lint command was found. Build enables code-style and analyzers (`EnforceCodeStyleInBuild=true`); final CI build uses warnings-as-errors.

## Bounded Target Inventory

### High Priority
| File | Symbols / behavior | Testability | Current classification | Planned test location |
|---|---|---:|---|---|
| `src/Libraries/Microsoft.Extensions.DataIngestion/Chunkers/DocumentTokenChunker.cs` | `DocumentTokenChunker.ProcessAsync`; exact BPE boundaries, overlap/no-overlap, provenance, cancellation | High | Partial | restore/adapt `Chunkers/DocumentTokenChunkerTests.cs`, `NoOverlapTokenChunkerTests.cs`, `OverlapTokenChunkerTests.cs`; preserve `SharedDocumentChunkerTests.cs` |
| `src/Libraries/Microsoft.Extensions.DataIngestion/Chunkers/HeaderChunker.cs` | heading stack/context, token limits, long text/table splitting, cancellation | High | Untested directly | restore/adapt `Chunkers/HeaderChunkerTests.cs` |
| `src/Libraries/Microsoft.Extensions.DataIngestion/Chunkers/SectionChunker.cs` | nested sections/context, limits, empty sections, cancellation | High | Partial | restore/adapt `Chunkers/SectionChunkerTests.cs`; preserve shared tests |
| `src/Libraries/Microsoft.Extensions.DataIngestion/Chunkers/SemanticSimilarityChunker.cs` | embedding input/order, percentile boundaries, grouping, count mismatch, cancellation | High | Untested directly | restore/adapt `Chunkers/SemanticSimilarityChunkerTests.cs` |
| `src/Libraries/Microsoft.Extensions.DataIngestion/Chunkers/ElementsChunker.cs` | internal text/table packing and positive exact token counts | High | Partial indirectly | exercise through Header/Section/Semantic tests |
| `src/Libraries/Microsoft.Extensions.DataIngestion/IngestionPipeline.cs` | all non-generic seams, multiple content types, document/chunk processors, provider writer, per-file failure, cancellation token flow, Activity status/tags, directory overload, disposal | High | Partial: one happy-path contract test | expand `IngestionPipelineTests.cs` with adapted deterministic Preview 2 cases |
| `src/Libraries/Microsoft.Extensions.DataIngestion/Processors/ImageAlternativeTextEnricher.cs` | image selection, batching, payloads, immutable nested rewrites, metadata, mismatch/failure, cancellation | High | Partial: one immutable rewrite test | expand `Processors/AlternativeTextEnricherTests.cs` |
| `src/Libraries/Microsoft.Extensions.DataIngestion/Writers/VectorStoreWriter.cs` | typed records, real batch boundaries, incremental replace-after-upsert, failures, cancellation, mixed-document guard | High | Substantial but missing observable upsert-count, cancellation, and failure ordering | expand `Writers/VectorStoreWriterTests.cs`; use an instrumented local fake where provider APIs permit |
| `src/Libraries/Microsoft.Extensions.DataIngestion/Writers/IngestionChunkVectorRecord.cs` | polymorphic `AIContent`, page persistence, `Embedding => Content` | High | Partial/substantial through writer tests | keep writer tests; add direct record edge cases only where not provider-dependent |
| `src/Libraries/Microsoft.Extensions.DataIngestion/Writers/EmbeddingGeneratorExtensions.cs` | string-to-`TextContent` adapter, options/token forwarding, service lookup, disposal | High | Untested | add `Writers/EmbeddingGeneratorExtensionsTests.cs` |
| `src/Libraries/Microsoft.Extensions.DataIngestion/Utils/Batching.cs` | chat-enricher batch size, result-count mismatch, failure best-effort semantics, cancellation | High | Partial indirectly | existing processor tests plus focused additions only where a mandatory path is absent |

### Medium Priority
| File | Symbols / behavior | Testability | Current classification | Notes |
|---|---|---:|---|---|
| `src/Libraries/Microsoft.Extensions.DataIngestion.Abstractions/IngestionChunk.cs` | non-generic `AIContent`, required positive `TokenCount`, normalized typed provenance | High | Substantial constructor coverage | preserve `IngestionChunkTests.cs`; add reflection/API-shape contract if needed |
| `src/Libraries/Microsoft.Extensions.DataIngestion.Abstractions/IngestionChunker.cs` | non-generic `ProcessAsync(IngestionDocument, CancellationToken)` | High | Structural only | cover with contract reflection tests and concrete chunkers |
| `src/Libraries/Microsoft.Extensions.DataIngestion.Abstractions/IngestionChunkProcessor.cs` | non-generic chunk stream | High | Indirect | contract reflection plus pipeline fake |
| `src/Libraries/Microsoft.Extensions.DataIngestion.Abstractions/IngestionChunkWriter.cs` | non-generic write stream and disposal | High | Indirect | contract reflection plus pipeline/vector writer |
| `src/Libraries/Microsoft.Extensions.DataIngestion.Abstractions/IngestionDocumentProcessor.cs` | neutral document processor seam | High | Indirect | pipeline and image enricher |
| `src/Libraries/Microsoft.Extensions.DataIngestion.Abstractions/IngestionDocumentReader.cs` | file/stream bridge, identifier/media type, cancellation forwarding | High | Partial | fake reader in pipeline; deterministic argument tests may be retained independently of providers |
| `src/Libraries/Microsoft.Extensions.DataIngestion.Abstractions/IngestionDocument.cs` | thin identity/metadata wrapper around shared `Document` | High | Partial | preserve `IngestionDocumentTests.cs` |
| `src/Libraries/Microsoft.Extensions.DataIngestion/Writers/VectorStoreExtensions.cs` | typed collection definition and provider-resolved `AIContent` embeddings | High | Substantial indirectly | existing InMemory/SQLite writer tests and extraction composition |
| `src/Libraries/Microsoft.Extensions.DataIngestion/Writers/VectorStoreWriterOptions.cs` | defaults/positive batch size/incremental flag | High | Substantial | preserve `VectorStoreWriterOptionsTests.cs` |
| `src/Libraries/Microsoft.Extensions.DataIngestion/Processors/{ClassificationEnricher,KeywordEnricher,SentimentEnricher,SummaryEnricher}.cs` | enrichment, batching, failures | High | Substantial current tests | preservation scope; do not rewrite working coverage |
| `src/Libraries/Microsoft.Extensions.Documents.Abstractions/{Document,DocumentNode,DocumentContainer,DocumentText,DocumentImage,DocumentTable,DocumentTextProjection}.cs` | closed immutable neutral tree, projection, serialization, validation | High | Partial via four broad tests | preserve `DocumentTests.cs`; add only directly required boundary fixtures/invariants |

### Low Priority / Skip
| File / area | Reason |
|---|---|
| deleted `src/.../IngestionDocumentElement.cs` and deleted old element extensions | Incompatible hierarchy; explicitly must not be restored |
| MarkItDown live/provider conformance | Requires installed external process and/or remote documents; not deterministic |
| OpenAI, Azure, Ollama, or other external embedding/chat providers | Live credentials/network are outside this behavior-test scope |
| generated API baseline JSON | Contract artifacts, not executable behavior targets |
| `ValueStringBuilder.cs`, diagnostics/log source-generated files | implementation detail unless a scoped behavior test exposes a defect |

## Source-to-Test Pairs and Coverage Classification
- `Documents.Abstractions/Document*.cs` → `test/Libraries/Microsoft.Extensions.Documents.Abstractions.Tests/DocumentTests.cs`: **partial**, four broad tests cover traversal/projection, polymorphic round-trip, closed neutrality, and one table validation case.
- `DataIngestion.Abstractions/IngestionChunk.cs` → `IngestionChunkTests.cs`: **substantial** constructor/API invariant coverage, including zero and negative tokens.
- `DataIngestion.Abstractions/IngestionDocument.cs` → `IngestionDocumentTests.cs`: **partial**, wrapper identity/document and metadata shape.
- abstract reader/chunker/processor/writer contracts → concrete processor/writer tests and `IngestionPipelineTests.cs`: **partial**, no current explicit non-generic signature guard.
- `DocumentTokenChunker.cs`/`ElementsChunker.cs` → `SharedDocumentChunkerTests.cs`: **partial**, strong provenance/separator/token regression coverage but missing broad no-overlap and overlap cases.
- `HeaderChunker.cs` → no current direct reference: **untested directly**.
- `SectionChunker.cs`/`ElementsChunker.cs` → `SharedDocumentChunkerTests.cs`: **partial**, only one section-specific behavior plus shared count checks.
- `SemanticSimilarityChunker.cs` → no current direct reference: **untested directly**.
- `IngestionPipeline.cs` → `IngestionPipelineTests.cs`: **partial**, only non-generic happy path.
- `ImageAlternativeTextEnricher.cs` → `Processors/AlternativeTextEnricherTests.cs`: **partial**, immutable identity rewrite only.
- chunk enrichers/`Batching.cs` → `ClassificationEnricherTests.cs`, `KeywordEnricherTests.cs`, `SentimentEnricherTests.cs`, `SummaryEnricherTests.cs`: **substantial for normal/failure enrichment**, batching depth varies; cancellation is not explicit.
- `VectorStoreWriter.cs`, `VectorStoreExtensions.cs`, `IngestionChunkVectorRecord.cs` → abstract `Writers/VectorStoreWriterTests.cs`, executed by `InMemoryVectorStoreWriterTests.cs` and (on .NETCoreApp) `SqliteVectorStoreWriterTests.cs`: **substantial** for typed records, metadata, incremental replacement, persistence, provider embeddings, and multiple-document failure; actual batch-call count and cancellation/failure ordering remain gaps.
- `EmbeddingGeneratorExtensions.cs` → no current test: **untested**.
- deterministic end-to-end composition → `DataIngestion.DocumentExtraction.Tests/DocumentExtractionReaderTests.cs`: **substantial for its narrow composition path**, including typed records, pages, and provider-driven embedding.

## Existing Test Projects
- **Project file**: `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Microsoft.Extensions.DataIngestion.Tests.csproj`
  - **Target source projects**: DataIngestion, DataIngestion.Markdig, DataIngestion.MarkItDown, Documents.Abstractions, diagnostics test support.
  - **Relevant test files**: `IngestionChunkTests.cs`, `IngestionDocumentTests.cs`, `IngestionPipelineTests.cs`, `Chunkers/*.cs`, `Processors/*.cs`, `Writers/*.cs`, `Readers/MarkdownReaderTests.cs`, `Readers/MarkItDownMcpReaderTests.cs`, and `Utils/TestDocuments.cs`.
- **Project file**: `test/Libraries/Microsoft.Extensions.Documents.Abstractions.Tests/Microsoft.Extensions.Documents.Abstractions.Tests.csproj`
  - **Target source project**: Documents.Abstractions (`ProjectUnderTest=true`).
  - **Test files**: `DocumentTests.cs`.
- **Project file**: `test/Libraries/Microsoft.Extensions.DataIngestion.DocumentExtraction.Tests/Microsoft.Extensions.DataIngestion.DocumentExtraction.Tests.csproj`
  - **Target source project**: DataIngestion.DocumentExtraction (`ProjectUnderTest=true`), with DataIngestion and Documents.Abstractions composition references.
  - **Test files**: `DocumentExtractionReaderTests.cs`; links the existing deterministic `TestEmbeddingGenerator.cs`.

## Existing Test Conventions
- xUnit `[Fact]`, `[Theory]`, `MemberData`/`InlineData`; classes generally end in `Tests`.
- Async streams are materialized with `ToListAsync()` or passed via `ToAsyncEnumerable()`.
- Use real pinned Tiktoken tokenizers and assert exact `CountTokens(..., considerNormalization: false)`, not guessed word counts.
- Use `TestDocuments.Create/Text/Section` to author the neutral tree. Extend that fixture with table/image helpers if useful; do not recreate the old mutable hierarchy.
- Use xUnit `Assert.IsType<TextContent/DataContent>`, sequence equality, and explicit payload/property assertions.
- `TestChatClient` and deterministic embedding delegates replace external providers. InMemory and local temporary SQLite are accepted deterministic stores.
- Temporary files use `Path.GetTempFileName()`/`try-finally`; Activities use an in-memory OpenTelemetry exporter.
- Abstract shared writer tests run against provider-specific subclasses. Preserve this pattern and account for each inherited test executing once per provider.
- Representative current tests: `Chunkers/SharedDocumentChunkerTests.cs` and `Writers/VectorStoreWriterTests.cs`.

## History Findings
- Common base: `f6ba2df16275bfc5eaf50aeb9327e2ec34ee8129`; authoritative Preview 2 parent documented as `e124c123afeeda2f271f3b99a70eb3cfe187a471`.
- Neutral-tree commits: `dbb482bb97` introduced the immutable shared tree; `e87c2af3d4` ported MEDI and deleted the old hierarchy/tests; `94ba6ea998` fixed exact token counts; `704a3e44ef` prevented overlap-only terminal chunks.
- The exact **75 → 58** delta is explained by the chunker replacement: 22 deterministic declarations in the six deleted concrete chunker files were replaced by 5 in `SharedDocumentChunkerTests.cs`, a net loss of 17. Preserve the five new neutral regressions and adapt the 22 old behavior specifications where still semantically applicable.
- Deleted concrete chunker behavior inventory:
  - DocumentToken/no-overlap/overlap: `SingleChunkText`, `TwoChunks`, `ManyChunks`, `VerifyTokenCount`, `TokenChunking_WithOverlap`.
  - Header: non-trivial heading contexts, token limit, context-too-large failure, newline split, oversized table-row failure, multi-row/one-row table splits, exact token count.
  - Section: one/two/empty/nested sections, size limit, section heading context.
  - SemanticSimilarity: single paragraph, topic split, mixed element/table topic grouping.
- Additional deterministic losses outside that 17-test arithmetic:
  - Pipeline: six tests became one. Adapt the previous file/list and directory orchestration, mixed `TextContent`/`DataContent`, provider embedding, per-file failure isolation, and Activity assertions.
  - Image enrichment: five declarations became one. Adapt null guards, payload/selection, batch count, and logged best-effort failure to immutable neutral-tree output.
- Current writer tests improved over the base (seven declarations became nine) and already preserve typed custom records, polymorphic content, page persistence, provider embeddings, incremental ingestion, and multi-document failure. Extend rather than replace.
- Current Preview 2 comparison documentation explicitly requires non-generic contracts, `AIContent`, positive `TokenCount`, typed records, provider embeddings, batch limits, incremental ingestion, and typed provenance.

## Requirement-to-Test Map
| Checklist item | Implementation target |
|---|---|
| 1, 2 | preserve/extend `IngestionChunkTests`; assert every emitted chunk uses non-generic `AIContent` and exact positive tokens in every concrete chunker suite |
| 3 | add reflection/API-shape assertions for `IngestionChunker`, `IngestionChunkProcessor`, `IngestionChunkWriter`, and `IngestionPipeline`; exercise fakes through pipeline |
| 4 | preserve custom `TestChunkRecordWithMetadata`/`TestVectorStoreWriterWithMetadata` and generic constraints |
| 5 | strengthen writer batching to observe upsert boundaries; restore image-enricher batch count; preserve chunk-enricher batching |
| 6 | preserve both incremental tests; add failure-order assertion only if an instrumented store can prove old data is retained until successful replacement |
| 7 | add pre-canceled and mid-stream token-flow tests for chunkers, pipeline, writer, and image/chat boundaries as applicable; assert current Preview 2 semantics rather than assuming propagation |
| 8 | restore pipeline per-file isolation and image-enricher best-effort logging; add semantic embedding-count mismatch and writer failure/cancellation cases |
| 9 | preserve four chunk-enricher suites and verify pipeline processor ordering/rewrites |
| 10 | preserve polymorphic content/page/context/metadata persistence across InMemory and SQLite where supported |
| 11 | preserve writer embedding assertions and restore pipeline embedding-provider assertion; directly test the embedding adapter |
| 12 | separate adapted suites for DocumentToken overlap/no-overlap, Header, Section, and SemanticSimilarity; keep all five shared neutral regressions |
| 13 | adapt pipeline mixed content, provider, failure, directory/files, cancellation token, disposal, and Activity status/tag tests |
| 14 | adapt image payload, nested image/table-cell rewrite, skip rules, batching, mismatch/failure, and metadata preservation tests |
| 15 | do not delete/replace existing tests; intentional external exclusions are listed below |

## Deterministic Boundaries and Intentional Dispositions
- **Keep/run**: Tiktoken data packages, `TestChatClient`, delegate embedding generators, InMemory VectorData, temporary local SQLite on .NETCoreApp, temporary files/directories, in-memory OpenTelemetry.
- **Do not restore** historical `DocumentReaderConformanceTests.SupportsStreams`, `SupportsFiles`, or `SupportsImages`: they download remote PDF/DOCX files and are network/content dependent.
- **Do not restore** historical `MarkItDownReaderTests` provider conformance: it depends on an externally installed MarkItDown process and inherited network fixtures. Deterministic reader argument/cancellation behavior may be tested with local fakes or local authored inputs instead.
- **Keep** current `MarkItDownMcpReaderTests` argument/file validation because they do not connect to the placeholder localhost URI. Do not add actual MCP-server tests here.
- **Do not add** live OpenAI/Azure/Ollama/vector-database conformance. Provider-driven embedding means proving a configured local VectorData provider invokes the fake `IEmbeddingGenerator<AIContent,...>`, not calling a hosted model.
- **Preserve** current Markdown authored-tree tests, but broad Markdown parser restoration is secondary unless directly needed by the pipeline fixtures.

## Risks and Blockers
- The Roslyn pairing analyzer completed, but its per-file JSON could not be retained after `dotnet run` prefixed non-JSON output. It was not rerun because the request required exactly one invocation. Pairing evidence therefore combines analyzer counts with bounded symbol references.
- The current test-discovery command builds with existing analyzer/XML-doc warnings. Local discovery succeeded because warnings-as-errors was off; CI final build may expose pre-existing warnings.
- Exact BPE boundaries must use the same pinned tokenizer/model and `considerNormalization: false`; avoid word-splitting approximations from old tests.
- Neutral tables project tab-separated text, not old Markdown table source. Port table split expectations to `DocumentTable`/`DocumentTableCell` and `DocumentTextProjection` semantics.
- Neutral documents are immutable. Image-enrichment tests must assert a rewritten document and unchanged original, including nested containers/table cells and copied ingestion metadata.
- `IngestionPipeline` currently catches all `Exception`, including cancellation exceptions, and emits failed `IngestionResult` values. Before changing production, tests must characterize Preview 2 cancellation semantics and token forwarding; do not “fix” this by assumption.
- `Batching` and `ImageAlternativeTextEnricher` also catch broad exceptions. Cancellation expectations must distinguish best-effort enrichment from pipeline/writer cancellation.
- Existing writer `BatchesChunks` only verifies final record count. A recording VectorStoreCollection/provider may be required to prove batch-call boundaries without network I/O.
- Do not make production changes unless an adapted deterministic behavior test proves a directly coupled incompatibility with the neutral model or Preview 2 contract.

## Recommendations
1. Recreate the four concrete chunker test families first, using neutral fixtures and retaining the five shared regression tests; this directly repairs the 17-test behavior loss.
2. Restore deterministic pipeline breadth next with fake seams and local InMemory persistence: file/directory overloads, mixed content, provider embedding, failure isolation, cancellation characterization, Activities, and disposal.
3. Restore image-enrichment selection/payload/batching/failure tests against immutable tree rewrites.
4. Add explicit non-generic contract guards and direct embedding-adapter tests.
5. Strengthen writer batching/cancellation/failure-order tests while preserving all existing typed-record, persistence, incremental, and provider matrix tests.
6. Run net10 scoped loops, then both target test projects, then `build.cmd -test -configuration Release` for full harness-equivalent validation.
