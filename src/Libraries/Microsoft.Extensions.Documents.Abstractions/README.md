# Microsoft.Extensions.Documents.Abstractions

This package defines a provider-neutral immutable semantic document tree shared by document producers and consumers.

`Document.Children` is the canonical hierarchy. Every node has a stable typed identifier, optional producer- or processor-supplied physical-page references, and typed source-node provenance. Logical sections, lists, list items, and quotes remain hierarchy containers; pages are annotations and never replace logical structure.

Page references are copied in the order supplied by the producer or processor. The shared model does not infer, sort, deduplicate, or normalize provider page conventions; aggregation belongs to the consuming layer.

`Document.Text` is the deterministic plain-text projection and always uses `\n` separators. The model contains no Markdown, provider objects, extraction geometry, confidence, or arbitrary property dictionaries.

The finite node union has stable `System.Text.Json` `$type` discriminators. `DocumentOpaque` is the explicit versioned envelope for unsupported or provider-specific semantic content; its logical kind, schema version, identity, position, references, and JSON payload round-trip without being projected as ordinary text. External node derivation is closed and unknown discriminators fail deserialization rather than silently changing meaning.
