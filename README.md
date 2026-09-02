# dotnet-depgraph

`dotnet-depgraph` discovers every C#, F#, and Visual Basic project below a root and creates an offline, interactive architecture graph. It combines evaluated project references with the resolved, per-target NuGet topology in `project.assets.json`. Disconnected applications and tools remain separate graph islands.

> [!NOTE]
> This project—including its source code, tests, and documentation—was fully generated using AI. It has been used and tested internally against a real-world .NET source tree containing approximately 200 projects, but it has not undergone an independent security or correctness audit. Review the implementation and validate its output before relying on it for critical architectural, security, or operational decisions.

It analyzes project/package structure—not types, namespaces, calls, or compiled assemblies.

## Build, test, install

Requires the .NET 10 SDK. The checked-in browser bundle means Node.js is not needed by users.

```bash
dotnet restore DotNetDependencyGraph.slnx
dotnet build DotNetDependencyGraph.slnx --no-restore
dotnet test DotNetDependencyGraph.slnx --no-build
dotnet pack src/DotNetDependencyGraph.Cli -c Release -o artifacts/packages
dotnet tool install --global dotnet-depgraph --add-source artifacts/packages
```

Quick start:

```bash
dotnet-depgraph scan --root /path/to/source --output /tmp/dependency-report
```

Open `index.html` directly. Its graph data is embedded and its Cytoscape.js runtime is adjacent, so no HTTP server or network connection is required.

For fast renderer iteration from an existing canonical graph, skip discovery, MSBuild, and restore entirely:

```bash
dotnet-depgraph render --graph /path/to/graph.json --output /tmp/dependency-report-rendered
```

`render` accepts graph schema `1.0`, supports `--seed`, `--force`, `--include-package`, `--exclude-package`, `--filter-mode`, and `--collapse-local-packages`, and refuses unsupported schema versions or unrelated files in a nonempty output directory.

## Why restore data is required

Literal `<PackageReference>` elements are not a transitive graph and can be changed by imports, conditions, and central package management. The tool uses evaluated MSBuild metadata and NuGet's `NuGet.ProjectModel` lock-file API. Each target framework/RID is resolved independently before identical logical edges are aggregated. Missing or invalid assets are reported and make the result incomplete; they are never treated as an empty dependency set.

Restore modes are:

- `never`: never writes to the source tree; consumes existing assets only.
- `missing` (default): runs `dotnet restore` only where an evaluated assets path is absent.
- `always`: restores every discovered project.

Restore is bounded by `--jobs`, uses argument-list process invocation, and continues after per-project failures. `--fail-on-incomplete` changes an otherwise successful partial scan from exit 0 to exit 3. Restore may update normal `obj` files and NuGet caches.

## Filtering and views

Package patterns are repeatable, case-insensitive globs: `*` means any characters and `?` means one character. Excludes win over includes. Projects remain visible by default.

```bash
dotnet-depgraph scan --root /repos/product --output /tmp/product-graph \
  --include-package 'Company.*' --exclude-package 'Company.Legacy.*' \
  --filter-mode contract --restore missing --target-framework all --jobs 4 --seed 42
```

`strict` removes hidden packages and incident edges. `contract` traverses hidden dependency nodes and adds a dashed `contracted-path` only to the first retained node reached. It records minimum hidden hops, bounded path samples/counts, and contributing contexts. Raw facts are never modified. The viewer can switch among raw, strict, and contracted data.

Packable local projects are matched case-insensitively to their evaluated `PackageId`. A unique match creates a subordinate `produces-package` edge. Ambiguous producers generate diagnostics. Separate identity is the raw truth; “collapse local packages” visually projects references onto the producer without replacing the resolved package version or historical dependency metadata.

## Targets, discovery, and evaluation

Use repeatable `--target-framework` and `--runtime` selections. `all` is the default TFM selection. Edge contexts retain owner, assets path, TFM, RID, requested range, resolved version, directness, and observation count.

Discovery is deterministic and excludes `.git`, `.svn`, `.hg`, `bin`, `obj`, and `node_modules`. It does not follow directory symlinks/reparse points, preventing cycles. Inaccessible paths become diagnostics. `--include-path` and `--exclude-path` accept normalized root-relative globs. Duplicate filenames are safe because project IDs contain the complete root-relative path.

MSBuild evaluation uses `dotnet msbuild -getProperty/-getItem`, including a framework-specific pass for multi-target project references. This avoids compilation while honoring imports and conditions. `--property Name=Value` is repeatable and also passed safely to restore.

> MSBuild evaluation and restore can execute repository-controlled logic. Analyze only trusted source trees.

## Viewer

The report uses bundled Cytoscape.js 3.34.2 with d3-force 3.0.0 (licenses included in every report). A live many-body simulation repels every node, edge springs retain dependency neighborhoods, rendered-size-aware collision prevents overlap, and weak per-component centering keeps disconnected components as separate islands. Initial positions are seeded; the simulation visibly settles, cools to idle, and reheats after dragging or physics changes.

Search is partial and case-insensitive. Selection shows metadata and immediate dependencies/dependents, with green outgoing dependencies and orange incoming dependents. Controls filter node/edge kind, component, community, TFM, RID, and version skew; isolate one-to-three-hop neighborhoods; switch raw/strict/contract and separate/collapsed producer views; navigate components; reset/fit; and export displayed JSON or a PNG.

The collapsible Physics / Layout panel controls repulsion, edge-spring distance and strength, collision spacing, and weak gravity. It also pauses/resumes physics, deliberately randomizes and reruns the layout, resets tuned defaults, and fits all visible islands. Settings are bounded and stored locally under the versioned key `dotnet-depgraph.physics.v2`; reset removes the saved settings. No graph data is persisted.

Node diameter is `clamp(18 + 6 × log2(transitiveDependents + inDegree + 1), 18, 52)` pixels, preserving relative importance without allowing hubs to cover clusters. Color indicates topology-derived community. Projects have distinct shapes; version skew has a modest red ring. The 12 visually largest nodes remain labeled by default at every zoom level, configurable from 0–50 in the Labels panel; additional labels appear progressively while zooming. Hovered and selected labels use full, untruncated text at a stable screen-space size. Overview edges use contrasting colors and focused edges reveal arrowheads. Contracted paths are dashed and producer mappings dotted.

## Output and schema

- `index.html`: offline entry point with safely JSON-escaped embedded data.
- `graph.json`: deterministic canonical raw graph, schema version `1.0`.
- `diagnostics.json`: completeness counters and ordered diagnostics.
- `summary.md`: raw/display counts and component representatives.
- `graph.graphml`: interoperable directed raw graph export.

The schema is documented formally in [`docs/graph-schema.json`](docs/graph-schema.json). Package nodes aggregate versions but retain versions and contexts; IDs do not contain versions. Dependency metrics exclude `produces-package`, while weak components include it so a mapped local producer is not presented as unrelated. A cycle member does not count itself in transitive counts.

Exit codes: `0` success (including a disclosed partial report unless strict completeness was requested), `1` fatal generation failure, `2` invalid command/arguments, `3` incomplete with `--fail-on-incomplete`, and `130` cancellation.

## Configuration

Pass `--config examples/dotnet-depgraph.config.json`. Schema `1.0` supports path/package filters, restore/filter defaults, global properties, seed, collapsed-producer default, ordered project rules (`pathGlob`, category, label, tag, color), package display aliases, and explicit package-to-project producer mappings. Repeatable CLI filters replace configured lists when supplied; scalar CLI arguments override config. See the [fictional example](examples/dotnet-depgraph.config.json).

## Fixtures, performance, and limitations

`fixtures/build-representative.sh` builds a private local package chain (`Feature → Storage → Serialization`) and restores a repository containing multi-target conditions, central package management, local producers, ambiguities, duplicate names, disconnected tools, and isolated projects. A committed lock file covers RID target extraction. Tests validate graph facts and report safety, not screenshots.

The viewer targets roughly 2,000 nodes and 10,000 edges. Overview edges use high-contrast colors, while a configurable number of the visually largest nodes remain labeled at every zoom level. Hovered, selected, and searched labels stay a readable screen-space size instead of shrinking with graph zoom. Filters and neighborhoods reduce render work. The core uses linear graph passes except exact per-node reachability counts, which trade memory for straightforward bounded behavior at this scale.

Known limitations: staleness is not guessed (use `always` when necessary); path glob `*` may cross `/` and is deliberately simpler than gitignore syntax; centrality is a stable logarithmic reach/degree measure rather than full betweenness; community detection is deterministic label propagation rather than Louvain; GraphML exports the raw view; no version-expanded graph mode is provided; viewer position persistence and SVG export are not implemented. See [`IMPLEMENTATION_STATUS.md`](IMPLEMENTATION_STATUS.md).
