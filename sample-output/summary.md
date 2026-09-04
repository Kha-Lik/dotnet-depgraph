# .NET dependency graph report

## Viewing the report

Open `index.html` in a modern browser. The report is self-contained and works offline; keep its generated files together in the same directory.

## Viewer quick guide

- **Search** finds nodes by partial name or ID. Select a node to inspect metadata, detected/effective community assignments, dependency direction, runnable impact, and architectural-role evidence.
- **View** switches between the canonical raw graph, strict filtering, and reachability-preserving contracted filtering. **Collapse local packages** visually combines unambiguous producer projects with their package identities.
- **Community granularity** switches between coarse, standard, and fine detected partitions without changing dependency edges. **Color by** can show effective communities or node kinds.
- **Size by** scales nodes using all transitive dependents or runnable dependents. The remaining filters narrow by neighborhood, kind, edge kind, component, community, TFM, RID, version skew, or minimum runnable-dependent count.
- The **Communities** legend supports navigation and manual rename, recolor, create, reassign, merge, restore, import, and export operations. Manual changes affect presentation only; they do not rewrite dependencies or automatic analysis.
- Select two nodes and use **Explain path** to show a directed dependency path. **Fit all** restores the overview, while the Physics/Layout and Labels panels tune presentation.
- **PNG** exports the current canvas. **Displayed JSON** exports the currently selected and filtered projection; `graph.json` remains the canonical machine-readable graph.

Communities and architectural roles are dependency-topology heuristics, not confirmed business domains or design violations.

## Generated analysis

- Raw graph: 39 nodes, 51 edges
- Display graph: 39 nodes, 51 edges (0 contracted)
- Projects discovered/evaluated/with assets: 4/4/4
- Components: 1; isolated display nodes: 2
- Warnings/errors: 0/0
- Frameworks: net10.0
- RIDs: none
- Communities: Leiden/CPM; standard resolution 0.5; 1 strict-hierarchy groups; trial stability 1
- Community scope: source-owned
- Detection vertices/nodes: 2/4
- Local projects/produced packages: 4/2; producer pairs collapsed: 2
- Excluded tests/system packages/third-party packages/unresolved external nodes: 2/4/29/0
- Included unmapped internal/system/third-party packages: 0/0/0
- Contracted detection edges: 0 at weight 0.25
- Community projection weights: project/package/dependency 3/2/1; tests excluded: True; producer pairs collapsed: True

## Detected communities

These are the automatically detected communities at standard granularity, ordered by size. Names are inferred from member-node labels; use the stable key when correlating a community with `graph.json` or the viewer.

| Community | Stable key | Members | Detection vertices | Expanded producer packages | Projects | Packages | Tests | Runnable | Representative nodes |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| Cli | `community:v1:8cf6393f4dc0efb1` | 6 | 2 | 2 | 4 | 2 | 2 | 1 | DotNetDependencyGraph.Cli |

## Components

- Component 0: 39 nodes; representative: xunit.v3.core.mtp-v1, NuGet.Packaging, Microsoft.Testing.Platform
