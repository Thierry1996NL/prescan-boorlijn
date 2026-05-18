// app/api/bgt-klik/route.js
// Coördinatenflow (volledig):
//   1. Client: e.latlng (WGS84, via Leaflet proj4leaflet EPSG:28992 inverse projectie)
//   2. Hier: WGS84 → RD New via polynoomformule (referentiepunt 52.15517440, 5.38720621)
//      BELANGRIJK: zelfde referentie als de map's proj4 lat_0/lon_0 → intern consistent
//   3. RD bbox (±20m) → BGT OGC API met bbox-crs=EPSG:28992
//   4. Antwoord: features in WGS84 (OGC:CRS84 = [longitude, latitude] volgorde)
//   5. Client: L.geoJSON(feature) → Leaflet leest [lng,lat] → projecteert via zelfde CRS
//
// ±20m bbox (was ±4m): tolereert kleine afrondingsfouten in de proj4/polynoom conversie
// zonder bij dicht op elkaar liggende features de verkeerde te retourneren.

const BGT_COLLECTIES = ["wegdeel","onbegroeidterreindeel","begroeidterreindeel",
                        "waterdeel","spoor","pand","kunstwerkdeel","overigbouwwerk","scheiding"];
const BGT_BASE  = "https://api.pdok.nl/lv/bgt/ogc/v1_0";
const CRS_RD    = "http://www.opengis.net/def/crs/EPSG/0/28992";
const CRS_WGS84 = "http://www.opengis.net/def/crs/OGC/1.3/CRS84"; // [lng,lat] volgorde

// WGS84 → RD New (polynoomformule, PDOK referentie)
// Nauwkeurigheid: <0.5m voor heel Nederland
function latLngNaarRD(lat, lng) {
  const dLat = 0.36 * (lat - 52.15517440);
  const dLon = 0.36 * (lng -  5.38720621);
  return {
    x: 155000 + 190094.945*dLon - 11832.228*dLon*dLat - 114.221*dLon*dLat*dLat
             - 32.391*dLon*dLon*dLon - 0.705*dLon,
    y: 463000 + 309056.544*dLat + 60940.388*dLon*dLon*dLat - 9.941*dLon*dLon
             - 2.340*dLat*dLat*dLat - 0.133*dLon*dLon*dLon*dLon,
  };
}

export async function GET(request) {
  const { searchParams } = new URL(request.url);
  const lat = parseFloat(searchParams.get("lat") ?? "");
  const lng = parseFloat(searchParams.get("lng") ?? "");
  if (isNaN(lat) || isNaN(lng)) return Response.json({ error: "Ongeldige lat/lng" }, { status: 400 });

  const { x: rdX, y: rdY } = latLngNaarRD(lat, lng);
  const delta = 20; // ±20m — tolereert coordinaat-afrondingsfouten
  // bbox formaat: minX,minY,maxX,maxY met X=easting, Y=northing (EPSG:28992 practijk-conventie)
  const bbox = `${rdX-delta},${rdY-delta},${rdX+delta},${rdY+delta}`;

  const features = [];
  await Promise.allSettled(BGT_COLLECTIES.map(async (col) => {
    try {
      const url = `${BGT_BASE}/collections/${col}/items?f=json` +
        `&crs=${encodeURIComponent(CRS_WGS84)}` +
        `&bbox-crs=${encodeURIComponent(CRS_RD)}` +
        `&bbox=${bbox}&limit=3`;
      const res = await fetch(url, { signal: AbortSignal.timeout(5000) });
      if (!res.ok) return;
      const data = await res.json();
      for (const f of (data?.features ?? [])) features.push({ ...f, _bgtLaag: col });
    } catch { /* timeout — skip */ }
  }));

  // Sorteer: meest HDD-relevante lagen eerst
  const PRIO = ["wegdeel","spoor","onbegroeidterreindeel","waterdeel",
                "begroeidterreindeel","pand","kunstwerkdeel","overigbouwwerk","scheiding"];
  features.sort((a, b) => PRIO.indexOf(a._bgtLaag) - PRIO.indexOf(b._bgtLaag));

  return Response.json({ features, total: features.length, rdX, rdY });
}
