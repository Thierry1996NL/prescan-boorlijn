// app/api/bgt/route.js
// BGT proxy met Vercel edge-caching — voorkomt rate limiting door PDOK

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

const OGC_BASE = "https://api.pdok.nl/lv/bgt/ogc/v1/collections";
const OGC_COLS = ["begroeidterreindeel","onbegroeidterreindeel","wegdeel","waterdeel","spoor"];
const H = { "User-Agent": "PrescanAI/1.0" };

// PDOK OGC API ophalen met Vercel caching (1 uur)
async function fetchOgc(collection, bbox) {
  const url = `${OGC_BASE}/${collection}/items?bbox=${bbox}`
    + `&bbox-crs=http://www.opengis.net/def/crs/OGC/1.3/CRS84&limit=100&f=json`;
  try {
    // next: revalidate = Vercel slaat dit op in edge cache → max 1 PDOK-call/uur per bbox
    const res = await fetch(url, { headers: H, next: { revalidate: 3600 } });
    if (!res.ok) return [];
    const data = await res.json();
    return (data.features || []).map(f => ({
      ...f,
      id: `${collection}.${f.id || f.properties?.identificatie || Math.random()}`,
    }));
  } catch { return []; }
}

export async function GET(request) {
  const { searchParams } = new URL(request.url);

  // Bepaal bbox
  const isBulk = searchParams.has("minLat");
  let minLat, maxLat, minLng, maxLng;
  if (isBulk) {
    minLat = parseFloat(searchParams.get("minLat"));
    maxLat = parseFloat(searchParams.get("maxLat"));
    minLng = parseFloat(searchParams.get("minLng"));
    maxLng = parseFloat(searchParams.get("maxLng"));
  } else {
    const lat = parseFloat(searchParams.get("lat"));
    const lng = parseFloat(searchParams.get("lng"));
    if (isNaN(lat)||isNaN(lng)) return Response.json({features:[],error:"Ongeldige coördinaten"});
    const d = 0.003;
    minLat=lat-d; maxLat=lat+d; minLng=lng-d; maxLng=lng+d;
  }

  const buf = 0.002;
  const bbox = `${minLng-buf},${minLat-buf},${maxLng+buf},${maxLat+buf}`;

  // Haal alle collecties parallel op (parallel = sneller, en elk resultaat apart gecached)
  const results = await Promise.all(OGC_COLS.map(col => fetchOgc(col, bbox)));
  const allFeatures = results.flat();

  // Debug: bereken RD voor het middelpunt
  const midLat = (minLat+maxLat)/2, midLng = (minLng+maxLng)/2;
  const rd = wgs84NaarRd(midLat, midLng);

  return Response.json({
    type: "FeatureCollection",
    features: allFeatures,
    _source: "OGC_parallel",
    _debug: {
      bbox, count: allFeatures.length,
      rdX: rd.x, rdY: rd.y,
      perCollectie: OGC_COLS.map((c,i)=>({col:c,n:results[i].length}))
    }
  });
}
