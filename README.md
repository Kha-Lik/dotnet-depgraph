# dotnet-depgraph

`dotnet-depgraph` turns a .NET source tree into an offline, interactive architecture graph of projects, packages, and their transitive dependencies.

> [!NOTE]
> This project—including its source code, tests, and documentation—was fully generated using AI. It has been used and tested internally against a real-world .NET source tree containing approximately 200 projects, but it has not undergone an independent security or correctness audit. Review the implementation and validate its output before relying on it for critical architectural, security, or operational decisions.

It discovers C#, F#, and Visual Basic projects, evaluates their real MSBuild references, and reads resolved NuGet dependencies from `project.assets.json`. Disconnected applications and tools remain separate graph islands.

The tool analyzes project and package structure. It does not analyze types, namespaces, method calls, or compiled assemblies.

## Table of contents

- [Quick start](#quick-start)
- [Understanding the report](#understanding-the-report)
- [Using the viewer](#using-the-viewer)
- [Filtering the graph](#filtering-the-graph)
- [How scanning works](#how-scanning-works)
- [Community detection](#community-detection)
- [Configuration](#configuration)
- [Output files and schema](#output-files-and-schema)
- [Performance and limitations](#performance-and-limitations)
- [Development](#development)
- [Research basis](#research-basis)

## Quick start

### Prerequisites

- .NET 10 SDK
- A trusted .NET source tree to analyze

Node.js and a web server are not required. The browser libraries are bundled with every report.

### Build and install locally

```bash
dotnet restore DotNetDependencyGraph.slnx
dotnet build DotNetDependencyGraph.slnx --no-restore
dotnet pack src/DotNetDependencyGraph.Cli -c Release -o artifacts/packages
dotnet tool install --global dotnet-depgraph --add-source artifacts/packages
```

### Create your first report

```bash
dotnet-depgraph scan \
  --root /path/to/source \
  --output /tmp/dependency-report
```

Open `/tmp/dependency-report/index.html` directly in a modern browser. The report works offline as long as its generated files remain together.

By default, the scan:

- discovers all supported projects below `--root`;
- evaluates project references with MSBuild;
- restores only projects whose assets file is missing;
- includes every target framework;
- shows projects and a reachability-preserving, contracted package view; and
- detects architectural communities with deterministic defaults.

> [!WARNING]
> MSBuild evaluation and `dotnet restore` can execute repository-controlled logic. Analyze only source trees you trust.

### A more focused example

```bash
dotnet-depgraph scan \
  --root /repos/product \
  --output /tmp/product-graph \
  --include-package 'Company.*' \
  --exclude-package 'Company.Legacy.*' \
  --exclude-project 'Tools/*' \
  --collapse-local-packages \
  --filter-mode contract \
  --restore missing \
  --target-framework all \
  --jobs 4 \
  --seed 42
```

### Re-render without rescanning

Use the canonical `graph.json` to regenerate the browser report without discovery, MSBuild evaluation, or restore:

```bash
dotnet-depgraph render \
  --graph /path/to/dependency-report/graph.json \
  --output /tmp/dependency-report-rendered \
  --include-package 'Company.*' \
  --filter-mode contract \
  --collapse-local-packages
```

The embedded community analysis is reused when compatible. Passing any `--community-*` option recomputes it from the canonical nodes and edges without accessing the source tree.

Use `--force` only when replacing known report files in a nonempty output directory. Unrelated files are refused.

## Understanding the report

### Graph model

Arrows point from a consumer to its dependency:

```text
Application -> Library -> Package
```

The canonical graph retains projects and packages as separate identities. Each edge can carry one or more contexts with its owning project, assets path, target framework, runtime identifier, requested range, resolved version, directness, and observation count.

### Views

| View | Behavior |
| --- | --- |
| `raw` | Shows the canonical projects, packages, and dependency edges. |
| `strict` | Removes packages hidden by filters and removes their incident edges. |
| `contract` | Traverses hidden dependencies and adds a dashed path to the first retained node reached. |

Contracted edges record the minimum hidden hop count, bounded path samples and counts, and contributing target contexts. They never replace raw edges in `graph.json`.

### Local project and package identities

Packable projects are matched case-insensitively to their evaluated `PackageId`:

- one producer creates a dotted `produces-package` edge;
- multiple possible producers create a diagnostic; and
- `--collapse-local-packages` makes the merged projection the viewer default.

The viewer can switch between collapsed and separate identities. The canonical graph always retains the project, package, resolved version, historical metadata, and producer edge.

## Using the viewer

### Explore view

#### Find and inspect nodes

- Search by a partial, case-insensitive name or ID.
- Select a node to see metadata, dependencies, dependents, communities, architectural role, and runnable impact.
- Ctrl-click, or Cmd-click on macOS, to select multiple nodes.
- Select two nodes and choose **Explain path** to show one deterministic directed dependency path.
- Isolate neighborhoods one to three hops away from the selection.

#### Filter and navigate

Narrow the graph by node or edge kind, component, effective community, target framework, runtime identifier, version skew, minimum runnable-dependent count, or raw/strict/contracted view. Components can be navigated independently; **Fit all** returns to the overview.

#### Communities and presentation

The community legend follows the selected coarse, standard, or fine granularity. It supports hover highlighting, click-to-isolate, rename, recolor, create from selection, multi-node reassignment, effective merge, restore, import, export, and effective-mapping download.

Community overrides change presentation only. They do not change edges, reachability, strongly connected components, or automatic CPM results. Browser persistence uses `dotnet-depgraph.communities.v1:<graph fingerprint>`; exported overrides use schema version 1. Use `--community-overrides` to seed a report after fingerprint validation.

The Physics / Layout panel controls repulsion, link distance and strength, collision spacing, drag threshold, component gravity, and weaker community-centroid attraction. Attraction remains inside each disconnected component and never creates an edge. Settings use `dotnet-depgraph.physics.v3`, with a one-time v2 migration fallback.

#### Labels, colors, and exports

- Node size defaults to `clamp(18 + 6 × log2(transitiveDependents + inDegree + 1), 18, 52)` pixels.
- Size can instead represent runnable-dependent count.
- Color can represent effective community or node kind.
- Projects use distinct shapes; version skew adds a red ring.
- The 12 visually largest nodes remain labeled by default; the Labels panel accepts 0–50.
- More labels appear while zooming; hovered and selected labels stay readable at a stable screen size.
- Overview edges use contrasting colors; focused edges show arrowheads.
- Contracted paths are dashed and producer mappings are dotted.
- **PNG** exports the Explore canvas; **Displayed JSON** exports the active filtered projection.

The report uses bundled Cytoscape.js 3.34.2, d3-force 3.0.0, and Coloris 0.25.0. Its live simulation preserves dependency neighborhoods, avoids node overlap, and keeps disconnected components as separate islands. Coloris provides the offline group-creation color picker with an explicit **OK** action.

### Manual layout view

Use Manual layout to organize the active Explore projection into explicit feature regions without changing dependency facts.

#### Create a board

The first visit can preview either current effective communities as separate regions or every visible node in one Unassigned region. Accept the preview to create the board.

Explore and Manual keep independent node positions, selections, viewports, and paused states when switching tabs.

#### Organize nodes and regions

- Create rectangular or circular regions.
- Drag nodes within their bounds.
- Drag whole regions by the body or header.
- Resize and repack a region.
- Rename, recolor, reshape, merge, delete, fit, or automatically arrange groups. Arrangement treats each supergroup as one unit and preserves the relative positions of its child groups.
- Ctrl/Cmd-click group names or headers to select several groups, then wrap them in a named, colored supergroup. Drag its header to move all child groups together, or dissolve the wrapper without deleting them.
- Move multiple nodes with destination preview and automatic target-region growth.
- Right-click a node to create a group from the current selection in an in-place dialog, move it quickly, or remove it to Unassigned.
- Pin nodes or relax one selected group.

The local layout visibly updates nodes inside region bounds. It can run globally, pause, resume, or apply to one group.

#### Create a supergroup

1. Ctrl/Cmd-click group names in the left **Groups** sidebar or group headers on the canvas to select at least two groups.
2. In the **Supergroups** section of the left sidebar, enter a name and optionally choose a color.
3. Click **Create supergroup**.

A group can belong to only one supergroup. Drag the supergroup header to move all its child groups together. Use the pin control on a child group's header to preserve its relative position, then use the supergroup header's arrange control to grid-pack the remaining child groups around pinned groups. Right-click a group header to remove it from its current supergroup or move it directly into another existing supergroup. The other supergroup header controls can collapse the entire supergroup or hide and highlight dependencies crossing its boundary. Use **Dissolve** to remove the wrapper without deleting or merging the child groups.

#### Reset a board

- **Start over from Explore** rebuilds the board from the current Explore groups.
- **Reset to Unassigned** rebuilds it with every node in one Unassigned group.

Both actions replace the current manual board after confirmation. Download the existing layout first if you may need it again.

#### Control dependency visibility

- Select a node to emphasize its dependency edges.
- Hide every edge incident to selected nodes and reveal those connections temporarily.
- Collapse a group or supergroup while keeping its header and external connections available.
- Hide or highlight dependencies that cross a group or supergroup boundary.

These controls do not rewrite canonical edges, automatic communities, GraphML, reachability, or dependency metrics.

#### Save and restore work

All persistent edits participate in session undo and redo. Boards autosave in the browser and can be downloaded or imported as versioned `manual-layout.json` files. Supergroups, collapsed-region settings, and cross-region-edge settings are included.

Imported layouts must match both the graph topology and captured display projection. The viewer reports whether browser persistence succeeded; download the layout when durable or portable storage is required.

## Filtering the graph

### Package filters

Use repeatable `--include-package` and `--exclude-package` options. Matching is case-insensitive, and exclusions take precedence.

### Project filters

Use repeatable `--include-project` and `--exclude-project` options to hide project nodes without preventing evaluation. Produced packages remain available, and contract mode can preserve reachability through hidden projects.

Patterns match normalized paths relative to `--root`. For `--root /repo/Renovation`, use `--exclude-project 'Tools/*'`, not `Renovation/Tools/*`.

### Discovery path filters

Use `--include-path` and `--exclude-path` to control which projects are discovered at all. Prefer project filters when local producer information must remain available.

### Glob syntax

- `*` matches any number of characters and may cross `/`.
- `?` matches one character.

## How scanning works

### Discovery

Discovery is deterministic and ignores `.git`, `.svn`, `.hg`, `bin`, `obj`, and `node_modules`. It does not follow directory symlinks or reparse points. Inaccessible paths become diagnostics. Duplicate filenames are safe because project IDs include the complete root-relative path.

### MSBuild evaluation

Literal project and package reference elements are insufficient because imports, conditions, multi-targeting, and central package management can change their effective values.

The tool uses `dotnet msbuild -getProperty/-getItem`, including framework-specific evaluation for multi-target project references. This honors MSBuild behavior without compiling. Repeatable `--property Name=Value` arguments apply to evaluation and restore.

### NuGet assets and restore

Transitive packages come from `project.assets.json` through NuGet's `NuGet.ProjectModel` lock-file API. Each target framework and runtime identifier is resolved independently before identical logical edges are aggregated.

Missing or invalid assets produce diagnostics and mark the report incomplete. They are never treated as an empty dependency set.

| Restore mode | Behavior |
| --- | --- |
| `never` | Reads existing assets only and never writes restore output to the source tree. |
| `missing` | Default. Restores only when an evaluated assets path is absent. |
| `always` | Restores every discovered project. |

Restore concurrency is bounded by `--jobs`. Per-project failures do not stop remaining restores. Restore may update normal `obj` files and NuGet caches. Use `--fail-on-incomplete` to return exit code 3 instead of 0 for a partial report.

### Frameworks and runtimes

- `--target-framework` is repeatable; `all` is the default.
- `--runtime` is repeatable and selects RID-specific targets.

## Community detection

Communities are dependency-topology heuristics, not confirmed business domains or design violations.

### Typical controls

```bash
dotnet-depgraph scan \
  --root /repos/product \
  --output /tmp/product-graph \
  --community-resolution 0.5 \
  --community-seed 42 \
  --community-trials 10 \
  --community-levels 3 \
  --community-target-size 20 \
  --community-min-size 2
```

| Option | Purpose |
| --- | --- |
| `--community-resolution` | CPM resolution. Higher values generally produce smaller groups. |
| `--community-seed` | Makes seeded trials deterministic. |
| `--community-trials` | Sets the trials evaluated at each resolution. |
| `--community-levels` | Sets the maximum precomputed hierarchy depth. |
| `--community-target-size` | Provides a soft size hint, not a hard partition limit. |
| `--community-min-size` | Provides a soft minimum useful-split hint. |
| `--community-contracted-weight` | Weights paths through excluded packages; default `0.25`. |

`--verbosity normal` reports timed stages. `detailed` also reports per-project evaluation and assets progress; `quiet` suppresses progress messages.

### Default scope

Detection uses a source-owned projection of the complete canonical graph, independent of display filters. It includes projects below the root, packages with exactly one local producer, and packages matched by an explicit internal-package pattern.

System packages, third-party packages, unresolved nodes, and unknown external dependencies remain in the canonical graph but are excluded from detection, naming, representatives, and community counts by default.

Broader analysis must be enabled explicitly:

- `--community-include-third-party`
- `--community-include-system-packages`
- `--community-internal-package <glob>`
- `--community-include-unmapped-internal-packages`

The effective scope and all included, excluded, collapsed-producer, and contracted-edge counts appear in `summary.md` and the viewer.

### Detection pipeline

The managed implementation uses Leiden optimization with the Constant Potts Model (CPM):

```text
Σc(internalWeight(c) − gamma × size(c) × (size(c)−1)/2)
```

The default pipeline:

1. Contracts compatible dependency paths through excluded packages at weight `0.25`.
2. Collapses each unambiguous local project/package producer pair exactly once.
3. Excludes tests from production optimization.
4. Runs seeded Leiden trials across a bounded resolution profile around `0.5`.
5. Records Adjusted Rand stability and connectedness.
6. Builds strict nested partitions by rerunning Leiden on sufficiently large parent subgraphs.
7. Expands producer identities and assigns tests using direct and bounded transitive production evidence.

Contraction follows dependency edges only, requires compatible owner, TFM, and RID contexts, and never traverses producer mappings.

| Edge | Default weight |
| --- | ---: |
| Direct project reference | 3.0 |
| Package reference | 2.0 |
| Package dependency | 1.0 |

Observation and context multiplicity do not multiply structural weights. Cohesive topology is not split merely to satisfy a target count.

### Names and derived analysis

Automatic names and representatives prefer non-test local projects, then locally produced package identities. Representatives are ranked by within-community connectivity, deduplicated by display label, and diversified by name tokens. Exact test-assignment ties and missing evidence become informational diagnostics.

Names and paths label detected groups but do not force membership. Use community overrides or Manual layout when dependency shape does not reflect the intended semantic boundary.

Each node records runnable impact, bridge evidence, articulation status, betweenness, neighboring-community count, and a neutral role. The analysis also records runnable-to-dependency shortest paths, directed cross-community counts, and quotient-graph cycles. No embeddings, AI calls, or network requests are used.

## Configuration

```bash
dotnet-depgraph scan \
  --root /repos/product \
  --output /tmp/product-graph \
  --config examples/dotnet-depgraph.config.json
```

See [examples/dotnet-depgraph.config.json](examples/dotnet-depgraph.config.json) for discovery and display filters, MSBuild properties, community settings, project rules, package aliases, and known package producers.

Configuration schema `1.0` supports:

- discovery, package, and project filters;
- restore and collapse defaults;
- MSBuild properties;
- community scope, weights, and optimizer settings;
- project labels, categories, tags, and color hints;
- package aliases; and
- explicit package-to-project producer mappings.

Repeatable CLI filters replace configured lists when supplied. Scalar community arguments override configured values.

## Output files and schema

| File | Purpose |
| --- | --- |
| `index.html` | Offline entry point with safely escaped embedded graph data. |
| `graph.json` | Deterministic canonical graph and `communityAnalysis`; schema `2.0`. |
| `diagnostics.json` | Completeness counters, projection statistics, and ordered diagnostics. |
| `summary.md` | Generated quick guide, counts, settings, and community representatives. |
| `graph.graphml` | Interoperable directed export of the raw graph. |

The formal schema is [docs/graph-schema.json](docs/graph-schema.json). Unsupported schema versions fail clearly. Canonical nodes and edges remain raw and unfiltered.

### Exit codes

| Code | Meaning |
| ---: | --- |
| `0` | Success, including a disclosed partial report unless strict completeness was requested. |
| `1` | Fatal generation failure. |
| `2` | Invalid command or arguments. |
| `3` | Incomplete result with `--fail-on-incomplete`. |
| `130` | Cancellation. |

## Performance and limitations

The viewer targets approximately 2,000 nodes and 10,000 edges. Filters and neighborhood isolation reduce rendering work. Most core graph passes are linear; exact per-node reachability counts trade memory for straightforward bounded behavior at this scale.

Known limitations:

- Asset staleness is not inferred. Use `--restore always` when freshness is required.
- Path glob `*` may cross `/`.
- Communities cannot recover semantic distinctions absent from dependency topology.
- Community target size is not an optimizer constraint.
- Path explanation returns one deterministic path rather than enumerating cyclic alternatives.
- GraphML always exports the raw graph.
- Version-expanded graph mode is not available.
- Manual-layout PNG composition and SVG export are not implemented.

## Development

### Run the test suite

```bash
dotnet restore DotNetDependencyGraph.slnx
dotnet build DotNetDependencyGraph.slnx --no-restore
pwsh tests/DotNetDependencyGraph.IntegrationTests/bin/Debug/net10.0/playwright.ps1 install chromium
dotnet test DotNetDependencyGraph.slnx --no-build
```

The Playwright command installs Chromium for integration tests; it is not required to view reports.

### Fixtures

`fixtures/build-representative.sh` builds a private package chain:

```text
Feature -> Storage -> Serialization
```

The representative repository covers multi-target conditions, central package management, local and ambiguous producers, duplicate names, disconnected tools, isolated projects, and RID extraction from a committed lock file.

Integration tests generate a real offline report and exercise pointer handling, independent selection, group and connection controls, editing, persistence, and layout behavior in Chromium. They do not rely on screenshot comparisons.

## Research basis

- Traag, Waltman, and van Eck, [“From Louvain to Leiden: guaranteeing well-connected communities”](https://doi.org/10.1038/s41598-019-41695-z) (2019).
- Traag, Van Dooren, and Nesterov, [“Narrow scope for resolution-limit-free community detection”](https://doi.org/10.1103/PhysRevE.84.016114) (2011).
- Traag, Krings, and Van Dooren, [“Significant scales in community structure”](https://doi.org/10.1038/srep02930) (2013).
- Fortunato and Barthélemy, [“Resolution limit in community detection”](https://doi.org/10.1073/pnas.0605965104) (2007).
- Rosvall and Bergstrom, [“Multilevel compression of random walks in networks reveals hierarchical organization”](https://doi.org/10.1371/journal.pone.0018209) (2011), as an alternative hierarchy design.

These papers motivate the algorithms and tradeoffs; they do not validate the business meaning of this tool's output.
