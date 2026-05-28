// app/api/bro/route.js
// BRO (Basisregistratie Ondergrond) — gratis, geen API key nodig
// Fetcht sonderingen (CPT) en boorprofielen (BHR) nabij het boortracé

const BRO_BASE = "https://publiek.broservices.nl";

// Voeg marge toe aan bbox (in graden, ~500m)
function bboxMetMarge(minLat, maxLat, minLng, maxLng, margeM = 500) {
  const dLat = margeM / 111000;
  const dLng = margeM / (111000 * Math.cos((((minLat + maxLat) / 2) * Math.PI) / 180));
  return {
    minLat: minLat - dLat, maxLat: maxLat + dLat,
    minLng: minLng - dLng, maxLng: maxLng + dLng,
  };
}

async function fetchBROType(type, bbox, signal) {
  // BRO verwacht bbox als: minx,miny,maxx,maxy (lng,lat volgorde)
  const bboxStr = `${bbox.minLng.toFixed(6)},${bbox.minLat.toFixed(6)},${bbox.maxLng.toFixed(6)},${bbox.maxLat.toFixed(6)}`;
  const endpoints = {
    cpt:  `${BRO_BASE}/sr/cpt/v1/characteristics/searches?bbox=${bboxStr}`,
    bhrp: `${BRO_BASE}/sr/bhrp/v1/characteristics/searches?bbox=${bboxStr}`,
    bhrg: `${BRO_BASE}/sr/bhrg/v1/characteristics/searches?bbox=${bboxStr}`,
  };
  const url = endpoints[type];
  if (!url) return [];

  try {
    const res = await fetch(url, {
      signal,
      headers: { "Accept": "application/json" },
    });
    if (!res.ok) return [];
    const data = await res.json();
    return (data.features ?? []).map(f => ({
      id:       f.properties?.broId ?? f.id ?? "?",
      type,
      lat:      f.geometry?.coordinates?.[1],
      lng:      f.geometry?.coordinates?.[0],
      diepte:   f.properties?.deliveredVerticalPosition?.offset ?? f.properties?.finalDepth ?? null,
      datum:    f.properties?.registrationPeriod?.beginDate ?? f.properties?.deliveryDate ?? null,
      kwaliteit: f.properties?.qualityClass ?? null,
      naam:     f.properties?.objectIdAccountableParty ?? null,
      bronhouder: f.properties?.deliveryAccountableParty ?? null,
    })).filter(f => f.lat && f.lng);
  } catch {
    return [];
  }
}

// Haal detail op voor één BRO-object (voor popup)
async function fetchBRODetail(type, broId, signal) {
  const paths = { cpt: "cpt", bhrp: "bhrp", bhrg: "bhrg" };
  const path = paths[type];
  if (!path) return null;

  try {
    const res = await fetch(`${BRO_BASE}/sr/${path}/v1/objects/${broId}`, {
      signal,
      headers: { "Accept": "application/json" },
    });
    if (!res.ok) return null;
    return await res.json();
  } catch {
    return null;
  }
}

export async function GET(request) {
  const { searchParams } = new URL(request.url);
  const broId  = searchParams.get("broId");
  const type   = searchParams.get("type");
  const detail = searchParams.get("detail") === "true";

  // Detail-request voor popup
  if (detail && broId && type) {
    const data = await fetchBRODetail(type, broId, request.signal);
    return Response.json(data ?? { error: "Niet gevonden" });
  }

  // Zoek-request voor alle objecten nabij tracé
  const minLat = parseFloat(searchParams.get("minLat") ?? "0");
  const maxLat = parseFloat(searchParams.get("maxLat") ?? "0");
  const minLng = parseFloat(searchParams.get("minLng") ?? "0");
  const maxLng = parseFloat(searchParams.get("maxLng") ?? "0");

  if (!minLat || !maxLat) {
    return Response.json({ error: "Geen coördinaten" }, { status: 400 });
  }

  const bbox = bboxMetMarge(minLat, maxLat, minLng, maxLng, 600);
  const [cptn, bhrp, bhrg] = await Promise.allSettled([
    fetchBROType("cpt",  bbox, request.signal),
    fetchBROType("bhrp", bbox, request.signal),
    fetchBROType("bhrg", bbox, request.signal),
  ]);

  return Response.json({
    cpt:  cptn.status  === "fulfilled" ? cptn.value  : [],
    bhrp: bhrp.status === "fulfilled" ? bhrp.value : [],
    bhrg: bhrg.status === "fulfilled" ? bhrg.value : [],
  });
}
