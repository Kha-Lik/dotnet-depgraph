# .NET dependency graph report

## Viewing the report

Open `index.html` in a modern browser. The report is self-contained and works offline; keep its generated files together in the same directory.

## Viewer quick guide

- **Search** finds nodes by partial name or ID. Select a node to inspect metadata, detected/effective community assignments, dependency direction, runnable impact, and architectural-role evidence.
- **View** switches between the canonical raw graph, strict filtering, and reachability-preserving contracted filtering. **Collapse local packages** visually combines unambiguous producer projects with their package identities.
- **Community granularity** switches between coarse, standard, and fine detected partitions without changing dependency edges. **Color by** can show effective communities or node kinds.
- **Size by** scales nodes using all transitive dependents or runnable dependents. The remaining filters narrow by neighborhood, kind, edge kind, component, community, TFM, RID, version skew, or minimum runnable-dependent count.
- The **Communities** legend supports navigation and manual rename, recolor, create, reassign, merge, restore, import, and export operations. Manual changes affect presentation only; they do not rewrite dependencies or automatic analysis.
- Select the first node, then **Ctrl-click** the second (**Cmd-click** on macOS), and use **Explain path** to show a directed dependency path. **Fit all** restores the overview, while the Physics/Layout and Labels panels tune presentation.
- **PNG** exports the current canvas. **Displayed JSON** exports the currently selected and filtered projection; `graph.json` remains the canonical machine-readable graph.

Communities and architectural roles are dependency-topology heuristics, not confirmed business domains or design violations.

## Generated analysis

{{COMPLETENESS_NOTICE}}- Raw graph: {{RAW_NODE_COUNT}} nodes, {{RAW_EDGE_COUNT}} edges
- Display graph: {{DISPLAY_NODE_COUNT}} nodes, {{DISPLAY_EDGE_COUNT}} edges ({{CONTRACTED_EDGE_COUNT}} contracted)
- Projects discovered/evaluated/with assets: {{DISCOVERED_PROJECT_COUNT}}/{{EVALUATED_PROJECT_COUNT}}/{{VALID_ASSETS_PROJECT_COUNT}}
- Components: {{COMPONENT_COUNT}}; isolated display nodes: {{ISOLATED_NODE_COUNT}}
- Warnings/errors: {{WARNING_COUNT}}/{{ERROR_COUNT}}
- Frameworks: {{FRAMEWORKS}}
- RIDs: {{RIDS}}
- Communities: Leiden/CPM; standard resolution {{STANDARD_RESOLUTION}}; {{STANDARD_COMMUNITY_COUNT}} strict-hierarchy groups; trial stability {{STANDARD_STABILITY}}
- Community scope: {{COMMUNITY_SCOPE}}
- Detection vertices/nodes: {{DETECTION_VERTEX_COUNT}}/{{DETECTION_NODE_COUNT}}
- Local projects/produced packages: {{LOCAL_PROJECT_COUNT}}/{{LOCAL_PRODUCED_PACKAGE_COUNT}}; producer pairs collapsed: {{COLLAPSED_PRODUCER_PAIR_COUNT}}
- Excluded tests/system packages/third-party packages/unresolved external nodes: {{EXCLUDED_TEST_PROJECT_COUNT}}/{{EXCLUDED_SYSTEM_PACKAGE_COUNT}}/{{EXCLUDED_THIRD_PARTY_PACKAGE_COUNT}}/{{EXCLUDED_UNRESOLVED_EXTERNAL_COUNT}}
- Included unmapped internal/system/third-party packages: {{INCLUDED_UNMAPPED_INTERNAL_PACKAGE_COUNT}}/{{INCLUDED_SYSTEM_PACKAGE_COUNT}}/{{INCLUDED_THIRD_PARTY_PACKAGE_COUNT}}
- Contracted detection edges: {{DETECTION_CONTRACTED_EDGE_COUNT}} at weight {{CONTRACTED_PATH_WEIGHT}}
- Community projection weights: project/package/dependency {{PROJECT_REFERENCE_WEIGHT}}/{{PACKAGE_REFERENCE_WEIGHT}}/{{PACKAGE_DEPENDENCY_WEIGHT}}; tests excluded: {{TESTS_EXCLUDED}}; producer pairs collapsed: {{PRODUCER_PAIRS_COLLAPSED}}

## Detected communities

These are the automatically detected communities at standard granularity, ordered by size. Names are inferred from member-node labels; use the stable key when correlating a community with `graph.json` or the viewer.

| Community | Stable key | Members | Detection vertices | Expanded producer packages | Projects | Packages | Tests | Runnable | Representative nodes |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
{{COMMUNITY_ROWS}}

## Components

{{COMPONENT_ROWS}}
