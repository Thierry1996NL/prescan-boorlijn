// app/api/bgt/route.js — Grondige debug versie

export const runtime = "edge";
export const preferredRegion = "fra1";

export async function GET(request) {
  const { searchParams } = new URL(request.url);
  const lat = parseFloat(searchParams.get("lat"));
  const lng = parseFloat(searchParams.get("lng"));
  if (!lat || !lng) return Response.json({ error: "lat/lng verplicht" }, { status: 400 });

  const delta = 0.0003;
  const bbox = `${lng - delta},${lat - delta},${lng + delta},${lat + delta}`;
  const resultaten = [];

  // Test 1: PDOK BGT OGC op service.pdok.nl
  try {
    const url = `https://service.pdok.nl/lv/bgt/ogc/v1_0/collections/wegdeel/items?bbox=${bbox}&limit=1&f=json`;
    const r = await fetch(url, { signal: AbortSignal.timeout(4000) });
    const body = await r.text();
    resultaten.push({ test: "pdok_ogc_service", status: r.status, preview: body.slice(0, 200) });
  } catch(e) { resultaten.push({ test: "pdok_ogc_service", fout: e.message }); }

  // Test 2: PDOK BGT OGC op api.pdok.nl (alternatief domein)
  try {
    const url = `https://api.pdok.nl/lv/bgt/ogc/v1_0/collections/wegdeel/items?bbox=${bbox}&limit=1&f=json`;
    const r = await fetch(url, { signal: AbortSignal.timeout(4000) });
    const body = await r.text();
    resultaten.push({ test: "pdok_ogc_api", status: r.status, preview: body.slice(0, 200) });
  } catch(e) { resultaten.push({ test: "pdok_ogc_api", fout: e.message }); }

  // Test 3: PDOK WFS
  try {
    const url = `https://service.pdok.nl/lv/bgt/wfs/v1_0?service=WFS&version=2.0.0&request=GetFeature&typeName=bgt:wegdeel&bbox=${bbox}&outputFormat=application/json&count=1`;
    const r = await fetch(url, { signal: AbortSignal.timeout(4000) });
    const body = await r.text();
    resultaten.push({ test: "pdok_wfs", status: r.status, preview: body.slice(0, 200) });
  } catch(e) { resultaten.push({ test: "pdok_wfs", fout: e.message }); }

  // Test 4: Nominatim reverse geocode met extratags
  try {
    const url = `https://nominatim.openstreetmap.org/reverse?lat=${lat}&lon=${lng}&format=json&extratags=1`;
    const r = await fetch(url, { headers: { "User-Agent": "PrescanBoorlijnAI/1.0" }, signal: AbortSignal.timeout(4000) });
    const body = await r.text();
    resultaten.push({ test: "nominatim", status: r.status, preview: body.slice(0, 400) });
  } catch(e) { resultaten.push({ test: "nominatim", fout: e.message }); }

  // Test 5: Overpass zonder headers
  try {
    const query = `[out:json][timeout:3];way(around:15,${lat},${lng})[surface];out tags 1;`;
    const url = `https://overpass-api.de/api/interpreter?data=${encodeURIComponent(query)}`;
    const r = await fetch(url, { signal: AbortSignal.timeout(5000) });
    const body = await r.text();
    resultaten.push({ test: "overpass", status: r.status, preview: body.slice(0, 300) });
  } catch(e) { resultaten.push({ test: "overpass", fout: e.message }); }

  return Response.json({ resultaten, lat, lng, bbox });
}
