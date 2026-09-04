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

`render` accepts graph schema `2.0`, supports the scan-time community controls, `--community-overrides`, `--seed`, `--force`, package and project filters, `--filter-mode`, and `--collapse-local-packages`, and refuses unsupported schema versions or unrelated files in a nonempty output directory. Explicit community settings recompute the analysis from canonical nodes and edges without touching the source tree.

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
  --exclude-project 'Tools/*' --collapse-local-packages \
  --filter-mode contract --restore missing --target-framework all --jobs 4 --seed 42
```

`strict` removes hidden packages and incident edges. `contract` traverses hidden dependency nodes and adds a dashed `contracted-path` only to the first retained node reached. It records minimum hidden hops, bounded path samples/counts, and contributing contexts. Raw facts are never modified. The viewer can switch among raw, strict, and contracted data.

Use repeatable `--include-project` and `--exclude-project` options to filter project nodes without preventing their evaluation. These patterns match normalized, root-relative project paths. For `--root /repo/Renovation`, use `--exclude-project 'Tools/*'`, not `Renovation/Tools/*`. Package nodes produced by hidden projects remain available, and contract mode preserves dependency reachability through those projects.

Packable local projects are matched case-insensitively to their evaluated `PackageId`. A unique match creates a subordinate `produces-package` edge. Ambiguous producers generate diagnostics. Separate identity is the raw truth; “collapse local packages” visually projects references onto the producer without replacing the resolved package version or historical dependency metadata.

Pass `--collapse-local-packages` to make that merged producer/package projection the generated viewer's default. The viewer checkbox can still switch back to separate nodes, and the canonical `graph.json` always retains both identities and their producer edge.

## Targets, discovery, and evaluation

Use repeatable `--target-framework` and `--runtime` selections. `all` is the default TFM selection. Edge contexts retain owner, assets path, TFM, RID, requested range, resolved version, directness, and observation count.

Discovery is deterministic and excludes `.git`, `.svn`, `.hg`, `bin`, `obj`, and `node_modules`. It does not follow directory symlinks/reparse points, preventing cycles. Inaccessible paths become diagnostics. `--include-path` and `--exclude-path` accept normalized root-relative globs and control which projects are discovered at all; use project filters instead when produced packages must be retained. Duplicate filenames are safe because project IDs contain the complete root-relative path.

MSBuild evaluation uses `dotnet msbuild -getProperty/-getItem`, including a framework-specific pass for multi-target project references. This avoids compilation while honoring imports and conditions. `--property Name=Value` is repeatable and also passed safely to restore.

> MSBuild evaluation and restore can execute repository-controlled logic. Analyze only trusted source trees.

## Hierarchical communities and architectural analysis

The .NET tool detects communities with a deterministic managed implementation of Leiden optimizing the Constant Potts Model (CPM). CPM quality is `Σc(internalWeight(c) − gamma × size(c) × (size(c)−1)/2)`. Higher `gamma` generally produces smaller groups. The defaults evaluate a bounded resolution profile around `0.5`, run 10 seeded trials at each resolution, record Adjusted Rand stability and connectedness, and deliberately build strict nested partitions by rerunning Leiden on sufficiently large induced parent subgraphs. `--community-target-size` and `--community-min-size` are soft hints; cohesive topology is never chopped merely to hit a count.

`--verbosity normal` reports timed scan, projection, resolution, hierarchy, derived-analysis, and report-writing stages. `--verbosity detailed` additionally reports evaluation and assets progress for every discovered project; `quiet` suppresses progress messages.

```bash
dotnet-depgraph scan --root /repos/product --output /tmp/product-graph \
  --community-resolution 0.5 --community-seed 42 \
  --community-trials 10 --community-levels 3 \
  --community-target-size 20 --community-min-size 2
```

Detection uses an undirected positive-weight projection of the complete canonical graph, independent of display filters. Defaults weight project references `3.0`, direct project-to-package references `2.0`, and package dependencies `1.0`. Observation/TFM/RID context multiplicity does not multiply structural weight. Contracted and producer edges have zero detection weight. An unambiguous eligible project/package producer pair is temporarily collapsed, then the stored path is projected back to both canonical identities. Weak components are processed independently. Direction remains intact in the canonical graph for paths, blast radius, SCCs, coupling, and cross-community summaries.

Test projects are excluded from production optimization and assigned afterward using direct and bounded transitive production dependency evidence. Exact ties and missing evidence are surfaced as informational diagnostics. Automatic names use transparent distinctive name/path tokens; low-confidence names use several representatives instead of claiming a business domain. Keys and high-separation fill/border color pairs are deterministic; the paired visual encoding avoids collapsing dozens of communities into a small repeating palette.

Each node records dependency-topology runnable impact, bridge evidence, articulation status, betweenness, neighboring-community count, and a neutral role. The embedded analysis also contains runnable-to-dependency shortest paths, directed cross-community counts, and quotient-graph cycles. These are topology heuristics, not confirmed runtime impact or design violations.

> Communities represent dependency-topology structure, not guaranteed business domains.

Names and paths help label detected groups but do not force membership. Similar dependency shapes may be semantically different; use the manual override layer for those boundaries. The intended extension point is a future explicitly enabled attributed/multilayer projection—this phase makes no embeddings, AI calls, or network requests.

## Viewer

The report uses bundled Cytoscape.js 3.34.2 with d3-force 3.0.0 (licenses included in every report). A live many-body simulation repels every node, edge springs retain dependency neighborhoods, rendered-size-aware collision prevents overlap, and weak per-component centering keeps disconnected components as separate islands. Initial positions are seeded; the simulation visibly settles, cools to idle, and reheats after dragging or physics changes.

Search is partial and case-insensitive. Selection shows metadata, detected/effective assignment, runnable blast radius, role evidence, and immediate dependencies/dependents. Controls filter node/edge kind, component, effective community, TFM, RID, and version skew; isolate one-to-three-hop neighborhoods; switch raw/strict/contract, coarse/standard/fine community granularity, community/node-kind color, and separate/collapsed producer views; navigate components; explain a shortest directed path between two selected nodes; reset/fit; and export displayed JSON or a PNG.

The collapsible community legend is synchronized with granularity and color mode. Hover highlights, click isolates, and Ctrl/Cmd-click multi-selects. It supports deterministic automatic names/colors plus rename, recolor, create-from-selection, multi-node reassign, effective merge, per-node/community restore, reset-all, import/export, and effective-mapping download. Overrides never change edges, reachability, SCCs, or automatic CPM results. They persist immediately under `dotnet-depgraph.communities.v1:<graph fingerprint>` and export as schema version 1; `--community-overrides` can seed a rendered report after strict fingerprint validation.

The collapsible Physics / Layout panel controls repulsion, edge-spring distance and strength, collision spacing, drag threshold, weak component gravity, and weaker community-centroid attraction. Attraction is scoped within each disconnected component and never creates an edge. Drag threshold is measured in screen pixels, preventing clicks and small pointer jitter from reheating the graph. Settings moved to `dotnet-depgraph.physics.v3`; the viewer reads v2 once as a migration fallback. Community override storage contains only styles and assignment IDs, not graph topology.

Node diameter defaults to `clamp(18 + 6 × log2(transitiveDependents + inDegree + 1), 18, 52)` pixels, preserving relative importance without allowing hubs to cover clusters; the viewer can instead size by runnable-dependent count and filter by its minimum. Color indicates topology-derived community. Projects have distinct shapes; version skew has a modest red ring. The 12 visually largest nodes remain labeled by default at every zoom level, configurable from 0–50 in the Labels panel; additional labels appear progressively while zooming. Hovered and selected labels use full, untruncated text at a stable screen-space size. Overview edges use contrasting colors and focused edges reveal arrowheads. Contracted paths are dashed and producer mappings dotted.

## Output and schema

- `index.html`: offline entry point with safely JSON-escaped embedded data.
- `graph.json`: deterministic canonical raw graph plus derived `communityAnalysis`, schema version `2.0`.
- `diagnostics.json`: completeness counters and ordered diagnostics.
- `summary.md`: a bundled quick-start/viewer guide enriched with generated raw/display counts, completeness, community settings, and component representatives.
- `graph.graphml`: interoperable directed raw graph export.

The schema is documented formally in [`docs/graph-schema.json`](docs/graph-schema.json). `communityAnalysis` records its version, implementation, settings, projection rules, graph fingerprint, resolution profile, strict hierarchy, stable records, automatic node paths, granularity mappings, cross-community dependencies, quotient cycles, runnable paths, and diagnostics. Canonical nodes/edges remain raw and unfiltered. Unsupported schema versions fail clearly.

Exit codes: `0` success (including a disclosed partial report unless strict completeness was requested), `1` fatal generation failure, `2` invalid command/arguments, `3` incomplete with `--fail-on-incomplete`, and `130` cancellation.

## Configuration

Pass `--config examples/dotnet-depgraph.config.json`. Configuration schema `1.0` supports a `communities` section in addition to discovery/display filters and existing rules. Repeatable CLI filters replace configured lists when supplied; scalar community CLI arguments override config. See the [fictional example](examples/dotnet-depgraph.config.json).

## Fixtures, performance, and limitations

`fixtures/build-representative.sh` builds a private local package chain (`Feature → Storage → Serialization`) and restores a repository containing multi-target conditions, central package management, local producers, ambiguities, duplicate names, disconnected tools, and isolated projects. A committed lock file covers RID target extraction. Tests validate graph facts and report safety, not screenshots.

The viewer targets roughly 2,000 nodes and 10,000 edges. Overview edges use high-contrast colors, while a configurable number of the visually largest nodes remain labeled at every zoom level. Hovered, selected, and searched labels stay a readable screen-space size instead of shrinking with graph zoom. Filters and neighborhoods reduce render work. The core uses linear graph passes except exact per-node reachability counts, which trade memory for straightforward bounded behavior at this scale.

Known limitations: staleness is not guessed (use `always` when necessary); path glob `*` may cross `/`; communities cannot recover semantic distinctions absent from topology; target size is not an optimizer constraint; shortest active-view explanations return one deterministic path rather than enumerating cyclic alternatives; GraphML exports the raw view; no version-expanded graph mode is provided; viewer position persistence and SVG export are not implemented. See [`IMPLEMENTATION_STATUS.md`](IMPLEMENTATION_STATUS.md).

## Research basis

- Traag, Waltman, and van Eck, [“From Louvain to Leiden: guaranteeing well-connected communities”](https://doi.org/10.1038/s41598-019-41695-z) (2019).
- Traag, Van Dooren, and Nesterov, [“Narrow scope for resolution-limit-free community detection”](https://doi.org/10.1103/PhysRevE.84.016114) (2011).
- Traag, Krings, and Van Dooren, [“Significant scales in community structure”](https://doi.org/10.1038/srep02930) (2013).
- Fortunato and Barthélemy, [“Resolution limit in community detection”](https://doi.org/10.1073/pnas.0605965104) (2007).
- Rosvall and Bergstrom, [“Multilevel compression of random walks in networks reveals hierarchical organization”](https://doi.org/10.1371/journal.pone.0018209) (2011), as an alternative hierarchy design.

These papers motivate algorithms and tradeoffs; they do not validate the business meaning of this tool's output.
