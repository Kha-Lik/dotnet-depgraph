/* Versioned manual-board model and session command history. */
(() => {
  "use strict";
  const G = window.DepGraphManualGeometry;
  const clone = (value) => structuredClone(value);
  const color = (value) => /^#[0-9a-f]{6}$/i.test(value || "");
  const id = (prefix) =>
    `${prefix}:${crypto?.randomUUID?.() || `${Date.now()}-${Math.random().toString(16).slice(2)}`}`;
  function translateGroup(board, groupId, dx, dy) {
    const group = board.groups[groupId];
    if (group.shape === "circle") {
      group.cx += dx;
      group.cy += dy;
    } else {
      group.x += dx;
      group.y += dy;
    }
    Object.values(board.placements)
      .filter((placement) => placement.groupId === groupId)
      .forEach((placement) => {
        placement.x += dx;
        placement.y += dy;
      });
  }
  function supergroupBounds(board, supergroup) {
    const envelopes = supergroup.groupIds
        .map((groupId) => board.groups[groupId])
        .filter(Boolean)
        .map(G.envelope),
      padding = 24,
      header = 30,
      x = Math.min(...envelopes.map((value) => value.left)) - padding,
      y = Math.min(...envelopes.map((value) => value.top)) - padding - header,
      width =
        Math.max(...envelopes.map((value) => value.right)) -
        Math.min(...envelopes.map((value) => value.left)) +
        padding * 2,
      height =
        Math.max(...envelopes.map((value) => value.bottom)) -
        Math.min(...envelopes.map((value) => value.top)) +
        padding * 2 +
        header;
    return {
      x,
      y,
      width,
      height,
      left: x,
      right: x + width,
      top: y,
      bottom: y + height,
      header,
    };
  }
  function translateSupergroup(board, supergroupId, dx, dy) {
    board.supergroups[supergroupId].groupIds.forEach((groupId) =>
      translateGroup(board, groupId, dx, dy),
    );
  }
  function cleanSupergroups(board) {
    Object.entries(board.supergroups || {}).forEach(([supergroupId, value]) => {
      value.groupIds = [
        ...new Set(value.groupIds.filter((groupId) => board.groups[groupId])),
      ];
      value.pinnedGroupIds = [
        ...new Set(
          (value.pinnedGroupIds || []).filter((groupId) =>
            value.groupIds.includes(groupId),
          ),
        ),
      ];
      if (value.groupIds.length < 2) delete board.supergroups[supergroupId];
    });
  }
  function repackForShape(board, groupId, shape) {
    const group = board.groups[groupId],
      env = G.envelope(group),
      memberIds = Object.entries(board.placements)
        .filter(([, placement]) => placement.groupId === groupId)
        .map(([entityId]) => entityId),
      header = shape === "circle" ? 24 : 28;
    if (shape === "circle") {
      delete group.x;
      delete group.y;
      delete group.width;
      delete group.height;
      Object.assign(group, {
        shape,
        cx: (env.left + env.right) / 2,
        cy: (env.top + group.header + env.bottom) / 2,
        radius: Math.max(
          55,
          Math.min(env.right - env.left, env.bottom - env.top - group.header) /
            2,
        ),
        header,
      });
    } else {
      delete group.cx;
      delete group.cy;
      delete group.radius;
      Object.assign(group, {
        shape,
        x: env.left,
        y: env.top,
        width: Math.max(90, env.right - env.left),
        height: Math.max(header + 60, env.bottom - env.top),
        header,
      });
    }
    let points = null;
    for (let attempt = 0; attempt < 16 && !points; attempt++) {
      const pinned = memberIds.filter(
          (entityId) => board.placements[entityId].pinned,
        ),
        movable = memberIds.filter(
          (entityId) => !board.placements[entityId].pinned,
        );
      if (shape === "circle") {
        group.radius = Math.max(
          group.radius,
          ...pinned.map(
            (entityId) =>
              Math.hypot(
                board.placements[entityId].x - group.cx,
                board.placements[entityId].y - group.cy,
              ) +
              board.entities[entityId].radius +
              8,
          ),
          55,
        );
      } else if (pinned.length) {
        const left = Math.min(
            group.x,
            ...pinned.map(
              (entityId) =>
                board.placements[entityId].x -
                board.entities[entityId].radius -
                8,
            ),
          ),
          right = Math.max(
            group.x + group.width,
            ...pinned.map(
              (entityId) =>
                board.placements[entityId].x +
                board.entities[entityId].radius +
                8,
            ),
          ),
          top = Math.min(
            group.y,
            ...pinned.map(
              (entityId) =>
                board.placements[entityId].y -
                board.entities[entityId].radius -
                header -
                8,
            ),
          ),
          bottom = Math.max(
            group.y + group.height,
            ...pinned.map(
              (entityId) =>
                board.placements[entityId].y +
                board.entities[entityId].radius +
                8,
            ),
          );
        Object.assign(group, {
          x: left,
          y: top,
          width: right - left,
          height: bottom - top,
        });
      }
      const occupied = pinned.map((entityId) => ({
          ...board.placements[entityId],
          radius: board.entities[entityId].radius,
        })),
        candidate = G.slots(
          group,
          movable.map((entityId) => board.entities[entityId].radius),
          occupied,
        );
      if (candidate.every(Boolean)) points = { movable, candidate };
      else if (shape === "circle") group.radius *= 1.18;
      else {
        group.width *= 1.18;
        group.height = header + (group.height - header) * 1.18;
      }
    }
    if (!points)
      throw new Error(
        "The members cannot be packed into that shape while preserving pins.",
      );
    points.movable.forEach((entityId, index) =>
      Object.assign(board.placements[entityId], points.candidate[index]),
    );
    const others = Object.values(board.groups).filter(
      (candidate) => candidate.id !== groupId,
    );
    if (others.some((candidate) => !G.separated(group, candidate))) {
      const current = G.envelope(group),
        left =
          Math.max(...others.map((candidate) => G.envelope(candidate).right)) +
          40;
      translateGroup(board, groupId, left - current.left, 0);
    }
  }
  class ManualState {
    constructor(board, onChange) {
      board.supergroups ||= {};
      this.board = board;
      this.undoStack = [];
      this.redoStack = [];
      this.onChange = onChange;
    }
    transact(label, change) {
      const before = clone(this.board),
        draft = clone(this.board);
      change(draft);
      const errors = G.validate(draft);
      if (errors.length) throw new Error(errors[0]);
      this.undoStack.push({ label, board: before });
      if (this.undoStack.length > 60) this.undoStack.shift();
      this.redoStack.length = 0;
      this.board = draft;
      this.onChange?.(label);
      return this.board;
    }
    replace(board, label = "Load layout") {
      const previous = this.board;
      this.board = board;
      try {
        const errors = G.validate(board);
        if (errors.length) throw new Error(errors[0]);
      } catch (e) {
        this.board = previous;
        throw e;
      }
      this.undoStack.push({ label, board: clone(previous) });
      this.redoStack.length = 0;
      this.onChange?.(label);
    }
    undo() {
      const item = this.undoStack.pop();
      if (!item) return;
      this.redoStack.push({ label: item.label, board: clone(this.board) });
      this.board = item.board;
      this.onChange?.("Undo");
    }
    redo() {
      const item = this.redoStack.pop();
      if (!item) return;
      this.undoStack.push({ label: item.label, board: clone(this.board) });
      this.board = item.board;
      this.onChange?.("Redo");
    }
    addGroup(name, groupColor, shape, entityIds) {
      const groupId = id("group");
      this.transact("Create group", (board) => {
        const sourceGroups = [
            ...new Set(
              entityIds.map((entityId) => board.placements[entityId]?.groupId),
            ),
          ]
            .map((sourceId) => board.groups[sourceId])
            .filter(Boolean),
          sourceEnvelopes = sourceGroups.map(G.envelope),
          sourceBounds = {
            left: Math.min(...sourceEnvelopes.map((env) => env.left)),
            right: Math.max(...sourceEnvelopes.map((env) => env.right)),
            top: Math.min(...sourceEnvelopes.map((env) => env.top)),
            bottom: Math.max(...sourceEnvelopes.map((env) => env.bottom)),
          },
          radii = entityIds.map((entityId) => board.entities[entityId].radius),
          maximum = Math.max(12, ...radii),
          spacing = maximum * 2 + 10,
          columns = Math.max(1, Math.ceil(Math.sqrt(entityIds.length))),
          rows = Math.max(1, Math.ceil(entityIds.length / columns));
        const group =
          shape === "circle"
            ? {
                id: groupId,
                name,
                color: groupColor,
                shape,
                cx: 0,
                cy: 0,
                radius: Math.max(
                  70,
                  Math.sqrt(entityIds.length) * spacing * 0.72,
                ),
                header: 24,
              }
            : {
                id: groupId,
                name,
                color: groupColor,
                shape: "rectangle",
                x: 0,
                y: 0,
                width: Math.max(
                  140,
                  maximum * 2 + (columns - 1) * spacing + 24,
                ),
                height: Math.max(
                  120,
                  28 + maximum * 2 + (rows - 1) * spacing + 24,
                ),
                header: 28,
              };
        group.collapsed = false;
        group.outerEdgeMode = "visible";
        let placed = null;
        for (let attempt = 0; attempt < 24 && !placed; attempt++) {
          const candidate = G.slots(group, radii);
          if (candidate.every(Boolean)) placed = candidate;
          else if (group.shape === "circle") group.radius *= 1.16;
          else {
            group.width *= 1.12;
            group.height = group.header + (group.height - group.header) * 1.12;
          }
        }
        if (!placed)
          throw new Error("The selected nodes could not be packed safely.");
        const initial = G.envelope(group),
          dx = sourceBounds.right + 40 - initial.left,
          dy = sourceBounds.top - initial.top,
          translate = (moveX, moveY) => {
            if (group.shape === "circle") {
              group.cx += moveX;
              group.cy += moveY;
            } else {
              group.x += moveX;
              group.y += moveY;
            }
            placed.forEach((point) => {
              point.x += moveX;
              point.y += moveY;
            });
          };
        translate(dx, dy);
        while (
          Object.values(board.groups).some(
            (existing) => !G.separated(existing, group),
          )
        ) {
          const envelope = G.envelope(group);
          translate(0, envelope.bottom - envelope.top + 40);
        }
        board.groups[groupId] = group;
        entityIds.forEach((entityId, index) =>
          Object.assign(board.placements[entityId], placed[index], { groupId }),
        );
      });
      return groupId;
    }
    assign(entityIds, groupId) {
      this.transact("Assign nodes", (board) => {
        const group = board.groups[groupId];
        if (!group) throw new Error("Choose an existing group.");
        const moving = new Set(entityIds),
          occupied = Object.entries(board.placements)
            .filter(([key, p]) => p.groupId === groupId && !moving.has(key))
            .map(([key, p]) => ({
              x: p.x,
              y: p.y,
              radius: board.entities[key].radius,
            }));
        const radii = entityIds.map((key) => board.entities[key].radius);
        let slots = null;
        for (let attempt = 0; attempt < 24 && !slots; attempt++) {
          const candidate = G.slots(group, radii, clone(occupied));
          if (candidate.every(Boolean)) slots = candidate;
          else if (group.shape === "circle") group.radius *= 1.16;
          else {
            group.width *= 1.12;
            group.height = group.header + (group.height - group.header) * 1.12;
          }
        }
        if (!slots)
          throw new Error("The selected nodes could not be packed safely.");

        const others = Object.values(board.groups).filter(
          (candidate) => candidate.id !== groupId,
        );
        if (others.some((candidate) => !G.separated(group, candidate))) {
          const current = G.envelope(group),
            left =
              Math.max(
                ...others.map((candidate) => G.envelope(candidate).right),
              ) + 40,
            dx = left - current.left;
          translateGroup(board, groupId, dx, 0);
          slots.forEach((point) => (point.x += dx));
        }
        entityIds.forEach((key, index) =>
          Object.assign(board.placements[key], slots[index], { groupId }),
        );
      });
    }
    toggleGroupCollapsed(groupId) {
      this.transact("Toggle group collapse", (board) => {
        const group = board.groups[groupId];
        if (!group) throw new Error("Choose an existing group.");
        group.collapsed = !group.collapsed;
      });
    }
    toggleGroupOuterEdges(groupId, mode) {
      if (!["hidden", "highlighted"].includes(mode))
        throw new Error("Unknown outer-edge mode.");
      this.transact("Change group outer edges", (board) => {
        const group = board.groups[groupId];
        if (!group) throw new Error("Choose an existing group.");
        group.outerEdgeMode = group.outerEdgeMode === mode ? "visible" : mode;
      });
    }
    updateGroup(groupId, values) {
      this.transact("Edit group", (board) => {
        const group = board.groups[groupId];
        if (!group) throw new Error("Choose a group first.");
        if (values.name != null) {
          const name = values.name.trim();
          if (!name) throw new Error("Group name cannot be empty.");
          group.name = name;
        }
        if (values.color != null) {
          if (!color(values.color))
            throw new Error("Group color must be #RRGGBB.");
          group.color = values.color;
        }
        if (values.shape && values.shape !== group.shape)
          repackForShape(board, groupId, values.shape);
      });
    }
    resizeGroup(groupId, requested) {
      this.transact("Resize group", (board) => {
        const group = board.groups[groupId],
          original = clone(group),
          memberIds = Object.entries(board.placements)
            .filter(([, placement]) => placement.groupId === groupId)
            .map(([entityId]) => entityId);
        if (!group || requested.shape !== group.shape)
          throw new Error("The resized group is invalid.");
        if (group.shape === "circle")
          group.radius = Math.max(45, Number(requested.radius));
        else {
          group.width = Math.max(90, Number(requested.width));
          group.height = Math.max(group.header + 60, Number(requested.height));
        }
        if (
          memberIds.every((entityId) =>
            G.contains(
              group,
              board.placements[entityId],
              board.entities[entityId].radius,
            ),
          )
        )
          return;
        const pinned = memberIds.filter(
            (entityId) => board.placements[entityId].pinned,
          ),
          movable = memberIds.filter(
            (entityId) => !board.placements[entityId].pinned,
          );
        let packed = null;
        for (let attempt = 0; attempt < 20 && !packed; attempt++) {
          const pinsFit = pinned.every((entityId) =>
            G.contains(
              group,
              board.placements[entityId],
              board.entities[entityId].radius,
            ),
          );
          if (pinsFit) {
            const occupied = pinned.map((entityId) => ({
                ...board.placements[entityId],
                radius: board.entities[entityId].radius,
              })),
              points = G.slots(
                group,
                movable.map((entityId) => board.entities[entityId].radius),
                occupied,
              );
            if (points.every(Boolean)) packed = points;
          }
          if (packed) break;
          if (group.shape === "circle")
            group.radius = Math.min(
              original.radius,
              Math.max(group.radius + 8, group.radius * 1.1),
            );
          else {
            group.width = Math.min(
              original.width,
              Math.max(group.width + 12, group.width * 1.08),
            );
            group.height = Math.min(
              original.height,
              Math.max(group.height + 12, group.height * 1.08),
            );
          }
        }
        if (!packed)
          throw new Error(
            "The requested size cannot accommodate this group, even after attempting a compact repack. Release pins or choose a larger size.",
          );
        movable.forEach((entityId, index) =>
          Object.assign(board.placements[entityId], packed[index]),
        );
      });
    }
    fitGroup(groupId) {
      this.transact("Fit group to members", (board) => {
        const group = board.groups[groupId],
          members = Object.entries(board.placements).filter(
            ([, p]) => p.groupId === groupId,
          );
        if (!group || !members.length) return;
        if (group.shape === "circle") {
          const cx =
              members.reduce((sum, [, p]) => sum + p.x, 0) / members.length,
            cy = members.reduce((sum, [, p]) => sum + p.y, 0) / members.length;
          group.cx = cx;
          group.cy = cy;
          group.radius = Math.max(
            55,
            ...members.map(
              ([entityId, p]) =>
                Math.hypot(p.x - cx, p.y - cy) +
                board.entities[entityId].radius +
                12,
            ),
          );
        } else {
          const left =
              Math.min(
                ...members.map(
                  ([entityId, p]) => p.x - board.entities[entityId].radius,
                ),
              ) - 12,
            right =
              Math.max(
                ...members.map(
                  ([entityId, p]) => p.x + board.entities[entityId].radius,
                ),
              ) + 12,
            top =
              Math.min(
                ...members.map(
                  ([entityId, p]) => p.y - board.entities[entityId].radius,
                ),
              ) -
              group.header -
              12,
            bottom =
              Math.max(
                ...members.map(
                  ([entityId, p]) => p.y + board.entities[entityId].radius,
                ),
              ) + 12;
          Object.assign(group, {
            x: left,
            y: top,
            width: Math.max(90, right - left),
            height: Math.max(group.header + 60, bottom - top),
          });
        }
      });
    }
    addSupergroup(name, groupColor, groupIds) {
      const unique = [...new Set(groupIds)],
        occupied = new Set(
          Object.values(this.board.supergroups || {}).flatMap(
            (supergroup) => supergroup.groupIds,
          ),
        );
      if (unique.length < 2)
        throw new Error("Select at least two groups for a supergroup.");
      if (unique.some((groupId) => !this.board.groups[groupId]))
        throw new Error("Every selected group must still exist.");
      if (unique.some((groupId) => occupied.has(groupId)))
        throw new Error("A group can belong to only one supergroup.");
      const supergroupId = id("supergroup");
      this.transact("Create supergroup", (board) => {
        board.supergroups ||= {};
        board.supergroups[supergroupId] = {
          id: supergroupId,
          name: name.trim() || "Supergroup",
          color: color(groupColor) ? groupColor : "#8b5cf6",
          groupIds: unique,
          pinnedGroupIds: [],
          collapsed: false,
          outerEdgeMode: "visible",
        };
      });
      return supergroupId;
    }
    toggleSupergroupCollapsed(supergroupId) {
      this.transact("Toggle supergroup collapse", (board) => {
        const supergroup = board.supergroups?.[supergroupId];
        if (!supergroup) throw new Error("Choose an existing supergroup.");
        supergroup.collapsed = !supergroup.collapsed;
      });
    }
    toggleSupergroupOuterEdges(supergroupId, mode) {
      if (!["hidden", "highlighted"].includes(mode))
        throw new Error("Unknown outer-edge mode.");
      this.transact("Change supergroup outer edges", (board) => {
        const supergroup = board.supergroups?.[supergroupId];
        if (!supergroup) throw new Error("Choose an existing supergroup.");
        supergroup.outerEdgeMode =
          supergroup.outerEdgeMode === mode ? "visible" : mode;
      });
    }
    toggleGroupPinnedInSupergroup(groupId) {
      this.transact("Toggle group pin in supergroup", (board) => {
        const supergroup = Object.values(board.supergroups || {}).find(
          (candidate) => candidate.groupIds.includes(groupId),
        );
        if (!supergroup)
          throw new Error("Only groups inside a supergroup can be pinned.");
        supergroup.pinnedGroupIds ||= [];
        if (supergroup.pinnedGroupIds.includes(groupId))
          supergroup.pinnedGroupIds = supergroup.pinnedGroupIds.filter(
            (candidate) => candidate !== groupId,
          );
        else supergroup.pinnedGroupIds.push(groupId);
      });
    }
    arrangeSupergroup(supergroupId) {
      this.transact("Arrange groups in supergroup", (board) => {
        const supergroup = board.supergroups?.[supergroupId];
        if (!supergroup) throw new Error("Choose an existing supergroup.");
        const groups = supergroup.groupIds
            .map((groupId) => board.groups[groupId])
            .filter(Boolean),
          pinned = new Set(supergroup.pinnedGroupIds || []),
          envelopes = groups.map(G.envelope),
          origin = {
            x: Math.min(...envelopes.map((value) => value.left)),
            y: Math.min(...envelopes.map((value) => value.top)),
          },
          gap = 24,
          cellWidth =
            Math.max(
              ...envelopes.map((value) => value.right - value.left),
            ) + gap,
          cellHeight =
            Math.max(
              ...envelopes.map((value) => value.bottom - value.top),
            ) + gap,
          columns = Math.max(1, Math.ceil(Math.sqrt(groups.length))),
          occupied = groups
            .filter((group) => pinned.has(group.id))
            .map((group) => group);
        let slot = 0;
        groups
          .filter((group) => !pinned.has(group.id))
          .sort((a, b) => a.id.localeCompare(b.id))
          .forEach((group) => {
            const current = G.envelope(group),
              width = current.right - current.left,
              height = current.bottom - current.top;
            let candidate;
            do {
              candidate = {
                shape: "rectangle",
                x: origin.x + (slot % columns) * cellWidth,
                y: origin.y + Math.floor(slot / columns) * cellHeight,
                width,
                height,
                header: 0,
              };
              slot++;
            } while (
              occupied.some((other) => !G.separated(candidate, other, gap))
            );
            translateGroup(
              board,
              group.id,
              candidate.x - current.left,
              candidate.y - current.top,
            );
            occupied.push(group);
          });
        const childIds = new Set(supergroup.groupIds),
          external = Object.values(board.groups).filter(
            (group) => !childIds.has(group.id),
          );
        if (
          groups.some((group) =>
            external.some((other) => !G.separated(group, other)),
          )
        ) {
          const bounds = supergroupBounds(board, supergroup),
            right =
              Math.max(...external.map((group) => G.envelope(group).right)) +
              40;
          translateSupergroup(board, supergroupId, right - bounds.left, 0);
        }
      });
    }
    moveGroupToSupergroup(groupId, targetSupergroupId) {
      this.transact(
        targetSupergroupId
          ? "Move group to supergroup"
          : "Remove group from supergroup",
        (board) => {
          if (!board.groups[groupId])
            throw new Error("Choose an existing group.");
          const source = Object.values(board.supergroups || {}).find(
            (supergroup) => supergroup.groupIds.includes(groupId),
          );
          if (source) {
            source.groupIds = source.groupIds.filter(
              (candidate) => candidate !== groupId,
            );
            source.pinnedGroupIds = (source.pinnedGroupIds || []).filter(
              (candidate) => candidate !== groupId,
            );
          }
          if (targetSupergroupId) {
            const target = board.supergroups?.[targetSupergroupId];
            if (!target) throw new Error("Choose an existing supergroup.");
            if (!target.groupIds.includes(groupId))
              target.groupIds.push(groupId);
          } else if (!source)
            throw new Error("This group is not inside a supergroup.");
          cleanSupergroups(board);
        },
      );
    }
    deleteSupergroup(supergroupId) {
      this.transact("Dissolve supergroup", (board) => {
        if (!board.supergroups?.[supergroupId])
          throw new Error("Choose an existing supergroup.");
        delete board.supergroups[supergroupId];
      });
    }
    moveSupergroup(supergroupId, dx, dy) {
      this.transact("Move supergroup", (board) =>
        translateSupergroup(board, supergroupId, dx, dy),
      );
    }
    deleteGroup(groupId) {
      const members = Object.entries(this.board.placements)
        .filter(([, p]) => p.groupId === groupId)
        .map(([entityId]) => entityId);
      if (groupId === "group:unassigned" && members.length)
        throw new Error("Unassigned cannot be deleted while it has members.");
      this.transact("Delete group", (board) => {
        if (members.length) {
          const unassigned = board.groups["group:unassigned"],
            occupied = Object.entries(board.placements)
              .filter(([, p]) => p.groupId === "group:unassigned")
              .map(([key, p]) => ({
                x: p.x,
                y: p.y,
                radius: board.entities[key].radius,
              })),
            radii = members.map((key) => board.entities[key].radius);
          let points = G.slots(unassigned, radii, clone(occupied));
          while (points.some((point) => !point)) {
            if (unassigned.shape === "circle") unassigned.radius *= 1.2;
            else {
              unassigned.width *= 1.2;
              unassigned.height =
                unassigned.header +
                (unassigned.height - unassigned.header) * 1.2;
            }
            points = G.slots(unassigned, radii, clone(occupied));
          }
          members.forEach((key, index) =>
            Object.assign(board.placements[key], points[index], {
              groupId: "group:unassigned",
            }),
          );
          const remaining = Object.values(board.groups).filter(
            (group) =>
              group.id !== "group:unassigned" && group.id !== groupId,
          );
          if (remaining.some((group) => !G.separated(unassigned, group))) {
            const current = G.envelope(unassigned),
              left =
                Math.max(...remaining.map((group) => G.envelope(group).right)) +
                40;
            translateGroup(
              board,
              "group:unassigned",
              left - current.left,
              0,
            );
          }
        }
        delete board.groups[groupId];
        cleanSupergroups(board);
      });
    }
    mergeGroups(groupIds) {
      const unique = [...new Set(groupIds)];
      if (unique.length < 2)
        throw new Error("Select nodes from at least two groups.");
      this.transact("Merge groups", (board) => {
        const targetId = unique.includes("group:unassigned")
            ? unique.find((value) => value !== "group:unassigned")
            : unique[0],
          target = board.groups[targetId],
          merging = unique.map((value) => board.groups[value]).filter(Boolean),
          envs = merging.map(G.envelope);
        const left = Math.min(...envs.map((e) => e.left)),
          right = Math.max(...envs.map((e) => e.right)),
          top = Math.min(...envs.map((e) => e.top)),
          bottom = Math.max(...envs.map((e) => e.bottom));
        Object.assign(target, {
          shape: "rectangle",
          x: left,
          y: top,
          width: right - left,
          height: bottom - top,
          header: 28,
        });
        Object.values(board.placements)
          .filter((p) => unique.includes(p.groupId))
          .forEach((p) => (p.groupId = targetId));
        unique
          .filter((value) => value !== targetId)
          .forEach((value) => delete board.groups[value]);
        cleanSupergroups(board);
      });
    }
  }
  function createBoard(spec, useGroups) {
    const entities = Object.fromEntries(
      spec.nodes.map((node) => [
        node.id,
        {
          id: node.id,
          canonicalIds: node.canonicalIds,
          radius: node.radius,
          label: node.label,
        },
      ]),
    );
    const groupSpecs = useGroups
      ? spec.groups.filter((group) => group.nodeIds.length)
      : [
          {
            key: "unassigned",
            name: "Unassigned",
            color: "#6e7681",
            nodeIds: spec.nodes.map((n) => n.id),
          },
        ];
    if (!groupSpecs.some((group) => group.key === "unassigned"))
      groupSpecs.push({
        key: "unassigned",
        name: "Unassigned",
        color: "#6e7681",
        nodeIds: [],
      });
    const groups = {},
      placements = {},
      columns = Math.max(1, Math.ceil(Math.sqrt(groupSpecs.length))),
      cell = Math.max(
        275,
        ...groupSpecs.map(
          (source) =>
            Math.max(
              230,
              Math.ceil(Math.sqrt(source.nodeIds.length)) * 72 + 50,
            ) + 45,
        ),
      );
    groupSpecs.forEach((source, index) => {
      const groupId =
          source.key === "unassigned" ? "group:unassigned" : id("group"),
        count = source.nodeIds.length,
        side = Math.max(230, Math.ceil(Math.sqrt(count)) * 72 + 50);
      const group = {
        id: groupId,
        name: source.name,
        color: color(source.color) ? source.color : "#6e7681",
        shape: "rectangle",
        x: (index % columns) * cell,
        y: Math.floor(index / columns) * cell,
        width: side,
        height: side,
        header: 28,
        collapsed: false,
        outerEdgeMode: "visible",
      };
      groups[groupId] = group;
      const slots = G.slots(
        group,
        source.nodeIds.map((entityId) => entities[entityId].radius),
      );
      source.nodeIds.forEach((entityId, slotIndex) => {
        const point = slots[slotIndex];
        placements[entityId] = {
          groupId,
          x: point.x,
          y: point.y,
          pinned: false,
        };
      });
    });
    const assigned = new Set(Object.keys(placements)),
      missing = spec.nodes.filter((node) => !assigned.has(node.id));
    if (missing.length) {
      const groupId = "group:unassigned",
        group = groups[groupId] || {
          id: groupId,
          name: "Unassigned",
          color: "#6e7681",
          shape: "rectangle",
          x: 0,
          y: (Math.ceil(groupSpecs.length / columns) + 1) * 300,
          width: Math.max(230, Math.ceil(Math.sqrt(missing.length)) * 72 + 50),
          height: Math.max(230, Math.ceil(Math.sqrt(missing.length)) * 72 + 50),
          header: 28,
          collapsed: false,
          outerEdgeMode: "visible",
        };
      groups[groupId] = group;
      const points = G.slots(
        group,
        missing.map((node) => node.radius),
      );
      missing.forEach(
        (node, i) =>
          (placements[node.id] = { groupId, ...points[i], pinned: false }),
      );
    }
    return {
      format: "dotnet-depgraph.manual-layout",
      version: 1,
      graphFingerprint: spec.graphFingerprint,
      projectionFingerprint: spec.projectionFingerprint,
      layoutId: id("layout"),
      name: "Manual layout",
      sourceView: spec.sourceView.view,
      collapseLocalPackages: spec.sourceView.collapseLocalPackages,
      capturedScope: {
        nodeCount: spec.sourceView.nodeCount,
        edgeCount: spec.sourceView.edgeCount,
      },
      sourceGranularity: spec.sourceGranularity,
      entities,
      groups,
      supergroups: {},
      placements,
      mutedEntityIds: [],
      viewport: spec.viewport,
      layoutSettings: { version: 1 },
      savedAt: new Date().toISOString(),
    };
  }
  window.DepGraphManualState = {
    ManualState,
    createBoard,
    color,
    supergroupBounds,
    translateSupergroup,
  };
})();
