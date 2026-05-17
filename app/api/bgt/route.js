// app/api/bgt/route.js
// Server-side proxy voor PDOK BGT WFS — omzeilt CORS in de browser

export async function GET(request) {
  const { searchParams } = new URL(request.url);
  const lat = parseFloat(searchParams.get("lat"));
  const lng = parseFloat(searchParams.get("lng"));

  if (isNaN(lat) || isNaN(lng)) {
    return Response.json({ error: "Ongeldige coördinaten", features: [] }, { status: 400 });
  }

  const d    = 0.0003; // ~30m straal
  const bbox = `${lng-d},${lat-d},${lng+d},${lat+d}`;
  const url  = `https://service.pdok.nl/lv/bgt/wfs/v1_0?service=WFS&version=2.0.0&request=GetFeature`
    + `&typeName=bgt:wegdeel,bgt:onbegroeidterreindeel,bgt:begroeidterreindeel,bgt:waterdeel,bgt:spoor`
    + `&outputFormat=application/json&bbox=${bbox},EPSG:4326&srsName=EPSG:4326`;

  try {
    const res = await fetch(url, {
      headers: { "User-Agent": "PrescanAI/1.0 (Vercel)" },
      next: { revalidate: 86400 }, // cache 24h — BGT verandert zelden
    });

    if (!res.ok) {
      return Response.json(
        { error: `PDOK HTTP ${res.status}`, features: [] },
        { status: 200 }
      );
    }

    const data = await res.json();
    return Response.json(data);
  } catch (err) {
    return Response.json({ error: err.message, features: [] }, { status: 200 });
  }
}
