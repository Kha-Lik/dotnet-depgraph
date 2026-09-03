# Phase 2 rendering status

## Implemented

- d3-force 3.0.0 many-body repulsion, link springs, rendered-radius collision, per-component centering, finite alpha cooling, and drag/slider reheating.
- Deterministic seeded starting positions and separated component anchors; `Fit all` and component navigation retain access to every island.
- Progressive labels for overview importance and zoom, configurable persistent labels for the largest nodes, and full screen-space labels for hover and selection.
- Bounded logarithmic node diameters (18–52 px), modest borders, selection halo, contrasting overview edges, focused direction arrows, and preserved edge-kind styles.
- Persisted, bounded controls for repulsion, link distance, link strength, node spacing, drag threshold, and gravity, plus pause/resume, rerun, reset defaults, and fit.
- Render-only `render --graph ... --output ...` flow with schema validation and no scanner/restore access.
- Locally bundled Cytoscape and d3 assets with combined notices; generated reports contain no CDN references.

## Automated evidence

- Node size is tested for monotonicity, bounds, and determinism.
- Collision radius is tested to include rendered radius and configurable padding.
- Overview-label policy is tested to remain selective and zoom-aware.
- Physics defaults and versioned local-storage key are tested.
- Report generation tests cover local scripts, controls, escaping, and absence of remote script sources.
- The render command is tested against a nonexistent source root and an unsupported schema version.
- Existing graph/filter/TFM extraction tests remain in the suite.

## Manual/browser evidence

- Sanitized report (20 raw nodes/10 raw edges; 17/7 displayed): nodes appeared in 81 ms and settled in 3.1 s with zero overlaps; the 12 visually largest nodes remain labeled at Fit all by default.
- Local 920-node/7,883-edge reference report: nodes appeared in 1.6 s and settled in 15.1 s with zero overlaps; the 12 visually largest nodes remain labeled at Fit all by default in headless Chrome.
- Coarse headless-Chrome process-tree RSS was 2.44 GiB for the 136 MiB reference input versus 1.95 GiB for the sample/browser baseline. Canvas edges retain only rendering fields; canonical edge contexts remain in the single embedded payload and are restored on JSON export.
- A synthetic graph with the same node/edge counts and reference degree distribution settled in 175 force ticks with zero overlaps; the same machine-only check found zero overlaps for the reference topology.
- Hover/selection label disclosure without truncation, zoom-independent screen sizing, persisted label-count control, click-versus-drag threshold handling, drag reheat, physics slider reheat, pause/resume, reset, and fit passed with no browser console errors.
- The viewer exposes `window.__depgraphDebug.overlapCount()` and reports the settled count in the header for repeatable inspection.

## Remaining limitations

- The force layout is intentionally nondeterministic after direct user dragging, though initial seeded positions and generated files remain deterministic.
- Collision uses conservative circles around noncircular shapes; labels do not participate in collision.
- Physics changes reheat the full displayed graph. Component anchors limit unrelated-island travel, but simulations are not separate worker threads.
- Browser automation is optional and is not added unless a compatible browser is available locally; no brittle pixel-coordinate assertions are used.
