// app/api/bgt/route.js
// Oppervlaktype via OpenStreetMap Overpass API

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

export async function GET(request) {
  const { searchParams } = new URL(request.url);
  const lat = parseFloat(searchParams.get("lat"));
  const lng = parseFloat(searchParams.get("lng"));

  if (!lat || !lng) {
    return Response.json({ error: "lat en lng zijn verplicht" }, { status: 400 });
  }

  const query = `[out:json][timeout:8];(way(around:20,${lat},${lng})[highway][surface];way(around:20,${lat},${lng})[surface];way(around:20,${lat},${lng})[landuse];);out tags 5;`;

  try {
    // Gebruik GET request met query parameter
    const url = `https://overpass-api.de/api/interpreter?data=${encodeURIComponent(query)}`;

    const res = await fetch(url, {
      method: "GET",
      headers: { "Accept": "application/json" },
      signal: AbortSignal.timeout(9000),
    });

    if (!res.ok) {
      return Response.json({ laag: null, type: null, fout: `HTTP ${res.status}` });
    }

    const json = await res.json();
    const elements = json.elements ?? [];

    for (const el of elements) {
      const tags = el.tags ?? {};
      const surface = tags.surface ?? null;
      const landuse = tags.landuse ?? null;

      if (surface && OSM_NAAR_NL[surface]) {
        return Response.json({ laag: "osm", type: surface, vertaald: OSM_NAAR_NL[surface], bron: "openstreetmap" });
      }

      if (surface) {
        return Response.json({ laag: "osm", type: surface, vertaald: { label: surface, kleur: "#6b7280", icoon: "📍", herstel: "?" }, bron: "openstreetmap" });
      }

      if (landuse && LANDUSE_NAAR_NL[landuse]) {
        return Response.json({ laag: "osm_landuse", type: landuse, vertaald: LANDUSE_NAAR_NL[landuse], bron: "openstreetmap" });
      }
    }

    return Response.json({ laag: null, type: null, elementen: elements.length });
  } catch (e) {
    return Response.json({ laag: null, type: null, fout: e.message });
  }
}
