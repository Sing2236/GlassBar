import { cloneDesign } from "../lib/designSchema";

function make(id, kind, author, name, summary, changes) {
  const document = cloneDesign();
  document.kind = kind;
  document.metadata = { name, summary, tags: changes.tags || [] };
  Object.assign(document[kind], changes);
  return { id, kind, author, name, summary, document, downloads: changes.downloads || 0, status: "approved" };
}

export const seedDesigns = [
  make("midnight", "glassbar", "glasspilot", "Midnight Current", "Deep blue glass with a cool cyan edge.", {
    backgroundStart: "#0f2636", backgroundEnd: "#11141d", accent: "#64ddff", opacity: 91, radius: 24,
    tags: ["dark", "blue"], downloads: 1284
  }),
  make("paper", "glassbar", "minima", "Paper Glass", "A bright, quiet bar for light desktops.", {
    backgroundStart: "#edf7fb", backgroundEnd: "#cbd8e3", accent: "#1688c8", border: "#ffffff", opacity: 78,
    tags: ["light", "minimal"], downloads: 908
  }),
  make("focus", "widget", "nocturne", "Focus Capsule", "A compact focus timer with a restrained status line.", {
    title: "Focus", value: "42:18", detail: "Do not disturb", icon: "timer", accent: "#8be9fd",
    tags: ["productivity"], downloads: 762
  }),
  make("weather", "widget", "northstar", "Weather Line", "Temperature, condition, and location in one glance.", {
    title: "Chicago", value: "68°", detail: "Clear skies", icon: "weather", dataSource: "weather", accent: "#fbbf24",
    tags: ["weather"], downloads: 611
  }),
  make("rain", "animation", "ethereal", "Fine Rain", "Soft diagonal rain with a short cyan trail.", {
    name: "Fine Rain", shape: "line", motion: "fall", density: 52, speed: 61, trail: 70, primaryColor: "#93e1ff",
    tags: ["rain", "calm"], downloads: 1470
  }),
  make("embers", "animation", "luma", "Quiet Embers", "Warm particles that rise slowly through the glass.", {
    name: "Quiet Embers", shape: "orb", motion: "rise", density: 35, speed: 29, glow: 62, primaryColor: "#fb923c", secondaryColor: "#fde68a",
    tags: ["warm", "particles"], downloads: 534
  })
];
