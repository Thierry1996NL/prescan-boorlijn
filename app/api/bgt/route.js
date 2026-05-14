// app/api/bgt/route.js — Edge runtime Frankfurt

export const runtime = "edge";
export const preferredRegion = "fra1";

const BGT_NAAR_NL = {
  "gesloten verharding": { label: "Asfalt / Beton", kleur: "#374151", icoon: "🛣", herstel: "Hoog" },
  "open verharding": { label: "Klinkers / Tegels", kleur: "#6b7280", icoon: "🧱", herstel: "Midden" },
  "half verhard": { label: "Half verhard", kleur: "#92400e", icoon: "🪨", herstel: "Laag" },
  "onverhard": { label: "Onverhard", kleur: "#78350f", icoon: "🌱", herstel: "Laag" },
  "gras- en kruidachtigen": { label: "Grasberm", kleur: "#16a34a", icoon: "🌿", herstel: "Laag" },
  "groenvoorziening": { label: "Groen", kleur: "#15803d", icoon: "🌳", herstel: "Laag" },
  "zand": { label: "Zand", kleur: "#d97706", icoon: "🏖", herstel: "Laag" },
};

export async function GET(request) {
  const { searchParams } = new URL(request.url);
  const lat = parseFloat(searchParams.get("lat"));
  const lng = parseFloat(searchParams.get("lng"));
  if (!lat || !lng) return Response.json({ error: "lat/lng verplicht" }, { status: 400 });

  const delta = 0.0003;
  const bbox = `${lng - delta},${lat - delta},${lng + delta},${lat + delta}`;

  // Probeer alleen wegdeel — snelste laag
  for (const { naam, prop } of [
    { naam: "wegdeel", prop: "fysiekVoorkomen" },
    { naam: "begroeidterreindeel", prop: "fysiekVoorkomen" },
    { naam: "onbegroeidterreindeel", prop: "fysiekVoorkomen" },
  ]) {
    try {
      const res = await fetch(
        `https://service.pdok.nl/lv/bgt/ogc/v1_0/collections/${naam}/items?bbox=${bbox}&limit=1&f=json`,
        { signal: AbortSignal.timeout(3000) }
      );
      if (!res.ok) continue;
      const json = await res.json();
      const props = json.features?.[0]?.properties ?? {};
      const type = props[prop] ?? null;
      if (type && type !== "transitie") {
        const vertaald = BGT_NAAR_NL[type.toLowerCase()] ?? { label: type, kleur: "#6b7280", icoon: "📍", herstel: "?" };
        return Response.json({ laag: naam, type, vertaald, bron: "pdok" });
      }
    } catch (e) { continue; }
  }

  return Response.json({ laag: null, type: null });
}
