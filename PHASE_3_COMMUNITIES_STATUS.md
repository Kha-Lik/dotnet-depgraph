# Phase 3 communities status

This checklist builds on the verified rendering work in `PHASE_2_RENDERING_STATUS.md`.

## Baseline

- [x] Clean worktree inspected on 2026-09-03.
- [x] Baseline: `dotnet test DotNetDependencyGraph.slnx --no-restore -v minimal` — 32 passed.
- [x] Existing implementation confirmed as filter-dependent flat label propagation.
- [x] Library search found no suitable maintained pure-.NET Leiden + CPM package; JavaScript, Rust, C++ and Python implementations do not meet the packaged-tool runtime constraint.

## Core detection and schema

- [x] Documented full-graph `CommunityProjection` with producer collapse and test exclusion.
- [x] Pure managed deterministic Leiden/CPM implementation with local move, refinement and aggregation.
- [x] Multiple trials, resolution profile, connectedness invariant and deterministic tie-breaking.
- [x] Strict coarse/standard/fine hierarchy with supported-split rejection.
- [x] Post-detection test assignment with evidence and tie diagnostics.
- [x] Schema 2.0 with required embedded `communityAnalysis`.
- [x] CLI/config/render recomputation controls.
- [x] Public two-clique fixture matches `leidenalg` 0.12.0 / igraph 1.0.0 membership at CPM gamma 0.2, seed 42 (reference quality uses exactly 2× objective scaling).

## Derived architecture analysis

- [x] Runnable-project blast radius and bounded persisted paths.
- [x] Cross-community directed summaries and quotient cycles.
- [x] Bridge/articulation/betweenness roles with neutral evidence.
- [x] Deterministic community names, stable keys and accessible colors.

## Viewer

- [x] Coarse/standard/fine selector and synchronized community legend.
- [x] Rename/recolor/create/reassign/merge/restore/reset overrides.
- [x] Fingerprint-scoped local persistence plus import/export/effective mapping.
- [x] Community details, effective cross-community information and dependency-path explanation.
- [x] Weak bounded community attraction integrated into existing physics.

## Verification

- [x] Synthetic nested/Core-like, projection, test-assignment, analysis and override fixtures.
- [x] Build, tests, format, pack and installed-tool scan — 45 tests passed; package 1.1.0 builds and installs cleanly when stale higher-version local packages are absent.
- [x] Byte-identical community-analysis determinism check.
- [x] Sanitized sample regenerated and browser-inspected, including granularity, override persistence, sizing/filtering and community-map controls.
- [X] Private graph rendered locally without committing identifiers or topology.

The private graph artifact was not present in the workspace or its previously used bounded temporary location during final verification; no private identifiers or topology were copied into the repository.
