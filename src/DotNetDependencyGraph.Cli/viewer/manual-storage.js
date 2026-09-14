/* Safe browser and portable persistence for a matching manual-board projection. */
(() => {
  "use strict";
  const G = window.DepGraphManualGeometry;
  function origin(group) {
    return group.shape === "circle"
      ? { x: group.cx, y: group.cy }
      : { x: group.x, y: group.y };
  }
  function documentFromBoard(board) {
    const result = structuredClone(board);
    result.coordinateSpace = "region-relative";
    Object.values(result.placements).forEach((placement) => {
      const point = origin(result.groups[placement.groupId]);
      placement.x -= point.x;
      placement.y -= point.y;
    });
    return result;
  }
  function boardFromDocument(document) {
    const result = structuredClone(document);
    if (result.coordinateSpace === "region-relative")
      Object.values(result.placements || {}).forEach((placement) => {
        const point = origin(result.groups?.[placement.groupId] || {});
        placement.x += point.x;
        placement.y += point.y;
      });
    delete result.coordinateSpace;
    return result;
  }
  function validate(candidate, expected) {
    if (
      !candidate ||
      candidate.format !== "dotnet-depgraph.manual-layout" ||
      candidate.version !== 1
    )
      throw new Error(
        "Layout must use dotnet-depgraph.manual-layout version 1.",
      );
    if (candidate.graphFingerprint !== expected.graphFingerprint)
      throw new Error("This layout belongs to a different graph topology.");
    if (candidate.projectionFingerprint !== expected.projectionFingerprint)
      throw new Error(
        "This layout uses a different view, collapse setting, or entity set.",
      );
    if (
      !candidate.entities ||
      !candidate.groups ||
      !candidate.placements ||
      !Array.isArray(candidate.mutedEntityIds)
    )
      throw new Error("Layout document is incomplete.");
    const normalized = boardFromDocument(candidate),
      expectedIds = Object.keys(expected.entities).sort(),
      actualIds = Object.keys(normalized.entities).sort();
    normalized.supergroups ||= {};
    if (JSON.stringify(expectedIds) !== JSON.stringify(actualIds))
      throw new Error("Layout entity IDs do not match this board.");
    for (const entityId of actualIds) {
      const entity = normalized.entities[entityId],
        original = expected.entities[entityId];
      if (
        !Array.isArray(entity.canonicalIds) ||
        JSON.stringify([...entity.canonicalIds].sort()) !==
          JSON.stringify([...original.canonicalIds].sort()) ||
        !Number.isFinite(entity.radius) ||
        entity.radius <= 0
      )
        throw new Error(`Invalid entity mapping or radius for ${entityId}.`);
    }
    for (const [groupId, group] of Object.entries(normalized.groups)) {
      if (
        group.id !== groupId ||
        typeof group.name !== "string" ||
        !window.DepGraphManualState.color(group.color) ||
        (group.collapsed != null && typeof group.collapsed !== "boolean") ||
        (group.outerEdgeMode != null &&
          !["visible", "hidden", "highlighted"].includes(group.outerEdgeMode))
      )
        throw new Error(`Invalid group ${groupId}.`);
      group.collapsed = !!group.collapsed;
      group.outerEdgeMode = group.outerEdgeMode || "visible";
    }
    for (const [supergroupId, supergroup] of Object.entries(
      normalized.supergroups,
    )) {
      if (
        supergroup.id !== supergroupId ||
        typeof supergroup.name !== "string" ||
        !window.DepGraphManualState.color(supergroup.color) ||
        !Array.isArray(supergroup.groupIds)
      )
        throw new Error(`Invalid supergroup ${supergroupId}.`);
    }
    const muted = new Set(normalized.mutedEntityIds);
    if (
      muted.size !== normalized.mutedEntityIds.length ||
      [...muted].some((entityId) => !normalized.entities[entityId])
    )
      throw new Error("Muted entity IDs are invalid.");
    const errors = G.validate(normalized);
    if (errors.length) throw new Error(errors[0]);
    normalized.savedAt = String(candidate.savedAt || "");
    return normalized;
  }
  class ManualStorage {
    constructor(scope, status) {
      this.scope = scope;
      this.status = status;
      this.key = `dotnet-depgraph.manual-layout.v1:${scope.graphFingerprint}:${scope.projectionFingerprint}`;
      this.timer = null;
    }
    load() {
      try {
        const raw = localStorage.getItem(this.key);
        return raw ? validate(JSON.parse(raw), this.scope) : null;
      } catch (error) {
        this.status(`Browser layout ignored: ${error.message}`, true);
        return null;
      }
    }
    save(board) {
      clearTimeout(this.timer);
      this.status("Saving");
      this.timer = setTimeout(() => {
        try {
          const value = documentFromBoard(board);
          value.savedAt = new Date().toISOString();
          localStorage.setItem(this.key, JSON.stringify(value));
          this.status("Saved in this browser");
        } catch {
          this.status(
            "Browser save unavailable — download to keep changes",
            true,
          );
        }
      }, 250);
    }
    checkpoint(board) {
      clearTimeout(this.timer);
      try {
        const value = documentFromBoard(board);
        value.savedAt = new Date().toISOString();
        localStorage.setItem(this.key, JSON.stringify(value));
        this.status("Saved in this browser");
      } catch {
        this.status(
          "Browser save unavailable — download to keep changes",
          true,
        );
      }
    }
    importText(text) {
      return validate(JSON.parse(text), this.scope);
    }
    download(board) {
      const value = documentFromBoard(board);
      value.savedAt = new Date().toISOString();
      const a = document.createElement("a");
      a.download = "manual-layout.json";
      a.href = URL.createObjectURL(
        new Blob([JSON.stringify(value, null, 2)], {
          type: "application/json",
        }),
      );
      a.click();
      setTimeout(() => URL.revokeObjectURL(a.href), 1000);
    }
  }
  window.DepGraphManualStorage = { ManualStorage, validate, documentFromBoard };
})();
