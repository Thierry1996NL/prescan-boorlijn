// app/api/bgt/route.js
// Server-side BGT proxy met meerdere PDOK endpoints (v1_0 is afgesloten)

function wgs84NaarRd(lat, lng) {
  let x = 155000 + (lng - 5.38720621) * 67000;
  let y = 463000 + (lat - 52.15517440) * 111300;
  for (let i = 0; i < 5; i++) {
    const dX = (x-155000)/100000, dY = (y-463000)/100000;
    const sN = 3235.65389*dY - 32.58297*dX*dX - 0.24750*dY*dY - 0.84978*dX*dX*dY - 0.06550*dY*dY*dY;
    const sE = 5260.52916*dX + 105.94684*dX*dY + 2.45656*dX*dY*dY - 0.81885*dX*dX*dX;
    x += (lng - (5.38720621 + sE/3600)) * 67000;
    y += (lat - (52.15517440 + sN/3600)) * 111300;
  }
  return { x: Math.round(x), y: Math.round(y) };
}

const LAAG_TYPEN = [
  { name: "wegdeel",               resultType: "wegdeel"  },
  { name: "waterdeel",              resultType: "waterdeel" },
  { name: "onbegroeidterreindeel",  resultType: "onbegroeid" },
  { name: "begroeidterreindeel",    resultType: "begroeid" },
  { name: "spoor",                  resultType: "spoor" },
];

export async function GET(request) {
  const { searchParams } = new URL(request.url);
  const lat = parseFloat(searchParams.get("lat"));
  const lng = parseFloat(searchParams.get("lng"));
  if (isNaN(lat) || isNaN(lng)) {
    return Response.json({ features: [], error: "Ongeldige coördinaten" }, { status: 400 });
  }

  const rd  = wgs84NaarRd(lat, lng);
  const buf = 300;
  const rdBbox = `${rd.x-buf},${rd.y-buf},${rd.x+buf},${rd.y+buf}`;
  // WGS84 bbox (lng,lat volgorde = CRS:84)
  const d = 0.004;
  const wgsBbox = `${lng-d},${lat-d},${lng+d},${lat+d}`;

  const h = { "User-Agent": "PrescanAI/1.0", "Accept": "application/json" };
  const tried = [];

  // ── Poging 1: WFS v2_0 + RD New ───────────────────────────────
  const lagen = "bgt:wegdeel,bgt:onbegroeidterreindeel,bgt:begroeidterreindeel,bgt:waterdeel,bgt:spoor";
  const wfsUrls = [
    `https://service.pdok.nl/lv/bgt/wfs/v2_0?service=WFS&version=2.0.0&request=GetFeature&typeName=${lagen}&outputFormat=application/json&bbox=${rdBbox},EPSG:28992&srsName=EPSG:28992&count=10`,
    `https://service.pdok.nl/lv/bgt/wfs/v1_0?service=WFS&version=2.0.0&request=GetFeature&typeName=${lagen}&outputFormat=application/json&bbox=${rdBbox},EPSG:28992&srsName=EPSG:28992&count=10`,
    `https://service.pdok.nl/lv/bgt/wfs?service=WFS&version=2.0.0&request=GetFeature&typeName=${lagen}&outputFormat=application/json&bbox=${rdBbox},EPSG:28992&srsName=EPSG:28992&count=10`,
  ];

  for (const url of wfsUrls) {
    tried.push(url.substring(0, 80));
    try {
      const res = await fetch(url, { headers: h, cache: "no-store" });
      const text = await res.text();
      if (!res.ok || text.includes("404") || text.includes("not found")) continue;
      let data; try { data = JSON.parse(text); } catch { continue; }
      if (data.features?.length) {
        return Response.json({ ...data, _debug: { rdX: rd.x, rdY: rd.y, source: url.substring(0,80) } });
      }
    } catch { continue; }
  }

  // ── Poging 2: OGC API Features (nieuwe PDOK API) ───────────────
  for (const laag of LAAG_TYPEN) {
    const ogcUrl = `https://api.pdok.nl/lv/bgt/ogc/v1/collections/${laag.name}/items?bbox=${wgsBbox}&bbox-crs=http://www.opengis.net/def/crs/OGC/1.3/CRS84&limit=5&f=json`;
    tried.push(`OGC:${laag.name}`);
    try {
      const res = await fetch(ogcUrl, { headers: h, cache: "no-store" });
      const text = await res.text();
      if (!res.ok || text.includes("404")) continue;
      let data; try { data = JSON.parse(text); } catch { continue; }
      if (data.features?.length) {
        // OGC API geeft features terug zonder namespace prefix — voeg toe voor classificatie
        const enriched = data.features.map(f => ({
          ...f,
          id: `${laag.name}.${f.id || Math.random()}`,
        }));
        return Response.json({
          type: "FeatureCollection", features: enriched,
          _debug: { rdX: rd.x, rdY: rd.y, source: `OGC:${laag.name}` }
        });
      }
    } catch { continue; }
  }

  // ── Poging 3: PDOK Locatieserver reverse (adres = tertiaire fallback) ──
  const lsUrl = `https://api.pdok.nl/bzk/locatieserver/search/v3_1/reverse?lat=${lat}&lon=${lng}&type=adres&distance=200`;
  tried.push("locatieserver");
  try {
    const res = await fetch(lsUrl, { headers: h, cache: "no-store" });
    if (res.ok) {
      const data = await res.json();
      const doc = data.response?.docs?.[0];
      if (doc) {
        return Response.json({
          features: [],
          _locatieserver: { adres: doc.weergavenaam, type: doc.type },
          _debug: { rdX: rd.x, rdY: rd.y, source: "locatieserver", tried }
        });
      }
    }
  } catch { /* ignore */ }

  return Response.json({
    features: [],
    error: "Geen BGT data gevonden via alle endpoints",
    _debug: { rdX: rd.x, rdY: rd.y, wgsBbox, rdBbox, tried }
  });
}
