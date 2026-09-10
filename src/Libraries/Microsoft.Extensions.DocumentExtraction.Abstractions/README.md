# Microsoft.Extensions.DocumentExtraction.Abstractions

.NET developers need to turn documents such as scanned images and PDFs into structured, AI-ready content, recovering text along with layout, tables, figures, and coordinates in a provider-neutral way. The `Microsoft.Extensions.DocumentExtraction` libraries provide a unified approach for representing document-extraction components, complementing `Microsoft.Extensions.DataIngestion` (extraction pulls content out of documents; ingestion feeds that content into a retrieval pipeline).

## The packages

The [Microsoft.Extensions.DocumentExtraction.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.DocumentExtraction.Abstractions) package provides the core exchange types, including [`IDocumentExtractionClient`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.documentextraction.idocumentextractionclient), [`DocumentExtractionResult`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.documentextraction.documentextractionresult), and [`DocumentPage`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.documentextraction.documentpage). Semantic content uses the provider-neutral [`Microsoft.Extensions.Documents.Document`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.documents.document) tree; extraction evidence retains geometry, confidence, raw representations, and provider properties outside that shared waist. Any .NET library that provides a document-extraction engine can implement these abstractions to enable seamless integration with consuming code.

The [Microsoft.Extensions.DocumentExtraction](https://www.nuget.org/packages/Microsoft.Extensions.DocumentExtraction) package has an implicit dependency on the `Microsoft.Extensions.DocumentExtraction.Abstractions` package. This package enables you to easily integrate components such as logging, telemetry, and options configuration into your applications using familiar dependency injection and builder patterns.

## Which package to reference

Libraries that provide implementations of the abstractions typically reference only `Microsoft.Extensions.DocumentExtraction.Abstractions`.

To also have access to higher-level utilities for working with document-extraction clients, reference the `Microsoft.Extensions.DocumentExtraction` package instead (which itself references `Microsoft.Extensions.DocumentExtraction.Abstractions`). Most consuming applications and services should reference the `Microsoft.Extensions.DocumentExtraction` package along with a library that provides a concrete implementation of the abstractions.

## Content contract

`DocumentPage.Document` is the canonical semantic content in provider reading order. `DocumentPage.Text` is the deterministic plain-text projection of that tree: text nodes contribute literal text, table cells recurse in row/column order, and image descriptions contribute once. Text-bearing nodes are separated by blank lines; table columns use tabs and rows use newlines. Binary images, opaque extension nodes, and provider-formatted Markdown do not implicitly become text.

`DocumentPage.Markdown` is nullable and preserves an exact provider-supplied page rendering. The libraries do not synthesize it or parse it back into the semantic tree. `DocumentExtractionEvidence` is indexed by shared node identity through `DocumentExtractionResult.Evidence` and `TryGetEvidence`; providers without geometry remain valid. `DocumentExtractionResult` intentionally has no Markdown aggregation because page fragments are not necessarily a complete provider-supplied document rendering; its `Text` is derived from the merged canonical document.

## Install the package

From the command-line:

```console
dotnet add package Microsoft.Extensions.DocumentExtraction.Abstractions --prerelease
```

Or directly in the C# project file:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.DocumentExtraction.Abstractions" Version="[CURRENTVERSION]" />
</ItemGroup>
```

## Documentation

Refer to the [Microsoft.Extensions.DocumentExtraction libraries documentation](https://learn.microsoft.com/dotnet/api/microsoft.extensions.documentextraction) for more information and API usage examples.

## Feedback & Contributing

We welcome feedback and contributions in [our GitHub repo](https://github.com/dotnet/extensions).
