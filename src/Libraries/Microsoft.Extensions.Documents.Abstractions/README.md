# Microsoft.Extensions.Documents.Abstractions

This package defines a provider-neutral, immutable semantic document tree shared by document producers and consumers.

`Document.Children` is the single canonical hierarchy. Each node has a stable typed identifier, optional physical-page references, and typed source-node provenance. Logical sections, lists, list items, and quotes remain hierarchy containers; pages are annotations and never replace logical structure.

`Document.Text` is the deterministic plain-text projection of the canonical tree and always uses `\n` separators. The model does not contain Markdown, provider objects, extraction geometry, confidence, or arbitrary property dictionaries. Those concerns belong to producing or consuming packages.

Serialization and schema versioning are intentionally not part of this initial contract.
