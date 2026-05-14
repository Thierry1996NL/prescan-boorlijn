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

// Point-in-polygon test (ray casting)
function puntInPolygon(lat, lng, polygon) {
  let inside = false;
  const coords = polygon[0] ?? [];
  for (let i = 0, j = coords.length - 1; i < coords.length; j = i++) {
    const [xi, yi] = coords[i];
    const [xj, yj] = coords[j];
    if (((yi > lat) !== (yj > lat)) && (lng < (xj - xi) * (lat - yi) / (yj - yi) + xi)) {
      inside = !inside;
    }
  }
  return inside;
}

function puntInGeometry(lat, lng, geometry) {
  if (!geometry) return false;
  if (geometry.type === "Polygon") return puntInPolygon(lat, lng, geometry.coordinates);
  if (geometry.type === "MultiPolygon") return geometry.coordinates.some(p => puntInPolygon(lat, lng, p));
  return false;
}

export async function GET(request) {
  const { searchParams } = new URL(request.url);
  const lat = parseFloat(searchParams.get("lat"));
  const lng = parseFloat(searchParams.get("lng"));
  if (!lat || !lng) return Response.json({ error: "lat/lng verplicht" }, { status: 400 });

  const delta = 0.0004;
  const bbox = `${lng - delta},${lat - delta},${lng + delta},${lat + delta}`;

  // Volgorde: gras/berm eerst, dan verharding
  const COLLECTIES = [
    { naam: "begroeidterreindeel",   prop: "fysiek_voorkomen" },
    { naam: "onbegroeidterreindeel", prop: "fysiek_voorkomen" },
    { naam: "ondersteunendwegdeel",  prop: "fysiek_voorkomen" },
    { naam: "waterdeel",             prop: "type_water" },
    { naam: "wegdeel",               prop: "fysiek_voorkomen" },
  ];

  for (const { naam, prop } of COLLECTIES) {
    try {
      const url = `https://api.pdok.nl/lv/bgt/ogc/v1/collections/${naam}/items?bbox=${bbox}&limit=20&f=json`;
      const res = await fetch(url, { signal: AbortSignal.timeout(4000) });
      if (!res.ok) continue;

      const json = await res.json();
      const actief = (json.features ?? []).filter(f => f.properties?.status === "bestaand");

      // Zoek het object dat het klikpunt BEVAT
      for (const feature of actief) {
        const bevat = puntInGeometry(lat, lng, feature.geometry);
        if (!bevat) continue;

        const props = feature.properties ?? {};
        const type = props[prop] ?? null;
        if (!type || type === "transitie") continue;

        const lc = type.toLowerCase();
        const vertaald = BGT_NAAR_NL[lc] ?? {
          label: type.charAt(0).toUpperCase() + type.slice(1),
          kleur: "#6b7280", icoon: "📍", herstel: "?",
        };
        return Response.json({ laag: naam, type, vertaald, bron: "pdok-bgt" });
      }
    } catch (e) { continue; }
  }

  return Response.json({ laag: null, type: null });
}
