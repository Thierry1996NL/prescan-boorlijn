// app/api/bro/route.js — BRO/Dinoloket data via PDOK WFS
// Haalt CPT sonderingen, boorprofielen en peilbuizen op voor een bbox
// Geen API key nodig — volledig gratis PDOK opendata

// PDOK WFS endpoints voor BRO puntdata
const WFS_ENDPOINTS = {
  cpt: [
    // Nieuw service.pdok.nl (geprobeerd eerst)
    "https://service.pdok.nl/bzk/bro-cpt/wfs/v1_0",
    // Oud geodata.nationaalgeoregister.nl (fallback)
    "https://geodata.nationaalgeoregister.nl/brocpt/wfs",
  ],
  bhr: [
    "https://service.pdok.nl/bzk/bro-bhr/wfs/v1_0",
    "https://geodata.nationaalgeoregister.nl/brobhr/wfs",
  ],
  gmw: [
    "https://service.pdok.nl/bzk/bro-gmw/wfs/v1_0",
    "https://geodata.nationaalgeoregister.nl/brogmw/wfs",
  ],
};

const TYPE_NAMES = {
  cpt: ["brocpt:CPT", "cpt"],
  bhr: ["brobhr:BHR", "bhr"],
  gmw: ["brogmw:GMW", "gmw"],
};

async function wfsOpvragen(type, minLat, maxLat, minLng, maxLng, signal) {
  const urls = WFS_ENDPOINTS[type] ?? [];
  const typeNames = TYPE_NAMES[type] ?? [type];
  const bbox = `${minLng},${minLat},${maxLng},${maxLat},EPSG:4326`;

  for (const baseUrl of urls) {
    for (const typeName of typeNames) {
      try {
        const params = new URLSearchParams({
          service: "WFS",
          version: "2.0.0",
          request: "GetFeature",
          typeName,
          count: "500",
          outputFormat: "application/json",
          bbox,
        });
        const res = await fetch(`${baseUrl}?${params}`, {
          signal,
          headers: { Accept: "application/json, application/geo+json" },
        });
        if (!res.ok) continue;
        const text = await res.text();
        if (!text || text.trim().startsWith("<")) continue; // XML error response
        const json = JSON.parse(text);
        const features = json.features ?? json.members ?? [];
        if (features.length > 0 || json.numberMatched === 0) {
          return features.map(f => ({
            id:         f.properties?.broId ?? f.properties?.BRO_ID ?? f.id ?? "?",
            type,
            lat:        f.geometry?.coordinates?.[1],
            lng:        f.geometry?.coordinates?.[0],
            diepte:     f.properties?.["diepte"] ?? f.properties?.["einddiepte"] ?? f.properties?.["finalDepth"] ?? null,
            datum:      f.properties?.["registratieTijdstip"] ?? f.properties?.["datumInvoer"] ?? null,
            kwaliteit:  f.properties?.["kwaliteitsklasse"] ?? f.properties?.["kwaliteitsRegime"] ?? null,
            bronhouder: f.properties?.["bronhouder"] ?? null,
            naam:       f.properties?.["naam"] ?? f.properties?.["locatieBeschrijving"] ?? null,
          })).filter(f => f.lat && f.lng);
        }
      } catch {}
    }
  }
  return [];
}

export async function GET(request) {
  const { searchParams } = new URL(request.url);
  const minLat = parseFloat(searchParams.get("minLat") ?? "0");
  const maxLat = parseFloat(searchParams.get("maxLat") ?? "0");
  const minLng = parseFloat(searchParams.get("minLng") ?? "0");
  const maxLng = parseFloat(searchParams.get("maxLng") ?? "0");

  if (!minLat || !maxLat) {
    return Response.json({ error: "Geen coördinaten meegegeven" }, { status: 400 });
  }

  // Straal van ~800m rond het tracé
  const marge = 0.008; // ~800m in graden
  const bbox = {
    minLat: minLat - marge, maxLat: maxLat + marge,
    minLng: minLng - marge, maxLng: maxLng + marge,
  };

  const [cpt, bhr, gmw] = await Promise.allSettled([
    wfsOpvragen("cpt", bbox.minLat, bbox.maxLat, bbox.minLng, bbox.maxLng, request.signal),
    wfsOpvragen("bhr", bbox.minLat, bbox.maxLat, bbox.minLng, bbox.maxLng, request.signal),
    wfsOpvragen("gmw", bbox.minLat, bbox.maxLat, bbox.minLng, bbox.maxLng, request.signal),
  ]);

  return Response.json({
    cpt:  cpt.status  === "fulfilled" ? cpt.value  : [],
    bhr:  bhr.status  === "fulfilled" ? bhr.value  : [],
    gmw:  gmw.status  === "fulfilled" ? gmw.value  : [],
  });
}
