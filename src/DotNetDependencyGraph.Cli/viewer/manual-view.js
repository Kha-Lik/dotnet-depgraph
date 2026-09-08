/* Manual-layout UI/controller. All repository strings are rendered with textContent. */
(() => {
  "use strict";
  const G = window.DepGraphManualGeometry,
    S = window.DepGraphManualState;
  const svgElement = (name) =>
    document.createElementNS("http://www.w3.org/2000/svg", name);
  class ManualView {
    constructor(options) {
      Object.assign(this, options);
      this.active = false;
      this.model = null;
      this.storage = null;
      this.layout = null;
      this.preview = null;
      this.explore = null;
      this.drag = null;
      this.manualSelection = [];
      this.layer = document.getElementById("manual-regions");
      this.groupLayer = svgElement("g");
      this.layer.append(this.groupLayer);
      this.installGroupInspector();
      this.bind();
    }
    installGroupInspector() {
      const panel = document.createElement("section");
      panel.id = "manual-group-inspector";
      const heading = document.createElement("h3");
      heading.textContent = "Group";
      const makeField = (text, input) => {
        const label = document.createElement("label");
        label.append(document.createTextNode(text), input);
        return label;
      };
      const name = document.createElement("input");
      name.id = "manual-edit-name";
      name.maxLength = 80;
      const color = document.createElement("input");
      color.id = "manual-edit-color";
      color.type = "color";
      const shape = document.createElement("select");
      shape.id = "manual-edit-shape";
      shape.add(new Option("Rectangle", "rectangle"));
      shape.add(new Option("Circle", "circle"));
      const buttons = [
        ["manual-update-group", "Apply group changes"],
        ["manual-fit-group", "Fit group to members"],
        ["manual-merge-groups", "Merge selected groups"],
        ["manual-delete-group", "Delete group"],
      ].map(([id, text]) => {
        const button = document.createElement("button");
        button.id = id;
        button.textContent = text;
        return button;
      });
      panel.append(
        heading,
        makeField("Name", name),
        makeField("Color", color),
        makeField("Shape", shape),
        ...buttons,
      );
      document.querySelector("#manual-workspace .manual-actions").after(panel);
    }
    bind() {
      document.getElementById("tab-explore").onclick = () => this.leave();
      document.getElementById("tab-manual").onclick = () => this.enter();
      document.getElementById("manual-create-current").onclick = () =>
        this.previewBoard(true);
      document.getElementById("manual-create-unassigned").onclick = () =>
        this.previewBoard(false);
      document.getElementById("manual-preview-accept").onclick = () =>
        this.acceptPreview();
      document.getElementById("manual-preview-cancel").onclick = () =>
        this.cancelPreview();
      document.getElementById("manual-undo").onclick = () => {
        this.layout?.stop();
        this.model?.undo();
        this.paint();
      };
      document.getElementById("manual-redo").onclick = () => {
        this.layout?.stop();
        this.model?.redo();
        this.paint();
      };
      document.getElementById("manual-run").onclick = () => {
        if (this.model) {
          this.layout.start();
          this.setLayoutControls("Layout running", true, false);
        }
      };
      document.getElementById("manual-pause").onclick = () => {
        if (!this.layout) return;
        if (this.layout.paused) {
          if (this.layout.resume())
            this.setLayoutControls("Layout running", true, false);
        } else if (this.layout.pause())
          this.setLayoutControls("Layout paused", true, true);
      };
      document.getElementById("manual-arrange").onclick = () =>
        this.arrangeGroups();
      document.getElementById("manual-save").onclick = () =>
        this.model && this.storage.download(this.model.board);
      document.getElementById("manual-load").onclick = () =>
        document.getElementById("manual-load-file").click();
      document.getElementById("manual-load-file").onchange = (event) =>
        this.loadFile(event);
      document.getElementById("manual-start-over").onclick = () =>
        this.startOver();
      document.getElementById("manual-new-group").onclick = () =>
        this.openGroupEditor();
      document.getElementById("manual-group-create").onclick = () =>
        this.createGroup();
      document.getElementById("manual-group-cancel").onclick = () =>
        (document.getElementById("manual-group-editor").hidden = true);
      document.getElementById("manual-assign").onclick = () =>
        this.assignSelected();
      document.getElementById("manual-mute").onclick = () =>
        this.toggleMuted(true);
      document.getElementById("manual-unmute").onclick = () =>
        this.toggleMuted(false);
      document.getElementById("manual-reveal").onclick = () =>
        this.temporaryReveal();
      document.getElementById("manual-restore-all").onclick = () =>
        this.restoreAllConnections();
      document.getElementById("manual-update-group").onclick = () =>
        this.updateGroup();
      document.getElementById("manual-fit-group").onclick = () =>
        this.fitGroup();
      document.getElementById("manual-merge-groups").onclick = () =>
        this.mergeGroups();
      document.getElementById("manual-delete-group").onclick = () =>
        this.deleteGroup();
      this.cy.on("pan zoom", () => {
        if (this.active) {
          this.transform();
          if (this.model) {
            this.model.board.viewport = {
              pan: { ...this.cy.pan() },
              zoom: this.cy.zoom(),
            };
            this.storage?.save(this.model.board);
          }
        }
      });
      this.cy.on("select unselect", "node", () => {
        if (this.active) this.renderInspector();
      });
      this.cy.on("tap", "node", (event) => this.nodeTap(event));
      this.cy.on("tap", (event) => {
        if (!this.active || event.target !== this.cy || !this.model) return;
        const group = [...Object.values(this.model.board.groups)]
          .reverse()
          .find((candidate) => G.contains(candidate, event.position));
        if (group) this.selectGroup(group.id);
        else {
          this.selectedGroupId = null;
          this.cy.nodes().unselect();
          this.paint();
        }
      });
      this.cy.on("grab", "node", (event) => this.nodeGrab(event));
      this.cy.on("drag", "node", (event) => this.nodeDrag(event));
      this.cy.on("free", "node", (event) => this.nodeFree(event));
      window.addEventListener("keydown", (event) => {
        if (!this.active || !(event.ctrlKey || event.metaKey)) return;
        if (event.key.toLowerCase() === "z") {
          event.preventDefault();
          event.shiftKey ? this.model?.redo() : this.model?.undo();
          this.paint();
        } else if (event.key.toLowerCase() === "y") {
          event.preventDefault();
          this.model?.redo();
          this.paint();
        }
      });
      this.setLayoutControls("Paused", false, false);
    }
    scope() {
      const spec = this.getSpec();
      return {
        graphFingerprint: spec.graphFingerprint,
        projectionFingerprint: spec.projectionFingerprint,
        entities: Object.fromEntries(
          spec.nodes.map((n) => [n.id, { canonicalIds: n.canonicalIds }]),
        ),
      };
    }
    enter() {
      if (this.active) return;
      this.active = true;
      this.explore = this.captureExplore();
      this.stopExplore();
      this.setMode("manual");
      this.cy.nodes().unselect();
      this.cy
        .elements()
        .removeClass(
          "faded upstream downstream hover-edge community-highlight",
        );
      document.body.classList.add("manual-mode");
      document
        .getElementById("tab-manual")
        .setAttribute("aria-selected", "true");
      document
        .getElementById("tab-explore")
        .setAttribute("aria-selected", "false");
      const scope = this.scope();
      this.storage = new window.DepGraphManualStorage.ManualStorage(
        scope,
        (text, warning) => this.setSaveStatus(text, warning),
      );
      const saved = this.storage.load();
      if (saved) this.mount(saved);
      else this.showCreate();
    }
    leave() {
      if (!this.active) return;
      this.layout?.stop();
      this.manualSelection = this.cy
        .nodes(":selected")
        .map((node) => node.id());
      if (this.model) this.storage.checkpoint(this.model.board);
      this.active = false;
      this.preview = null;
      this.layer.hidden = true;
      document.body.classList.remove("manual-mode");
      document
        .getElementById("tab-manual")
        .setAttribute("aria-selected", "false");
      document
        .getElementById("tab-explore")
        .setAttribute("aria-selected", "true");
      this.restoreExplore(this.explore);
      this.setMode("explore");
    }
    showCreate() {
      document.getElementById("manual-empty").hidden = false;
      document.getElementById("manual-workspace").hidden = true;
      this.layer.hidden = true;
      this.setLayoutControls("Paused", false, false);
    }
    previewBoard(useGroups) {
      this.preview = S.createBoard(this.getSpec(), useGroups);
      this.applyBoard(this.preview);
      this.layer.hidden = false;
      document.getElementById("manual-preview-actions").hidden = false;
      document.getElementById("manual-empty-copy").hidden = true;
      this.renderRegions(this.preview);
      this.fitBoard(this.preview);
    }
    acceptPreview() {
      if (!this.preview) return;
      this.mount(this.preview, true);
      this.preview = null;
    }
    cancelPreview() {
      this.preview = null;
      document.getElementById("manual-preview-actions").hidden = true;
      document.getElementById("manual-empty-copy").hidden = false;
      this.restoreExplore(this.explore);
      this.stopExplore();
      this.layer.hidden = true;
    }
    mount(board, fresh = false) {
      document.getElementById("manual-empty").hidden = true;
      document.getElementById("manual-workspace").hidden = false;
      document.getElementById("manual-preview-actions").hidden = true;
      document.getElementById("manual-empty-copy").hidden = false;
      this.layer.hidden = false;
      this.model = new S.ManualState(board, () => {
        this.storage.save(this.model.board);
        this.paint();
      });
      this.layout = new window.DepGraphManualLayout(
        this.cy,
        this.model,
        () => this.paint(false),
        (status) => this.setLayoutControls(status, false, false),
      );
      this.paint();
      this.setLayoutControls("Paused", false, false);
      this.cy.nodes().unselect();
      this.manualSelection.forEach((id) => this.cy.$id(id).select());
      if (fresh || !board.viewport) this.fitBoard(board);
      else this.cy.viewport(board.viewport);
    }
    startOver() {
      if (!this.model) return;
      if (
        !confirm(
          "Replace this manual board from the current Explore snapshot? Download it first if you need a portable copy.",
        )
      )
        return;
      this.previewBoard(true);
    }
    setStatus(text) {
      const status = document.getElementById("manual-run-status");
      status.textContent = text;
      status.dataset.state = /running/i.test(text)
        ? "running"
        : /constrained|stopped/i.test(text)
          ? "warning"
          : "paused";
    }
    setLayoutControls(text, active, paused) {
      this.setStatus(text);
      const button = document.getElementById("manual-pause");
      button.disabled = !active;
      button.textContent = paused ? "Resume layout" : "Pause layout";
      button.setAttribute("aria-pressed", paused ? "true" : "false");
    }
    setSaveStatus(text, warning) {
      const status = document.getElementById("manual-save-status");
      status.textContent = text;
      status.classList.toggle("warning", !!warning);
    }
    applyBoard(board) {
      const muted = new Set(board.mutedEntityIds);
      this.cy.batch(() => {
        this.cy.nodes().forEach((node) => {
          const placement = board.placements[node.id()];
          if (!placement) return;
          const group = board.groups[placement.groupId];
          node.position({ x: placement.x, y: placement.y });
          node.data("color", group.color);
          node.data("manualGroup", placement.groupId);
          node.data("manualMuted", muted.has(node.id()));
          node.toggleClass("manual-muted", muted.has(node.id()));
        });
        this.cy.edges().forEach((edge) => {
          const hidden =
            muted.has(edge.source().id()) || muted.has(edge.target().id());
          edge.toggleClass("manual-hidden", hidden);
          edge.removeClass("manual-reveal");
        });
      });
    }
    paint(updateGraph = true) {
      if (!this.model) return;
      if (updateGraph) this.applyBoard(this.model.board);
      this.renderRegions(this.model.board);
      this.renderGroups();
      this.renderInspector();
      this.transform();
      this.updateUndo();
    }
    transform() {
      const pan = this.cy.pan(),
        zoom = this.cy.zoom();
      this.groupLayer.setAttribute(
        "transform",
        `translate(${pan.x} ${pan.y}) scale(${zoom})`,
      );
    }
    renderRegions(board) {
      this.groupLayer.replaceChildren();
      Object.values(board.groups).forEach((group) => {
        const root = svgElement("g");
        root.dataset.groupId = group.id;
        root.classList.add("manual-region");
        if (group.id === this.selectedGroupId) root.classList.add("selected");
        const shape = svgElement(group.shape === "circle" ? "circle" : "rect"),
          header = svgElement("rect"),
          label = svgElement("text"),
          handle = svgElement("rect");
        if (group.shape === "circle") {
          shape.setAttribute("cx", group.cx);
          shape.setAttribute("cy", group.cy);
          shape.setAttribute("r", group.radius);
          header.setAttribute("x", group.cx - group.radius);
          header.setAttribute("y", group.cy - group.radius - group.header);
          header.setAttribute("width", group.radius * 2);
          header.setAttribute("height", group.header);
          label.setAttribute("x", group.cx - group.radius + 8);
          label.setAttribute("y", group.cy - group.radius - 8);
          handle.setAttribute("x", group.cx + group.radius - 8);
          handle.setAttribute("y", group.cy + group.radius - 8);
        } else {
          shape.setAttribute("x", group.x);
          shape.setAttribute("y", group.y);
          shape.setAttribute("width", group.width);
          shape.setAttribute("height", group.height);
          header.setAttribute("x", group.x);
          header.setAttribute("y", group.y);
          header.setAttribute("width", group.width);
          header.setAttribute("height", group.header);
          label.setAttribute("x", group.x + 8);
          label.setAttribute("y", group.y + 19);
          handle.setAttribute("x", group.x + group.width - 8);
          handle.setAttribute("y", group.y + group.height - 8);
        }
        shape.classList.add("manual-region-body");
        shape.style.fill = group.color;
        header.classList.add("manual-region-header");
        handle.classList.add("manual-resize-handle");
        handle.setAttribute("width", 16);
        handle.setAttribute("height", 16);
        label.textContent = `${group.name} · ${Object.values(board.placements).filter((p) => p.groupId === group.id).length}`;
        header.addEventListener("pointerdown", (event) =>
          this.regionPointerDown(event, group.id, false),
        );
        handle.addEventListener("pointerdown", (event) =>
          this.regionPointerDown(event, group.id, true),
        );
        header.addEventListener("click", () => this.selectGroup(group.id));
        root.append(shape, header, label, handle);
        this.groupLayer.append(root);
      });
      this.transform();
    }
    regionPointerDown(event, groupId, resize) {
      if (!this.model) return;
      event.preventDefault();
      this.layout.stop();
      const before = structuredClone(this.model.board),
        origin = this.worldPoint(event),
        group = structuredClone(before.groups[groupId]);
      const move = (current) => {
        const point = this.worldPoint(current),
          dx = point.x - origin.x,
          dy = point.y - origin.y,
          target = this.model.board.groups[groupId];
        if (resize) {
          if (target.shape === "circle")
            target.radius = Math.max(45, group.radius + Math.max(dx, dy));
          else {
            target.width = Math.max(90, group.width + dx);
            target.height = Math.max(target.header + 60, group.height + dy);
          }
        } else {
          if (target.shape === "circle") {
            target.cx = group.cx + dx;
            target.cy = group.cy + dy;
          } else {
            target.x = group.x + dx;
            target.y = group.y + dy;
          }
          Object.entries(this.model.board.placements)
            .filter(([, p]) => p.groupId === groupId)
            .forEach(([id, p]) => {
              p.x = before.placements[id].x + dx;
              p.y = before.placements[id].y + dy;
            });
        }
        this.paint();
      };
      const up = () => {
        window.removeEventListener("pointermove", move);
        window.removeEventListener("pointerup", up);
        window.removeEventListener("pointercancel", up);
        const after = structuredClone(this.model.board);
        this.model.board = before;
        try {
          if (resize) this.model.resizeGroup(groupId, after.groups[groupId]);
          else
            this.model.transact("Move group", (board) => {
              board.groups[groupId] = after.groups[groupId];
              Object.entries(after.placements)
                .filter(([, p]) => p.groupId === groupId)
                .forEach(([id, p]) => (board.placements[id] = p));
            });
        } catch (error) {
          this.notice(error.message);
          this.paint();
        }
      };
      window.addEventListener("pointermove", move);
      window.addEventListener("pointerup", up, { once: true });
      window.addEventListener("pointercancel", up, { once: true });
    }
    worldPoint(event) {
      const rect = this.layer.getBoundingClientRect(),
        pan = this.cy.pan(),
        zoom = this.cy.zoom();
      return {
        x: (event.clientX - rect.left - pan.x) / zoom,
        y: (event.clientY - rect.top - pan.y) / zoom,
      };
    }
    selectedIds() {
      return this.cy
        .nodes(":selected")
        .map((node) => node.id())
        .filter((id) => this.model?.board.entities[id]);
    }
    nodeGrab(event) {
      if (!this.active || !this.model) return;
      this.layout.stop();
      const id = event.target.id(),
        placement = this.model.board.placements[id],
        originalEvent = event.originalEvent || {},
        additive = !!(originalEvent.ctrlKey || originalEvent.metaKey),
        selectionBefore = this.cy.nodes(":selected").map((node) => node.id());
      if (!additive) {
        this.cy.nodes().unselect();
        event.target.select();
      } else if (!event.target.selected()) event.target.select();
      if (placement)
        this.drag = {
          id,
          before: structuredClone(this.model.board),
          original: { x: placement.x, y: placement.y },
          selection: this.cy.nodes(":selected").map((node) => node.id()),
          selectionBefore,
          additive,
        };
    }
    nodeDrag(event) {
      if (!this.active || !this.drag || this.drag.id !== event.target.id())
        return;
      const placement = this.model.board.placements[this.drag.id],
        region = this.model.board.groups[placement.groupId],
        point = G.project(
          region,
          event.target.position(),
          this.model.board.entities[this.drag.id].radius,
        );
      Object.assign(placement, point);
      event.target.position(point);
    }
    nodeFree(event) {
      if (!this.active || !this.drag || this.drag.id !== event.target.id())
        return;
      const drag = this.drag,
        final = { ...this.model.board.placements[drag.id] },
        moved = final.x !== drag.original.x || final.y !== drag.original.y;
      this.drag = null;
      this.lastNodeGesture = {
        id: drag.id,
        selectionBefore: drag.selectionBefore,
        additive: drag.additive,
        moved,
      };
      this.model.board = drag.before;
      this.cy.nodes().unselect();
      drag.selection.forEach((id) => this.cy.$id(id).select());
      if (!moved) return;
      this.model.transact("Move node", (board) =>
        Object.assign(board.placements[drag.id], final),
      );
    }
    nodeTap(event) {
      if (
        !this.active ||
        !this.lastNodeGesture ||
        this.lastNodeGesture.id !== event.target.id()
      )
        return;
      const gesture = this.lastNodeGesture;
      this.lastNodeGesture = null;
      if (gesture.moved) return;
      const selected = new Set(gesture.selectionBefore);
      if (gesture.additive)
        selected.has(gesture.id)
          ? selected.delete(gesture.id)
          : selected.add(gesture.id);
      else {
        selected.clear();
        selected.add(gesture.id);
      }
      this.cy.nodes().unselect();
      selected.forEach((id) => this.cy.$id(id).select());
    }
    openGroupEditor() {
      if (!this.selectedIds().length)
        return this.notice("Select one or more nodes first.");
      document.getElementById("manual-group-editor").hidden = false;
    }
    createGroup() {
      const ids = this.selectedIds(),
        name = document.getElementById("manual-group-name").value.trim(),
        color = document.getElementById("manual-group-color").value,
        shape = document.getElementById("manual-group-shape").value;
      if (!ids.length || !name)
        return this.notice("A name and selected nodes are required.");
      try {
        this.model.addGroup(name, color, shape, ids);
        document.getElementById("manual-group-editor").hidden = true;
      } catch (error) {
        this.notice(error.message);
      }
    }
    assignSelected() {
      const ids = this.selectedIds(),
        groupId = document.getElementById("manual-assign-target").value;
      if (!ids.length) return this.notice("Select one or more nodes first.");
      try {
        this.layout.stop();
        this.model.assign(ids, groupId);
      } catch (error) {
        this.notice(error.message);
      }
    }
    toggleMuted(mute) {
      const ids = this.selectedIds();
      if (!ids.length) return;
      this.model.transact(
        mute ? "Hide connections" : "Restore connections",
        (board) => {
          const muted = new Set(board.mutedEntityIds);
          ids.forEach((id) => (mute ? muted.add(id) : muted.delete(id)));
          board.mutedEntityIds = [...muted].sort();
        },
      );
    }
    temporaryReveal() {
      const ids = new Set(this.selectedIds());
      this.cy
        .edges()
        .filter(
          (edge) => ids.has(edge.source().id()) || ids.has(edge.target().id()),
        )
        .addClass("manual-reveal");
    }
    restoreAllConnections() {
      this.model.transact(
        "Restore all connections",
        (board) => (board.mutedEntityIds = []),
      );
    }
    selectGroup(groupId) {
      this.selectedGroupId = groupId;
      const members = Object.entries(this.model.board.placements)
        .filter(([, p]) => p.groupId === groupId)
        .map(([id]) => this.cy.$id(id));
      this.cy.nodes().unselect();
      members.forEach((node) => node.select());
      this.renderRegions(this.model.board);
      this.renderInspector();
    }
    renderGroups() {
      const list = document.getElementById("manual-group-list"),
        target = document.getElementById("manual-assign-target");
      list.replaceChildren();
      target.replaceChildren();
      Object.values(this.model.board.groups)
        .sort((a, b) => a.name.localeCompare(b.name))
        .forEach((group) => {
          const button = document.createElement("button");
          button.textContent = `${group.name} (${Object.values(this.model.board.placements).filter((p) => p.groupId === group.id).length})`;
          button.style.borderLeftColor = group.color;
          button.onclick = () => this.selectGroup(group.id);
          list.append(button);
          target.add(new Option(group.name, group.id));
        });
      const muted = document.getElementById("manual-muted-list");
      muted.replaceChildren();
      this.model.board.mutedEntityIds.forEach((id) => {
        const button = document.createElement("button");
        button.textContent = this.model.board.entities[id].label;
        button.onclick = () => {
          this.cy.nodes().unselect();
          this.cy.$id(id).select();
          this.renderInspector();
        };
        muted.append(button);
      });
    }
    renderInspector() {
      if (!this.model) return;
      const ids = this.selectedIds(),
        summary = document.getElementById("manual-selection-summary");
      summary.textContent = ids.length
        ? `${ids.length} selected: ${ids
            .slice(0, 4)
            .map((id) => this.model.board.entities[id].label)
            .join(", ")}${ids.length > 4 ? "…" : ""}`
        : "Select nodes or a region.";
      const first = ids[0] && this.model.board.placements[ids[0]],
        groupId = first?.groupId || this.selectedGroupId,
        group = this.model.board.groups[groupId];
      if (first) {
        this.selectedGroupId = first.groupId;
        document.getElementById("manual-assign-target").value = first.groupId;
      }
      if (group) {
        document.getElementById("manual-edit-name").value = group.name;
        document.getElementById("manual-edit-color").value = group.color;
        document.getElementById("manual-edit-shape").value = group.shape;
      }
      document.getElementById("manual-group-inspector").hidden = !group;
      document.getElementById("manual-pin").onclick = () =>
        this.setPinned(true);
      document.getElementById("manual-unpin").onclick = () =>
        this.setPinned(false);
      document.getElementById("manual-relax-group").onclick = () =>
        groupId && this.layout.start([groupId]);
    }
    updateGroup() {
      const groupId = this.selectedGroupId;
      if (!groupId) return;
      try {
        this.layout.stop();
        this.model.updateGroup(groupId, {
          name: document.getElementById("manual-edit-name").value,
          color: document.getElementById("manual-edit-color").value,
          shape: document.getElementById("manual-edit-shape").value,
        });
      } catch (error) {
        this.notice(error.message);
      }
    }
    fitGroup() {
      if (!this.selectedGroupId) return;
      try {
        this.layout.stop();
        this.model.fitGroup(this.selectedGroupId);
      } catch (error) {
        this.notice(error.message);
      }
    }
    mergeGroups() {
      const groups = this.selectedIds().map(
        (id) => this.model.board.placements[id].groupId,
      );
      try {
        this.layout.stop();
        this.model.mergeGroups(groups);
      } catch (error) {
        this.notice(error.message);
      }
    }
    deleteGroup() {
      const groupId = this.selectedGroupId;
      if (
        !groupId ||
        !confirm(
          `Delete ${this.model.board.groups[groupId]?.name || "this group"}? Its members will move to Unassigned.`,
        )
      )
        return;
      try {
        this.layout.stop();
        this.model.deleteGroup(groupId);
        this.selectedGroupId = null;
      } catch (error) {
        this.notice(error.message);
      }
    }
    setPinned(pinned) {
      const ids = this.selectedIds();
      if (!ids.length) return;
      this.model.transact(pinned ? "Pin nodes" : "Unpin nodes", (board) =>
        ids.forEach((id) => (board.placements[id].pinned = pinned)),
      );
    }
    arrangeGroups() {
      if (!this.model) return;
      this.layout.stop();
      this.model.transact("Arrange groups", (board) => {
        const groups = Object.values(board.groups).sort((a, b) =>
            a.id.localeCompare(b.id),
          ),
          columns = Math.ceil(Math.sqrt(groups.length));
        groups.forEach((group, index) => {
          const old = G.envelope(group),
            nx = (index % columns) * 360,
            ny = Math.floor(index / columns) * 330,
            dx = nx - old.left,
            dy = ny - old.top;
          if (group.shape === "circle") {
            group.cx += dx;
            group.cy += dy;
          } else {
            group.x += dx;
            group.y += dy;
          }
          Object.entries(board.placements)
            .filter(([, p]) => p.groupId === group.id)
            .forEach(([, p]) => {
              p.x += dx;
              p.y += dy;
            });
        });
      });
      this.fitBoard(this.model.board);
    }
    fitBoard(board) {
      requestAnimationFrame(() => {
        const boxes = Object.values(board.groups).map(G.envelope),
          left = Math.min(...boxes.map((b) => b.left)),
          right = Math.max(...boxes.map((b) => b.right)),
          top = Math.min(...boxes.map((b) => b.top)),
          bottom = Math.max(...boxes.map((b) => b.bottom)),
          width = this.cy.width(),
          height = this.cy.height(),
          zoom = Math.min(
            1.3,
            Math.max(
              0.04,
              Math.min(
                (width - 80) / Math.max(1, right - left),
                (height - 80) / Math.max(1, bottom - top),
              ),
            ),
          ),
          viewport = {
            zoom,
            pan: {
              x: width / 2 - ((left + right) / 2) * zoom,
              y: height / 2 - ((top + bottom) / 2) * zoom,
            },
          };
        board.viewport = viewport;
        this.cy.viewport(viewport);
      });
    }
    updateUndo() {
      document.getElementById("manual-undo").disabled =
        !this.model.undoStack.length;
      document.getElementById("manual-redo").disabled =
        !this.model.redoStack.length;
    }
    async loadFile(event) {
      const file = event.target.files?.[0];
      event.target.value = "";
      if (!file || !this.model) return;
      try {
        const board = this.storage.importText(await file.text());
        this.layout.stop();
        this.model.replace(board);
        this.fitBoard(board);
      } catch (error) {
        this.notice(`Layout import failed: ${error.message}`);
      }
    }
  }
  window.DepGraphManualView = ManualView;
})();
