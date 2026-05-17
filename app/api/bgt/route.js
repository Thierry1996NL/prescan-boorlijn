// app/api/bgt/route.js
// Bulk BGT query — één request voor de gehele boorlijn bbox

function wgs84NaarRd(lat, lng) {
  let x = 155000 + (lng - 5.38720621) * 67000;
  let y = 463000 + (lat - 52.15517440) * 111300;
  for (let i = 0; i < 5; i++) {
    const dX=(x-155000)/100000, dY=(y-463000)/100000;
    const sN=3235.65389*dY-32.58297*dX*dX-0.24750*dY*dY-0.84978*dX*dX*dY-0.06550*dY*dY*dY;
    const sE=5260.52916*dX+105.94684*dX*dY+2.45656*dX*dY*dY-0.81885*dX*dX*dX;
    x += (lng-(5.38720621+sE/3600))*67000;
    y += (lat-(52.15517440+sN/3600))*111300;
  }
  return { x: Math.round(x), y: Math.round(y) };
}

const OGC_COLLECTIONS = [
  "wegdeel", "waterdeel", "onbegroeidterreindeel", "begroeidterreindeel", "spoor"
];

export async function GET(request) {
  const { searchParams } = new URL(request.url);
  const lat = parseFloat(searchParams.get("lat"));
  const lng = parseFloat(searchParams.get("lng"));
  // Bulk mode: minLat,minLng,maxLat,maxLng voor heel tracé
  const minLat = parseFloat(searchParams.get("minLat") || lat);
  const maxLat = parseFloat(searchParams.get("maxLat") || lat);
  const minLng = parseFloat(searchParams.get("minLng") || lng);
  const maxLng = parseFloat(searchParams.get("maxLng") || lng);
  const isBulk = searchParams.has("minLat");

  if (isNaN(lat) && !isBulk) {
    return Response.json({ features: [], error: "Ongeldige coördinaten" }, { status: 400 });
  }

  const h = { "User-Agent": "PrescanAI/1.0", "Accept": "application/json" };

  if (isBulk) {
    // ── Bulk mode: geef ALLE BGT-features voor het tracé terug ───
    const buf = 0.002; // ~200m buffer rondom het tracé
    const bbox = `${minLng-buf},${minLat-buf},${maxLng+buf},${maxLat+buf}`;
    const rdMin = wgs84NaarRd(minLat, minLng);
    const rdMax = wgs84NaarRd(maxLat, maxLng);
    const rdBbox = `${rdMin.x-300},${rdMin.y-300},${rdMax.x+300},${rdMax.y+300}`;
    const lagen = "bgt:wegdeel,bgt:onbegroeidterreindeel,bgt:begroeidterreindeel,bgt:waterdeel,bgt:spoor";

    // Probeer WFS v2_0 bulk eerst
    const wfsUrl = `https://service.pdok.nl/lv/bgt/wfs/v2_0?service=WFS&version=2.0.0&request=GetFeature`
      + `&typeName=${lagen}&outputFormat=application/json&bbox=${rdBbox},EPSG:28992&srsName=EPSG:28992&count=200`;
    try {
      const res = await fetch(wfsUrl, { headers: h, cache: "no-store" });
      const text = await res.text();
      if (res.ok && !text.includes("404") && !text.includes("not found")) {
        const data = JSON.parse(text);
        if (data.features?.length > 0) {
          return Response.json({ ...data, _source: "WFS_v2_bulk" });
        }
      }
    } catch { /* fallthrough */ }

    // Fallback: OGC API bulk per collectie
    const allFeatures = [];
    for (const col of OGC_COLLECTIONS) {
      const ogcUrl = `https://api.pdok.nl/lv/bgt/ogc/v1/collections/${col}/items`
        + `?bbox=${bbox}&bbox-crs=http://www.opengis.net/def/crs/OGC/1.3/CRS84&limit=100&f=json`;
      try {
        const res = await fetch(ogcUrl, { headers: h, cache: "no-store" });
        const text = await res.text();
        if (!res.ok || text.includes("404")) continue;
        const data = JSON.parse(text);
        if (data.features?.length) {
          data.features.forEach(f => {
            allFeatures.push({ ...f, id: `${col}.${f.id || f.properties?.identificatie || Math.random()}` });
          });
        }
      } catch { continue; }
    }
    return Response.json({
      type: "FeatureCollection", features: allFeatures,
      _source: "OGC_bulk", _debug: { bbox, count: allFeatures.length }
    });
  }

  // ── Enkelpunt modus (voor test knop) ─────────────────────────
  const rd = wgs84NaarRd(lat, lng);
  const buf = 300;
  const rdBbox = `${rd.x-buf},${rd.y-buf},${rd.x+buf},${rd.y+buf}`;
  const wgsBbox = `${lng-0.004},${lat-0.004},${lng+0.004},${lat+0.004}`;
  const lagen = "bgt:wegdeel,bgt:onbegroeidterreindeel,bgt:begroeidterreindeel,bgt:waterdeel,bgt:spoor";

  // WFS
  for (const ver of ["v2_0","v1_0",""]) {
    const base = ver ? `/lv/bgt/wfs/${ver}` : `/lv/bgt/wfs`;
    const url = `https://service.pdok.nl${base}?service=WFS&version=2.0.0&request=GetFeature&typeName=${lagen}&outputFormat=application/json&bbox=${rdBbox},EPSG:28992&srsName=EPSG:28992&count=10`;
    try {
      const res = await fetch(url, { headers: h, cache: "no-store" });
      const text = await res.text();
      if (!res.ok||text.includes("404")) continue;
      const data = JSON.parse(text);
      if (data.features?.length) return Response.json({ ...data, _debug: { rdX: rd.x, rdY: rd.y, source: `WFS_${ver}` } });
    } catch { continue; }
  }
  // OGC
  for (const col of OGC_COLLECTIONS) {
    const url = `https://api.pdok.nl/lv/bgt/ogc/v1/collections/${col}/items?bbox=${wgsBbox}&bbox-crs=http://www.opengis.net/def/crs/OGC/1.3/CRS84&limit=10&f=json`;
    try {
      const res = await fetch(url, { headers: h, cache: "no-store" });
      const text = await res.text();
      if (!res.ok||text.includes("404")) continue;
      const data = JSON.parse(text);
      if (data.features?.length) {
        const enriched = data.features.map(f => ({ ...f, id: `${col}.${f.id||Math.random()}` }));
        return Response.json({ type:"FeatureCollection", features: enriched, _debug: { rdX: rd.x, rdY: rd.y, source: `OGC_${col}` } });
      }
    } catch { continue; }
  }
  return Response.json({ features: [], _debug: { rdX: rd.x, rdY: rd.y } });
}
