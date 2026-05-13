// app/api/bgt/route.js
// Proxy voor PDOK BGT WMS GetFeatureInfo — omzeilt CORS

export async function GET(request) {
  const { searchParams } = new URL(request.url);
  const lat = parseFloat(searchParams.get("lat"));
  const lng = parseFloat(searchParams.get("lng"));

  if (!lat || !lng) {
    return Response.json({ error: "lat en lng zijn verplicht" }, { status: 400 });
  }

  const delta = 0.0005;
  const W = 101, H = 101;
  const I = 50, J = 50; // altijd middelpunt van de bbox

  const BGT_LAGEN = [
    { laag: "wegdeel", prop: "fysiekVoorkomen" },
    { laag: "begroeidterreindeel", prop: "fysiekVoorkomen" },
    { laag: "onbegroeidterreindeel", prop: "fysiekVoorkomen" },
    { laag: "ondersteunendwegdeel", prop: "fysiekVoorkomen" },
    { laag: "waterdeel", prop: "typeWater" },
  ];

  for (const { laag, prop } of BGT_LAGEN) {
    try {
      const params = new URLSearchParams({
        SERVICE: "WMS",
        VERSION: "1.3.0",
        REQUEST: "GetFeatureInfo",
        LAYERS: laag,
        QUERY_LAYERS: laag,
        CRS: "EPSG:4326",
        BBOX: `${lat - delta},${lng - delta},${lat + delta},${lng + delta}`,
        WIDTH: String(W),
        HEIGHT: String(H),
        I: String(I),
        J: String(J),
        INFO_FORMAT: "application/json",
        FEATURE_COUNT: "1",
      });

      const res = await fetch(
        `https://service.pdok.nl/lv/bgt/wms/v1_0?${params}`,
        { headers: { "User-Agent": "PrescanBoorlijnAI/1.0" } }
      );

      if (!res.ok) continue;

      const json = await res.json();
      const features = json.features ?? [];

      if (features.length > 0) {
        const props = features[0].properties ?? {};
        const type = props[prop] ?? null;
        if (type && type !== "transitie" && type !== "") {
          return Response.json({ laag, type });
        }
      }
    } catch (e) {
      console.error("BGT proxy fout:", laag, e);
    }
  }

  return Response.json({ laag: null, type: null });
}
