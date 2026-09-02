/* Cytoscape.js and d3-force are bundled locally. Repository-derived strings are only assigned through textContent. */
(() => {
  "use strict";

  const payload = JSON.parse(document.getElementById("graph-data").textContent);
  const $ = (id) => document.getElementById(id);
  const state = {
    view: payload.defaults.filterMode || "contract",
    selected: null,
    hovered: null,
    collapsed: !!payload.defaults.collapseLocalPackages,
    simulation: null,
    forceNodes: [],
    forceById: new Map(),
    paused: false,
    run: 0,
    fitted: false,
  };
  const palette = [
    "#58a6ff",
    "#f0883e",
    "#3fb950",
    "#a371f7",
    "#f85149",
    "#d29922",
    "#39c5cf",
    "#db61a2",
    "#7ee787",
    "#79c0ff",
    "#ffa657",
    "#bc8cff",
  ];
  const physicsDefaults = Object.freeze({ ...payload.defaults.physics });
  const storageKey = payload.defaults.physicsStorageKey ||
    "dotnet-depgraph.physics.v2";
  const viewerStorageKey = payload.defaults.viewerStorageKey ||
    "dotnet-depgraph.viewer.v1";
  const defaultImportantLabelCount = payload.defaults.importantLabelCount ?? 12;
  const bounds = {
    repulsion: [100, 5000],
    linkDistance: [30, 240],
    linkStrength: [.02, 1],
    collisionPadding: [2, 50],
    gravity: [0, .2],
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
            Math.max(0, d.transitiveDependents || 0) +
              Math.max(0, d.inDegree || 0) + 1,
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
      return validatedPhysics(JSON.parse(localStorage.getItem(storageKey)));
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
  function graph() {
    return payload[state.view] || payload.raw;
  }

  function elements() {
    const g = graph(), producer = new Map();
    if (state.collapsed) {
      payload.raw.edges.filter((e) => e.kind === "produces-package").forEach(
        (e) => producer.set(e.target, e.source),
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
      ...visibleNodes.map((n) => ({
        data: {
          ...n,
          size: nodeDiameter(n),
          rank: ranks.get(n.id),
          color: n.colorHint ||
            palette[(n.community < 0 ? 0 : n.community) % palette.length],
        },
      })),
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
          "border-width": 1,
          "border-color": "#0d1117",
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
          "border-color": "#f0f6fc",
          "border-width": 1.5,
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
        selector: "node[versionSkew]",
        style: { "border-color": "#f85149", "border-width": 2.5 },
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
      );
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
      x: n.position("x"),
      y: n.position("y"),
    }));
    state.forceNodes = nodes;
    state.forceById = new Map(nodes.map((n) => [n.id, n]));
    componentCenters(nodes);
    nodes.forEach((n, i) => initialPosition(n, i, randomize));
    refreshPositions();
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
    populate("community", new Set(g.nodes.map((n) => n.community)));
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
  function applyFilters() {
    cy.elements().removeClass("faded search-match").style("display", "element");
    const kind = $("kind").value,
      ek = $("edgeKind").value,
      component = $("component").value,
      community = $("community").value,
      tfm = $("tfm").value,
      rid = $("rid").value,
      skew = $("skew").checked,
      q = $("search").value.trim().toLowerCase();
    cy.nodes().forEach((n) => {
      const d = n.data(),
        match = (!kind || d.kind === kind) &&
          (!component || String(d.component) === component) &&
          (!community || String(d.community) === community) &&
          (!tfm || (d.targetFrameworks || []).includes(tfm)) &&
          (!rid || (d.runtimeIdentifiers || []).includes(rid)) &&
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
      Classification: d.classification || "—",
      Path: d.path || "—",
      Versions: (d.versions || []).join(", ") || "—",
      Frameworks: (d.targetFrameworks || []).join(", ") || "—",
      RIDs: (d.runtimeIdentifiers || []).join(", ") || "—",
      Component: d.component,
      Community: d.community,
      "Direct dependencies": d.directDependencies,
      "Transitive dependencies": d.transitiveDependencies,
      "Direct dependents": d.directDependents,
      "Transitive dependents": d.transitiveDependents,
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
      gravity: "gravity",
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
    d.fx = e.target.position("x");
    d.fy = e.target.position("y");
    state.simulation?.alphaTarget(.12).alpha(
      Math.max(.35, state.simulation.alpha()),
    ).restart();
    setStatus("Physics reheated by drag");
  });
  cy.on("drag", "node", (e) => {
    const d = state.forceById.get(e.target.id());
    if (d) {
      d.fx = e.target.position("x");
      d.fy = e.target.position("y");
    }
  });
  cy.on("free", "node", (e) => {
    const d = state.forceById.get(e.target.id());
    if (!d) return;
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
  [
    ["repulsion", "repulsion"],
    ["linkDistance", "link-distance"],
    ["linkStrength", "link-strength"],
    ["collisionPadding", "collision-padding"],
    ["gravity", "gravity"],
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
      schemaVersion: "display-1.0",
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
  };
  syncControls();
  populateFilters();
  startSimulation(false);
  updateLabels();
})();
