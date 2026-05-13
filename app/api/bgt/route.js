// app/api/bgt/route.js
// Proxy voor PDOK BGT via OGC API Features — omzeilt CORS

export async function GET(request) {
  const { searchParams } = new URL(request.url);
  const lat = parseFloat(searchParams.get("lat"));
  const lng = parseFloat(searchParams.get("lng"));

  if (!lat || !lng) {
    return Response.json({ error: "lat en lng zijn verplicht" }, { status: 400 });
  }

  const delta = 0.0003; // ~30m radius
  // OGC API Features gebruikt lng,lat volgorde (x,y)
  const bbox = `${lng - delta},${lat - delta},${lng + delta},${lat + delta}`;

  const COLLECTIES = [
    { naam: "wegdeel", prop: "fysiekVoorkomen" },
    { naam: "begroeidterreindeel", prop: "fysiekVoorkomen" },
    { naam: "onbegroeidterreindeel", prop: "fysiekVoorkomen" },
    { naam: "ondersteunendwegdeel", prop: "fysiekVoorkomen" },
    { naam: "waterdeel", prop: "typeWater" },
  ];

  for (const { naam, prop } of COLLECTIES) {
    try {
      const url = `https://service.pdok.nl/lv/bgt/ogc/v1_0/collections/${naam}/items?bbox=${bbox}&limit=1&f=json`;
      
      const res = await fetch(url, {
        headers: {
          "User-Agent": "PrescanBoorlijnAI/1.0",
          "Accept": "application/geo+json, application/json",
        },
        next: { revalidate: 0 },
      });

      if (!res.ok) {
        console.log(`BGT ${naam}: HTTP ${res.status}`);
        continue;
      }

      const json = await res.json();
      const features = json.features ?? [];

      if (features.length > 0) {
        const props = features[0].properties ?? {};
        // Probeer meerdere property namen
        const type = props[prop]
          ?? props[`plus-${prop}`]
          ?? props[`plus_${prop}`]
          ?? props["fysiekVoorkomen"]
          ?? props["typeWater"]
          ?? null;

        if (type && type !== "transitie" && type.trim() !== "") {
          return Response.json({ laag: naam, type: type.trim(), bron: "ogc" });
        }
      }
    } catch (e) {
      console.error(`BGT ${naam} fout:`, e.message);
    }
  }

  return Response.json({ 
    laag: null, 
    type: null, 
    debug: { lat, lng, bbox }
  });
}
