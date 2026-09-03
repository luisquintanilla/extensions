# Microsoft.Extensions.Documents.Abstractions

This package defines a provider-neutral, immutable semantic document tree shared by document producers and consumers.

`Document.Children` is the single canonical hierarchy. Each node has a stable typed identifier, optional physical-page references, and typed source-node provenance. Logical sections, lists, list items, and quotes remain hierarchy containers; pages are annotations and never replace logical structure.

`Document.Text` is the deterministic plain-text projection of the canonical tree and always uses `\n` separators. The model does not contain Markdown, provider objects, extraction geometry, confidence, or arbitrary property dictionaries. Those concerns belong to producing or consuming packages.

The tree has an explicit `System.Text.Json` polymorphic contract with stable `$type` discriminators for the finite node union. Nested containers, tables, cells, images, identifiers, source-node lineage, and page references round-trip. External node derivation is closed so unknown semantic kinds cannot silently disappear from traversal, projection, or serialization.
