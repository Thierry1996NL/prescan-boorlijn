// app/api/bgt/route.js
// Server-side proxy voor PDOK BGT WFS

export async function GET(request) {
  const { searchParams } = new URL(request.url);
  const lat = parseFloat(searchParams.get("lat"));
  const lng = parseFloat(searchParams.get("lng"));

  if (isNaN(lat) || isNaN(lng)) {
    return Response.json({ error: "Ongeldige coördinaten", features: [] }, { status: 400 });
  }

  // CRS:84 gebruikt altijd longitude,latitude volgorde (niet EPSG:4326 die Y,X verwacht)
  const d    = 0.0005; // ~50m straal
  const bbox = `${lng-d},${lat-d},${lng+d},${lat+d}`;
  const lagen = "bgt:wegdeel,bgt:onbegroeidterreindeel,bgt:begroeidterreindeel,bgt:waterdeel,bgt:spoor";

  const url = `https://service.pdok.nl/lv/bgt/wfs/v1_0?service=WFS&version=2.0.0&request=GetFeature`
    + `&typeName=${lagen}`
    + `&outputFormat=application/json`
    + `&bbox=${bbox},urn:ogc:def:crs:OGC:1.3:CRS84`   // CRS:84 = lng,lat (correct!)
    + `&count=10`;

  try {
    const res = await fetch(url, {
      headers: { "User-Agent": "PrescanAI/1.0" },
      next: { revalidate: 86400 },
    });

    if (!res.ok) {
      // Fallback: probeer met lat,lng volgorde (EPSG:4326 strict)
      const urlFallback = `https://service.pdok.nl/lv/bgt/wfs/v1_0?service=WFS&version=2.0.0&request=GetFeature`
        + `&typeName=${lagen}`
        + `&outputFormat=application/json`
        + `&bbox=${lat-d},${lng-d},${lat+d},${lng+d},EPSG:4326`
        + `&count=10`;
      const res2 = await fetch(urlFallback, { headers: { "User-Agent": "PrescanAI/1.0" } });
      if (!res2.ok) return Response.json({ error: `PDOK HTTP ${res.status}`, features: [], url }, { status: 200 });
      const data2 = await res2.json();
      return Response.json({ ...data2, _source: "fallback_EPSG4326" });
    }

    const data = await res.json();

    // Als 0 features: probeer fallback met andere bbox-volgorde
    if (!data.features?.length) {
      const urlFallback = `https://service.pdok.nl/lv/bgt/wfs/v1_0?service=WFS&version=2.0.0&request=GetFeature`
        + `&typeName=${lagen}`
        + `&outputFormat=application/json`
        + `&bbox=${lat-d},${lng-d},${lat+d},${lng+d},EPSG:4326`
        + `&count=10`;
      const res2 = await fetch(urlFallback, { headers: { "User-Agent": "PrescanAI/1.0" } });
      if (res2.ok) {
        const data2 = await res2.json();
        if (data2.features?.length) {
          return Response.json({ ...data2, _source: "fallback_EPSG4326" });
        }
      }
    }

    return Response.json({ ...data, _source: "CRS84" });
  } catch (err) {
    return Response.json({ error: err.message, features: [], url }, { status: 200 });
  }
}
