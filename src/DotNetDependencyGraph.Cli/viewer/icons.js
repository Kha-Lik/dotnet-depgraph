/* Small dependency-free SVG icon set for the offline report controls. */
(() => {
  "use strict";
  const paths = {
    add: "M12 5v14M5 12h14",
    arrange: "M4 4h6v6H4zM14 4h6v6h-6zM4 14h6v6H4zM14 14h6v6h-6z",
    assign: "M4 7h10M11 4l3 3-3 3M20 17H10M13 14l-3 3 3 3",
    delete: "M5 7h14M9 7V4h6v3M8 10v8M12 10v8M16 10v8M6 7l1 14h10l1-14",
    collapse: "M3 12h18M8 8l-4 4 4 4M16 8l4 4-4 4",
    download: "M12 3v12M7 10l5 5 5-5M4 20h16",
    eye: "M2.5 12s3.5-6 9.5-6 9.5 6 9.5 6-3.5 6-9.5 6-9.5-6-9.5-6zM12 9a3 3 0 1 1 0 6 3 3 0 0 1 0-6z",
    expand: "M3 12h18M4 12l4-4M4 12l4 4M20 12l-4-4M20 12l-4 4",
    fit: "M4 9V4h5M15 4h5v5M20 15v5h-5M9 20H4v-5",
    hide: "M3 3l18 18M10.6 10.7A2 2 0 0 0 13.3 13.4M9.8 5.2A11.8 11.8 0 0 1 12 5c6 0 9.5 7 9.5 7a15 15 0 0 1-2.2 3.2M6.2 6.3A16 16 0 0 0 2.5 12s3.5 7 9.5 7a10.8 10.8 0 0 0 3.2-.5",
    highlight:
      "M12 2v4M12 18v4M2 12h4M18 12h4M4.9 4.9l2.8 2.8M16.3 16.3l2.8 2.8M19.1 4.9l-2.8 2.8M7.7 16.3l-2.8 2.8",
    image:
      "M4 4h16v16H4zM7 16l4-4 3 3 2-2 4 4M8.5 9a1.5 1.5 0 1 0 0-3 1.5 1.5 0 0 0 0 3z",
    merge: "M4 4h10v10H4zM10 10h10v10H10zM12 8v8M8 12h8",
    pause: "M8 5v14M16 5v14",
    pin: "M8 4h8l-2 6 3 3H7l3-3zM12 13v8",
    play: "M8 5l11 7-11 7z",
    redo: "M20 7h-9a7 7 0 0 0-7 7v3M16 3l4 4-4 4",
    refresh: "M20 11a8 8 0 1 0-2.3 5.7M20 4v7h-7",
    resize: "M8 20H4v-4M4 20l6-6M14 20h6v-6M20 20l-8-8",
    route: "M5 5h5a3 3 0 0 1 3 3v8a3 3 0 0 0 3 3h3M16 16l3 3-3 3M5 2v6M2 5h6",
    save: "M5 3h12l2 2v16H5zM8 3v6h8V3M8 21v-7h8v7",
    undo: "M4 7h9a7 7 0 0 1 7 7v3M8 3 4 7l4 4",
    unpin: "M4 4l16 16M8 4h8l-2 6 3 3H9M12 15v6",
    upload: "M12 21V9M7 14l5-5 5 5M4 4h16",
    vector:
      "M3 4h7v7H3zM16.5 4a3.5 3.5 0 1 1 0 7 3.5 3.5 0 0 1 0-7zM7 20l4-7 4 7z",
  };
  function svg(name) {
    const element = document.createElementNS(
        "http://www.w3.org/2000/svg",
        "svg",
      ),
      path = document.createElementNS("http://www.w3.org/2000/svg", "path");
    element.classList.add("button-icon");
    element.setAttribute("viewBox", "0 0 24 24");
    element.setAttribute("aria-hidden", "true");
    element.setAttribute("focusable", "false");
    path.setAttribute("d", paths[name] || paths.add);
    element.append(path);
    return element;
  }
  function button(target, name, label, iconOnly = false) {
    const element =
      typeof target === "string" ? document.getElementById(target) : target;
    if (!element) return null;
    const icon = svg(name);
    element.replaceChildren(icon);
    element.classList.add("has-icon");
    element.classList.toggle("icon-only", iconOnly);
    element.setAttribute("aria-label", label);
    if (!element.title || element.dataset.iconTitle === "true") {
      element.title = label;
      element.dataset.iconTitle = "true";
    }
    if (!iconOnly) {
      const text = document.createElement("span");
      text.className = "button-label";
      text.textContent = label;
      element.append(text);
    }
    return element;
  }
  function svgControl(name, label) {
    const root = document.createElementNS("http://www.w3.org/2000/svg", "g"),
      background = document.createElementNS(
        "http://www.w3.org/2000/svg",
        "rect",
      ),
      icon = svg(name),
      title = document.createElementNS("http://www.w3.org/2000/svg", "title");
    root.setAttribute("aria-label", label);
    background.setAttribute("width", 18);
    background.setAttribute("height", 18);
    background.setAttribute("rx", 3);
    icon.setAttribute("x", 1);
    icon.setAttribute("y", 1);
    icon.setAttribute("width", 16);
    icon.setAttribute("height", 16);
    title.textContent = label;
    root.append(background, icon, title);
    return root;
  }
  function svgButton(name, label, active, onActivate) {
    const root = svgControl(name, label);
    root.classList.add("svg-icon-button");
    if (active) root.classList.add("active");
    root.setAttribute("role", "button");
    root.setAttribute("tabindex", "0");
    root.setAttribute("aria-pressed", active ? "true" : "false");
    const activate = (event) => {
      event.preventDefault();
      event.stopPropagation();
      onActivate();
    };
    root.addEventListener("pointerdown", (event) => {
      event.preventDefault();
      event.stopPropagation();
    });
    root.addEventListener("click", activate);
    root.addEventListener("keydown", (event) => {
      if (event.key === "Enter" || event.key === " ") activate(event);
    });
    return root;
  }
  window.DepGraphIcons = { button, svg, svgButton, svgControl };
})();
