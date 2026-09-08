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
      this.regionBodyDrag = null;
      this.nodePointerSelection = null;
      this.pointerAdditive = false;
      this.pointerSelection = [];
      this.manualSelection = [];
      this.contextNodeId = null;
      this.assignmentTargetId = null;
      this.assignmentSelectionKey = "";
      this.layer = document.getElementById("manual-regions");
      this.groupLayer = svgElement("g");
      this.layer.append(this.groupLayer);
      this.setRegionsVisible(false);
      this.installGroupInspector();
      this.installContextMenu();
      this.installButtonIcons();
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
    installContextMenu() {
      const menu = document.createElement("div");
      menu.id = "manual-node-menu";
      menu.setAttribute("role", "menu");
      menu.hidden = true;
      const title = document.createElement("strong");
      title.id = "manual-node-menu-title";
      const remove = document.createElement("button");
      remove.id = "manual-node-remove-group";
      remove.setAttribute("role", "menuitem");
      remove.textContent = "Remove from group";
      remove.onclick = () => this.moveContextNode("group:unassigned");
      const moveLabel = document.createElement("span");
      moveLabel.className = "manual-node-menu-label";
      moveLabel.textContent = "Move to group";
      const options = document.createElement("div");
      options.id = "manual-node-move-options";
      menu.append(title, remove, moveLabel, options);
      menu.addEventListener("contextmenu", (event) => event.preventDefault());
      document.body.append(menu);
      this.contextMenu = menu;
    }
    installButtonIcons() {
      const I = window.DepGraphIcons;
      [
        ["manual-new-group", "add", "New group", false],
        ["manual-assign", "assign", "Move selected", false],
        ["manual-undo", "undo", "Undo", true],
        ["manual-redo", "redo", "Redo", true],
        ["manual-run", "play", "Run layout", false],
        ["manual-arrange", "arrange", "Arrange groups", true],
        ["manual-save", "save", "Save layout", true],
        ["manual-load", "upload", "Load layout", true],
        ["manual-restore-all", "eye", "Restore all connections", false],
        ["manual-pin", "pin", "Pin", false],
        ["manual-unpin", "unpin", "Unpin", false],
        ["manual-mute", "hide", "Hide connections", false],
        ["manual-unmute", "eye", "Restore connections", false],
        ["manual-reveal", "eye", "Reveal temporarily", false],
        ["manual-relax-group", "refresh", "Relax selected group", false],
        ["manual-group-create", "add", "Create", false],
        ["manual-update-group", "save", "Apply group changes", false],
        ["manual-fit-group", "fit", "Fit group to members", false],
        ["manual-merge-groups", "merge", "Merge selected groups", false],
        ["manual-delete-group", "delete", "Delete group", false],
        ["manual-start-over", "refresh", "Start over from Explore", false],
      ].forEach((args) => I.button(...args));
      I.button(
        "manual-node-remove-group",
        "assign",
        "Remove from group",
        false,
      );
    }
    bind() {
      this.cy.container().addEventListener(
        "pointerdown",
        (event) => {
          if (event.button === 0)
            this.pointerAdditive = event.ctrlKey || event.metaKey;
          this.pointerSelection = this.cy
            .nodes(":selected")
            .map((node) => node.id());
        },
        true,
      );
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
      document.getElementById("manual-assign-target").onchange = (event) => {
        this.assignmentTargetId = event.target.value || null;
        this.updateAssignmentTargetHighlight();
        this.updateAssignmentButton();
      };
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
        if (this.active) {
          this.renderInspector();
          this.updateSelectionFocus();
        }
      });
      this.cy.on("tap", "node", (event) => this.nodeTap(event));
      this.cy.on("cxttap", "node", (event) => this.openContextMenu(event));
      this.cy.on("tapstart", (event) => this.regionBodyStart(event));
      this.cy.on("tapdrag", (event) => this.regionBodyMove(event));
      this.cy.on("tapend", (event) => this.regionBodyEnd(event));
      this.cy.on("tap", (event) => {
        if (!this.active || event.target !== this.cy || !this.model) return;
        const group = [...Object.values(this.model.board.groups)]
          .reverse()
          .find(
            (candidate) =>
              !candidate.collapsed && G.contains(candidate, event.position),
          );
        if (group) this.selectGroup(group.id);
        else {
          this.selectedGroupId = null;
          this.cy.nodes().unselect();
          this.paint();
        }
      });
      this.cy.on("mousedown", "node", (event) =>
        this.rememberNodePointerSelection(event),
      );
      this.cy.on("grab", "node", (event) => this.nodeGrab(event));
      this.cy.on("drag", "node", (event) => this.nodeDrag(event));
      this.cy.on("free", "node", (event) => this.nodeFree(event));
      window.addEventListener("keydown", (event) => {
        if (event.key === "Escape") this.hideContextMenu();
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
      document.addEventListener("pointerdown", (event) => {
        if (
          !this.contextMenu.hidden &&
          !this.contextMenu.contains(event.target)
        )
          this.hideContextMenu();
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
      if (this.regionBodyDrag) {
        this.model.board = this.regionBodyDrag.before;
        this.cy.userPanningEnabled(this.regionBodyDrag.panning);
        this.cy.boxSelectionEnabled(this.regionBodyDrag.boxSelection);
        this.regionBodyDrag = null;
      }
      this.manualSelection = this.cy
        .nodes(":selected")
        .map((node) => node.id());
      if (this.model) this.storage.checkpoint(this.model.board);
      this.active = false;
      this.preview = null;
      this.hideContextMenu();
      this.clearSelectionFocus();
      this.setRegionsVisible(false);
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
      this.setRegionsVisible(false);
      this.setLayoutControls("Paused", false, false);
    }
    previewBoard(useGroups) {
      this.preview = S.createBoard(this.getSpec(), useGroups);
      this.applyBoard(this.preview);
      this.setRegionsVisible(true);
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
      this.setRegionsVisible(false);
    }
    mount(board, fresh = false) {
      document.getElementById("manual-empty").hidden = true;
      document.getElementById("manual-workspace").hidden = false;
      document.getElementById("manual-preview-actions").hidden = true;
      document.getElementById("manual-empty-copy").hidden = false;
      this.setRegionsVisible(true);
      this.model = new S.ManualState(board, () => {
        this.storage.save(this.model.board);
        this.paint();
      });
      this.layout = new window.DepGraphManualLayout(
        this.cy,
        this.model,
        () => this.paintLayoutFrame(),
        (status) => this.setLayoutControls(status, false, false),
      );
      this.paint();
      this.setLayoutControls("Paused", false, false);
      this.cy.nodes().unselect();
      this.manualSelection.forEach((id) => this.cy.$id(id).select());
      if (fresh || !board.viewport) this.fitBoard(board);
      else this.cy.viewport(board.viewport);
    }
    setRegionsVisible(visible) {
      this.layer.classList.toggle("manual-regions-visible", visible);
      this.layer.setAttribute("aria-hidden", visible ? "false" : "true");
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
      window.DepGraphIcons.button(
        button,
        paused ? "play" : "pause",
        paused ? "Resume layout" : "Pause layout",
        false,
      );
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
        this.applyPositions(board);
        this.cy.nodes().forEach((node) => {
          const placement = board.placements[node.id()];
          if (!placement) return;
          const group = board.groups[placement.groupId];
          node.data("color", group.color);
          node.data("manualGroup", placement.groupId);
          node.data("manualMuted", muted.has(node.id()));
          node.toggleClass("manual-muted", muted.has(node.id()));
          node.toggleClass("manual-collapsed-member", !!group.collapsed);
        });
        this.cy.edges().forEach((edge) => {
          const sourceId = edge.source().id(),
            targetId = edge.target().id(),
            sourceGroupId = board.placements[sourceId]?.groupId,
            targetGroupId = board.placements[targetId]?.groupId,
            sourceGroup = board.groups[sourceGroupId],
            targetGroup = board.groups[targetGroupId],
            outer = sourceGroupId !== targetGroupId,
            groupHidden =
              outer &&
              (sourceGroup?.outerEdgeMode === "hidden" ||
                targetGroup?.outerEdgeMode === "hidden"),
            groupHighlighted =
              outer &&
              !groupHidden &&
              (sourceGroup?.outerEdgeMode === "highlighted" ||
                targetGroup?.outerEdgeMode === "highlighted"),
            hidden = muted.has(sourceId) || muted.has(targetId),
            collapsedInternal = !outer && !!sourceGroup?.collapsed;
          edge.toggleClass("manual-hidden", hidden);
          edge.toggleClass("manual-collapsed-internal", collapsedInternal);
          edge.toggleClass("manual-group-outer-hidden", groupHidden);
          edge.toggleClass("manual-outer-highlight", groupHighlighted);
          edge.removeClass("manual-reveal");
        });
      });
    }
    applyPositions(board) {
      this.cy.nodes().forEach((node) => {
        const placement = board.placements[node.id()];
        if (!placement) return;
        const group = board.groups[placement.groupId];
        node.position(
          group.collapsed
            ? this.groupAnchor(group)
            : { x: placement.x, y: placement.y },
        );
      });
    }
    paintLayoutFrame() {
      if (!this.active || !this.model) return;
      this.cy.batch(() => this.applyPositions(this.model.board));
    }
    groupAnchor(group) {
      if (group.collapsed)
        return group.shape === "circle"
          ? {
              x: group.cx,
              y: group.cy - group.radius - group.header / 2,
            }
          : {
              x: group.x + group.width / 2,
              y: group.y + group.header / 2,
            };
      const area = G.usable(group);
      return area.shape === "circle"
        ? { x: area.cx, y: area.cy }
        : {
            x: (area.left + area.right) / 2,
            y: (area.top + area.bottom) / 2,
          };
    }
    paint(updateGraph = true) {
      if (!this.model) return;
      if (updateGraph) this.applyBoard(this.model.board);
      this.renderRegions(this.model.board);
      this.renderGroups();
      this.renderInspector();
      this.transform();
      this.updateUndo();
      this.updateSelectionFocus();
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
        if (group.collapsed) root.classList.add("collapsed");
        if (group.id === this.selectedGroupId) root.classList.add("selected");
        if (group.id === this.assignmentTargetId)
          root.classList.add("assignment-target");
        const shape = svgElement(group.shape === "circle" ? "circle" : "rect"),
          header = svgElement("rect"),
          label = svgElement("text"),
          handle = window.DepGraphIcons.svgControl("resize", "Resize group");
        let headerX, headerY, headerWidth;
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
          handle.setAttribute(
            "transform",
            `translate(${group.cx + group.radius * Math.SQRT1_2 - 9} ${group.cy + group.radius * Math.SQRT1_2 - 9})`,
          );
          headerX = group.cx - group.radius;
          headerY = group.cy - group.radius - group.header;
          headerWidth = group.radius * 2;
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
          handle.setAttribute(
            "transform",
            `translate(${group.x + group.width - 18} ${group.y + group.height - 18})`,
          );
          headerX = group.x;
          headerY = group.y;
          headerWidth = group.width;
        }
        shape.classList.add("manual-region-body");
        shape.style.fill = group.color;
        header.classList.add("manual-region-header");
        handle.classList.add("manual-resize-handle");
        const memberCount = Object.values(board.placements).filter(
          (p) => p.groupId === group.id,
        ).length;
        label.textContent = `${group.name} · ${memberCount}`;
        header.addEventListener("pointerdown", (event) =>
          this.regionPointerDown(event, group.id, false),
        );
        handle.addEventListener("pointerdown", (event) =>
          this.regionPointerDown(event, group.id, true),
        );
        header.addEventListener("click", () => this.selectGroup(group.id));
        root.append(shape, header, label, handle);
        const actions = [
          {
            action: "collapse",
            icon: group.collapsed ? "expand" : "collapse",
            active: !!group.collapsed,
            label: group.collapsed ? "Expand group" : "Collapse group",
          },
          {
            action: "hide-outer",
            icon: "hide",
            active: group.outerEdgeMode === "hidden",
            label:
              group.outerEdgeMode === "hidden"
                ? "Show outer edges"
                : "Hide outer edges",
          },
          {
            action: "highlight-outer",
            icon: "highlight",
            active: group.outerEdgeMode === "highlighted",
            label:
              group.outerEdgeMode === "highlighted"
                ? "Clear outer-edge highlight"
                : "Highlight outer edges",
          },
        ];
        actions.forEach((action, index) =>
          root.append(
            this.regionAction(
              group.id,
              action,
              headerX + headerWidth - 62 + index * 20,
              headerY + Math.max(2, (group.header - 18) / 2),
            ),
          ),
        );
        this.groupLayer.append(root);
      });
      this.transform();
    }
    regionAction(groupId, action, x, y) {
      const root = window.DepGraphIcons.svgButton(
        action.icon,
        action.label,
        action.active,
        () => this.toggleGroupControl(groupId, action.action),
      );
      root.classList.add("manual-region-action");
      root.dataset.action = action.action;
      root.setAttribute("transform", `translate(${x} ${y})`);
      return root;
    }
    toggleGroupControl(groupId, action) {
      const group = this.model?.board.groups[groupId];
      if (!group) return;
      try {
        this.layout.stop();
        this.selectedGroupId = groupId;
        if (action === "collapse") {
          if (!group.collapsed)
            this.cy
              .nodes(":selected")
              .filter(
                (node) =>
                  this.model.board.placements[node.id()]?.groupId === groupId,
              )
              .unselect();
          this.model.toggleGroupCollapsed(groupId);
        } else
          this.model.toggleGroupOuterEdges(
            groupId,
            action === "hide-outer" ? "hidden" : "highlighted",
          );
      } catch (error) {
        this.notice(error.message);
      }
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
    regionBodyStart(event) {
      if (!this.active || !this.model || event.target !== this.cy) return;
      const group = [...Object.values(this.model.board.groups)]
        .reverse()
        .find(
          (candidate) =>
            !candidate.collapsed && G.contains(candidate, event.position),
        );
      if (!group) return;
      event.originalEvent?.preventDefault();
      this.layout.stop();
      this.regionBodyDrag = {
        groupId: group.id,
        before: structuredClone(this.model.board),
        origin: { ...event.position },
        moved: false,
        panning: this.cy.userPanningEnabled(),
        boxSelection: this.cy.boxSelectionEnabled(),
      };
      this.cy.userPanningEnabled(false);
      this.cy.boxSelectionEnabled(false);
    }
    regionBodyMove(event) {
      const drag = this.regionBodyDrag;
      if (!drag) return;
      const dx = event.position.x - drag.origin.x,
        dy = event.position.y - drag.origin.y;
      if (Math.hypot(dx, dy) < 1) return;
      drag.moved = true;
      const original = drag.before.groups[drag.groupId],
        target = this.model.board.groups[drag.groupId];
      if (target.shape === "circle") {
        target.cx = original.cx + dx;
        target.cy = original.cy + dy;
      } else {
        target.x = original.x + dx;
        target.y = original.y + dy;
      }
      Object.entries(this.model.board.placements)
        .filter(([, placement]) => placement.groupId === drag.groupId)
        .forEach(([id, placement]) => {
          placement.x = drag.before.placements[id].x + dx;
          placement.y = drag.before.placements[id].y + dy;
        });
      this.paint();
    }
    regionBodyEnd() {
      const drag = this.regionBodyDrag;
      if (!drag) return;
      this.regionBodyDrag = null;
      this.cy.userPanningEnabled(drag.panning);
      this.cy.boxSelectionEnabled(drag.boxSelection);
      if (!drag.moved) return;
      const after = structuredClone(this.model.board);
      this.model.board = drag.before;
      try {
        this.model.transact("Move group", (board) => {
          board.groups[drag.groupId] = after.groups[drag.groupId];
          Object.entries(after.placements)
            .filter(([, placement]) => placement.groupId === drag.groupId)
            .forEach(([id, placement]) => (board.placements[id] = placement));
        });
      } catch (error) {
        this.notice(error.message);
        this.paint();
      }
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
    clearSelectionFocus() {
      this.cy.elements().removeClass("faded upstream downstream hover-edge");
    }
    updateSelectionFocus() {
      this.clearSelectionFocus();
      if (!this.active || !this.model) return;
      const selected = this.cy.nodes(":selected");
      if (!selected.length) return;
      let related = selected;
      selected.forEach((node) => {
        const incoming = node.incomers(),
          outgoing = node.outgoers();
        related = related.union(incoming).union(outgoing);
        incoming.edges().addClass("upstream");
        outgoing.edges().addClass("downstream");
      });
      this.cy.elements().difference(related).addClass("faded");
    }
    openContextMenu(event) {
      if (!this.active || !this.model) return;
      event.originalEvent?.preventDefault();
      const node = event.target,
        nodeId = node.id(),
        placement = this.model.board.placements[nodeId],
        currentGroupId = placement?.groupId;
      if (!placement) return;
      this.contextNodeId = nodeId;
      this.cy.nodes().unselect();
      node.select();
      document.getElementById("manual-node-menu-title").textContent =
        this.model.board.entities[nodeId].label;
      const remove = document.getElementById("manual-node-remove-group");
      remove.disabled = currentGroupId === "group:unassigned";
      remove.title = remove.disabled ? "This node is already unassigned." : "";
      const options = document.getElementById("manual-node-move-options");
      options.replaceChildren();
      Object.values(this.model.board.groups)
        .filter((group) => group.id !== currentGroupId)
        .sort((a, b) => a.name.localeCompare(b.name))
        .forEach((group) => {
          const button = document.createElement("button");
          button.type = "button";
          button.setAttribute("role", "menuitem");
          button.dataset.groupId = group.id;
          button.textContent = group.name;
          button.onclick = () => this.moveContextNode(group.id);
          options.append(button);
        });
      this.contextMenu.hidden = false;
      const canvas = document.getElementById("canvas").getBoundingClientRect(),
        x = canvas.left + event.renderedPosition.x + 8,
        y = canvas.top + event.renderedPosition.y + 8;
      this.contextMenu.style.left = `${Math.max(8, Math.min(x, window.innerWidth - this.contextMenu.offsetWidth - 8))}px`;
      this.contextMenu.style.top = `${Math.max(8, Math.min(y, window.innerHeight - this.contextMenu.offsetHeight - 8))}px`;
    }
    hideContextMenu() {
      if (!this.contextMenu) return;
      this.contextMenu.hidden = true;
      this.contextNodeId = null;
    }
    moveContextNode(groupId) {
      const nodeId = this.contextNodeId;
      if (!nodeId || !this.model?.board.groups[groupId]) return;
      try {
        this.layout.stop();
        this.model.assign([nodeId], groupId);
        this.cy.nodes().unselect();
        this.cy.$id(nodeId).select();
      } catch (error) {
        this.notice(error.message);
      } finally {
        this.hideContextMenu();
      }
    }
    rememberNodePointerSelection(event) {
      const originalEvent = event.originalEvent || {};
      if (originalEvent.button != null && originalEvent.button !== 0) return;
      this.nodePointerSelection = {
        id: event.target.id(),
        additive: !!(
          originalEvent.ctrlKey ||
          originalEvent.metaKey ||
          this.pointerAdditive
        ),
        selection: this.cy.nodes(":selected").map((node) => node.id()),
      };
    }
    nodeGrab(event) {
      if (!this.active || !this.model) return;
      this.layout.stop();
      const id = event.target.id(),
        placement = this.model.board.placements[id],
        pointer =
          this.nodePointerSelection?.id === id
            ? this.nodePointerSelection
            : {
                additive: this.pointerAdditive,
                selection: this.cy.nodes(":selected").map((node) => node.id()),
              },
        additive = pointer.additive,
        selectionBefore = pointer.selection;
      this.nodePointerSelection = null;
      if (!additive) {
        this.cy.nodes().unselect();
        event.target.select();
      } else {
        this.cy.nodes().unselect();
        selectionBefore.forEach((selectedId) =>
          this.cy.$id(selectedId).select(),
        );
        if (!event.target.selected()) event.target.select();
      }
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
      if (!moved) {
        const selected = new Set(this.pointerSelection);
        if (this.pointerAdditive)
          selected.has(drag.id)
            ? selected.delete(drag.id)
            : selected.add(drag.id);
        else {
          selected.clear();
          selected.add(drag.id);
        }
        this.cy.nodes().unselect();
        selected.forEach((id) => this.cy.$id(id).select());
        this.lastNodeGesture = null;
        return;
      }
      this.model.transact("Move node", (board) =>
        Object.assign(board.placements[drag.id], final),
      );
    }
    nodeTap(event) {
      if (!this.active) return;
      const gesture =
        this.lastNodeGesture?.id === event.target.id()
          ? this.lastNodeGesture
          : {
              id: event.target.id(),
              selectionBefore: this.pointerSelection,
              additive: this.pointerAdditive,
              moved: false,
            };
      this.lastNodeGesture = null;
      if (gesture.moved) return;
      const selected = new Set(gesture.selectionBefore),
        additive =
          gesture.additive ||
          this.pointerAdditive ||
          !!(event.originalEvent?.ctrlKey || event.originalEvent?.metaKey);
      if (additive)
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
        groupId = this.assignmentTargetId;
      if (!ids.length) return this.notice("Select one or more nodes first.");
      if (!groupId) return this.notice("Choose a destination group.");
      try {
        this.layout.stop();
        this.model.assign(ids, groupId);
        this.assignmentTargetId = null;
        this.renderAssignmentControls(ids);
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
      const list = document.getElementById("manual-group-list");
      list.replaceChildren();
      Object.values(this.model.board.groups)
        .sort((a, b) => a.name.localeCompare(b.name))
        .forEach((group) => {
          const button = document.createElement("button");
          button.textContent = `${group.name} (${Object.values(this.model.board.placements).filter((p) => p.groupId === group.id).length})`;
          button.style.borderLeftColor = group.color;
          button.onclick = () => this.selectGroup(group.id);
          list.append(button);
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
      this.renderAssignmentControls(ids);
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
    renderAssignmentControls(ids) {
      const target = document.getElementById("manual-assign-target"),
        selectionKey = [...ids].sort().join("|");
      if (selectionKey !== this.assignmentSelectionKey) {
        this.assignmentSelectionKey = selectionKey;
        this.assignmentTargetId = null;
      }
      target.replaceChildren(
        new Option(ids.length ? "Move selected to…" : "Select nodes first", ""),
      );
      if (ids.length)
        Object.values(this.model.board.groups)
          .filter(
            (group) =>
              !ids.every(
                (id) => this.model.board.placements[id]?.groupId === group.id,
              ),
          )
          .sort((a, b) => a.name.localeCompare(b.name))
          .forEach((group) => target.add(new Option(group.name, group.id)));
      const available = [...target.options].some((option) => option.value);
      if (
        this.assignmentTargetId &&
        [...target.options].some(
          (option) => option.value === this.assignmentTargetId,
        )
      )
        target.value = this.assignmentTargetId;
      else this.assignmentTargetId = null;
      target.disabled = !ids.length || !available;
      this.updateAssignmentTargetHighlight();
      this.updateAssignmentButton();
    }
    updateAssignmentTargetHighlight() {
      this.groupLayer
        .querySelectorAll(".manual-region")
        .forEach((region) =>
          region.classList.toggle(
            "assignment-target",
            !!this.assignmentTargetId &&
              region.dataset.groupId === this.assignmentTargetId,
          ),
        );
    }
    updateAssignmentButton() {
      const ids = this.selectedIds(),
        button = document.getElementById("manual-assign"),
        group = this.model?.board.groups[this.assignmentTargetId],
        label = ids.length ? `Move ${ids.length} selected` : "Move selected";
      button.disabled = !ids.length || !group;
      window.DepGraphIcons.button(button, "assign", label, false);
      button.title = group
        ? `${label} to ${group.name}`
        : ids.length
          ? "Choose a destination group"
          : "Select one or more nodes first";
      button.dataset.iconTitle = "false";
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
