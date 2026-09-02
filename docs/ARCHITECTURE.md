# Architecture

The tool has four stages: deterministic project discovery and MSBuild evaluation; authoritative per-target extraction from NuGet lock files; immutable canonical graph aggregation plus derived display transformations; and deterministic JSON/GraphML/HTML rendering. The raw graph is never package-filtered. Display views are projections that either remove hidden nodes or replace paths through them with clearly marked contracted edges.

`DotNetDependencyGraph.Core` owns models, extraction, algorithms, diagnostics, and rendering. `DotNetDependencyGraph.Cli` owns argument parsing, restore orchestration, cancellation, and exit codes. The browser viewer consumes embedded JSON and bundled Cytoscape.js, so generated reports work over `file://` without a server.

Stable identities use normalized root-relative project paths and lower-case package IDs. Observations from different TFM/RID targets are resolved independently and only then aggregated into logical edges with retained contexts.
