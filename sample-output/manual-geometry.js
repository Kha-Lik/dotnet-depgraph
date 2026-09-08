/* Geometry primitives for the manual layout. Kept framework-free for offline use and tests. */
(() => {
  "use strict";
  const finite = (value) => Number.isFinite(Number(value));
  const envelope = (region) =>
    region.shape === "circle"
      ? {
          left: region.cx - region.radius,
          right: region.cx + region.radius,
          top: region.cy - region.radius - region.header,
          bottom: region.cy + region.radius,
        }
      : {
          left: region.x,
          right: region.x + region.width,
          top: region.y,
          bottom: region.y + region.height,
        };
  function usable(region) {
    if (region.shape === "circle")
      return {
        shape: "circle",
        cx: region.cx,
        cy: region.cy,
        radius: region.radius,
      };
    return {
      shape: "rectangle",
      left: region.x,
      right: region.x + region.width,
      top: region.y + region.header,
      bottom: region.y + region.height,
    };
  }
  function contains(region, point, radius = 0) {
    const area = usable(region);
    if (area.shape === "circle")
      return (
        Math.hypot(point.x - area.cx, point.y - area.cy) <=
        area.radius - radius + 1e-7
      );
    return (
      point.x >= area.left + radius - 1e-6 &&
      point.x <= area.right - radius + 1e-6 &&
      point.y >= area.top + radius - 1e-6 &&
      point.y <= area.bottom - radius + 1e-6
    );
  }
  function project(region, point, radius = 0) {
    const area = usable(region);
    if (area.shape === "rectangle")
      return {
        x: Math.max(area.left + radius, Math.min(area.right - radius, point.x)),
        y: Math.max(area.top + radius, Math.min(area.bottom - radius, point.y)),
      };
    const allowed = Math.max(0, area.radius - radius),
      dx = point.x - area.cx,
      dy = point.y - area.cy,
      distance = Math.hypot(dx, dy);
    if (distance <= allowed) return { x: point.x, y: point.y };
    if (!distance) return { x: area.cx + allowed, y: area.cy };
    return {
      x: area.cx + (dx / distance) * allowed,
      y: area.cy + (dy / distance) * allowed,
    };
  }
  function separated(a, b, gap = 18) {
    const x = envelope(a),
      y = envelope(b);
    return (
      x.right + gap <= y.left ||
      y.right + gap <= x.left ||
      x.bottom + gap <= y.top ||
      y.bottom + gap <= x.top
    );
  }
  function validRegion(region) {
    if (
      !region ||
      !["rectangle", "circle"].includes(region.shape) ||
      !finite(region.header) ||
      region.header < 0
    )
      return false;
    return region.shape === "circle"
      ? finite(region.cx) &&
          finite(region.cy) &&
          finite(region.radius) &&
          Math.abs(region.cx) <= 1e7 &&
          Math.abs(region.cy) <= 1e7 &&
          region.radius >= 45 &&
          region.radius <= 1e7
      : finite(region.x) &&
          finite(region.y) &&
          finite(region.width) &&
          finite(region.height) &&
          Math.abs(region.x) <= 1e7 &&
          Math.abs(region.y) <= 1e7 &&
          region.width >= 90 &&
          region.width <= 1e7 &&
          region.height >= region.header + 60 &&
          region.height <= 1e7;
  }
  function slots(region, radii, occupied = []) {
    const maximum = Math.max(12, ...radii),
      spacing = maximum * 2 + 10,
      area = usable(region),
      candidates = [];
    const bounds =
      area.shape === "circle"
        ? {
            left: area.cx - area.radius,
            right: area.cx + area.radius,
            top: area.cy - area.radius,
            bottom: area.cy + area.radius,
          }
        : area;
    for (
      let y = bounds.top + maximum;
      y <= bounds.bottom - maximum + 1e-7;
      y += spacing
    ) {
      for (
        let x = bounds.left + maximum;
        x <= bounds.right - maximum + 1e-7;
        x += spacing
      ) {
        if (
          contains(region, { x, y }, maximum) &&
          occupied.every(
            (p) => Math.hypot(p.x - x, p.y - y) >= p.radius + maximum + 4,
          )
        )
          candidates.push({ x, y });
      }
    }
    return radii.map((radius) => {
      const index = candidates.findIndex((p) =>
        occupied.every(
          (o) => Math.hypot(o.x - p.x, o.y - p.y) >= o.radius + radius + 4,
        ),
      );
      if (index < 0) return null;
      const point = candidates.splice(index, 1)[0];
      occupied.push({ ...point, radius });
      return point;
    });
  }
  function validate(board) {
    const errors = [],
      entityIds = new Set(Object.keys(board.entities || {})),
      placed = new Set(),
      groups = Object.values(board.groups || {});
    groups.forEach((group, index) => {
      if (!validRegion(group))
        errors.push(`Group ${group.id || index} has invalid geometry.`);
      groups.slice(index + 1).forEach((other) => {
        if (!separated(group, other))
          errors.push(`Groups ${group.name} and ${other.name} overlap.`);
      });
    });
    Object.entries(board.placements || {}).forEach(([id, placement]) => {
      if (
        !entityIds.has(id) ||
        placed.has(id) ||
        !board.groups?.[placement.groupId]
      )
        errors.push(`Invalid placement for ${id}.`);
      else {
        placed.add(id);
        const entity = board.entities[id];
        if (
          !finite(placement.x) ||
          !finite(placement.y) ||
          Math.abs(placement.x) > 1e7 ||
          Math.abs(placement.y) > 1e7 ||
          !contains(board.groups[placement.groupId], placement, entity.radius)
        )
          errors.push(`${id} is outside its group.`);
      }
    });
    entityIds.forEach((id) => {
      if (!placed.has(id)) errors.push(`${id} has no placement.`);
    });
    return errors;
  }
  window.DepGraphManualGeometry = {
    envelope,
    usable,
    contains,
    project,
    separated,
    validRegion,
    slots,
    validate,
  };
})();
