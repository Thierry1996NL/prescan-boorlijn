// app/api/bgt-download/route.js
// PDOK BGT Custom Download API proxy
// Docs: https://api.pdok.nl/lv/bgt/download/v1_0/ui/

const PDOK_BASE = "https://api.pdok.nl/lv/bgt/download/v1_0/full/custom";

// Alle relevante BGT-lagen voor HDD-prescan
const BGT_FEATURETYPES = [
  "begroeidterreindeel",
  "onbegroeidterreindeel",
  "waterdeel",
  "ondersteunendwaterdeel",
  "wegdeel",
  "ondersteunendwegdeel",
  "weginrichtingselement",
  "pand",
  "overigbouwwerk",
  "kunstwerkdeel",
  "tunneldeel",
  "spoor",
  "scheiding",
  "vegetatieobject",
  "waterinrichtingselement",
  "functioneelgebied",
];

// POST /api/bgt-download  → maak download-aanvraag
export async function POST(request) {
  try {
    const { sw, ne, format = "geopackage" } = await request.json();

    if (!sw?.lat || !sw?.lng || !ne?.lat || !ne?.lng) {
      return Response.json({ error: "Ongeldige coördinaten" }, { status: 400 });
    }

    // Maximale bbox-grootte check (~10km²)
    const dLat = Math.abs(ne.lat - sw.lat);
    const dLng = Math.abs(ne.lng - sw.lng);
    if (dLat > 0.15 || dLng > 0.2) {
      return Response.json(
        { error: "Gebied te groot — maximaal ~10×10 km" },
        { status: 400 }
      );
    }

    // WKT polygon in WGS84 (lng lat)
    const poly = `POLYGON((${sw.lng} ${sw.lat},${ne.lng} ${sw.lat},${ne.lng} ${ne.lat},${sw.lng} ${ne.lat},${sw.lng} ${sw.lat}))`;

    const res = await fetch(PDOK_BASE, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "Accept": "application/json",
      },
      body: JSON.stringify({
        featuretypes: BGT_FEATURETYPES,
        format,          // "geopackage" of "gml"
        geofilter: poly,
      }),
    });

    if (!res.ok) {
      const txt = await res.text();
      return Response.json(
        { error: `PDOK ${res.status}: ${txt}` },
        { status: res.status }
      );
    }

    const data = await res.json();
    return Response.json(data);
  } catch (e) {
    return Response.json({ error: e.message }, { status: 500 });
  }
}

// GET /api/bgt-download?id={downloadRequestId}  → poll status
export async function GET(request) {
  const { searchParams } = new URL(request.url);
  const id = searchParams.get("id");
  if (!id) return Response.json({ error: "Geen id" }, { status: 400 });

  try {
    const res = await fetch(`${PDOK_BASE}/${id}`, {
      headers: { "Accept": "application/json" },
    });
    if (!res.ok) {
      return Response.json({ error: `PDOK ${res.status}` }, { status: res.status });
    }
    return Response.json(await res.json());
  } catch (e) {
    return Response.json({ error: e.message }, { status: 500 });
  }
}
