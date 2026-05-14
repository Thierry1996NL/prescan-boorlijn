// app/api/bgt/route.js
// Draait in Frankfurt (fra1) — PDOK accepteert Europese IP's

const OSM_NAAR_NL = {
  asphalt: { label: "Asfalt / Beton", kleur: "#374151", icoon: "🛣", herstel: "Hoog" },
  concrete: { label: "Asfalt / Beton", kleur: "#374151", icoon: "🛣", herstel: "Hoog" },
  paving_stones: { label: "Klinkers / Tegels", kleur: "#6b7280", icoon: "🧱", herstel: "Midden" },
  cobblestone: { label: "Klinkers / Tegels", kleur: "#6b7280", icoon: "🧱", herstel: "Midden" },
  sett: { label: "Klinkers / Tegels", kleur: "#6b7280", icoon: "🧱", herstel: "Midden" },
  gravel: { label: "Half verhard", kleur: "#92400e", icoon: "🪨", herstel: "Laag" },
  compacted: { label: "Half verhard", kleur: "#92400e", icoon: "🪨", herstel: "Laag" },
  fine_gravel: { label: "Half verhard", kleur: "#92400e", icoon: "🪨", herstel: "Laag" },
  dirt: { label: "Onverhard", kleur: "#78350f", icoon: "🌱", herstel: "Laag" },
  earth: { label: "Onverhard", kleur: "#78350f", icoon: "🌱", herstel: "Laag" },
  grass: { label: "Grasberm", kleur: "#16a34a", icoon: "🌿", herstel: "Laag" },
  grass_paver: { label: "Grasberm", kleur: "#16a34a", icoon: "🌿", herstel: "Laag" },
  sand: { label: "Zand", kleur: "#d97706", icoon: "🏖", herstel: "Laag" },
};

const LANDUSE_NAAR_NL = {
  grass: { label: "Grasberm", kleur: "#16a34a", icoon: "🌿", herstel: "Laag" },
  meadow: { label: "Weiland", kleur: "#16a34a", icoon: "🌿", herstel: "Laag" },
  forest: { label: "Bos", kleur: "#14532d", icoon: "🌲", herstel: "Laag" },
  residential: { label: "Woongebied", kleur: "#6b7280", icoon: "🏘", herstel: "Midden" },
  industrial: { label: "Industrieterrein", kleur: "#374151", icoon: "🏭", herstel: "Hoog" },
  farmland: { label: "Landbouw", kleur: "#d97706", icoon: "🌾", herstel: "Laag" },
};

export const runtime = "edge";
export const preferredRegion = "fra1";

export async function GET(request) {
  const { searchParams } = new URL(request.url);
  const lat = parseFloat(searchParams.get("lat"));
  const lng = parseFloat(searchParams.get("lng"));

  if (!lat || !lng) {
    return Response.json({ error: "lat en lng zijn verplicht" }, { status: 400 });
  }

  const delta = 0.0003;
  const bbox = `${lng - delta},${lat - delta},${lng + delta},${lat + delta}`;

  const COLLECTIES = [
    { naam: "wegdeel", prop: "fysiekVoorkomen" },
    { naam: "begroeidterreindeel", prop: "fysiekVoorkomen" },
    { naam: "onbegroeidterreindeel", prop: "fysiekVoorkomen" },
    { naam: "ondersteunendwegdeel", prop: "fysiekVoorkomen" },
    { naam: "waterdeel", prop: "typeWater" },
  ];

  const BGT_NAAR_NL = {
    "gesloten verharding": { label: "Asfalt / Beton", kleur: "#374151", icoon: "🛣", herstel: "Hoog" },
    "open verharding": { label: "Klinkers / Tegels", kleur: "#6b7280", icoon: "🧱", herstel: "Midden" },
    "half verhard": { label: "Half verhard", kleur: "#92400e", icoon: "🪨", herstel: "Laag" },
    "onverhard": { label: "Onverhard", kleur: "#78350f", icoon: "🌱", herstel: "Laag" },
    "gras- en kruidachtigen": { label: "Grasberm", kleur: "#16a34a", icoon: "🌿", herstel: "Laag" },
    "groenvoorziening": { label: "Groen / Plantsoen", kleur: "#15803d", icoon: "🌳", herstel: "Laag" },
    "struiken": { label: "Struiken", kleur: "#166534", icoon: "🌿", herstel: "Laag" },
    "zand": { label: "Zand", kleur: "#d97706", icoon: "🏖", herstel: "Laag" },
  };

  // Probeer PDOK BGT OGC API (werkt vanuit Europa)
  for (const { naam, prop } of COLLECTIES) {
    try {
      const url = `https://service.pdok.nl/lv/bgt/ogc/v1_0/collections/${naam}/items?bbox=${bbox}&limit=1&f=json`;
      const res = await fetch(url, {
        headers: {
          "Accept": "application/json",
          "User-Agent": "PrescanBoorlijnAI/1.0",
        },
      });

      if (!res.ok) continue;
      const json = await res.json();
      const features = json.features ?? [];

      if (features.length > 0) {
        const props = features[0].properties ?? {};
        const type = props[prop] ?? props[`plus-${prop}`] ?? null;
        if (type && type !== "transitie" && type.trim() !== "") {
          const vertaald = BGT_NAAR_NL[type.toLowerCase()] ?? {
            label: type,
            kleur: "#6b7280",
            icoon: "📍",
            herstel: "?",
          };
          return Response.json({ laag: naam, type, vertaald, bron: "pdok-bgt" });
        }
      }
    } catch (e) {
      continue;
    }
  }

  // Fallback: Overpass OSM (ook vanuit Europa betrouwbaar)
  try {
    const query = `[out:json][timeout:8];(way(around:20,${lat},${lng})[surface];way(around:20,${lat},${lng})[highway][surface];);out tags 5;`;
    const overpassUrl = `https://overpass-api.de/api/interpreter?data=${encodeURIComponent(query)}`;
    const res = await fetch(overpassUrl, {
      headers: { "Accept": "application/json", "User-Agent": "PrescanBoorlijnAI/1.0" },
    });

    if (res.ok) {
      const json = await res.json();
      for (const el of json.elements ?? []) {
        const surface = el.tags?.surface ?? null;
        if (surface) {
          const vertaald = OSM_NAAR_NL[surface] ?? { label: surface, kleur: "#6b7280", icoon: "📍", herstel: "?" };
          return Response.json({ laag: "osm", type: surface, vertaald, bron: "openstreetmap" });
        }
      }
    }
  } catch (e) {
    // stil falen
  }

  return Response.json({ laag: null, type: null });
}
