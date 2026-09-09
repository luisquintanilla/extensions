# Microsoft.Extensions.DataIngestion.DocumentExtraction

This package composes `IDocumentExtractionClient` with MEDI through `DocumentExtractionReader`.

It references both abstractions packages. Core MEDI does not reference Document Extraction, and no semantic mapping is required because both use `Microsoft.Extensions.Documents.Abstractions`.
