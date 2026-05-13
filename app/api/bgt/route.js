// app/api/bgt/route.js

export async function GET(request) {
  const { searchParams } = new URL(request.url);
  const lat = parseFloat(searchParams.get("lat"));
  const lng = parseFloat(searchParams.get("lng"));

  if (!lat || !lng) {
    return Response.json({ error: "lat en lng zijn verplicht" }, { status: 400 });
  }

  const delta = 0.0005;
  const bbox = `${lng - delta},${lat - delta},${lng + delta},${lat + delta}`;

  const debugLog = [];

  // Test 1: OGC API Features
  try {
    const url = `https://service.pdok.nl/lv/bgt/ogc/v1_0/collections/wegdeel/items?bbox=${bbox}&limit=1&f=json`;
    debugLog.push({ test: "ogc_url", url });
    const res = await fetch(url, { headers: { "Accept": "application/json" } });
    debugLog.push({ test: "ogc_status", status: res.status, ok: res.ok });
    if (res.ok) {
      const text = await res.text();
      debugLog.push({ test: "ogc_response", preview: text.substring(0, 500) });
      try {
        const json = JSON.parse(text);
        debugLog.push({ test: "ogc_features_count", count: json.features?.length ?? 0 });
        if (json.features?.length > 0) {
          debugLog.push({ test: "ogc_first_props", props: json.features[0].properties });
        }
      } catch (e) {
        debugLog.push({ test: "ogc_parse_error", error: e.message });
      }
    }
  } catch (e) {
    debugLog.push({ test: "ogc_error", error: e.message });
  }

  // Test 2: WFS met lng,lat volgorde
  try {
    const wfsUrl = `https://service.pdok.nl/lv/bgt/wfs/v1_0?service=WFS&version=2.0.0&request=GetFeature&typeName=bgt:wegdeel&bbox=${lng-delta},${lat-delta},${lng+delta},${lat+delta}&outputFormat=application/json&count=1`;
    debugLog.push({ test: "wfs_url", url: wfsUrl });
    const res = await fetch(wfsUrl, { headers: { "Accept": "application/json" } });
    debugLog.push({ test: "wfs_status", status: res.status });
    if (res.ok) {
      const text = await res.text();
      debugLog.push({ test: "wfs_response", preview: text.substring(0, 300) });
    }
  } catch (e) {
    debugLog.push({ test: "wfs_error", error: e.message });
  }

  return Response.json({ laag: null, type: null, debug: debugLog });
}
