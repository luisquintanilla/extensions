# Microsoft.Extensions.DataIngestion.DocumentExtraction

This package composes `IDocumentExtractionClient` with MEDI through `DocumentExtractionReader`.

It references both `Microsoft.Extensions.DocumentExtraction.Abstractions` and `Microsoft.Extensions.DataIngestion.Abstractions`. Core MEDI does not reference Document Extraction. No semantic mapping is required because both packages use the canonical tree from `Microsoft.Extensions.Documents.Abstractions`.
