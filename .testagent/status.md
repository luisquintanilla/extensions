# Test Generation Status

## Scope and outcome

- **Date**: 2026-09-10
- **Workspace**: `C:\Dev\copilot-worktrees\extensions\pr-2-luisquintanilla-neutral-document-tree`
- **Strategy**: Broad Research → Plan → Implement, six dependency-ordered phases.
- **Source of truth**: The current working tree was used throughout. No checkout, restore, reset, clean, stash, delete, or source reconstruction was performed.
- **Production changes**: The neutral tree now preserves producer page-reference order and multiplicity, exposes a versioned `DocumentOpaque` envelope, and keeps opaque content out of text projection. Document Extraction now indexes node evidence and the MEDI reader exposes a typed extraction-result handoff without copying geometry into the shared tree or vector records.
- **Existing tests**: Preserved. Existing suites were extended only where needed; no existing test file or incompatible element hierarchy was restored.
- **New/adapted test methods**: 86 across the phases, plus parameterized cases and the existing tests in modified files. The final DataIngestion net10.0 discovery count is 188 (baseline 84).

## Artifacts

- `.testagent/research.md`: bounded inventory, history findings, commands, checklist, and deterministic exclusions.
- `.testagent/plan.md`: six-phase implementation plan and planned requirement map.
- `.testagent/status.md`: this final review and validation record.

## Test files

### Added

- `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Chunkers/ChunkerTestAssertions.cs`
- `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Chunkers/DocumentTokenChunkerTests.cs`
- `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Chunkers/HeaderChunkerTests.cs`
- `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Chunkers/NoOverlapTokenChunkerTests.cs`
- `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Chunkers/OverlapTokenChunkerTests.cs`
- `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Chunkers/SectionChunkerTests.cs`
- `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Chunkers/SemanticSimilarityChunkerTests.cs`
- `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/IngestionAbstractionsContractTests.cs`
- `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Writers/EmbeddingGeneratorExtensionsTests.cs`
- `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Writers/VectorStoreWriterBehaviorTests.cs`

### Modified additively

- `test/Libraries/Microsoft.Extensions.Documents.Abstractions.Tests/DocumentTests.cs`
- `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/IngestionChunkTests.cs`
- `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/IngestionPipelineTests.cs`
- `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Processors/AlternativeTextEnricherTests.cs`
- `test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Processors/ClassificationEnricherTests.cs`
- `test/Libraries/Microsoft.Extensions.DataIngestion.DocumentExtraction.Tests/DocumentExtractionReaderTests.cs`
- `src/Libraries/Microsoft.Extensions.Documents.Abstractions/DocumentOpaque.cs`
- `src/Libraries/Microsoft.Extensions.DataIngestion.DocumentExtraction/DocumentExtractionReaderExtensions.cs`
- `test/Libraries/Microsoft.Extensions.DocumentExtraction.Abstractions.Tests/DocumentExtractionResultTests.cs`

The new C# production and test files use SDK implicit compile items; the public API manifests were regenerated for the new surfaces.

## Phase validation

| Phase | Scope | Validation result |
|---|---|---|
| 1 | Neutral tree, `IngestionChunk`, non-generic seams | Initial recovery run: Documents `DocumentTests`: 8 passed; DataIngestion abstraction/chunk filters: 15 passed; discovery increased by 12 cases. Follow-up neutral-waist validation is recorded below. |
| 2 | DocumentToken, Header, Section, SemanticSimilarity chunkers | Chunker filter: 63 passed; discovery increased by 51 cases. |
| 3 | Chunk enrichment batching and immutable image enrichment | Processor filter: 44 passed; discovery increased by 13 cases. |
| 4 | Pipeline orchestration, failure isolation, cancellation, Activities, disposal | `IngestionPipelineTests`: 14 passed; discovery increased by 13 cases. |
| 5 | Typed writer behavior, batching, incremental ordering, adapter | Writers filter: 42 passed; 12 existing InMemory/SQLite provider cases remained passing; discovery increased by 15 cases. |
| 6 | Document-extraction composition and provider embedding | `DocumentExtractionReaderTests`: 3 passed; one deterministic provider-persistence test was added. |

## Final validation commands and results

The repository scripts require a `powershell` command visible to child `cmd.exe` processes. The exact repository commands were run through `call .\build.cmd` with the installed Windows PowerShell directory made visible; no build behavior or project arguments were changed.

### Restore

```text
restore.cmd
```

Result: **succeeded**, all repository projects restored.

### Full workspace build

```text
build.cmd -build -configuration Release
```

Result: **succeeded**, `0 Warning(s)`, `0 Error(s)`.

### Full workspace test harness

```text
build.cmd -test -configuration Release
```

The fresh Release build completed and every in-scope document/data-ingestion test project passed on every target framework. The harness exit was non-zero only because of this unrelated environment-dependent existing test:

```text
Microsoft.Extensions.ServiceDiscovery.Yarp.Tests.YarpServiceDiscoveryTests.ServiceDiscoveryDestinationResolverTests_Dns
System.InvalidOperationException: No DNS records were found for service 'https://microsoft.com' (DNS name: 'microsoft.com').
```

In-scope results from the same fresh full-harness run:

| Test project | net8.0 | net9.0 | net10.0 | net462 |
|---|---:|---:|---:|---:|
| `Microsoft.Extensions.DataIngestion.Tests` | 188/188 | 188/188 | 188/188 | 176/176 |
| `Microsoft.Extensions.Documents.Abstractions.Tests` | 8/8 | 8/8 | 8/8 | 8/8 |
| `Microsoft.Extensions.DataIngestion.DocumentExtraction.Tests` | 3/3 | 3/3 | 3/3 | 3/3 |

The initial full build exposed net462-only test-source incompatibilities (`init`, `File.WriteAllTextAsync`, a `Split` overload, and `Zip`); these were corrected with equivalent test-only forms. The subsequent net462 run passed 176/176.

### Final quality-focused runs

```text
dotnet test test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Microsoft.Extensions.DataIngestion.Tests.csproj --framework net10.0 --no-restore --nologo --filter "FullyQualifiedName~Chunkers|FullyQualifiedName~Processors|FullyQualifiedName~Writers|FullyQualifiedName~IngestionChunkTests|FullyQualifiedName~IngestionPipelineTests|FullyQualifiedName~IngestionAbstractionsContractTests"
```

Result: affected net10.0 tests passed; the final focused rerun of the three cross-target-sensitive tests was 3/3.

```text
dotnet test test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Microsoft.Extensions.DataIngestion.Tests.csproj --framework net462 --no-restore --nologo
```

Result: **176 passed, 0 failed, 0 skipped**.

```text
git diff --check
```

Result: clean.

## Quality gates

### Pseudo-mutation / test-gap analysis

The bounded final review exercised 15 meaningful mutation candidates across chunkers, pipeline, enrichment, writers, and composition:

| Result | Count |
|---|---:|
| Non-equivalent mutations killed | 13 |
| Non-equivalent mutations survived | 0 |
| No-coverage mutations | 0 |
| Equivalent mutations | 2 |

The three confirmed gaps were closed by:

- `HeaderChunkerTests.ProcessAsync_LowerLevelSiblingClearsDeeperHeadingContext`
- `SemanticSimilarityChunkerTests.Constructor_AcceptsInclusiveThresholdPercentileBounds`
- `AlternativeTextEnricherTests.ProcessAsync_NullDocumentThrows`

The final gate rechecked these tests after the net462 compatibility and Activity/cancellation assertion adaptations. No unresolved high-priority mutation gap remains in the bounded scope. The follow-up production contract changes are validated below.

### Assertion-quality analysis

- **Test methods reviewed**: 107 (generated/modified files, including existing methods in extended suites).
- **Behavioral checks**: at least 999, including delegated checks through `ChunkerTestAssertions.Equal`.
- **Assertion categories**: 12/12 represented (value, boolean, null, exception, type, string, collection, comparison, approximate, negative, side-effect/call, and structural/deep).
- **Assertion-free tests**: 0.
- **Trivial-only tests**: 0.
- **Self-referential tests**: 0.

Exception-only guard tests and disposal/call-contract tests were retained because the exception or lifecycle/call behavior is their subject. The final review found no weak assertion requiring further changes.

## Observable semantics recorded

- **Chunk boundaries**: pinned tokenizer assertions use exact projected content and `considerNormalization: false`; overlap and no-overlap suites distinguish repeated boundary tokens from overlap-only terminal chunks.
- **Token counts**: every concrete chunk assertion checks a positive exact token count; constructor tests reject zero and negative counts.
- **Batching**: `VectorStoreWriterBehaviorTests.WriteAsync_BatchesUpsertsAtConfiguredBatchSize` observes local fake upsert batches `[3, 3, 1]`; image and classification tests observe configured chat batch boundaries.
- **Cancellation**: chunkers stop with `OperationCanceledException`; the pipeline records current catch-all failed-result semantics and forwards tokens; the writer stops before later upsert/delete operations; image and chunk enrichers characterize best-effort logging/partial rewrite behavior. The net462 framework does not preserve exception token identity, so the portable cancellation assertion checks the canceled source and thrown cancellation exception.
- **Failure**: pipeline per-file isolation, semantic embedding-count mismatch, header/table limit failures, image result mismatch/chat failure, and writer batch failure/no-later-mutation are asserted.
- **Activity**: success status/tags, failure status/error/tags, parent/trace identifiers, and document/file tags are asserted.
- **Persistence/provider embeddings**: InMemory and supported local SQLite provider matrices remain passing; provider-generated vectors are asserted through stored records/search results.
- **Image enrichment**: payloads, selection/skip rules, configured batches, nested container/table-cell rewrites, metadata/pages/node IDs, original-tree immutability, mismatch, failure logging, and cancellation are asserted.

## Requirement coverage

| User requirement (verbatim) | Concrete evidence |
|---|---|
| “non-generic AIContent chunk boundaries” | `DocumentTokenChunkerTests.ProcessAsync_EmitsNonGenericAIContentWithExactPositiveTokenCount`; `SemanticSimilarityChunkerTests.ProcessAsync_EveryChunkHasNonGenericAIContentAndExactPositiveTokenCount`; `IngestionPipelineTests.RunAsync_PreservesMixedTextContentAndDataContent`. |
| “required positive token counts” | `IngestionChunkTests.Constructor_RequiresPositiveTokenCount`; `HeaderChunkerTests.ProcessAsync_EveryChunkHasExactPositiveTokenCount`; `SectionChunkerTests.ProcessAsync_EveryChunkHasExactPositiveTokenCount`; shared `ChunkerTestAssertions.Equal` exact positive retokenization. |
| “non-generic pipeline/chunker/processor/writer contracts” | `IngestionAbstractionsContractTests.AbstractSeams_ExposeNonGenericContracts`; `IngestionPipelineTests.RunAsync_InvokesNonGenericSeamsInConfiguredOrder`; `IngestionPipelineTests.RunAsync_PreservesMixedTextContentAndDataContent`. |
| “typed VectorStoreWriter records” | Existing `VectorStoreWriterTests.CanWriteChunksWithCustomDefinition` and `CanWriteChunksWithMetadata`, plus `VectorStoreWriterBehaviorTests.EmbeddingProperty_ReturnsOriginalAIContent`; `DocumentExtractionReaderTests.SharedTreeFlowsThroughNonGenericChunksAndTypedWriter`. |
| “batching” | `VectorStoreWriterBehaviorTests.WriteAsync_BatchesUpsertsAtConfiguredBatchSize`; `AlternativeTextEnricherTests.ProcessAsync_BatchesSelectedImagesAtConfiguredSize`; `ClassificationEnricherTests.ProcessAsync_BatchesRequestsAtConfiguredSize`; existing `VectorStoreWriterTests.BatchesChunks`. |
| “incremental ingestion” | Existing `VectorStoreWriterTests.DoesSupportIncrementalIngestion` and `IncrementalIngestion_WithManyRecords_DeletesAllPreExistingChunks`; `VectorStoreWriterBehaviorTests.WriteAsync_IncrementalReplacementDeletesOldRecordsOnlyAfterSuccessfulUpsert`. |
| “cancellation” | `DocumentTokenChunkerTests.ProcessAsync_PreCanceledToken_ObservesCancellationAtChunkBoundary`; `SectionChunkerTests.ProcessAsync_CancellationBetweenSectionsStopsEnumeration`; `SemanticSimilarityChunkerTests.ProcessAsync_ForwardsCancellationToEmbeddingGenerator`; `IngestionPipelineTests.RunAsync_ForwardsCancellationTokenToEveryReachedStage`; `VectorStoreWriterBehaviorTests.WriteAsync_CancellationBetweenBatches_StopsBeforeIncrementalDelete`; `AlternativeTextEnricherTests.ProcessAsync_Cancellation_ForwardsTokenAndCharacterizesCatchBehavior`. |
| “failure behavior” | `HeaderChunkerTests.ProcessAsync_ContextLargerThanLimit_FailsDeterministically`; `HeaderChunkerTests.ProcessAsync_OversizedTableRow_FailsDeterministically`; `SemanticSimilarityChunkerTests.ProcessAsync_EmbeddingCountMismatchThrowsDeterministically`; `IngestionPipelineTests.RunAsync_MultipleFiles_IsolatesPerFileFailureAndPreservesResultOrder`; `AlternativeTextEnricherTests.ProcessAsync_ChatFailure_LogsAndReturnsBestEffortDocument`; `VectorStoreWriterBehaviorTests.WriteAsync_BatchFailure_PreservesCompletedCallOrderAndSkipsLaterMutation`. |
| “enrichment” | Existing Classification/Keyword/Sentiment/Summary suites remained passing; `ClassificationEnricherTests.ProcessAsync_ResultCountMismatch_UsesDocumentedBestEffortBehavior`; `IngestionPipelineTests.RunAsync_InvokesNonGenericSeamsInConfiguredOrder`. |
| “persistence” | Existing `VectorStoreWriterTests.PersistsPageProvenanceAndPolymorphicNonTextContent`, `NoPageProvenancePersistsNull`, and provider-specific InMemory/SQLite cases; `DocumentExtractionReaderTests.ExtractedDocument_UsesConfiguredProviderEmbedding`. |
| “provider-driven embeddings” | `IngestionPipelineTests.RunAsync_WithEmbeddingProvider_PersistsProviderGeneratedEmbedding`; existing `VectorStoreWriterTests` provider assertions; `EmbeddingGeneratorExtensionsTests.Adapter_ForwardsEmbeddingGenerationOptionsAndCancellationToken`; `DocumentExtractionReaderTests.ExtractedDocument_UsesConfiguredProviderEmbedding`. |
| “chunkers: DocumentToken with overlap and no-overlap, Header, Section, SemanticSimilarity” | `NoOverlapTokenChunkerTests.ProcessAsync_NoOverlap_TwoChunksHaveNoRepeatedTokens`; `OverlapTokenChunkerTests.ProcessAsync_Overlap_RepeatsConfiguredBoundaryTokens`; `HeaderChunkerTests.ProcessAsync_NestedHeadingsCarryExpectedHeadingStack`; `SectionChunkerTests.ProcessAsync_NestedSectionsIncludeAncestorHeadingContext`; `SemanticSimilarityChunkerTests.ProcessAsync_TopicChangeSplitsAtExpectedPercentileBoundary`. |
| “pipeline: multiple content types, embedding provider, failure, cancellation/activity where applicable” | `IngestionPipelineTests.RunAsync_PreservesMixedTextContentAndDataContent`; `RunAsync_WithEmbeddingProvider_PersistsProviderGeneratedEmbedding`; `RunAsync_MultipleFiles_IsolatesPerFileFailureAndPreservesResultOrder`; `RunAsync_PreCanceledToken_IsCapturedAsFailedResultAndSuppressesLaterStages`; `RunAsync_SuccessActivityHasExpectedUnsetStatusAndTags`; `RunAsync_FailureActivityHasExpectedErrorStatusAndFileTags`. |
| “image enrichment” | `AlternativeTextEnricherTests.ProcessAsync_SelectsOnlyImagesWithoutAlternativeText`; `BuildsExpectedImagePayloadAndPrompt`; `BatchesSelectedImagesAtConfiguredSize`; `RewritesNestedContainerAndTableCellImages`; `PreservesDocumentIdentityMetadataNodeIdsAndPages`; `ChatFailure_LogsAndReturnsBestEffortDocument`; `Cancellation_ForwardsTokenAndCharacterizesCatchBehavior`. |
| “preserve existing tests; exclude live external-provider/network conformance tests when not deterministic, documenting intentional dispositions” | Existing suites and provider matrices were preserved; only additive tests were made. Intentionally excluded historical remote `DocumentReaderConformanceTests.SupportsStreams/SupportsFiles/SupportsImages`, external-process/network MarkItDown provider conformance, real MCP-server tests, and live OpenAI/Azure/Ollama/remote-vector conformance. Deterministic MarkItDown argument tests, local InMemory/SQLite, fake chat/embedding providers, and authored neutral-tree composition remain included. |

## Explicit dispositions

- The deleted `IngestionDocumentElement`, `IngestionDocumentSection`, `IngestionDocumentHeader`, `IngestionDocumentParagraph`, `IngestionDocumentImage`, `IngestionDocumentTable`, and old element extensions were not restored. Historical scenarios were adapted to immutable `Document`/`DocumentNode`/`TestDocuments`.
- Live external-provider, network, remote-document, and external-process conformance tests were not restored because they are nondeterministic in this workspace.
- No line/branch coverage percentage is claimed; this status records behavior evidence and bounded pseudo-mutation results instead.
- Full repository harness completion is blocked only by the unrelated DNS-dependent `ServiceDiscoveryDestinationResolverTests_Dns`; all in-scope projects and target frameworks passed.

## Follow-up quality review

The read-only diff review identified and the final pass fixed two portability/assertion issues:

- `ChunkerTestAssertions.Equal` now compares source-node identifiers as an unordered set, matching the production `HashSet<DocumentNodeId>` implementation rather than relying on enumeration order.
- `DocumentTests.PolymorphicRoundTrip_PreservesEveryClosedNeutralNodeKind` now compares every concrete node's semantic state after serialization, including container roles/children, text role/level/language, table dimensions/cells, cell coordinates/spans/roles/content, image bytes/media type/source/description, IDs, pages, and source IDs.
- Test-only disposable fakes in `EmbeddingGeneratorExtensionsTests` and `VectorStoreWriterBehaviorTests` are disposed; the net462-incompatible `Enumerable.Zip` use was replaced with an indexed comparison and the nullable `Task.FromResult` fake was made explicit.

Final follow-up validation:

- `dotnet test test/Libraries/Microsoft.Extensions.DataIngestion.Tests/Microsoft.Extensions.DataIngestion.Tests.csproj --framework net10.0 --no-restore --nologo --filter "FullyQualifiedName~Chunkers|FullyQualifiedName~Writers"`: **108 passed**.
- `dotnet test test/Libraries/Microsoft.Extensions.Documents.Abstractions.Tests/Microsoft.Extensions.Documents.Abstractions.Tests.csproj --framework net10.0 --no-restore --nologo`: **8 passed** in the initial recovery run; the follow-up opaque/page-reference suite is **9 passed**.
- The corresponding net462 chunker/writer filter passed **96/96** and document suite passed **8/8**.
- `build.cmd -build -configuration Release`: **succeeded, exit code 0**.
- Final `build.cmd -test -configuration Release`: all in-scope projects passed (**258/258** on net8.0/net9.0/net10.0 and **246/246** on net462); the only remaining harness failure is the unrelated DNS-dependent `ServiceDiscoveryDestinationResolverTests_Dns`.
- `git diff --check`: clean before the follow-up production contract changes; the final working-tree check is recorded with the commit handoff.

The post-fix pseudo-mutation/assertion review found no new gap or trivial-only assertion; the strengthened round-trip and order-insensitive provenance checks supersede the two review findings.

## Follow-up neutral-waist validation

- Shared document tests: **9 passed** on `net8.0`, `net9.0`, `net10.0`, and `net462`.
- Document Extraction abstraction tests: **31 passed** on `net8.0`, `net9.0`, `net10.0`, and `net462`, including node-evidence lookup plus page dimensions, coordinate units/origin, geometry, confidence, raw representations, and provider properties.
- MEDI Document Extraction integration tests: **3 passed** on `net8.0`, `net9.0`, `net10.0`, and `net462`, including the typed extraction-result handoff.
- MEDI behavior suite: **188 passed** on `net8.0`, `net9.0`, and `net10.0`; **176 passed** on `net462`.
- `build.cmd -build -configuration Release`: **succeeded**, 0 errors and 303 analyzer warnings.
- Targeted changed-package `dotnet pack`: all five packages created successfully.
- API baselines were regenerated for the new `DocumentOpaque`, evidence lookup, and extraction-reader handoff surfaces; unrelated generated manifest drift was discarded.
- The opaque-node test verifies logical kind/schema/position/payload/reference round-trip, non-text projection, and rejection of an unknown `$type`.
