/* Cytoscape.js and d3-force are bundled locally. Repository-derived strings are only assigned through textContent. */
(() => {
  "use strict";

  const payload = JSON.parse(document.getElementById("graph-data").textContent);
  const $ = (id) => document.getElementById(id);
  const analysis = payload.raw.communityAnalysis;
  if (!analysis) throw new Error("Schema 2.0 report is missing communityAnalysis.");
  const automaticCommunities = new Map(
    analysis.communities.map((community) => [community.stableKey, community]),
  );
  $("color-mode").parentElement.after($("community-legend"));
  const projection = analysis.projection || {};
  const scope = document.createElement("section");
  scope.id = "community-scope";
  const scopeHeading = document.createElement("b");
  scopeHeading.textContent = "Community input";
  const scopeDetails = document.createElement("p");
  scopeDetails.textContent = `Scope: ${projection.scope || "legacy/unknown"}`;
  const scopeCounts = document.createElement("p");
  scopeCounts.textContent = `${projection.detectionVertexCount ?? "?"} detection vertices from ${projection.detectionNodeCount ?? "?"} nodes · ${projection.localProjectCount ?? "?"} local projects · ${projection.localProducedPackageCount ?? "?"} produced packages · ${projection.collapsedProducerPairCount ?? "?"} producer pairs collapsed · ${projection.contractedEdgeCount ?? "?"} contracted edges`;
  const scopeExcluded = document.createElement("p");
  scopeExcluded.textContent = `Excluded: ${projection.excludedTestProjectCount ?? "?"} tests · ${projection.excludedSystemPackageCount ?? "?"} system packages · ${projection.excludedThirdPartyPackageCount ?? "?"} third-party packages · ${projection.excludedUnresolvedExternalCount ?? "?"} unresolved external nodes`;
  const scopeIncluded = document.createElement("p");
  scopeIncluded.textContent = `Optional packages included: ${projection.includedUnmappedInternalPackageCount ?? "?"} unmapped internal · ${projection.includedSystemPackageCount ?? "?"} system · ${projection.includedThirdPartyPackageCount ?? "?"} third-party`;
  scope.append(scopeHeading, scopeDetails, scopeCounts, scopeExcluded, scopeIncluded);
  $("community-legend").before(scope);
  const state = {
    view: payload.defaults.filterMode || "contract",
    selected: null,
    hovered: null,
    collapsed: !!payload.defaults.collapseLocalPackages,
    simulation: null,
    forceNodes: [],
    forceById: new Map(),
    paused: false,
    dragGesture: null,
    run: 0,
    fitted: false,
    granularity: "standard",
    colorMode: "community",
    sizeMetric: "transitive",
    selectedCommunities: new Set(),
  };
  const borderPalette = ["#79C0FF", "#FFB77C", "#7EE787", "#D2A8FF", "#FF9492", "#E3B341", "#76E3EA", "#F778BA", "#B1BAC4", "#1F6FEB", "#A40E26", "#238636"];
  const physicsDefaults = Object.freeze({ ...payload.defaults.physics });
  const storageKey = payload.defaults.physicsStorageKey ||
    "dotnet-depgraph.physics.v2";
  const viewerStorageKey = payload.defaults.viewerStorageKey ||
    "dotnet-depgraph.viewer.v1";
  const overrideStorageKey = `${payload.defaults.communityOverrideStoragePrefix || "dotnet-depgraph.communities.v1"}:${analysis.graphFingerprint}`;
  const defaultImportantLabelCount = payload.defaults.importantLabelCount ?? 12;
  const bounds = {
    repulsion: [100, 5000],
    linkDistance: [30, 240],
    linkStrength: [.02, 1],
    collisionPadding: [2, 50],
    dragThreshold: [0, 30],
    gravity: [0, .2],
    communityAttraction: [0, .08],
  };
  const clamp = (value, min, max) =>
    Math.max(min, Math.min(max, Number(value)));
  const hash = (s, seed) => {
    let h = seed | 0;
    for (let i = 0; i < s.length; i++) {
      h = Math.imul(h ^ s.charCodeAt(i), 16777619);
    }
    return h >>> 0;
  };
  const nodeDiameter = (d) =>
    clamp(
      18 +
        6 *
          Math.log2(
            (state.sizeMetric === "runnable"
              ? Math.max(0, d.runnableDependentCount || 0)
              : Math.max(0, d.transitiveDependents || 0) +
                Math.max(0, d.inDegree || 0)) + 1,
          ),
      18,
      52,
    );
  const collisionRadius = (d, padding) =>
    nodeDiameter(d) / 2 + Math.max(0, padding);
  function overviewLabel(d, rank, count, zoom) {
    if (zoom < .35) return false;
    if (zoom >= 1.25) return true;
    return zoom >= .7 &&
      rank < clamp(Math.floor(Math.sqrt(Math.max(1, count))), 3, 18) &&
      (d.centrality || 0) > 0;
  }
  function validatedPhysics(candidate) {
    const result = {};
    for (const [key, range] of Object.entries(bounds)) {
      result[key] = clamp(
        candidate?.[key] ?? physicsDefaults[key],
        range[0],
        range[1],
      );
    }
    result.alphaDecay = clamp(
      candidate?.alphaDecay ?? physicsDefaults.alphaDecay,
      .01,
      .1,
    );
    result.alphaMin = clamp(
      candidate?.alphaMin ?? physicsDefaults.alphaMin,
      .0001,
      .02,
    );
    return result;
  }
  function loadPhysics() {
    try {
      const current = localStorage.getItem(storageKey);
      const migrated = current || localStorage.getItem("dotnet-depgraph.physics.v2");
      return validatedPhysics(JSON.parse(migrated));
    } catch {
      return validatedPhysics();
    }
  }
  let physics = loadPhysics();
  function loadViewerPreferences() {
    try {
      const saved = JSON.parse(localStorage.getItem(viewerStorageKey));
      return {
        importantLabelCount: clamp(
          saved?.importantLabelCount ?? defaultImportantLabelCount,
          0,
          50,
        ),
      };
    } catch {
      return { importantLabelCount: defaultImportantLabelCount };
    }
  }
  let viewerPreferences = loadViewerPreferences();

  function emptyOverrides() {
    return {
      version: 1,
      graphFingerprint: analysis.graphFingerprint,
      communityOverrides: [],
      manualCommunities: [],
      nodeAssignments: {},
    };
  }
  function validateOverrides(candidate) {
    if (!candidate || candidate.version !== 1) {
      throw new Error("Override document must have version 1.");
    }
    if (candidate.graphFingerprint !== analysis.graphFingerprint) {
      throw new Error("Override graphFingerprint does not match this graph.");
    }
    if (!Array.isArray(candidate.communityOverrides) ||
      !Array.isArray(candidate.manualCommunities) ||
      !candidate.nodeAssignments || typeof candidate.nodeAssignments !== "object") {
      throw new Error("Override document has an invalid shape.");
    }
    const nodeIds = new Set(payload.raw.nodes.map((node) => node.id));
    const manualIds = new Set();
    candidate.manualCommunities.forEach((community) => {
      if (!community.id?.startsWith("manual:") || manualIds.has(community.id) ||
        typeof community.name !== "string" || !validColor(community.color)) {
        throw new Error("Manual communities require unique manual: IDs, names, and #RRGGBB colors.");
      }
      manualIds.add(community.id);
    });
    const known = new Set([...automaticCommunities.keys(), ...manualIds]);
    for (const [nodeId, community] of Object.entries(candidate.nodeAssignments)) {
      if (!nodeIds.has(nodeId) || !known.has(community)) {
        throw new Error(`Unknown node/community assignment: ${nodeId} → ${community}`);
      }
    }
    return {
      ...emptyOverrides(),
      ...candidate,
      communityOverrides: candidate.communityOverrides.map((item) => ({ ...item })),
      manualCommunities: candidate.manualCommunities.map((item) => ({ ...item })),
      nodeAssignments: { ...candidate.nodeAssignments },
    };
  }
  function loadOverrides() {
    try {
      const local = localStorage.getItem(overrideStorageKey);
      return validateOverrides(local ? JSON.parse(local) : (payload.communityOverrides || emptyOverrides()));
    } catch (error) {
      queueMicrotask(() => showNotice(`Community overrides were ignored: ${error.message}`));
      return emptyOverrides();
    }
  }
  let overrides = loadOverrides();
  if (overrides.overrideDiagnostics?.length) queueMicrotask(() => showNotice(`${overrides.overrideDiagnostics.length} stale community override entries were not applied; export overrides to inspect diagnostics.`));
  function saveOverrides() {
    try { localStorage.setItem(overrideStorageKey, JSON.stringify(overrides)); } catch { /* storage may be disabled */ }
  }
  function validColor(value) { return /^#[0-9a-f]{6}$/i.test(value || ""); }
  function manualMap() { return new Map(overrides.manualCommunities.map((community) => [community.id, community])); }
  function automaticKey(nodeId) {
    return analysis.granularityAssignments[state.granularity]?.[nodeId] ||
      analysis.nodeAssignments[nodeId]?.detectedCommunityPath?.at(-1);
  }
  function effectiveKey(nodeId) { return overrides.nodeAssignments[nodeId] || automaticKey(nodeId); }
  function generatedBorder(key) { return borderPalette[hash(key, 0x6d2b79f5) % borderPalette.length]; }
  function communityInfo(key) {
    if (!key) return { stableKey: null, name: "Not in detection scope", color: "#484F58", borderColor: "#6E7681", memberNodeIds: [] };
    const manual = manualMap().get(key);
    if (manual) return { stableKey: key, borderColor: generatedBorder(key), ...manual, manual: true };
    const automatic = automaticCommunities.get(key) || { stableKey: key, name: key, color: "#8B949E", borderColor: generatedBorder(key), memberNodeIds: [] };
    const style = overrides.communityOverrides.find((item) => item.communityKey === key ||
      automatic.memberNodeIds?.includes(item.detectedAnchorNodeId));
    return { ...automatic, name: style?.name || automatic.name, color: style?.color || automatic.color, manual: !!style };
  }
  function showNotice(message) {
    const warning = $("warning"); warning.hidden = false; warning.textContent = message;
  }

  function savePhysics() {
    try {
      localStorage.setItem(storageKey, JSON.stringify(physics));
    } catch { /* storage may be disabled */ }
  }
  function saveViewerPreferences() {
    try {
      localStorage.setItem(viewerStorageKey, JSON.stringify(viewerPreferences));
    } catch { /* storage may be disabled */ }
  }
  function addDragThresholdControl() {
    const label = document.createElement("label"),
      output = document.createElement("output"),
      input = document.createElement("input");
    label.title =
      "Screen-pixel travel required before a pointer press becomes a drag";
    label.append(document.createTextNode("Drag threshold "));
    output.id = "drag-threshold-value";
    label.append(output);
    input.id = "drag-threshold";
    input.type = "range";
    input.min = "0";
    input.max = "30";
    input.step = "1";
    label.append(input);
    $("physics").querySelector(".physics-actions").before(label);
  }
  addDragThresholdControl();
  function graph() {
    return payload[state.view] || payload.raw;
  }

  function elements() {
    const g = graph(),
      producer = new Map(),
      graphNodeIds = new Set(
        g.nodes.map((n) => n.id),
      );
    if (state.collapsed) {
      payload.raw.edges.filter((e) => e.kind === "produces-package").forEach(
        (e) => {
          if (graphNodeIds.has(e.source) && graphNodeIds.has(e.target)) {
            producer.set(e.target, e.source);
          }
        },
      );
    }
    const visibleNodes = g.nodes.filter((n) => !producer.has(n.id)),
      edges = [],
      seen = new Set();
    for (const e of g.edges) {
      if (e.kind === "produces-package" && state.collapsed) continue;
      const source = producer.get(e.source) || e.source,
        target = producer.get(e.target) || e.target;
      if (source === target) continue;
      const key = `${source}|${target}|${e.kind}`;
      if (seen.has(key)) continue;
      seen.add(key);
      edges.push({
        data: {
          id: e.id + (state.collapsed ? ":collapsed" : ""),
          rawId: e.id,
          source,
          target,
          kind: e.kind,
          derived: e.derived,
          minimumHiddenHops: e.minimumHiddenHops,
          pathCount: e.pathCount,
          pathCountLimited: e.pathCountLimited,
        },
      });
    }
    const ranked = [...visibleNodes].sort((a, b) =>
      nodeDiameter(b) - nodeDiameter(a) ||
      (b.centrality || 0) - (a.centrality || 0) ||
      String(a.id).localeCompare(String(b.id))
    );
    const ranks = new Map(ranked.map((n, i) => [n.id, i]));
    return [
      ...visibleNodes.map((n) => {
        const detected = analysis.nodeAssignments[n.id]?.detectedCommunityPath || [],
          effective = effectiveKey(n.id),
          info = communityInfo(effective),
          effectivePath = overrides.nodeAssignments[n.id] ? [effective] : detected.slice(0, Math.max(0, detected.indexOf(effective)) + 1),
          kindColor = n.kind === "project" ? "#58A6FF" : n.kind === "package" ? "#8B949E" : "#F0883E";
        return {
          data: {
            ...n,
            size: nodeDiameter(n),
            rank: ranks.get(n.id),
            detectedCommunityPath: detected,
            effectiveCommunityPath: effectivePath,
            effectiveCommunity: effective,
            assignmentSource: overrides.nodeAssignments[n.id] ? "manual" :
              (analysis.nodeAssignments[n.id]?.assignmentSource || "automatic"),
            communityName: info.name,
            color: state.colorMode === "community" ? info.color : kindColor,
            borderColor: state.colorMode === "community" ? info.borderColor : "#F0F6FC",
          },
        };
      }),
      ...edges,
    ];
  }

  const cy = cytoscape({
    container: $("cy"),
    elements: elements(),
    pixelRatio: "auto",
    wheelSensitivity: .18,
    minZoom: .04,
    maxZoom: 4,
    textureOnViewport: true,
    motionBlur: false,
    hideEdgesOnViewport: false,
    style: [
      {
        selector: "node",
        style: {
          "background-color": "data(color)",
          "width": "data(size)",
          "height": "data(size)",
          "border-width": 3,
          "border-color": "data(borderColor)",
          "label": "",
          "font-size": 10,
          "color": "#e6edf3",
          "text-outline-width": 2,
          "text-outline-color": "#0d1117",
          "text-max-width": 180,
          "text-wrap": "ellipsis",
          "min-zoomed-font-size": 8,
        },
      },
      {
        selector: 'node[kind="project"]',
        style: {
          shape: "round-rectangle",
        },
      },
      {
        selector: 'node[classification="test-project"]',
        style: { shape: "diamond" },
      },
      {
        selector:
          'node[classification="executable"],node[classification="web-application"],node[classification="azure-functions"]',
        style: { shape: "hexagon" },
      },
      {
        selector: "node[versionSkew = true]",
        style: { "underlay-color": "#F85149", "underlay-opacity": .24, "underlay-padding": 2 },
      },
      {
        selector: "node.show-label,node:selected",
        style: {
          label: "data(label)",
          "z-index": 20,
          "min-zoomed-font-size": 0,
        },
      },
      {
        selector: "node:selected",
        style: {
          "overlay-color": "#58a6ff",
          "overlay-opacity": .24,
          "overlay-padding": 9,
        },
      },
      {
        selector: "node.search-match",
        style: {
          "overlay-color": "#d29922",
          "overlay-opacity": .2,
          "overlay-padding": 6,
        },
      },
      {
        selector: 'node[assignmentSource="manual"]',
        style: { "border-style": "dashed", "border-width": 3 },
      },
      {
        selector: 'node[architecturalRole="cross-community bridge"],node[architecturalRole="shared infrastructure"]',
        style: { "border-width": 4.5 },
      },
      {
        selector: "node.community-highlight",
        style: { "overlay-color": "#F0F6FC", "overlay-opacity": .18, "overlay-padding": 7 },
      },
      {
        selector: "edge",
        style: {
          width: 1.1,
          "line-color": "#8c98a5",
          "target-arrow-color": "#8c98a5",
          "target-arrow-shape": "none",
          "curve-style": "bezier",
          "opacity": .48,
          "arrow-scale": .7,
        },
      },
      {
        selector: 'edge[kind="project-reference"]',
        style: { "line-color": "#6cb6ff", "target-arrow-color": "#6cb6ff" },
      },
      {
        selector: 'edge[kind="package-reference"]',
        style: { "line-color": "#b1bac4", "target-arrow-color": "#b1bac4" },
      },
      {
        selector: 'edge[kind="contracted-path"]',
        style: {
          "line-style": "dashed",
          "line-color": "#e3b341",
          "target-arrow-color": "#e3b341",
          width: 1.2,
          opacity: .6,
        },
      },
      {
        selector: 'edge[kind="produces-package"]',
        style: {
          "line-style": "dotted",
          "line-color": "#bc8cff",
          "target-arrow-color": "#bc8cff",
          opacity: .38,
        },
      },
      { selector: ".faded", style: { opacity: .035 } },
      {
        selector: "edge.upstream",
        style: {
          "line-color": "#f0883e",
          "target-arrow-color": "#f0883e",
          "target-arrow-shape": "triangle",
          width: 2,
          opacity: 1,
          "z-index": 10,
        },
      },
      {
        selector: "edge.downstream",
        style: {
          "line-color": "#3fb950",
          "target-arrow-color": "#3fb950",
          "target-arrow-shape": "triangle",
          width: 2,
          opacity: 1,
          "z-index": 10,
        },
      },
      {
        selector: "edge.hover-edge",
        style: {
          "target-arrow-shape": "triangle",
          width: 1.5,
          opacity: .85,
          "z-index": 9,
        },
      },
      {
        selector: "edge.physics-active",
        style: {
          "curve-style": "haystack",
          "target-arrow-shape": "none",
          width: .75,
          opacity: .12,
        },
      },
    ],
    layout: { name: "preset" },
  });

  function componentCenters(nodes) {
    const groups = new Map();
    nodes.forEach((n) => {
      const key = String(n.component);
      if (!groups.has(key)) groups.set(key, []);
      groups.get(key).push(n);
    });
    const ordered = [...groups.entries()].sort((a, b) =>
      b[1].length - a[1].length ||
      a[0].localeCompare(b[0], undefined, { numeric: true })
    );
    const columns = Math.max(1, Math.ceil(Math.sqrt(ordered.length))),
      cell = Math.max(
        320,
        Math.sqrt(Math.max(1, ...ordered.map((x) => x[1].length))) *
            physics.linkDistance * 1.1 + 220,
      );
    for (let i = 0; i < ordered.length; i++) {
      const members = ordered[i][1],
        cx = (i % columns) * cell,
        cy = Math.floor(i / columns) * cell;
      members.forEach((n) => {
        n.cx = cx;
        n.cy = cy;
      });
    }
  }
  function initialPosition(node, index, randomize) {
    const seed = (payload.defaults.seed || 42) + state.run * 7919,
      h = hash(node.id, seed),
      angle = ((h % 3600) / 3600) * Math.PI * 2;
    const radius = (randomize ? 35 : 16) +
      Math.sqrt(index + 1) * physics.linkDistance * .8 +
      ((h >>> 12) % Math.max(20, physics.linkDistance));
    node.x = node.cx + Math.cos(angle) * radius;
    node.y = node.cy + Math.sin(angle) * radius;
    node.vx = 0;
    node.vy = 0;
    node.fx = null;
    node.fy = null;
  }
  function communityCentroidForce(strength) {
    let nodes = [];
    function force(alpha) {
      if (!strength) return;
      const groups = new Map();
      nodes.forEach((node) => {
        if (!node.community) return;
        const key = `${node.component}|${node.community}`;
        const group = groups.get(key) || { x: 0, y: 0, count: 0 };
        group.x += node.x; group.y += node.y; group.count++; groups.set(key, group);
      });
      nodes.forEach((node) => {
        if (!node.community) return;
        const group = groups.get(`${node.component}|${node.community}`);
        if (!group || group.count < 2) return;
        node.vx += (group.x / group.count - node.x) * strength * alpha;
        node.vy += (group.y / group.count - node.y) * strength * alpha;
      });
    }
    force.initialize = (value) => { nodes = value; };
    return force;
  }
  function forceOptions(simulation) {
    const byId = state.forceById,
      links = cy.edges().map((e) => ({
        source: e.source().id(),
        target: e.target().id(),
        kind: e.data("kind"),
      }));
    simulation
      .force(
        "link",
        d3.forceLink(links).id((d) => d.id).distance((link) => {
          const a = typeof link.source === "object"
              ? link.source
              : byId.get(link.source),
            b = typeof link.target === "object"
              ? link.target
              : byId.get(link.target),
            degreeSpace = Math.min(
              70,
              Math.sqrt(Math.max(a?.degree || 0, b?.degree || 0)) * 7,
            );
          return physics.linkDistance + (a?.size || 18) / 2 +
            (b?.size || 18) / 2 + degreeSpace +
            (link.kind === "contracted-path" ? 35 : 0);
        }).strength((link) =>
          physics.linkStrength /
          (1 +
            Math.sqrt(
                Math.max(link.source.degree || 0, link.target.degree || 0),
              ) / 5)
        ).iterations(1),
      )
      .force(
        "charge",
        d3.forceManyBody().strength((d) =>
          -physics.repulsion * (.75 + Math.min(40, d.degree || 0) / 80)
        ).distanceMin(8).distanceMax(1400).theta(.9),
      )
      .force(
        "collision",
        d3.forceCollide((d) => d.size / 2 + physics.collisionPadding).strength(
          1,
        ).iterations(6),
      )
      .force("x", d3.forceX((d) => d.cx).strength(physics.gravity)).force(
        "y",
        d3.forceY((d) => d.cy).strength(physics.gravity),
      ).force("community", communityCentroidForce(physics.communityAttraction));
  }
  function refreshPositions() {
    cy.batch(() =>
      state.forceNodes.forEach((d) => {
        const n = cy.$id(d.id);
        if (n.length) n.position({ x: d.x, y: d.y });
      })
    );
  }
  function overlapCount() {
    let overlaps = 0;
    for (let i = 0; i < state.forceNodes.length; i++) {
      for (let j = i + 1; j < state.forceNodes.length; j++) {
        const a = state.forceNodes[i],
          b = state.forceNodes[j],
          minimum = a.size / 2 + b.size / 2 + physics.collisionPadding * 2 - 1;
        if (Math.hypot(a.x - b.x, a.y - b.y) < minimum) {
          overlaps++;
        }
      }
    }
    return overlaps;
  }
  function setStatus(text) {
    $("physics-status").textContent = text;
  }
  function startSimulation(randomize = false) {
    if (state.simulation) state.simulation.stop();
    const nodes = cy.nodes().map((n) => ({
      id: n.id(),
      component: n.data("component"),
      size: n.data("size"),
      degree: n.degree(),
      community: n.data("effectiveCommunity"),
      x: n.position("x"),
      y: n.position("y"),
    }));
    state.forceNodes = nodes;
    state.forceById = new Map(nodes.map((n) => [n.id, n]));
    componentCenters(nodes);
    nodes.forEach((n, i) => initialPosition(n, i, randomize));
    refreshPositions();
    cy.fit(cy.elements(":visible"), 55);
    updateLabels();
    state.fitted = false;
    state.paused = false;
    $("pause").textContent = "Pause physics";
    const simulation = d3.forceSimulation(nodes).alpha(1).alphaMin(
      physics.alphaMin,
    ).alphaDecay(physics.alphaDecay).velocityDecay(.42);
    forceOptions(simulation);
    state.simulation = simulation;
    cy.edges().addClass("physics-active");
    let ticks = 0, lastPaint = 0;
    simulation.on("tick", () => {
      ticks++;
      const now = performance.now();
      if (now - lastPaint > 45) {
        refreshPositions();
        lastPaint = now;
      }
      if (ticks <= 90 && ticks % 30 === 0) {
        cy.fit(cy.elements(":visible"), 55);
        state.fitted = true;
        updateLabels();
      }
      if (ticks % 20 === 0) {
        setStatus(
          `Physics settling · ${Math.round(simulation.alpha() * 100)}%`,
        );
      }
    }).on("end", () => {
      refreshPositions();
      cy.edges().removeClass("physics-active");
      cy.fit(cy.elements(":visible"), 55);
      updateLabels();
      setStatus(`Physics settled · ${overlapCount()} overlaps`);
    });
    setStatus("Physics settling");
  }
  function reheat(alpha = .55) {
    if (!state.simulation) return;
    forceOptions(state.simulation);
    cy.edges().addClass("physics-active");
    state.simulation.alphaTarget(0).alpha(
      Math.max(alpha, state.simulation.alpha()),
    ).restart();
    state.paused = false;
    $("pause").textContent = "Pause physics";
    setStatus("Physics reheated");
  }
  function replace() {
    state.selected = null;
    state.hovered = null;
    cy.elements().remove();
    cy.add(elements());
    populateFilters();
    renderCommunityLegend();
    startSimulation(false);
    applyFilters();
  }

  function populate(id, values) {
    const s = $(id), old = s.value;
    s.replaceChildren(new Option("All", ""));
    [...values].sort((a, b) =>
      String(a).localeCompare(String(b), undefined, { numeric: true })
    ).forEach((v) => s.add(new Option(String(v), String(v))));
    s.value = old;
  }
  function populateFilters() {
    const g = graph();
    populate("kind", new Set(g.nodes.map((n) => n.kind)));
    populate("edgeKind", new Set(g.edges.map((e) => e.kind)));
    populate("component", new Set(g.nodes.map((n) => n.component)));
    populate("community", new Set(g.nodes.map((n) => effectiveKey(n.id))));
    [...$("community").options].slice(1).forEach((option) => {
      option.textContent = communityInfo(option.value).name;
    });
    populate("tfm", new Set(g.nodes.flatMap((n) => n.targetFrameworks || [])));
    populate(
      "rid",
      new Set(g.nodes.flatMap((n) => n.runtimeIdentifiers || [])),
    );
    const box = $("components");
    box.replaceChildren(
      Object.assign(document.createElement("b"), { textContent: "Components" }),
    );
    const groups = new Map();
    g.nodes.forEach((n) => {
      if (!groups.has(n.component)) groups.set(n.component, []);
      groups.get(n.component).push(n);
    });
    [...groups.entries()].sort((a, b) => b[1].length - a[1].length).forEach(
      ([id, nodes]) => {
        const b = document.createElement("button");
        b.className = "component-link";
        b.textContent = `${id}: ${nodes.length} nodes · ${
          nodes.sort((a, b) => b.centrality - a.centrality)[0]?.label || ""
        }`;
        b.onclick = () =>
          cy.fit(
            cy.nodes().filter((n) =>
              String(n.data("component")) === String(id)
            ),
            70,
          );
        box.appendChild(b);
      },
    );
  }
  function downloadJson(name, value) {
    const a = document.createElement("a");
    a.download = name;
    a.href = URL.createObjectURL(new Blob([JSON.stringify(value, null, 2)], { type: "application/json" }));
    a.click(); setTimeout(() => URL.revokeObjectURL(a.href), 1000);
  }
  function effectiveGroups() {
    const groups = new Map();
    graph().nodes.forEach((node) => {
      const key = effectiveKey(node.id); if (!key) return;
      const values = groups.get(key) || [];
      values.push(node); groups.set(key, values);
    });
    return groups;
  }
  function renderCommunityLegend() {
    const container = $("community-legend-rows"), query = $("community-search").value.trim().toLowerCase();
    container.replaceChildren();
    if (state.colorMode !== "community") {
      const message = document.createElement("p"); message.textContent = "Colors currently represent node kind: project, package, or unresolved."; container.appendChild(message); return;
    }
    const groups = [...effectiveGroups().entries()].map(([key, nodes]) => ({ key, nodes, info: communityInfo(key) }))
      .filter(item => !query || item.info.name.toLowerCase().includes(query) || item.nodes.some(node => node.label.toLowerCase().includes(query)))
      .sort((a, b) => {
        const aPath = analysis.nodeAssignments[a.nodes[0].id]?.detectedCommunityPath?.join("/") || a.key,
          bPath = analysis.nodeAssignments[b.nodes[0].id]?.detectedCommunityPath?.join("/") || b.key;
        return aPath.localeCompare(bPath) || b.nodes.length - a.nodes.length || a.info.name.localeCompare(b.info.name);
      });
    groups.forEach(({ key, nodes, info }) => {
      const row = document.createElement("div"); row.className = "community-row"; row.dataset.community = key;
      if (state.selectedCommunities.has(key)) row.classList.add("selected");
      const swatch = document.createElement("span"); swatch.className = "community-swatch"; swatch.style.backgroundColor = info.color; swatch.style.borderColor = info.borderColor || generatedBorder(key);
      row.style.marginLeft = `${Math.min(2, info.depth || 0) * 9}px`;
      const label = document.createElement("button"); label.className = "community-name";
      const projects = nodes.filter(node => node.kind === "project").length,
        packages = nodes.filter(node => node.kind === "package").length,
        runnable = nodes.filter(node => ["executable", "web-application", "azure-functions"].includes(node.classification)).length;
      label.textContent = `${info.name} — ${nodes.length}`;
      label.title = `${projects} projects · ${packages} packages · ${runnable} runnable${info.manual ? " · manual changes" : ""}`;
      label.onclick = (event) => {
        if (!event.ctrlKey && !event.metaKey) state.selectedCommunities.clear();
        state.selectedCommunities.has(key) ? state.selectedCommunities.delete(key) : state.selectedCommunities.add(key);
        showCommunityDetails(key); renderCommunityLegend(); applyFilters();
      };
      row.onmouseenter = () => highlightCommunity(key);
      row.onmouseleave = () => { cy.nodes().removeClass("community-highlight"); if (!state.selected) clearFocus(); };
      const edit = document.createElement("button"); edit.textContent = "Edit"; edit.title = "Rename or recolor"; edit.onclick = () => editCommunity(key);
      const restore = document.createElement("button"); restore.textContent = "↶"; restore.title = "Restore automatic community values and membership"; restore.onclick = () => restoreCommunity(key);
      const members = document.createElement("details"); members.className = "community-members";
      const memberSummary = document.createElement("summary"); memberSummary.textContent = "Members / children"; members.appendChild(memberSummary);
      (info.directChildKeys || []).forEach(childKey => { const child = document.createElement("div"); child.textContent = `↳ ${communityInfo(childKey).name}`; members.appendChild(child); });
      nodes.sort((a, b) => (b.centrality || 0) - (a.centrality || 0) || a.label.localeCompare(b.label)).slice(0, 5).forEach(node => { const member = document.createElement("button"); member.textContent = node.label; member.onclick = () => { const target = cy.$id(node.id); if (target.length) { target.select(); detail(target); cy.fit(target, 100); } }; members.appendChild(member); });
      row.append(swatch, label, edit, restore, members); container.appendChild(row);
    });
  }
  function highlightCommunity(key) {
    cy.nodes().removeClass("community-highlight");
    cy.nodes().filter(node => node.data("effectiveCommunity") === key).addClass("community-highlight");
  }
  function editCommunity(key) {
    const info = communityInfo(key), name = prompt("Community name", info.name); if (name === null || !name.trim()) return;
    const color = prompt("Community color (#RRGGBB)", info.color); if (color === null) return;
    if (!validColor(color)) { showNotice("Community color must use #RRGGBB format."); return; }
    const manual = overrides.manualCommunities.find(community => community.id === key);
    if (manual) { manual.name = name.trim(); manual.color = color.toUpperCase(); }
    else {
      let style = overrides.communityOverrides.find(item => item.communityKey === key);
      if (!style) { style = { communityKey: key, detectedAnchorNodeId: automaticCommunities.get(key)?.memberNodeIds?.[0] || "", name: null, color: null }; overrides.communityOverrides.push(style); }
      style.name = name.trim(); style.color = color.toUpperCase();
    }
    saveOverrides(); replace();
  }
  function restoreCommunity(key) {
    overrides.communityOverrides = overrides.communityOverrides.filter(item => item.communityKey !== key && !automaticCommunities.get(key)?.memberNodeIds?.includes(item.detectedAnchorNodeId));
    for (const [nodeId, assigned] of Object.entries(overrides.nodeAssignments)) if (assigned === key) delete overrides.nodeAssignments[nodeId];
    const manual = overrides.manualCommunities.find(community => community.id === key);
    if (manual) { for (const [nodeId, assigned] of Object.entries(overrides.nodeAssignments)) if (assigned === key) delete overrides.nodeAssignments[nodeId]; overrides.manualCommunities = overrides.manualCommunities.filter(community => community.id !== key); }
    state.selectedCommunities.delete(key); saveOverrides(); replace();
  }
  function createManual(name, color) {
    const slug = name.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "") || "community";
    let id = `manual:${slug}`, suffix = 2; const known = new Set(overrides.manualCommunities.map(item => item.id));
    while (known.has(id)) id = `manual:${slug}-${suffix++}`;
    overrides.manualCommunities.push({ id, name, color }); return id;
  }
  function selectedNodeIds() { return cy.nodes(":selected").map(node => node.id()); }
  function createFromSelection() {
    const nodes = selectedNodeIds(); if (!nodes.length && state.selected) nodes.push(state.selected);
    if (!nodes.length) { showNotice("Select one or more nodes first."); return; }
    const name = prompt("New manual community name", "Manual community"); if (!name?.trim()) return;
    const color = prompt("Community color (#RRGGBB)", "#F0883E"); if (!validColor(color)) { showNotice("Community color must use #RRGGBB format."); return; }
    const id = createManual(name.trim(), color.toUpperCase()); nodes.forEach(nodeId => overrides.nodeAssignments[nodeId] = id); saveOverrides(); replace();
  }
  function mergeSelectedCommunities() {
    if (state.selectedCommunities.size < 2) { showNotice("Select at least two legend communities with Ctrl/Cmd-click."); return; }
    const name = prompt("Merged effective community name", "Merged community"); if (!name?.trim()) return;
    const color = prompt("Community color (#RRGGBB)", "#BC8CFF"); if (!validColor(color)) { showNotice("Community color must use #RRGGBB format."); return; }
    const selected = new Set(state.selectedCommunities), id = createManual(name.trim(), color.toUpperCase());
    graph().nodes.forEach(node => { if (selected.has(effectiveKey(node.id))) overrides.nodeAssignments[node.id] = id; });
    state.selectedCommunities.clear(); saveOverrides(); replace();
  }
  function effectiveMatrix() {
    const rows = new Map();
    cy.edges().forEach(edge => {
      if (edge.data("kind") === "produces-package") return;
      const source = effectiveKey(edge.source().id()), target = effectiveKey(edge.target().id()); if (!source || !target || source === target) return;
      const key = `${source}\n${target}`, row = rows.get(key) || { sourceCommunity: source, targetCommunity: target, edgeCount: 0, edgeKinds: {} };
      row.edgeCount++; row.edgeKinds[edge.data("kind")] = (row.edgeKinds[edge.data("kind")] || 0) + 1; rows.set(key, row);
    });
    return [...rows.values()].sort((a, b) => b.edgeCount - a.edgeCount || a.sourceCommunity.localeCompare(b.sourceCommunity));
  }
  function showCommunityDetails(key) {
    const info = communityInfo(key), nodes = graph().nodes.filter(node => effectiveKey(node.id) === key), panel = $("details"); panel.replaceChildren();
    const title = document.createElement("h2"); title.textContent = info.name; panel.appendChild(title);
    const detected = automaticCommunities.get(key);
    const values = { "Effective nodes": nodes.length, Projects: nodes.filter(n => n.kind === "project").length, Packages: nodes.filter(n => n.kind === "package").length,
      "Detected quality": detected?.quality ?? "manual/effective partition", Stability: detected?.stability ?? "—", "Manual assignments": nodes.filter(n => overrides.nodeAssignments[n.id]).length };
    Object.entries(values).forEach(([name, value]) => { const row = document.createElement("div"); row.className = "detail"; const b = document.createElement("b"); b.textContent = name; const span = document.createElement("span"); span.textContent = String(value); row.append(b, span); panel.appendChild(row); });
    const matrix = document.createElement("div"); matrix.className = "detail"; const heading = document.createElement("b"); heading.textContent = "Cross-community dependencies (effective view)"; matrix.appendChild(heading);
    effectiveMatrix().filter(row => row.sourceCommunity === key || row.targetCommunity === key).slice(0, 20).forEach(row => { const line = document.createElement("button"); line.className = "matrix-row"; line.textContent = `${communityInfo(row.sourceCommunity).name} → ${communityInfo(row.targetCommunity).name}: ${row.edgeCount}`; line.onclick = () => { cy.edges().addClass("faded"); cy.edges().filter(edge => effectiveKey(edge.source().id()) === row.sourceCommunity && effectiveKey(edge.target().id()) === row.targetCommunity).removeClass("faded"); }; matrix.appendChild(line); }); panel.appendChild(matrix);
  }
  function showCommunityMap() {
    const panel = $("details"); panel.replaceChildren(); const title = document.createElement("h2"); title.textContent = "Effective community graph"; panel.appendChild(title);
    const groups = [...effectiveGroups().keys()].sort((a, b) => communityInfo(a).name.localeCompare(communityInfo(b).name)), size = 270, center = size / 2, radius = 100;
    const positions = new Map(groups.map((key, index) => [key, { x: center + Math.cos(index / Math.max(1, groups.length) * Math.PI * 2) * radius, y: center + Math.sin(index / Math.max(1, groups.length) * Math.PI * 2) * radius }]));
    const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg"); svg.setAttribute("viewBox", `0 0 ${size} ${size}`); svg.classList.add("community-map");
    const defs = document.createElementNS(svg.namespaceURI, "defs"), marker = document.createElementNS(svg.namespaceURI, "marker"); marker.setAttribute("id", "community-arrow"); marker.setAttribute("viewBox", "0 0 10 10"); marker.setAttribute("refX", "8"); marker.setAttribute("refY", "5"); marker.setAttribute("markerWidth", "5"); marker.setAttribute("markerHeight", "5"); marker.setAttribute("orient", "auto-start-reverse"); const arrow = document.createElementNS(svg.namespaceURI, "path"); arrow.setAttribute("d", "M 0 0 L 10 5 L 0 10 z"); arrow.setAttribute("fill", "#8B949E"); marker.appendChild(arrow); defs.appendChild(marker); svg.appendChild(defs);
    effectiveMatrix().forEach(edge => { const a = positions.get(edge.sourceCommunity), b = positions.get(edge.targetCommunity); if (!a || !b) return; const line = document.createElementNS(svg.namespaceURI, "line"); line.setAttribute("x1", a.x); line.setAttribute("y1", a.y); line.setAttribute("x2", b.x); line.setAttribute("y2", b.y); line.setAttribute("stroke", "#8B949E"); line.setAttribute("stroke-width", String(Math.min(4, 1 + Math.log2(edge.edgeCount)))); line.setAttribute("marker-end", "url(#community-arrow)"); svg.appendChild(line); });
    groups.forEach(key => { const p = positions.get(key), info = communityInfo(key); const circle = document.createElementNS(svg.namespaceURI, "circle"); circle.setAttribute("cx", p.x); circle.setAttribute("cy", p.y); circle.setAttribute("r", "8"); circle.setAttribute("fill", info.color); circle.setAttribute("stroke", info.borderColor || generatedBorder(key)); circle.setAttribute("stroke-width", "3"); circle.onclick = () => showCommunityDetails(key); const label = document.createElementNS(svg.namespaceURI, "text"); label.setAttribute("x", p.x + 10); label.setAttribute("y", p.y + 4); label.textContent = info.name.length > 18 ? `${info.name.slice(0, 17)}…` : info.name; svg.append(circle, label); });
    panel.appendChild(svg); const heading = document.createElement("h2"); heading.textContent = "Directed coupling matrix"; panel.appendChild(heading);
    effectiveMatrix().forEach(row => { const line = document.createElement("button"); line.className = "matrix-row"; line.textContent = `${communityInfo(row.sourceCommunity).name} → ${communityInfo(row.targetCommunity).name}: ${row.edgeCount}`; line.onclick = () => showCommunityDetails(row.sourceCommunity); panel.appendChild(line); });
  }
  function applyFilters() {
    cy.elements().removeClass("faded search-match").style("display", "element");
    const kind = $("kind").value,
      ek = $("edgeKind").value,
      component = $("component").value,
      community = $("community").value,
      tfm = $("tfm").value,
      rid = $("rid").value,
      skew = $("skew").checked,
      runnableMin = Math.max(0, Number($("runnable-min").value) || 0),
      q = $("search").value.trim().toLowerCase();
    cy.nodes().forEach((n) => {
      const d = n.data(),
        match = (!kind || d.kind === kind) &&
          (!component || String(d.component) === component) &&
          (!community || d.effectiveCommunity === community) &&
          (!state.selectedCommunities.size || state.selectedCommunities.has(d.effectiveCommunity)) &&
          (!tfm || (d.targetFrameworks || []).includes(tfm)) &&
          (!rid || (d.runtimeIdentifiers || []).includes(rid)) &&
          (d.runnableDependentCount || 0) >= runnableMin &&
          (!skew || d.versionSkew);
      if (!match) n.style("display", "none");
      if (
        q &&
        (d.label.toLowerCase().includes(q) || d.id.toLowerCase().includes(q))
      ) n.addClass("search-match");
    });
    cy.edges().forEach((e) => {
      if (
        (ek && e.data("kind") !== ek) ||
        e.source().style("display") === "none" ||
        e.target().style("display") === "none"
      ) e.style("display", "none");
    });
    const hops = Number($("hops").value);
    if (hops && state.selected) {
      let keep = cy.$id(state.selected), front = keep;
      for (let i = 0; i < hops; i++) {
        front = front.neighborhood();
        keep = keep.union(front);
      }
      cy.elements().difference(keep).style("display", "none");
    }
    if (q) {
      const matches = cy.nodes(".search-match:visible");
      cy.nodes(":visible").difference(matches).addClass("faded");
      if (matches.length) cy.fit(matches, 90);
    }
    updateLabels();
  }
  function updateLabels() {
    const zoom = cy.zoom(),
      count = cy.nodes(":visible").length,
      hops = Number($("hops").value);
    const allNodes = cy.nodes();
    allNodes
      .removeClass("show-label screen-label")
      .removeStyle("font-size")
      .removeStyle("text-outline-width")
      .removeStyle("text-max-width")
      .removeStyle("text-wrap");

    const visibleNodes = cy.nodes(":visible");
    visibleNodes.forEach((n) => {
      if (overviewLabel(n.data(), n.data("rank"), count, zoom)) {
        n.addClass("show-label");
      }
    });

    let screenLabels = visibleNodes.filter((n) =>
      n.data("rank") < viewerPreferences.importantLabelCount
    );
    screenLabels.addClass("show-label screen-label");

    const searchMatches = cy.nodes(".search-match:visible");
    searchMatches.addClass("show-label screen-label");
    screenLabels = screenLabels.union(searchMatches);

    if (state.selected) {
      const selected = cy.$id(state.selected);
      selected.addClass("show-label screen-label");
      screenLabels = screenLabels.union(selected);
      if (hops) {
        const neighborhood = selected.neighborhood("node:visible");
        neighborhood.addClass("show-label screen-label");
        screenLabels = screenLabels.union(neighborhood);
      }
    }
    if (state.hovered) {
      const hovered = cy.$id(state.hovered);
      hovered.addClass("show-label screen-label");
      screenLabels = screenLabels.union(hovered);
    }

    // Cytoscape font sizes are graph-space values. Dividing by zoom keeps
    // these interaction labels at a stable CSS-pixel size; pixelRatio then
    // handles the display's device-pixel density.
    const labelScale = 1 / Math.max(zoom, .01);
    screenLabels.style({
      "font-size": 12 * labelScale,
      "text-outline-width": 2 * labelScale,
      "text-max-width": 240 * labelScale,
      "text-wrap": "ellipsis",
    });

    const focusedId = state.hovered || state.selected;
    if (focusedId) {
      const focused = cy.$id(focusedId);
      focused.style({
        "font-size": 14 * labelScale,
        "text-outline-width": 2.4 * labelScale,
        "text-wrap": "none",
      });
    }
  }
  function clearFocus() {
    cy.elements().removeClass("faded upstream downstream hover-edge");
  }
  function focusNode(n, temporary = false) {
    clearFocus();
    const incoming = n.incomers(),
      outgoing = n.outgoers(),
      related = n.union(incoming).union(outgoing);
    cy.elements().difference(related).addClass("faded");
    if (temporary) {
      incoming.edges().add(outgoing.edges()).addClass("hover-edge");
      return;
    }
    incoming.edges().addClass("upstream");
    outgoing.edges().addClass("downstream");
  }
  function detail(n) {
    state.selected = n.id();
    focusNode(n);
    updateLabels();
    const up = n.incomers(),
      down = n.outgoers(),
      d = n.data(),
      panel = $("details");
    panel.replaceChildren();
    const title = document.createElement("h2");
    title.textContent = d.label;
    panel.appendChild(title);
    const values = {
      Identifier: d.id,
      Kind: d.kind,
      "Community ownership": analysis.nodeOwnership?.[d.id] || "legacy/unknown",
      Classification: d.classification || "—",
      Path: d.path || "—",
      Versions: (d.versions || []).join(", ") || "—",
      Frameworks: (d.targetFrameworks || []).join(", ") || "—",
      RIDs: (d.runtimeIdentifiers || []).join(", ") || "—",
      Component: d.component,
      "Detected community path": (d.detectedCommunityPath || []).map(key => communityInfo(key).name).join(" → ") || "—",
      "Effective community": d.communityName,
      "Assignment source": d.assignmentSource,
      "Direct dependencies": d.directDependencies,
      "Transitive dependencies": d.transitiveDependencies,
      "Direct dependents": d.directDependents,
      "Transitive dependents": d.transitiveDependents,
      "Runnable dependents": `${d.runnableDependentCount || 0}: ${(d.runnableDependentIds || []).join(", ")}`,
      "Affected communities": d.affectedCommunityCount || 0,
      "Architectural role": d.architecturalRole || "feature-local",
      "Role evidence": (d.roleEvidence || []).join("; ") || "—",
      "Betweenness centrality": d.betweennessCentrality || 0,
      "Version skew": d.versionSkew ? "yes" : "no",
      "Cycle member": d.inCycle ? "yes" : "no",
    };
    for (const [k, v] of Object.entries(values)) {
      const row = document.createElement("div");
      row.className = "detail";
      const b = document.createElement("b");
      b.textContent = k;
      const span = document.createElement("span");
      span.textContent = String(v);
      row.append(b, span);
      panel.appendChild(row);
    }
    const rel = document.createElement("div");
    rel.className = "detail";
    const b = document.createElement("b");
    b.textContent = "Immediate relationships";
    rel.appendChild(b);
    [...up.nodes(), ...down.nodes()].sort((a, b) =>
      a.data("label").localeCompare(b.data("label"))
    ).forEach((x) => {
      const p = document.createElement("div");
      p.textContent = (up.contains(x) ? "dependent: " : "dependency: ") +
        x.data("label");
      rel.appendChild(p);
    });
    panel.appendChild(rel);
    const assignment = document.createElement("div"); assignment.className = "detail";
    const assignmentHeading = document.createElement("b"); assignmentHeading.textContent = "Effective assignment";
    const select = document.createElement("select");
    [...effectiveGroups().keys()].sort((a, b) => communityInfo(a).name.localeCompare(communityInfo(b).name)).forEach(key => select.add(new Option(communityInfo(key).name, key)));
    select.value = d.effectiveCommunity;
    const move = document.createElement("button"); move.textContent = "Assign selected"; move.onclick = () => { const ids = selectedNodeIds(); if (!ids.length) ids.push(d.id); ids.forEach(id => overrides.nodeAssignments[id] = select.value); saveOverrides(); replace(); };
    const restore = document.createElement("button"); restore.textContent = "Restore node"; restore.onclick = () => { delete overrides.nodeAssignments[d.id]; saveOverrides(); replace(); };
    assignment.append(assignmentHeading, select, move, restore); panel.appendChild(assignment);
    const diagnostics = (graph().diagnostics || []).filter((x) =>
      x.projectId === d.id
    );
    if (diagnostics.length) {
      const warnings = document.createElement("div");
      warnings.className = "detail";
      const heading = document.createElement("b");
      heading.textContent = "Diagnostics";
      warnings.appendChild(heading);
      diagnostics.forEach((x) => {
        const p = document.createElement("div");
        p.textContent = `${x.severity}: ${x.message}`;
        warnings.appendChild(p);
      });
      panel.appendChild(warnings);
    }
  }
  function explainSelectedPath() {
    const selected = selectedNodeIds();
    if (selected.length !== 2) { showNotice("Select exactly two nodes (Ctrl/Cmd-click) to explain a directed dependency path."); return; }
    const [source, target] = selected, queue = [source], previous = new Map([[source, null]]);
    while (queue.length && !previous.has(target)) {
      const current = queue.shift();
      cy.$id(current).outgoers("edge:visible").sort((a, b) => a.id().localeCompare(b.id())).forEach(edge => {
        if (edge.data("kind") === "produces-package") return;
        const next = edge.target().id(); if (!previous.has(next)) { previous.set(next, edge); queue.push(next); }
      });
    }
    const panel = $("details"); panel.replaceChildren(); const title = document.createElement("h2"); title.textContent = "Shortest dependency path"; panel.appendChild(title);
    if (!previous.has(target)) { const p = document.createElement("p"); p.textContent = "No directed dependency path exists in the active view. Try reversing the selection order or switching view."; panel.appendChild(p); return; }
    const path = []; let cursor = target; while (cursor !== source) { const edge = previous.get(cursor); path.push({ node: cursor, edge }); cursor = edge.source().id(); } path.push({ node: source, edge: null }); path.reverse();
    path.forEach((step, index) => { const row = document.createElement("div"); row.className = "detail"; const node = cy.$id(step.node); row.textContent = index === 0 ? node.data("label") : `${step.edge.data("kind")} → ${node.data("label")}${step.edge.data("derived") ? " (contracted/derived)" : ""}`; panel.appendChild(row); });
    const persisted = analysis.runnableImpactPaths.find(item => item.source === source && item.target === target);
    const note = document.createElement("p"); note.className = "legend-help"; note.textContent = persisted ? "This runnable-impact path was computed and persisted by the .NET analysis." : "This active-view path was resolved from persisted canonical edges; alternatives are intentionally bounded."; panel.appendChild(note);
  }
  function syncControls() {
    const importantLabelCount = $("important-label-count");
    importantLabelCount.value = viewerPreferences.importantLabelCount;
    $("important-label-count-value").value = String(
      viewerPreferences.importantLabelCount,
    );
    const controls = {
      repulsion: "repulsion",
      linkDistance: "link-distance",
      linkStrength: "link-strength",
      collisionPadding: "collision-padding",
      dragThreshold: "drag-threshold",
      gravity: "gravity",
      communityAttraction: "community-attraction",
    };
    for (const [key, id] of Object.entries(controls)) {
      const input = $(id);
      input.value = physics[key];
      $(`${id}-value`).value = String(physics[key]);
    }
  }
  let sliderTimer;
  function sliderChanged(key, id) {
    physics = { ...physics, [key]: Number($(id).value) };
    $(`${id}-value`).value = $(id).value;
    savePhysics();
    clearTimeout(sliderTimer);
    sliderTimer = setTimeout(() => reheat(.65), 160);
  }

  cy.on("tap", "node", (e) => detail(e.target));
  cy.on("tap", (e) => {
    if (e.target === cy) {
      state.selected = null;
      clearFocus();
      updateLabels();
    }
  });
  cy.on("mouseover", "node", (e) => {
    state.hovered = e.target.id();
    if (!state.selected) focusNode(e.target, true);
    updateLabels();
  });
  cy.on("mouseout", "node", () => {
    state.hovered = null;
    if (state.selected) focusNode(cy.$id(state.selected));
    else clearFocus();
    updateLabels();
  });
  cy.on("zoom", updateLabels);
  cy.on("grab", "node", (e) => {
    const d = state.forceById.get(e.target.id());
    if (!d) return;
    state.dragGesture = {
      id: e.target.id(),
      position: { ...e.target.position() },
      renderedPosition: { ...e.target.renderedPosition() },
      active: false,
    };
  });
  cy.on("drag", "node", (e) => {
    const gesture = state.dragGesture,
      d = state.forceById.get(e.target.id());
    if (!gesture || gesture.id !== e.target.id() || !d) return;
    if (!gesture.active) {
      const current = e.target.renderedPosition();
      if (
        Math.hypot(
          current.x - gesture.renderedPosition.x,
          current.y - gesture.renderedPosition.y,
        ) < physics.dragThreshold
      ) return;
      gesture.active = true;
      state.simulation?.alphaTarget(.12).alpha(
        Math.max(.35, state.simulation.alpha()),
      ).restart();
      setStatus("Physics reheated by drag");
    }
    d.fx = e.target.position("x");
    d.fy = e.target.position("y");
  });
  cy.on("free", "node", (e) => {
    const gesture = state.dragGesture,
      d = state.forceById.get(e.target.id());
    state.dragGesture = null;
    if (!gesture || gesture.id !== e.target.id() || !d) return;
    if (!gesture.active) {
      e.target.position(gesture.position);
      d.x = gesture.position.x;
      d.y = gesture.position.y;
      d.vx = 0;
      d.vy = 0;
      d.fx = null;
      d.fy = null;
      return;
    }
    d.fx = null;
    d.fy = null;
    state.simulation?.alphaTarget(0).alpha(
      Math.max(.28, state.simulation.alpha()),
    ).restart();
  });
  [
    "search",
    "kind",
    "edgeKind",
    "component",
    "community",
    "tfm",
    "rid",
    "skew",
    "runnable-min",
    "hops",
  ].forEach((id) =>
    $(id).addEventListener(id === "search" ? "input" : "change", applyFilters)
  );
  $("view").value = state.view;
  $("collapse").checked = state.collapsed;
  $("view").onchange = (e) => {
    state.view = e.target.value;
    replace();
  };
  $("collapse").onchange = (e) => {
    state.collapsed = e.target.checked;
    replace();
  };
  $("granularity").value = state.granularity;
  $("granularity").onchange = (e) => { state.granularity = e.target.value; state.selectedCommunities.clear(); replace(); };
  $("color-mode").value = state.colorMode;
  $("color-mode").onchange = (e) => { state.colorMode = e.target.value; replace(); };
  $("size-metric").value = state.sizeMetric;
  $("size-metric").onchange = (e) => { state.sizeMetric = e.target.value; replace(); };
  $("community-search").addEventListener("input", renderCommunityLegend);
  $("community-create").onclick = createFromSelection;
  $("community-merge").onclick = mergeSelectedCommunities;
  $("community-export").onclick = () => downloadJson("community-overrides.json", overrides);
  $("community-effective").onclick = () => downloadJson("effective-community-mapping.json", {
    version: 1, graphFingerprint: analysis.graphFingerprint, granularity: state.granularity,
    assignments: Object.fromEntries(payload.raw.nodes.map(node => { const detected = analysis.nodeAssignments[node.id]?.detectedCommunityPath || [], effective = effectiveKey(node.id); return [node.id, { detectedCommunityPath: detected, effectiveCommunityPath: overrides.nodeAssignments[node.id] ? [effective] : detected.slice(0, Math.max(0, detected.indexOf(effective)) + 1), assignmentSource: overrides.nodeAssignments[node.id] ? "manual" : (analysis.nodeAssignments[node.id]?.assignmentSource || "automatic") }]; }))
  });
  const communityMapButton = document.createElement("button"); communityMapButton.id = "community-map"; communityMapButton.textContent = "Community graph"; communityMapButton.onclick = showCommunityMap; $("community-reset").before(communityMapButton);
  $("community-import").onclick = () => $("community-import-file").click();
  $("community-import-file").onchange = (event) => {
    const file = event.target.files?.[0]; if (!file) return; const reader = new FileReader();
    reader.onload = () => { try { overrides = validateOverrides(JSON.parse(String(reader.result))); saveOverrides(); replace(); } catch (error) { showNotice(`Community override import failed: ${error.message}`); } };
    reader.readAsText(file); event.target.value = "";
  };
  $("community-reset").onclick = () => { if (!confirm("Reset every community override for this graph?")) return; overrides = emptyOverrides(); try { localStorage.removeItem(overrideStorageKey); } catch {} state.selectedCommunities.clear(); replace(); };
  $("explain-path").onclick = explainSelectedPath;
  $("fit").onclick = () => cy.fit(cy.elements(":visible"), 55);
  $("reset").onclick = () => {
    state.selected = null;
    state.hovered = null;
    ["search", "kind", "edgeKind", "component", "community", "tfm", "rid"]
      .forEach((x) => $(x).value = "");
    $("skew").checked = false;
    $("hops").value = "0";
    clearFocus();
    applyFilters();
    cy.fit(55);
  };
  $("pause").onclick = () => {
    if (!state.simulation) return;
    if (state.paused) {
      state.paused = false;
      $("pause").textContent = "Pause physics";
      reheat(.3);
    } else {
      state.paused = true;
      state.simulation.stop();
      cy.edges().removeClass("physics-active");
      $("pause").textContent = "Resume physics";
      setStatus("Physics paused");
    }
  };
  $("rerun").onclick = () => {
    state.run++;
    startSimulation(true);
  };
  $("physics-reset").onclick = () => {
    physics = validatedPhysics(physicsDefaults);
    try {
      localStorage.removeItem(storageKey);
    } catch {}
    syncControls();
    reheat(.8);
  };
  $("important-label-count").addEventListener("input", () => {
    viewerPreferences = {
      ...viewerPreferences,
      importantLabelCount: clamp(
        Number($("important-label-count").value),
        0,
        50,
      ),
    };
    $("important-label-count-value").value = String(
      viewerPreferences.importantLabelCount,
    );
    saveViewerPreferences();
    updateLabels();
  });
  $("drag-threshold").addEventListener("input", () => {
    physics = {
      ...physics,
      dragThreshold: Number($("drag-threshold").value),
    };
    $("drag-threshold-value").value = $("drag-threshold").value;
    savePhysics();
  });
  [
    ["repulsion", "repulsion"],
    ["linkDistance", "link-distance"],
    ["linkStrength", "link-strength"],
    ["collisionPadding", "collision-padding"],
    ["gravity", "gravity"],
    ["communityAttraction", "community-attraction"],
  ].forEach(([key, id]) =>
    $(id).addEventListener("input", () => sliderChanged(key, id))
  );
  $("png").onclick = () => {
    const a = document.createElement("a");
    a.download = "dependency-graph.png";
    a.href = cy.png({ full: true, scale: 2, bg: "#0d1117" });
    a.click();
  };
  $("json").onclick = () => {
    const rawEdges = new Map(graph().edges.map((e) => [e.id, e])),
      edges = cy.edges(":visible").map((e) => {
        const d = e.data(), raw = rawEdges.get(d.rawId) || {};
        return { ...raw, id: d.id, source: d.source, target: d.target };
      });
    const shown = {
      schemaVersion: "display-2.0",
      projection: { view: state.view, collapseLocalPackages: state.collapsed, communityGranularity: state.granularity, effectiveOverrides: Object.keys(overrides.nodeAssignments).length > 0 },
      nodes: cy.nodes(":visible").map((n) => n.data()),
      edges,
    };
    const a = document.createElement("a");
    a.download = "displayed-graph.json";
    a.href = URL.createObjectURL(
      new Blob([JSON.stringify(shown, null, 2)], { type: "application/json" }),
    );
    a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 1000);
  };
  if (!payload.raw.completeness.complete) {
    const w = $("warning");
    w.hidden = false;
    w.replaceChildren(
      document.createTextNode(
        "Incomplete extraction — some projects lack authoritative data. ",
      ),
    );
    const a = document.createElement("a");
    a.href = "diagnostics.json";
    a.textContent = "Open diagnostics";
    w.appendChild(a);
  }
  window.__depgraphDebug = {
    cy,
    state,
    nodeDiameter,
    collisionRadius,
    overviewLabel,
    validatedPhysics,
    overlapCount,
    physics: () => ({ ...physics }),
    viewerPreferences: () => ({ ...viewerPreferences }),
    communityOverrides: () => structuredClone(overrides),
    effectiveMatrix,
    effectiveKey,
    overrideStorageKey,
  };
  syncControls();
  populateFilters();
  renderCommunityLegend();
  startSimulation(false);
  updateLabels();
})();
