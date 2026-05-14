// app/api/bgt/route.js

export const runtime = "edge";
export const preferredRegion = "fra1";

const BGT_NAAR_NL = {
  "gesloten verharding": { label: "Asfalt / Beton", kleur: "#374151", icoon: "🛣", herstel: "Hoog" },
  "open verharding": { label: "Klinkers / Tegels", kleur: "#6b7280", icoon: "🧱", herstel: "Midden" },
  "half verhard": { label: "Half verhard", kleur: "#92400e", icoon: "🪨", herstel: "Laag" },
  "onverhard": { label: "Onverhard", kleur: "#78350f", icoon: "🌱", herstel: "Laag" },
  "gras- en kruidachtigen": { label: "Grasberm", kleur: "#16a34a", icoon: "🌿", herstel: "Laag" },
  "groenvoorziening": { label: "Groen / Plantsoen", kleur: "#15803d", icoon: "🌳", herstel: "Laag" },
  "struiken": { label: "Struiken", kleur: "#166534", icoon: "🌿", herstel: "Laag" },
  "zand": { label: "Zand", kleur: "#d97706", icoon: "🏖", herstel: "Laag" },
  "rietland en moeras": { label: "Riet / Moeras", kleur: "#0369a1", icoon: "🌾", herstel: "Speciaal" },
};

export async function GET(request) {
  const { searchParams } = new URL(request.url);
  const lat = parseFloat(searchParams.get("lat"));
  const lng = parseFloat(searchParams.get("lng"));
  if (!lat || !lng) return Response.json({ error: "lat/lng verplicht" }, { status: 400 });

  const delta = 0.0003;
  const bbox = `${lng - delta},${lat - delta},${lng + delta},${lat + delta}`;

  // Correcte URL: api.pdok.nl met /v1/ (niet service.pdok.nl of /v1_0/)
  const COLLECTIES = [
    { naam: "wegdeel", prop: "fysiekVoorkomen" },
    { naam: "begroeidterreindeel", prop: "fysiekVoorkomen" },
    { naam: "onbegroeidterreindeel", prop: "fysiekVoorkomen" },
    { naam: "ondersteunendwegdeel", prop: "fysiekVoorkomen" },
    { naam: "waterdeel", prop: "typeWater" },
  ];

  for (const { naam, prop } of COLLECTIES) {
    try {
      const url = `https://api.pdok.nl/lv/bgt/ogc/v1/collections/${naam}/items?bbox=${bbox}&limit=1&f=json`;
      const res = await fetch(url, { signal: AbortSignal.timeout(4000) });
      if (!res.ok) continue;

      const json = await res.json();
      const features = json.features ?? [];

      if (features.length > 0) {
        const props = features[0].properties ?? {};
        const type = props[prop] ?? props[`plus-${prop}`] ?? null;
        if (type && type !== "transitie" && type.trim() !== "") {
          const lc = type.toLowerCase();
          const vertaald = BGT_NAAR_NL[lc] ?? { label: type, kleur: "#6b7280", icoon: "📍", herstel: "?" };
          return Response.json({ laag: naam, type, vertaald, bron: "pdok-bgt" });
        }
      }
    } catch (e) { continue; }
  }

  return Response.json({ laag: null, type: null });
}
