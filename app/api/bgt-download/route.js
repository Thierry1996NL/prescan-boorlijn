// app/api/bgt-download/route.js
// PDOK BGT Custom Download API proxy

const PDOK_BASE = "https://api.pdok.nl/lv/bgt/download/v1_0/full/custom";

const BGT_FEATURETYPES = [
  "begroeidterreindeel","onbegroeidterreindeel","waterdeel","ondersteunendwaterdeel",
  "wegdeel","ondersteunendwegdeel","weginrichtingselement","pand","overigbouwwerk",
  "kunstwerkdeel","tunneldeel","spoor","scheiding","vegetatieobject",
  "waterinrichtingselement","functioneelgebied",
];

export async function POST(request) {
  try {
    const { sw, ne } = await request.json();
    if (!sw?.lat || !sw?.lng || !ne?.lat || !ne?.lng)
      return Response.json({ error: "Ongeldige coördinaten" }, { status: 400 });

    // WKT polygon in WGS84 — lng lat volgorde
    const poly = `POLYGON((${sw.lng} ${sw.lat},${ne.lng} ${sw.lat},${ne.lng} ${ne.lat},${sw.lng} ${ne.lat},${sw.lng} ${sw.lat}))`;

    const res = await fetch(PDOK_BASE, {
      method: "POST",
      headers: { "Content-Type": "application/json", "Accept": "application/json" },
      body: JSON.stringify({
        featuretypes: BGT_FEATURETYPES,
        format: "gml",   // PDOK ondersteunt: gml, citygml, imgeo-gml
        geofilter: poly,
      }),
    });

    const txt = await res.text();
    console.log("[BGT-download] status:", res.status, "body:", txt.slice(0, 200));

    if (!res.ok) return Response.json({ error: `PDOK ${res.status}: ${txt}` }, { status: res.status });
    return Response.json(JSON.parse(txt));
  } catch (e) {
    console.error("[BGT-download] POST fout:", e.message);
    return Response.json({ error: e.message }, { status: 500 });
  }
}

export async function GET(request) {
  const id = new URL(request.url).searchParams.get("id");
  if (!id) return Response.json({ error: "Geen id" }, { status: 400 });
  try {
    const res = await fetch(`${PDOK_BASE}/${id}`, { headers: { "Accept": "application/json" } });
    const txt = await res.text();
    if (!res.ok) return Response.json({ error: `PDOK ${res.status}: ${txt}` }, { status: res.status });
    return Response.json(JSON.parse(txt));
  } catch (e) {
    return Response.json({ error: e.message }, { status: 500 });
  }
}
