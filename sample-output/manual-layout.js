/* Stopped d3 simulations stepped by one scheduler and projected before paint. */
(() => {
  "use strict";
  const G = window.DepGraphManualGeometry;
  class ManualLayout {
    constructor(cy, state, onPaint, onStop) {
      this.cy = cy;
      this.state = state;
      this.onPaint = onPaint;
      this.onStop = onStop;
      this.running = new Map();
      this.frame = 0;
      this.before = null;
      this.paused = false;
    }
    start(groupIds) {
      this.stop(false);
      this.paused = false;
      this.before = structuredClone(this.state.board);
      const board = this.state.board,
        muted = new Set(board.mutedEntityIds),
        requested = new Set(groupIds || Object.keys(board.groups));
      for (const groupId of requested) {
        const members = Object.entries(board.placements)
          .filter(([, p]) => p.groupId === groupId)
          .map(([id, p]) => ({
            id,
            x: p.x,
            y: p.y,
            vx: 0,
            vy: 0,
            fx: p.pinned ? p.x : null,
            fy: p.pinned ? p.y : null,
            radius: board.entities[id].radius,
          }));
        if (!members.length) continue;
        const ids = new Set(members.map((n) => n.id));
        const links = this.cy
          .edges()
          .filter(
            (edge) =>
              ids.has(edge.source().id()) &&
              ids.has(edge.target().id()) &&
              !muted.has(edge.source().id()) &&
              !muted.has(edge.target().id()),
          )
          .map((edge) => ({
            source: edge.source().id(),
            target: edge.target().id(),
          }));
        const region = board.groups[groupId],
          area = G.usable(region),
          center =
            area.shape === "circle"
              ? { x: area.cx, y: area.cy }
              : {
                  x: (area.left + area.right) / 2,
                  y: (area.top + area.bottom) / 2,
                };
        const simulation = d3
          .forceSimulation(members)
          .stop()
          .alpha(1)
          .alphaDecay(0.035)
          .velocityDecay(0.45)
          .force(
            "link",
            d3
              .forceLink(links)
              .id((d) => d.id)
              .distance(70)
              .strength(0.12),
          )
          .force("charge", d3.forceManyBody().strength(-260).distanceMax(450))
          .force(
            "collision",
            d3.forceCollide((d) => d.radius + 5).iterations(4),
          )
          .force("x", d3.forceX(center.x).strength(0.025))
          .force("y", d3.forceY(center.y).strength(0.025));
        this.running.set(groupId, { simulation, members, constrained: 0 });
      }
      if (this.running.size)
        this.frame = requestAnimationFrame(() => this.tick());
      else this.onStop?.("No groups to relax");
    }
    tick() {
      if (this.paused) return;
      const board = this.state.board;
      let active = 0,
        constrained = false;
      for (const [groupId, run] of this.running) {
        if (run.simulation.alpha() < 0.002) continue;
        active++;
        const region = board.groups[groupId];
        run.simulation.tick();
        run.members.forEach((node) => {
          const point = G.project(region, node, node.radius);
          if (point.x !== node.x) node.vx = 0;
          if (point.y !== node.y) node.vy = 0;
          node.x = point.x;
          node.y = point.y;
        });
        for (let pass = 0; pass < 4; pass++)
          for (let i = 0; i < run.members.length; i++)
            for (let j = i + 1; j < run.members.length; j++) {
              const a = run.members[i],
                b = run.members[j],
                minimum = a.radius + b.radius + 4,
                dx = b.x - a.x || 0.001,
                dy = b.y - a.y,
                distance = Math.hypot(dx, dy);
              if (distance >= minimum || (a.fx != null && b.fx != null))
                continue;
              constrained = true;
              const push = (minimum - distance) / 2,
                ux = dx / distance,
                uy = dy / distance;
              if (a.fx == null) {
                a.x -= ux * push;
                a.y -= uy * push;
                Object.assign(a, G.project(region, a, a.radius));
              }
              if (b.fx == null) {
                b.x += ux * push;
                b.y += uy * push;
                Object.assign(b, G.project(region, b, b.radius));
              }
            }
        run.members.forEach((node) =>
          Object.assign(board.placements[node.id], { x: node.x, y: node.y }),
        );
      }
      this.onPaint();
      if (
        active &&
        [...this.running.values()].some(
          (run) => run.simulation.alpha() >= 0.002,
        )
      )
        this.frame = requestAnimationFrame(() => this.tick());
      else {
        const before = this.before;
        this.running.clear();
        this.frame = 0;
        this.before = null;
        this.paused = false;
        if (before) {
          this.state.undoStack.push({ label: "Run layout", board: before });
          this.state.redoStack.length = 0;
          this.state.onChange?.("Layout settled");
        }
        this.onStop?.(
          constrained
            ? "Layout constrained; enlarge group or release pins"
            : "Layout complete · Paused",
        );
      }
    }
    pause() {
      if (!this.running.size || this.paused) return false;
      if (this.frame) cancelAnimationFrame(this.frame);
      this.frame = 0;
      this.paused = true;
      return true;
    }
    resume() {
      if (!this.running.size || !this.paused) return false;
      this.paused = false;
      this.frame = requestAnimationFrame(() => this.tick());
      return true;
    }
    stop(commit = true) {
      const wasActive = this.running.size > 0;
      if (this.frame) cancelAnimationFrame(this.frame);
      this.frame = 0;
      this.running.forEach((run) => run.simulation.stop());
      this.running.clear();
      if (commit && this.before) {
        this.state.undoStack.push({ label: "Run layout", board: this.before });
        this.state.redoStack.length = 0;
        this.state.onChange?.("Layout paused");
      }
      this.before = null;
      this.paused = false;
      if (commit && wasActive) this.onStop?.("Layout stopped · Paused");
    }
  }
  window.DepGraphManualLayout = ManualLayout;
})();
