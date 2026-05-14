// app/api/bgt/route.js — debug v2

export const runtime = "edge";
export const preferredRegion = "fra1";

export async function GET(request) {
  const { searchParams } = new URL(request.url);
  const lat = parseFloat(searchParams.get("lat"));
  const lng = parseFloat(searchParams.get("lng"));
  if (!lat || !lng) return Response.json({ error: "lat/lng verplicht" }, { status: 400 });

  // Grotere bbox — 100m radius
  const delta = 0.001;
  const bbox = `${lng - delta},${lat - delta},${lng + delta},${lat + delta}`;

  const resultaten = [];

  // Test 1: Controleer beschikbare collecties
  try {
    const r = await fetch("https://api.pdok.nl/lv/bgt/ogc/v1/collections?f=json", {
      signal: AbortSignal.timeout(4000)
    });
    const json = await r.json();
    const namen = json.collections?.map(c => c.id) ?? [];
    resultaten.push({ test: "collecties", namen });
  } catch(e) { resultaten.push({ test: "collecties", fout: e.message }); }

  // Test 2: Wegdeel met grotere bbox
  try {
    const url = `https://api.pdok.nl/lv/bgt/ogc/v1/collections/wegdeel/items?bbox=${bbox}&limit=3&f=json`;
    const r = await fetch(url, { signal: AbortSignal.timeout(4000) });
    const json = await r.json();
    resultaten.push({
      test: "wegdeel",
      status: r.status,
      aantal: json.features?.length ?? 0,
      eerste_props: json.features?.[0]?.properties ?? null,
    });
  } catch(e) { resultaten.push({ test: "wegdeel", fout: e.message }); }

  // Test 3: Begroeid terrein
  try {
    const url = `https://api.pdok.nl/lv/bgt/ogc/v1/collections/begroeidterreindeel/items?bbox=${bbox}&limit=3&f=json`;
    const r = await fetch(url, { signal: AbortSignal.timeout(4000) });
    const json = await r.json();
    resultaten.push({
      test: "begroeid",
      status: r.status,
      aantal: json.features?.length ?? 0,
      eerste_props: json.features?.[0]?.properties ?? null,
    });
  } catch(e) { resultaten.push({ test: "begroeid", fout: e.message }); }

  return Response.json({ resultaten, lat, lng, bbox });
}
