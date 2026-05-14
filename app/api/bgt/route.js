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
  "voetpad": { label: "Voetpad", kleur: "#6b7280", icoon: "🚶", herstel: "Midden" },
};

export async function GET(request) {
  const { searchParams } = new URL(request.url);
  const lat = parseFloat(searchParams.get("lat"));
  const lng = parseFloat(searchParams.get("lng"));
  if (!lat || !lng) return Response.json({ error: "lat/lng verplicht" }, { status: 400 });

  const delta = 0.001;
  const bbox = `${lng - delta},${lat - delta},${lng + delta},${lat + delta}`;

  const COLLECTIES = [
    { naam: "wegdeel",              prop: "fysiek_voorkomen" },
    { naam: "ondersteunendwegdeel", prop: "fysiek_voorkomen" },
    { naam: "begroeidterreindeel",  prop: "fysiek_voorkomen" },
    { naam: "onbegroeidterreindeel",prop: "fysiek_voorkomen" },
    { naam: "waterdeel",            prop: "type_water" },
  ];

  for (const { naam, prop } of COLLECTIES) {
    try {
      const url = `https://api.pdok.nl/lv/bgt/ogc/v1/collections/${naam}/items?bbox=${bbox}&limit=5&f=json`;
      const res = await fetch(url, { signal: AbortSignal.timeout(4000) });
      if (!res.ok) continue;

      const json = await res.json();

      // Filter op bestaande objecten
      const actief = (json.features ?? []).filter(
        f => f.properties?.status === "bestaand"
      );

      for (const feature of actief) {
        const props = feature.properties ?? {};
        const type = props[prop] ?? props["fysiek_voorkomen"] ?? props["type_water"] ?? null;

        if (type && type !== "transitie" && type.trim() !== "") {
          const lc = type.toLowerCase();
          const vertaald = BGT_NAAR_NL[lc] ?? {
            label: type.charAt(0).toUpperCase() + type.slice(1),
            kleur: "#6b7280",
            icoon: "📍",
            herstel: "?",
          };
          return Response.json({ laag: naam, type, vertaald, bron: "pdok-bgt" });
        }
      }
    } catch (e) { continue; }
  }

  return Response.json({ laag: null, type: null });
}
