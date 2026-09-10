# Test Implementation Plan

## Overview

This is a broad, dependency-ordered plan for the exact neutral-document/DataIngestion inventory in `research.md`. Existing tests remain in place. Missing Preview 2 behavior will be adapted to immutable `Document`/`DocumentNode` values authored with `TestDocuments`; the deleted `IngestionDocumentElement` hierarchy and its derived types will not be restored.

The implementation proceeds from neutral values and non-generic contracts, through concrete chunkers and enrichers, to the pipeline and persistence layers. Tests will use pinned Tiktoken tokenizers, `TestChatClient`, delegate/fake embedding generators, InMemory VectorData, temporary local SQLite where already supported, temporary files, and in-memory OpenTelemetry. No phase adds live provider/network tests, changes coverage tooling, creates a new test project, or makes unrelated production edits.

## Commands

- **Build (scoped)**: `dotnet build test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Microsoft.Extensions.DataIngestion.Tests.csproj --framework net10.0 --nologo`
- **Test (DataIngestion)**: `dotnet test test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Microsoft.Extensions.DataIngestion.Tests.csproj --framework net10.0 --no-restore --nologo`
- **Test (document model)**: `dotnet test test/Libraries/Microsoft.Extensions.Documents.Abstractions.Tests/Microsoft.Extensions.Documents.Abstractions.Tests.csproj --framework net10.0 --no-restore --nologo`
- **Test (composition)**: `dotnet test test/Libraries/Microsoft.Extensions.DataIngestion.DocumentExtraction.Tests/Microsoft.Extensions.DataIngestion.DocumentExtraction.Tests.csproj --framework net10.0 --no-restore --nologo`
- **Test (repository/CI equivalent)**: `build.cmd -test -configuration Release`
- **Lint**: No separate command; build analyzers/code style are authoritative and the final repository build uses warnings as errors.

## Strategy and Guardrails

- Use the broad strategy: cover every bounded target, emphasizing untested and partial files while preserving substantial suites.
- Add tests only to the existing project that already covers each source file. The DocumentExtraction project remains composition-only.
- Assert behavior and secondary observables: exact content/boundaries, exact positive token counts, provenance, request payloads, fake call order/count, cancellation-token identity, persisted records, activity status/tags, logs, original-tree immutability, and disposal.
- Port semantic expectations from Preview 2, not its source text. Neutral table expectations use tab-separated `DocumentTextProjection`, not old Markdown table text.
- Characterize current broad exception handling for cancellation in `IngestionPipeline`, `Batching`, and `ImageAlternativeTextEnricher`; do not assume that `OperationCanceledException` escapes.
- Production changes are out of scope unless a deterministic adapted test proves a directly coupled neutral-model incompatibility. Any such change must be minimal and recorded in `.testagent/status.md`.

## Phase Summary

| Phase | Focus | Bounded production files | Est. new/adapted tests |
|---|---|---:|---:|
| 1 | Neutral values and non-generic abstraction contracts | 16 | 8-12 |
| 2 | Concrete chunking behavior | 6 | 22-28 |
| 3 | Enrichment and immutable image rewriting | 6 | 10-15 |
| 4 | Pipeline orchestration, isolation, telemetry, and lifetime | 1 | 10-14 |
| 5 | Typed vector persistence and embedding adapter | 4 | 8-12 |
| 6 | Deterministic composition and full quality gate | composition only | 0-2 |

---

## Phase 1: Neutral Model and Non-Generic Contracts

### Overview

Establish the leaf/value fixtures and API-shape invariants needed by all later phases. Existing neutral-document and ingestion-wrapper tests are preserved; additions are limited to mandatory boundaries and reusable immutable fixtures.

### Files to Test

#### 1. Neutral document value and node types
- **Sources**:
  - `src/Libraries/Microsoft.Extensions.Documents.Abstractions/DocumentNodeId.cs`
  - `src/Libraries/Microsoft.Extensions.Documents.Abstractions/DocumentPageReference.cs`
  - `src/Libraries/Microsoft.Extensions.Documents.Abstractions/DocumentNode.cs`
  - `src/Libraries/Microsoft.Extensions.Documents.Abstractions/DocumentText.cs`
  - `src/Libraries/Microsoft.Extensions.Documents.Abstractions/DocumentImage.cs`
  - `src/Libraries/Microsoft.Extensions.Documents.Abstractions/DocumentTableCell.cs`
  - `src/Libraries/Microsoft.Extensions.Documents.Abstractions/DocumentTable.cs`
  - `src/Libraries/Microsoft.Extensions.Documents.Abstractions/DocumentContainer.cs`
  - `src/Libraries/Microsoft.Extensions.Documents.Abstractions/DocumentTextProjection.cs`
  - `src/Libraries/Microsoft.Extensions.Documents.Abstractions/Document.cs`
- **Test File**: `test/Libraries/Microsoft.Extensions.Documents.Abstractions.Tests/DocumentTests.cs`
- **Test Class**: `DocumentTests`
- **Fixture File**: `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Utils/TestDocuments.cs` (extend only with neutral image/table/nested-container helpers needed later)

**Planned tests**:
1. `NestedNeutralTree_ProjectsTextAndTraversesInStableOrder`
   - Build real text, image, table-cell/table, and nested-container nodes.
   - Assert deterministic traversal and tab-separated table projection.
2. `DocumentNodeIdAndPageReference_RoundTripWithNodeProvenance`
   - Assert IDs and page references survive immutable construction and serialization.
3. `PolymorphicRoundTrip_PreservesEveryClosedNeutralNodeKind`
   - Retain/strengthen the existing round-trip test rather than duplicating it.
4. `PublicDocumentModel_RemainsClosedAndNeutral`
   - Preserve the current neutrality test; reflection may assert the old element hierarchy is absent without introducing compile-time references to deleted types.
5. Preserve current validation tests, including invalid table structure, and add no parser/provider behavior.

**Fixtures/assertions**:
- Real immutable node trees; no mocks.
- Explicit runtime node types, sequence order, projected strings, IDs/pages, and unchanged source collections.

#### 2. Ingestion wrappers and non-generic abstract seams
- **Sources**:
  - `src/Libraries/Microsoft.Extensions.DataIngestion.Abstractions/IngestionDocument.cs`
  - `src/Libraries/Microsoft.Extensions.DataIngestion.Abstractions/IngestionChunk.cs`
  - `src/Libraries/Microsoft.Extensions.DataIngestion.Abstractions/IngestionDocumentReader.cs`
  - `src/Libraries/Microsoft.Extensions.DataIngestion.Abstractions/IngestionDocumentProcessor.cs`
  - `src/Libraries/Microsoft.Extensions.DataIngestion.Abstractions/IngestionChunker.cs`
  - `src/Libraries/Microsoft.Extensions.DataIngestion.Abstractions/IngestionChunkProcessor.cs`
  - `src/Libraries/Microsoft.Extensions.DataIngestion.Abstractions/IngestionChunkWriter.cs`
- **Test Files**:
  - `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/IngestionDocumentTests.cs`
  - `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/IngestionChunkTests.cs`
  - `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/IngestionAbstractionsContractTests.cs` (new)
- **Test Classes**: `IngestionDocumentTests`, `IngestionChunkTests`, `IngestionAbstractionsContractTests`

**Planned tests**:
1. `Constructor_AcceptsNonGenericAIContent` (theory for `TextContent` and `DataContent`)
   - Assert `IngestionChunk.Content` is the same non-generic `AIContent` instance and typed provenance is retained.
2. `Constructor_RequiresPositiveTokenCount` (theory for zero and negative)
   - Preserve current coverage and explicitly assert the parameter/exception contract.
3. `Constructor_PreservesDocumentNodeAndPageProvenance`
   - Assert document identity, node identity, and page reference separately.
4. `AbstractSeams_ExposeNonGenericContracts`
   - Reflection/API-shape guard for non-generic `IngestionDocumentProcessor`, `IngestionChunker`, `IngestionChunkProcessor`, and `IngestionChunkWriter`.
   - Assert relevant `ProcessAsync`/write stream element types use `IngestionDocument`/`IngestionChunk`, never generic content type parameters.
5. `ReaderBridge_ForwardsIdentifierMediaTypeAndCancellationToken`
   - A recording reader exercises the file/stream bridge with a temporary local file.
   - Assert identifier/media type and exact token forwarding; no external reader is used.
6. `WriterContract_IsAsynchronouslyDisposable`
   - Assert and exercise disposal through a minimal recording writer.

### Risks

- Reflection checks must validate intentional public shape without pinning compiler-generated details.
- Fixture helpers must return neutral immutable nodes and must not recreate mutable old element APIs.
- Keep additions proportional because `IngestionChunk` already has substantial constructor coverage.

### Phase Validation

- **Build**: `dotnet build test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Microsoft.Extensions.DataIngestion.Tests.csproj --framework net10.0 --nologo`
- **Narrow tests**:
  1. `dotnet test test/Libraries/Microsoft.Extensions.Documents.Abstractions.Tests/Microsoft.Extensions.Documents.Abstractions.Tests.csproj --framework net10.0 --no-restore --nologo --filter "FullyQualifiedName~DocumentTests"`
  2. `dotnet test test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Microsoft.Extensions.DataIngestion.Tests.csproj --framework net10.0 --no-restore --nologo --filter "FullyQualifiedName~IngestionDocumentTests|FullyQualifiedName~IngestionChunkTests|FullyQualifiedName~IngestionAbstractionsContractTests"`

### Success Criteria

- [ ] Neutral fixtures cover text, images, tables, nested containers, IDs, and pages.
- [ ] `TextContent` and `DataContent` satisfy the same non-generic chunk contract.
- [ ] Zero/negative token counts remain rejected.
- [ ] All abstract seam signatures and reader/disposal behavior are covered without old hierarchy types.

---

## Phase 2: Concrete Chunkers and Exact Boundaries

### Overview

Restore the lost deterministic chunker breadth first at the behavior layer. Keep every existing `SharedDocumentChunkerTests` regression and adapt applicable Preview 2 scenarios to neutral trees.

### Files to Test

#### 1. Document token chunking
- **Sources**:
  - `src/Libraries/Microsoft.Extensions.DataIngestion/Chunkers/DocumentTokenChunker.cs`
  - `src/Libraries/Microsoft.Extensions.DataIngestion/Chunkers/DocumentNodeExtensions.cs`
- **Test Files**:
  - `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Chunkers/DocumentTokenChunkerTests.cs`
  - `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Chunkers/NoOverlapTokenChunkerTests.cs`
  - `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Chunkers/OverlapTokenChunkerTests.cs`
  - preserve `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Chunkers/SharedDocumentChunkerTests.cs`
- **Test Classes**: `DocumentTokenChunkerTests`, `NoOverlapTokenChunkerTests`, `OverlapTokenChunkerTests`

**Methods**: `DocumentTokenChunker.ProcessAsync`

**Planned tests**:
1. `ProcessAsync_NoOverlap_SingleChunkPreservesExactBoundary`
2. `ProcessAsync_NoOverlap_TwoChunksHaveNoRepeatedTokens`
3. `ProcessAsync_NoOverlap_ManyChunksPreserveAllProjectedText`
4. `ProcessAsync_Overlap_RepeatsConfiguredBoundaryTokens`
5. `ProcessAsync_Overlap_DoesNotEmitOverlapOnlyTerminalChunk`
6. `ProcessAsync_EmitsNonGenericAIContentWithExactPositiveTokenCount`
   - For every chunk, assert `AIContent`/`TextContent`, `TokenCount > 0`, and equality with pinned tokenizer `CountTokens(..., considerNormalization: false)`.
7. `ProcessAsync_PreservesDocumentNodeAndPageProvenanceAcrossSplits`
8. `ProcessAsync_PreCanceledToken_ObservesCancellationAtChunkBoundary`

**Fixtures/assertions**:
- Pinned Tiktoken tokenizer and neutral `TestDocuments`.
- Re-tokenize each emitted payload; assert exact chunk sequence, overlap subsequences, no dropped/duplicate non-overlap text, and provenance.

#### 2. Shared element packing and header chunking
- **Sources**:
  - `src/Libraries/Microsoft.Extensions.DataIngestion/Chunkers/ElementsChunker.cs`
  - `src/Libraries/Microsoft.Extensions.DataIngestion/Chunkers/HeaderChunker.cs`
- **Test File**: `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Chunkers/HeaderChunkerTests.cs`
- **Test Class**: `HeaderChunkerTests`

**Methods**: `HeaderChunker.ProcessAsync`; `ElementsChunker` is covered through observable header/section/semantic output.

**Planned tests**:
1. `ProcessAsync_NestedHeadingsCarryExpectedHeadingStack`
2. `ProcessAsync_ResetsSiblingHeadingContext`
3. `ProcessAsync_SplitsLongTextOnNewlineWithinTokenLimit`
4. `ProcessAsync_ContextLargerThanLimit_FailsDeterministically`
5. `ProcessAsync_MultiRowTableSplitsAtRowBoundaries`
6. `ProcessAsync_OneRowTableUsesNeutralTabSeparatedProjection`
7. `ProcessAsync_OversizedTableRow_FailsDeterministically`
8. `ProcessAsync_EveryChunkHasExactPositiveTokenCount`
9. `ProcessAsync_PreCanceledToken_StopsPacking`

**Secondary observables**:
- Heading context order, separator placement, node/page provenance, tokenizer counts, and no empty chunks.

#### 3. Section chunking
- **Source**: `src/Libraries/Microsoft.Extensions.DataIngestion/Chunkers/SectionChunker.cs`
- **Test File**: `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Chunkers/SectionChunkerTests.cs`
- **Test Class**: `SectionChunkerTests`

**Methods**: `SectionChunker.ProcessAsync`

**Planned tests**:
1. `ProcessAsync_OneSectionProducesOneContextualChunk`
2. `ProcessAsync_TwoSectionsPreserveOrderAndIsolation`
3. `ProcessAsync_EmptySectionDoesNotProduceEmptyChunk`
4. `ProcessAsync_NestedSectionsIncludeAncestorHeadingContext`
5. `ProcessAsync_SectionOverLimitSplitsWithinConfiguredSize`
6. `ProcessAsync_EveryChunkHasExactPositiveTokenCount`
7. `ProcessAsync_CancellationBetweenSectionsStopsEnumeration`

#### 4. Semantic similarity chunking
- **Source**: `src/Libraries/Microsoft.Extensions.DataIngestion/Chunkers/SemanticSimilarityChunker.cs`
- **Test File**: `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Chunkers/SemanticSimilarityChunkerTests.cs`
- **Test Class**: `SemanticSimilarityChunkerTests`

**Methods**: `SemanticSimilarityChunker.ProcessAsync`

**Planned tests**:
1. `ProcessAsync_SingleParagraphProducesSingleChunk`
2. `ProcessAsync_TopicChangeSplitsAtExpectedPercentileBoundary`
3. `ProcessAsync_MixedTextAndTableGroupsBySemanticTopic`
4. `ProcessAsync_EmbeddingInputsPreserveProjectionOrder`
5. `ProcessAsync_EmbeddingCountMismatchThrowsDeterministically`
6. `ProcessAsync_ForwardsCancellationToEmbeddingGenerator`
7. `ProcessAsync_EveryChunkHasNonGenericAIContentAndExactPositiveTokenCount`

**Fixtures/assertions**:
- Deterministic fake `IEmbeddingGenerator<TextContent, Embedding<float>>` returning hand-authored vectors.
- Record input text, options, call order/count, and token identity.
- Assert percentile boundary/group membership, content order, no empty chunks, exact re-tokenized counts, and mismatch exception details.

### Risks

- Exact BPE assertions must use the repository-pinned tokenizer and `considerNormalization: false`.
- Ported table expectations must reflect neutral tab-separated projection.
- Overlap tests must distinguish intended overlap from the fixed overlap-only terminal regression.
- Semantic vectors should create unambiguous distances around the configured percentile.

### Phase Validation

- **Build**: `dotnet build test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Microsoft.Extensions.DataIngestion.Tests.csproj --framework net10.0 --nologo`
- **Narrow test**: `dotnet test test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Microsoft.Extensions.DataIngestion.Tests.csproj --framework net10.0 --no-restore --nologo --filter "FullyQualifiedName~Chunkers"`

### Success Criteria

- [ ] Existing five shared neutral regressions still pass.
- [ ] Separate overlap, no-overlap, Header, Section, and SemanticSimilarity suites exist.
- [ ] Every emitted chunk assertion includes non-generic content plus exact positive token count.
- [ ] Mismatch, limit failures, and cancellation are deterministic.

---

## Phase 3: Enrichment, Batching, and Immutable Image Rewrites

### Overview

Preserve all existing chunk-enrichment suites and restore image-enrichment breadth against immutable neutral trees. Exercise `Batching` through public enrichers rather than binding to implementation details.

### Files to Test

#### 1. Existing chunk enrichers and batching utility
- **Sources**:
  - `src/Libraries/Microsoft.Extensions.DataIngestion/Utils/Batching.cs`
  - `src/Libraries/Microsoft.Extensions.DataIngestion/Processors/ClassificationEnricher.cs`
  - `src/Libraries/Microsoft.Extensions.DataIngestion/Processors/KeywordEnricher.cs`
  - `src/Libraries/Microsoft.Extensions.DataIngestion/Processors/SentimentEnricher.cs`
  - `src/Libraries/Microsoft.Extensions.DataIngestion/Processors/SummaryEnricher.cs`
- **Existing Test Files**:
  - `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Processors/ClassificationEnricherTests.cs`
  - `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Processors/KeywordEnricherTests.cs`
  - `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Processors/SentimentEnricherTests.cs`
  - `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Processors/SummaryEnricherTests.cs`

**Methods**: each enricher's chunk-processing entry point; `Batching` via observable chat calls.

**Planned focused additions only if not already represented**:
1. `ProcessAsync_BatchesRequestsAtConfiguredSize`
   - Place in `ClassificationEnricherTests`; record `TestChatClient` calls and assert batch sizes/call count.
2. `ProcessAsync_ResultCountMismatch_UsesDocumentedBestEffortBehavior`
   - Assert output/order and log evidence, not merely absence of an exception.
3. `ProcessAsync_CancellationAtChatBoundary_CharacterizesBestEffortBehavior`
   - Assert exact token forwarding and whether the current catch policy returns unchanged chunks or propagates.

All existing happy-path and failure tests for classification, keywords, sentiment, and summary remain unchanged unless neutral-fixture adaptation is required.

#### 2. Image alternative-text enrichment
- **Source**: `src/Libraries/Microsoft.Extensions.DataIngestion/Processors/ImageAlternativeTextEnricher.cs`
- **Test File**: `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Processors/AlternativeTextEnricherTests.cs`
- **Test Class**: `AlternativeTextEnricherTests`

**Methods**: constructor/options validation and document-processing entry point.

**Planned tests**:
1. `Constructor_NullDependenciesThrow` (parameterized as appropriate)
2. `ProcessAsync_SelectsOnlyImagesWithoutAlternativeText`
3. `ProcessAsync_BuildsExpectedImagePayloadAndPrompt`
   - Assert `DataContent` media type/bytes or URI selection and request ordering.
4. `ProcessAsync_BatchesSelectedImagesAtConfiguredSize`
   - Assert exact chat call count and per-call payload count.
5. `ProcessAsync_RewritesNestedContainerAndTableCellImages`
   - Assert replacements occur at each nesting level while unrelated node instances/order are preserved.
6. `ProcessAsync_LeavesOriginalDocumentUnchanged`
   - Assert original image instances/alternative text remain unchanged and output document is the rewritten instance.
7. `ProcessAsync_SkipsImagesWithAlternativeTextOrUnsupportedPayload`
8. `ProcessAsync_PreservesDocumentIdentityMetadataNodeIdsAndPages`
9. `ProcessAsync_ResultCountMismatch_AppliesDocumentedBestEffortBehavior`
10. `ProcessAsync_ChatFailure_LogsAndReturnsBestEffortDocument`
11. `ProcessAsync_Cancellation_ForwardsTokenAndCharacterizesCatchBehavior`

**Fixtures/assertions**:
- `TestChatClient` with recorded request batches and deterministic replies.
- Neutral image helpers for root, nested container, and table-cell images.
- In-memory logger capture.
- Assert payload type/media type/content, exact selected node IDs, batch boundaries, immutable identity changes, unchanged original, copied ingestion metadata, log event, and cancellation-token identity.

### Risks

- Broad exception catches mean failure and cancellation tests must assert observed current behavior, including logs and partial/unchanged output.
- Immutable replacement assertions must not require every ancestor to retain reference identity; only unaffected branches should.
- Avoid duplicating substantial existing chunk-enricher coverage.

### Phase Validation

- **Build**: `dotnet build test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Microsoft.Extensions.DataIngestion.Tests.csproj --framework net10.0 --nologo`
- **Narrow test**: `dotnet test test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Microsoft.Extensions.DataIngestion.Tests.csproj --framework net10.0 --no-restore --nologo --filter "FullyQualifiedName~Processors"`

### Success Criteria

- [ ] Existing four chunk-enricher suites remain intact and passing.
- [ ] Batching has an observable call-boundary assertion.
- [ ] Image selection, payload, nested rewrite, skip, metadata, mismatch, failure, and cancellation paths are covered.
- [ ] Every image rewrite test proves original-tree immutability.

---

## Phase 4: Ingestion Pipeline Orchestration and Telemetry

### Overview

Exercise the top layer only after all seam and leaf behavior is established. Restore deterministic file/list/directory breadth, mixed content, provider-backed writing, failure isolation, cancellation characterization, activities, processor order, and disposal.

### Files to Test

#### 1. Ingestion pipeline
- **Source**: `src/Libraries/Microsoft.Extensions.DataIngestion/IngestionPipeline.cs`
- **Test File**: `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/IngestionPipelineTests.cs`
- **Test Class**: `IngestionPipelineTests`

**Methods**: public pipeline run/process overloads for one file, multiple files, and directory input; asynchronous disposal.

**Planned tests**:
1. `RunAsync_InvokesNonGenericSeamsInConfiguredOrder`
   - Record reader → document processors → chunker → chunk processors → writer calls.
   - Assert rewritten documents/chunks from one stage are the instances observed by the next.
2. `RunAsync_PreservesMixedTextContentAndDataContent`
   - Feed both non-generic `AIContent` runtime types and assert the writer receives each in order with positive tokens.
3. `RunAsync_WithEmbeddingProvider_PersistsProviderGeneratedEmbedding`
   - Use local InMemory VectorData plus a fake `IEmbeddingGenerator<AIContent, Embedding<float>>`.
   - Assert provider invocation/input type and stored embedding values.
4. `RunAsync_MultipleFiles_IsolatesPerFileFailureAndPreservesResultOrder`
   - Fail one reader/processor path, assert later files continue and one failed `IngestionResult` is emitted at the correct position.
5. `RunAsync_DirectoryEnumeratesExpectedLocalFiles`
   - Temporary directory only; assert discovered identifiers, order according to the API contract, and no network access.
6. `RunAsync_FileAndFileListOverloadsUseEquivalentStageComposition`
7. `RunAsync_ForwardsCancellationTokenToEveryReachedStage`
8. `RunAsync_PreCanceledToken_CharacterizesFailedResultSemantics`
   - Because the pipeline catches all exceptions, assert whether cancellation becomes a failed result and verify no later stage calls.
9. `RunAsync_MidStreamCancellation_StopsSubsequentStagesAndFiles`
10. `RunAsync_SuccessActivityHasExpectedStatusAndTags`
11. `RunAsync_FailureActivityHasExpectedStatusErrorAndFileTags`
12. `DisposeAsync_DisposesOwnedReaderWriterAndProcessorsExactlyOnce`
13. `DisposeAsync_AfterPartialFailureStillDisposesAllOwnedSeams`

**Fixtures/mocks**:
- Nested recording fakes for every non-generic abstract seam; each records input instance, call order, and cancellation token.
- Real `TextContent`/`DataContent` chunks, neutral documents, local temporary files/directories.
- Fake AIContent embedding generator and InMemory store for the provider path.
- `ActivityListener` or the existing in-memory OpenTelemetry exporter.
- Disposal counters and deterministic injected exceptions.

**Assertions/secondary observables**:
- Stage order and transformed instance flow, exact file/result order, call suppression after failure/cancellation, token identity, stored vector, Activity status/tags, and exactly-once disposal.

### Risks

- Cancellation tests must characterize the current catch-all behavior instead of changing it by assumption.
- File-system ordering should only be asserted where the public contract defines it; otherwise compare the expected set while separately checking result/input correlation.
- Keep provider-backed pipeline coverage local and fake-driven; do not duplicate the full writer provider matrix.

### Phase Validation

- **Build**: `dotnet build test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Microsoft.Extensions.DataIngestion.Tests.csproj --framework net10.0 --nologo`
- **Narrow test**: `dotnet test test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Microsoft.Extensions.DataIngestion.Tests.csproj --framework net10.0 --no-restore --nologo --filter "FullyQualifiedName~IngestionPipelineTests"`

### Success Criteria

- [ ] All non-generic seams are exercised in order with mixed content.
- [ ] Provider embedding is observed through a configured local provider.
- [ ] Per-file failure, cancellation flow/current result semantics, activities, directory/file overloads, and disposal are covered.

---

## Phase 5: Typed Vector Persistence and Embedding Adapter

### Overview

Extend rather than replace the substantial writer matrix. Keep typed records, persistence, incremental ingestion, and fake provider embedding tests stable; add only missing observable batch boundaries, cancellation/failure ordering, and direct adapter contracts.

### Files to Test

#### 1. Vector records, collection definition, and writer
- **Sources**:
  - `src/Libraries/Microsoft.Extensions.DataIngestion/Writers/IngestionChunkVectorRecord.cs`
  - `src/Libraries/Microsoft.Extensions.DataIngestion/Writers/VectorStoreExtensions.cs`
  - `src/Libraries/Microsoft.Extensions.DataIngestion/Writers/VectorStoreWriter.cs`
- **Test Files**:
  - `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Writers/VectorStoreWriterTests.cs`
  - `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Writers/InMemoryVectorStoreWriterTests.cs`
  - `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Writers/SqliteVectorStoreWriterTests.cs`
- **Test Classes**: existing abstract `VectorStoreWriterTests` and provider-specific subclasses

**Methods**: `VectorStoreWriter<TRecord>` write/dispose behavior; `VectorStoreExtensions` typed collection/configuration path; `IngestionChunkVectorRecord.Embedding`.

**Tests to preserve and strengthen**:
1. `WriteAsync_WritesTypedCustomRecordsWithMetadata`
   - Keep `TestChunkRecordWithMetadata`/`TestVectorStoreWriterWithMetadata` and generic constraints.
2. `WriteAsync_PersistsPolymorphicAIContentPageContextAndMetadata`
   - Cover `TextContent` and `DataContent`, page/node provenance, and provider round-trip.
3. `WriteAsync_GeneratesEmbeddingsThroughConfiguredProvider`
   - Assert fake `IEmbeddingGenerator<AIContent,...>` input/order and stored vector.
4. Existing incremental-ingestion tests
   - Preserve replacement semantics in InMemory and SQLite where supported.
5. Existing multiple-document guard/failure test
   - Preserve and add a no-mutation assertion if the provider permits it.

**Planned additions**:
1. `WriteAsync_BatchesUpsertsAtConfiguredBatchSize`
   - Use a recording local collection/provider only if the current VectorData API exposes batch calls.
   - For `N = 2 * batchSize + 1`, assert call sizes `[batchSize, batchSize, 1]`, record order, and final count.
   - If the provider API cannot expose calls without replacing product behavior, record the limitation in status and retain the final-count assertion; do not modify production solely for observability.
2. `WriteAsync_BatchFailure_PreservesCompletedCallOrderAndSkipsLaterMutation`
   - Inject failure on a selected upsert; assert completed batches, no later batch/delete call, surfaced exception, and retained prior records.
3. `WriteAsync_CancellationBetweenBatches_StopsBeforeIncrementalDelete`
   - Cancel from the recording provider; assert token identity, no later upsert/delete, and ordering.
4. `WriteAsync_IncrementalReplacementDeletesOldRecordsOnlyAfterSuccessfulUpsert`
   - Add only if recording API can prove operation order; otherwise preserve existing state-based tests and document the observability limit.
5. `EmbeddingProperty_ReturnsOriginalAIContent`
   - Direct record test for the `Embedding => Content` provider contract with both content runtime types.

**Fixtures/assertions**:
- Existing abstract-provider suite with InMemory and local temporary SQLite.
- Separate instrumented fake collection/provider for call boundaries and fault injection; do not force this into inherited provider tests.
- Assert typed CLR record type, batch call sizes/order, cancellation token, persisted content/provenance/metadata, old/new record state, and embedding vector.

#### 2. Embedding generator adapter
- **Source**: `src/Libraries/Microsoft.Extensions.DataIngestion/Writers/EmbeddingGeneratorExtensions.cs`
- **Test File**: `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Writers/EmbeddingGeneratorExtensionsTests.cs` (new)
- **Test Class**: `EmbeddingGeneratorExtensionsTests`

**Methods**: the file's string-to-`TextContent` adapter, service lookup, and disposal entry points.

**Planned tests**:
1. `Adapter_ConvertsStringInputToTextContent`
   - Assert exact text and input order observed by the inner generator.
2. `Adapter_ForwardsEmbeddingGenerationOptionsAndCancellationToken`
   - Assert reference identity for options and token equality.
3. `Adapter_ReturnsInnerEmbeddingsWithoutReordering`
4. `GetService_DelegatesToInnerGenerator`
5. `Dispose_DisposesInnerGeneratorExactlyOnce`
6. `DisposeAsync_DisposesAsyncInnerGeneratorExactlyOnce` (only if the adapter contract exposes async disposal)

### Risks

- Abstract writer tests execute once per provider; fake-only ordering tests should not accidentally multiply or impose unsupported SQLite internals.
- SQLite remains omitted on `net462`; no new provider is added.
- Incremental failure ordering should only be asserted when observable through a local recording API.
- Adapter tests should assert forwarding, not duplicate embedding algorithm behavior.

### Phase Validation

- **Build**: `dotnet build test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Microsoft.Extensions.DataIngestion.Tests.csproj --framework net10.0 --nologo`
- **Narrow test**: `dotnet test test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Microsoft.Extensions.DataIngestion.Tests.csproj --framework net10.0 --no-restore --nologo --filter "FullyQualifiedName~Writers"`

### Success Criteria

- [ ] Typed custom records and polymorphic `AIContent` persistence remain passing in the existing provider matrix.
- [ ] Real upsert boundaries are asserted if the provider API permits.
- [ ] Incremental, cancellation, and failure ordering are asserted without weakening existing state checks.
- [ ] Provider-driven embeddings and every adapter forwarding/lifetime contract are directly observed.

---

## Phase 6: Deterministic Composition and Pre-Completion Quality Gate

### Overview

Confirm that extraction → neutral document → chunk → typed writer composition remains deterministic, then run bounded quality analysis and the full repository harness. This phase does not broaden parser/provider scope.

### Composition Test

- **Production path**: existing DocumentExtraction reader composed with the already tested neutral model, DataIngestion chunkers, and vector writer; no new production target is introduced.
- **Test File**: `test/Libraries/Microsoft.Extensions.DataIngestion.DocumentExtraction.Tests/DocumentExtractionReaderTests.cs`
- **Test Class**: `DocumentExtractionReaderTests`

**Preserve/strengthen only if an assertion is missing**:
1. `ExtractedDocument_CanBeChunkedAndWrittenAsTypedRecords`
   - Assert neutral document output, typed records, pages, and context.
2. `ExtractedDocument_UsesConfiguredProviderEmbedding`
   - Assert the linked deterministic `TestEmbeddingGenerator` is called and its vector is persisted.

Do not restore remote `DocumentReaderConformanceTests`, broad Markdown parser tests, or any external extraction process.

### Narrow Composition Validation

- **Build**: `dotnet build test/Libraries/Microsoft.Extensions.DataIngestion.DocumentExtraction.Tests/Microsoft.Extensions.DataIngestion.DocumentExtraction.Tests.csproj --framework net10.0 --nologo`
- **Narrow test**: `dotnet test test/Libraries/Microsoft.Extensions.DataIngestion.DocumentExtraction.Tests/Microsoft.Extensions.DataIngestion.DocumentExtraction.Tests.csproj --framework net10.0 --no-restore --nologo --filter "FullyQualifiedName~DocumentExtractionReaderTests"`

### Pre-Completion Quality Gates

1. **Test-gap analysis**
   - Invoke `test-gap-analysis` against only the bounded inventory in `research.md` and the changed tests.
   - Verify every planned public behavior has a direct or clearly identified indirect test.
   - Do not rerun `find-untested-sources`; research records its required one-time invocation.
2. **Assertion quality**
   - Invoke `assertion-quality` on all added/modified test methods.
   - Reject presence-only, assertion-free, self-referential, or solely call-count tests.
   - Require primary result assertions plus relevant secondary observables (token count/provenance, request payload, operation order, token flow, logs/activity, persistence, or immutability).
3. **Prompt-scenario mapping**
   - Reconcile the 15-item checklist below against actual test fully-qualified names.
   - Mark each item `Pass`, `Intentional deterministic exclusion`, or `Blocked (reason)`. No item may be silently omitted.
4. **Scoped suites**
   - Run the complete net10 DataIngestion, Documents.Abstractions, and DocumentExtraction test projects using the commands in the Commands section.
   - Run discovery if needed: `dotnet test test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Microsoft.Extensions.DataIngestion.Tests.csproj --framework net10.0 --list-tests --nologo`.
5. **Repository harness**
   - Run `build.cmd -test -configuration Release` to validate all target frameworks and warnings-as-errors.
   - Make no code-coverage configuration/tooling changes.

### `.testagent/status.md` Contents

Create `.testagent/status.md` at implementation completion with:

- date/working-tree scope and a statement that existing tests were preserved;
- production files changed (expected: none; if any, exact deterministic failing test and minimal rationale);
- test/fixture files added or modified;
- phase-by-phase commands, exit results, and test counts;
- full repository harness result, including any pre-existing warning/blocker;
- a 15-row requirement-to-actual-test map using fully-qualified test names;
- `test-gap-analysis` findings and dispositions;
- `assertion-quality` findings and fixes/dispositions;
- batch-boundary API observability result;
- cancellation semantics actually observed for chunkers, pipeline, writer, and enrichment;
- deterministic external-test exclusions and old-hierarchy disposition;
- remaining gaps/blockers, with no claim of line/branch coverage.

### Success Criteria

- [ ] Deterministic extraction composition still proves neutral documents, typed records, pages, and provider embedding.
- [ ] Test-gap and assertion-quality gates have no unresolved high-priority bounded finding.
- [ ] All 15 prompt scenarios map to actual tests or explicit intentional exclusions.
- [ ] All scoped suites and `build.cmd -test -configuration Release` pass, or exact pre-existing blockers are documented.
- [ ] `.testagent/status.md` is complete and no coverage tooling or unrelated production files changed.

---

## Mandatory Requirement-to-Planned-Test Map

Each checklist item is separate so implementation and final status can map it to actual fully-qualified names.

| # | Requirement | Concrete planned tests/files |
|---:|---|---|
| 1 | Non-generic `AIContent` chunk boundaries | `IngestionChunkTests.Constructor_AcceptsNonGenericAIContent`; `DocumentTokenChunkerTests.ProcessAsync_EmitsNonGenericAIContentWithExactPositiveTokenCount`; `SemanticSimilarityChunkerTests.ProcessAsync_EveryChunkHasNonGenericAIContentAndExactPositiveTokenCount`; pipeline mixed-content test. |
| 2 | Required positive token counts | `IngestionChunkTests.Constructor_RequiresPositiveTokenCount`; every concrete chunker suite's `EveryChunkHasExactPositiveTokenCount`/equivalent re-tokenization assertion. |
| 3 | Non-generic pipeline/chunker/processor/writer contracts | `IngestionAbstractionsContractTests.AbstractSeams_ExposeNonGenericContracts`; `IngestionPipelineTests.RunAsync_InvokesNonGenericSeamsInConfiguredOrder`; `RunAsync_PreservesMixedTextContentAndDataContent`. |
| 4 | Typed `VectorStoreWriter` records | Preserve/strengthen `VectorStoreWriterTests.WriteAsync_WritesTypedCustomRecordsWithMetadata` and generic `TestVectorStoreWriterWithMetadata`; composition typed-record test. |
| 5 | Batching | `VectorStoreWriterTests.WriteAsync_BatchesUpsertsAtConfiguredBatchSize` if API-observable; `AlternativeTextEnricherTests.ProcessAsync_BatchesSelectedImagesAtConfiguredSize`; `ClassificationEnricherTests.ProcessAsync_BatchesRequestsAtConfiguredSize`. |
| 6 | Incremental ingestion | Preserve both existing provider incremental tests; add `WriteAsync_IncrementalReplacementDeletesOldRecordsOnlyAfterSuccessfulUpsert` when recording API permits and failure-retention assertions otherwise. |
| 7 | Cancellation | Concrete chunker cancellation tests; semantic generator token test; pipeline pre-canceled/mid-stream tests; writer between-batch test; image and chunk-enricher chat-boundary characterization tests. |
| 8 | Failure behavior | Header limit/table-row failures; semantic embedding-count mismatch; pipeline per-file isolation; image mismatch/chat-failure logging; writer batch failure/no-later-mutation. |
| 9 | Enrichment | Preserve all Classification/Keyword/Sentiment/Summary suites; `IngestionPipelineTests.RunAsync_InvokesNonGenericSeamsInConfiguredOrder` proves processor rewrites/order. |
| 10 | Persistence | Preserve/strengthen `WriteAsync_PersistsPolymorphicAIContentPageContextAndMetadata` across InMemory and supported SQLite; deterministic extraction composition. |
| 11 | Provider-driven embeddings | Preserve writer configured-provider test; `IngestionPipelineTests.RunAsync_WithEmbeddingProvider_PersistsProviderGeneratedEmbedding`; all direct `EmbeddingGeneratorExtensionsTests`; extraction composition provider test. |
| 12 | DocumentToken overlap/no-overlap, Header, Section, SemanticSimilarity | Separate planned files/classes under `Chunkers/`, with named boundary, context, semantic grouping, count, mismatch, and cancellation tests; retain `SharedDocumentChunkerTests`. |
| 13 | Pipeline multiple content types, embedding provider, failure, cancellation/activity, directory/file orchestration, disposal | Named `IngestionPipelineTests` in Phase 4 cover each concern separately, including both activity status paths and exactly-once disposal. |
| 14 | Image enrichment | Named `AlternativeTextEnricherTests` cover payload/selection, batch boundaries, nested container/table-cell immutable rewrites, skip rules, metadata, mismatch, failure/logging, and cancellation. |
| 15 | Preserve existing tests and exclude nondeterministic live tests | No existing test is deleted/replaced. Keep local MarkItDown MCP argument validation and authored Markdown tests. Intentionally do not restore remote `DocumentReaderConformanceTests`, external-process/network `MarkItDownReaderTests`, actual MCP-server tests, or live OpenAI/Azure/Ollama/vector-database tests. |

## Intentional Dispositions

### Old incompatible hierarchy

- Do not restore `IngestionDocumentElement`, `IngestionDocumentSection`, `IngestionDocumentHeader`, `IngestionDocumentParagraph`, `IngestionDocumentImage`, `IngestionDocumentTable`, or deleted element extensions.
- Adapt old scenarios to `Document`, immutable `DocumentNode` subtypes, `DocumentTextProjection`, and `TestDocuments`.
- Any old identity/mutation assertion becomes an immutable rewrite assertion: changed path gets new nodes, unaffected branches remain equivalent/identical where supported, and the original document remains unchanged.

### External and network behavior

- Do not restore historical remote `DocumentReaderConformanceTests.SupportsStreams`, `SupportsFiles`, or `SupportsImages`.
- Do not restore provider conformance in historical `MarkItDownReaderTests`; it requires an external process and inherited network fixtures.
- Keep current deterministic `MarkItDownMcpReaderTests` argument/file validation, but add no real MCP server.
- Add no live OpenAI, Azure, Ollama, or remote vector database calls. “Provider-driven” is satisfied by a configured local VectorData provider invoking a fake embedding generator.
- Continue using InMemory and existing temporary local SQLite only; do not destabilize working provider tests.
