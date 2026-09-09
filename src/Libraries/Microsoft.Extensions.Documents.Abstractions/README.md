# Microsoft.Extensions.Documents.Abstractions

This package defines a provider-neutral immutable semantic document tree shared by document producers and consumers.

`Document.Children` is the canonical hierarchy. Every node has a stable typed identifier, optional physical-page references, and typed source-node provenance. Logical sections, lists, list items, and quotes remain hierarchy containers; pages are annotations and never replace logical structure.

`Document.Text` is the deterministic plain-text projection and always uses `\n` separators. The model contains no Markdown, provider objects, extraction geometry, confidence, or arbitrary property dictionaries.

The finite node union has stable `System.Text.Json` `$type` discriminators. External node derivation is closed so unknown kinds cannot silently disappear from traversal, projection, or serialization.
