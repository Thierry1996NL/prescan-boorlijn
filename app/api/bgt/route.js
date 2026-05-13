// app/api/bgt/route.js
// Proxy voor PDOK BGT — omzeilt CORS

export async function GET(request) {
  const { searchParams } = new URL(request.url);
  const lat = parseFloat(searchParams.get("lat"));
  const lng = parseFloat(searchParams.get("lng"));

  if (!lat || !lng) {
    return Response.json({ error: "lat en lng zijn verplicht" }, { status: 400 });
  }

  const delta = 0.0005;
  const bbox = `${lat - delta},${lng - delta},${lat + delta},${lng + delta}`;

  const BGT_LAGEN = [
    { laag: "wegdeel", prop: "fysiekVoorkomen" },
    { laag: "begroeidterreindeel", prop: "fysiekVoorkomen" },
    { laag: "onbegroeidterreindeel", prop: "fysiekVoorkomen" },
    { laag: "ondersteunendwegdeel", prop: "fysiekVoorkomen" },
    { laag: "waterdeel", prop: "typeWater" },
  ];

  for (const { laag, prop } of BGT_LAGEN) {
    try {
      // WFS aanroep — werkt server-side zonder CORS
      const wfsParams = new URLSearchParams({
        service: "WFS",
        version: "2.0.0",
        request: "GetFeature",
        typeName: `bgt:${laag}`,
        bbox: `${lat - delta},${lng - delta},${lat + delta},${lng + delta},urn:ogc:def:crs:EPSG::4326`,
        outputFormat: "application/json",
        count: "1",
        srsName: "EPSG:4326",
      });

      const wfsRes = await fetch(
        `https://service.pdok.nl/lv/bgt/wfs/v1_0?${wfsParams}`,
        { headers: { "User-Agent": "Mozilla/5.0", Accept: "application/json" } }
      );

      if (wfsRes.ok) {
        const json = await wfsRes.json();
        const features = json.features ?? [];
        if (features.length > 0) {
          const props = features[0].properties ?? {};
          const type = props[prop] ?? props[`plus-${prop}`] ?? null;
          if (type && type !== "transitie" && type !== "") {
            return Response.json({ laag, type, bron: "wfs" });
          }
        }
      }

      // Fallback: WMS GetFeatureInfo
      const wmsParams = new URLSearchParams({
        SERVICE: "WMS", VERSION: "1.3.0", REQUEST: "GetFeatureInfo",
        LAYERS: laag, QUERY_LAYERS: laag, CRS: "EPSG:4326",
        BBOX: bbox, WIDTH: "101", HEIGHT: "101", I: "50", J: "50",
        INFO_FORMAT: "text/plain", FEATURE_COUNT: "1",
      });

      const wmsRes = await fetch(
        `https://service.pdok.nl/lv/bgt/wms/v1_0?${wmsParams}`,
        { headers: { "User-Agent": "Mozilla/5.0" } }
      );

      if (wmsRes.ok) {
        const tekst = await wmsRes.text();
        const regex = new RegExp(`${prop}\\s*=\\s*'?([^'\\n]+)'?`, "i");
        const match = tekst.match(regex);
        if (match?.[1] && match[1] !== "transitie") {
          return Response.json({ laag, type: match[1].trim(), bron: "wms" });
        }
      }

    } catch (e) {
      console.error("BGT fout:", laag, e.message);
    }
  }

  return Response.json({ laag: null, type: null, debug: { lat, lng, bbox } });
}
