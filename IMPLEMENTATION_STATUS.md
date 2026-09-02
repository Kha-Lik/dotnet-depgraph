# Implementation status

## Acceptance scenarios

- **A — full transitive chain: pass.** Local fixture output contains `App → Company.Feature → Company.Storage → Company.Serialization` with package topology derived from `project.assets.json`.
- **B — hidden external bridge: pass.** Unit tests prove strict removal and one described contracted edge; raw input remains unchanged.
- **C — disconnected tools: pass.** Representative fixtures contain ToolA, ToolB, and the main component; component navigation is generated without fake edges.
- **D — local package producer: pass.** Raw separate identities, inferred/explicit producer edge, collapsed viewer projection, and resolved package versions coexist.
- **E — incomplete restore data: pass.** The sample report includes valid facts plus missing/malformed diagnostics and an incomplete banner; exit behavior is controlled by `--fail-on-incomplete`.
- **F — useful overview: pass with documented limitation.** Cytoscape CoSE, seeded initial positions, communities, component navigation, search, selection, filters, and 1–3 hop isolation are implemented. Browser automation was unavailable in the development environment; syntax, embedded data, controls, escaping, and assets were smoke-tested without screenshots.

## Deliberate limitations

- Restore staleness is not inferred; modes are `never`, `missing`, and `always`.
- Framework-specific package directness comes from `PackageSpec`; project references are unioned across evaluated target frameworks and currently have an empty aggregate TFM context.
- Communities use deterministic label propagation; centrality is a stable degree/reach score.
- The force layout handles disconnected components with component spacing, but there is no separately persisted per-component bounding-box packer.
- No version-expanded node mode, position persistence, SVG export, or stale timestamp heuristic.
- Project classification overrides are path-based in schema 1.0; evaluated-metadata predicates beyond inferred category are not exposed yet.
- Tests use a committed RID lock file because a fully offline RID restore requires platform runtime packs that are not shipped with the fixture feed.
