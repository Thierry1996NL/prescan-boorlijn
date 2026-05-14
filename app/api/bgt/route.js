// app/api/bgt/route.js
// Oppervlaktype via OpenStreetMap - meerdere Overpass mirrors

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

async function vraagOverpass(endpoint, query) {
  const url = `${endpoint}?data=${encodeURIComponent(query)}`;
  const res = await fetch(url, {
    headers: {
      "Accept": "*/*",
      "User-Agent": "PrescanBoorlijnAI/1.0 (https://prescan-boorlijn.vercel.app)",
    },
    signal: AbortSignal.timeout(8000),
  });
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  return res.json();
}

export async function GET(request) {
  const { searchParams } = new URL(request.url);
  const lat = parseFloat(searchParams.get("lat"));
  const lng = parseFloat(searchParams.get("lng"));

  if (!lat || !lng) {
    return Response.json({ error: "lat en lng zijn verplicht" }, { status: 400 });
  }

  const query = `[out:json][timeout:8];(way(around:20,${lat},${lng})[surface];way(around:20,${lat},${lng})[highway][surface];);out tags 5;`;

  const MIRRORS = [
    "https://overpass.kumi.systems/api/interpreter",
    "https://maps.mail.ru/osm/tools/overpass/api/interpreter",
    "https://overpass-api.de/api/interpreter",
  ];

  let lastFout = null;

  for (const mirror of MIRRORS) {
    try {
      const json = await vraagOverpass(mirror, query);
      const elements = json.elements ?? [];

      for (const el of elements) {
        const tags = el.tags ?? {};
        const surface = tags.surface ?? null;
        const landuse = tags.landuse ?? null;

        if (surface) {
          const vertaald = OSM_NAAR_NL[surface] ?? { label: surface, kleur: "#6b7280", icoon: "📍", herstel: "?" };
          return Response.json({ laag: "osm", type: surface, vertaald, bron: mirror });
        }

        if (landuse && LANDUSE_NAAR_NL[landuse]) {
          return Response.json({ laag: "osm_landuse", type: landuse, vertaald: LANDUSE_NAAR_NL[landuse], bron: mirror });
        }
      }

      // Mirror werkte maar geen data gevonden
      return Response.json({ laag: null, type: null, elementen: elements.length, bron: mirror });

    } catch (e) {
      lastFout = `${mirror}: ${e.message}`;
      continue;
    }
  }

  return Response.json({ laag: null, type: null, fout: lastFout });
}
