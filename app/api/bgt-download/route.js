// app/api/bgt-download/route.js
// PDOK BGT Custom Download API proxy

const PDOK_BASE = "https://api.pdok.nl/lv/bgt/download/v1_0/full/custom";

const BGT_FEATURETYPES = [
  "begroeidterreindeel","onbegroeidterreindeel","waterdeel","ondersteunendwaterdeel",
  "wegdeel","ondersteunendwegdeel","weginrichtingselement","pand","overigbouwwerk",
  "kunstwerkdeel","tunneldeel","spoor","scheiding","vegetatieobject",
  "waterinrichtingselement","functioneelgebied",
];

// Probe welk formaat PDOK accepteert
async function tryFormat(poly, format) {
  const res = await fetch(PDOK_BASE, {
    method: "POST",
    headers: { "Content-Type": "application/json", "Accept": "application/json" },
    body: JSON.stringify({ featuretypes: BGT_FEATURETYPES, format, geofilter: poly }),
  });
  const txt = await res.text();
  return { status: res.status, body: txt };
}

export async function POST(request) {
  try {
    const { sw, ne } = await request.json();
    if (!sw?.lat || !sw?.lng || !ne?.lat || !ne?.lng)
      return Response.json({ error: "Ongeldige coördinaten" }, { status: 400 });

    // WKT in WGS84 (lng lat)
    const poly = `POLYGON((${sw.lng} ${sw.lat},${ne.lng} ${sw.lat},${ne.lng} ${ne.lat},${sw.lng} ${ne.lat},${sw.lng} ${sw.lat}))`;

    // Probeer formaten in volgorde van voorkeur
    const formats = ["gml", "citygml", "imgeo-gml", "GML", "application/gml+xml"];
    let lastError = "";

    for (const fmt of formats) {
      const { status, body } = await tryFormat(poly, fmt);
      console.log(`[BGT] format="${fmt}" → ${status}:`, body.slice(0, 200));

      if (status < 300) {
        try {
          const data = JSON.parse(body);
          if (data.downloadRequestId) {
            console.log(`[BGT] ✓ Werkt met format="${fmt}", id=${data.downloadRequestId}`);
            return Response.json({ ...data, format: fmt });
          }
        } catch {}
      }
      lastError = `format="${fmt}": HTTP ${status}: ${body}`;
    }

    // Geen enkel formaat werkte — geef volledige fout terug
    return Response.json({ error: lastError }, { status: 400 });
  } catch (e) {
    console.error("[BGT] POST fout:", e.message);
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
