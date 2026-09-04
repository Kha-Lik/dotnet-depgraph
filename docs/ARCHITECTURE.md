# Architecture

The tool has five stages: deterministic project discovery and MSBuild evaluation; authoritative per-target extraction from NuGet lock files; immutable canonical graph aggregation; full-graph community/architecture analysis; and derived display transformations plus deterministic JSON/GraphML/HTML rendering. The raw graph is never display-filtered.

`DotNetDependencyGraph.Core` is organized by architectural layer and then responsibility:

```text
Domain/{Graph,Communities}                 canonical contracts and schema validation
Application/{Scanning,Analysis,Filtering} scan orchestration and graph operations
Infrastructure/{MSBuild,NuGet,Processes,Output}
                                           external-system implementations and report output
Algorithms/Communities                    projection and Leiden/CPM implementation
Reporting/Rendering                       viewer rendering policy
```

The intended dependency flow is `CLI → Application → Domain`, with
Infrastructure supplying external-system implementations to Application and
Output consuming Application/Domain results. Community algorithms consume only
Domain contracts. Domain has no filesystem, process, MSBuild, NuGet, HTML, or
serialization implementation dependency. `DotNetDependencyGraph.Cli` owns
argument parsing, restore orchestration, cancellation, and exit codes. The
browser viewer consumes embedded JSON and bundled Cytoscape.js, so generated
reports work over `file://` without a server.

As part of this source-tree refactor, public Core types moved from the former
`DotNetDependencyGraph.Core` namespace into these responsibility namespaces.
Their names, members, behavior, and serialized property names are unchanged;
source consumers need to update their `using` directives.

Stable identities use normalized root-relative project paths and lower-case package IDs. Observations from different TFM/RID targets are resolved independently and only then aggregated into logical edges with retained contexts.

The analysis boundary is explicit: `raw graph → evidence-based ownership → retained source nodes → context-safe contraction through excluded packages → producer collapse → test exclusion → Leiden/CPM hierarchy → producer/test expansion → source-only naming and representatives → selected display subset → effective manual assignments`. Display filters never rerun detection. External packages stay canonical but do not receive automatic communities by default. Directed topology remains authoritative for blast radius, paths, SCCs, and cross-community coupling.

The managed Leiden implementation uses seeded canonical ordering, CPM local moving, subset-constrained refinement, aggregation, repeated optimization, and a final connectedness invariant. Hierarchy levels are not optimizer aggregation artifacts: higher-resolution Leiden is run on induced parent subgraphs and a split is retained only with objective, connectedness, and stability support.
