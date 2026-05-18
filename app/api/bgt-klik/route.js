// app/api/bgt-klik/route.js
// Auto-detecteert CRS van de response (EPSG:28992 meters OF WGS84 [lng,lat] OF WGS84 [lat,lng])
// en converteert altijd naar GeoJSON-standaard [longitude, latitude] voor L.geoJSON

const BGT_COLLECTIES = ["wegdeel","onbegroeidterreindeel","begroeidterreindeel",
                        "waterdeel","spoor","pand","kunstwerkdeel","overigbouwwerk","scheiding"];
const BGT_BASE = "https://api.pdok.nl/lv/bgt/ogc/v1_0";
const CRS_RD   = "http://www.opengis.net/def/crs/EPSG/0/28992";

// WGS84 → RD New
function latLngNaarRD(lat, lng) {
  const dLat = 0.36*(lat-52.15517440), dLon = 0.36*(lng-5.38720621);
  return {
    x: 155000 + 190094.945*dLon - 11832.228*dLon*dLat - 114.221*dLon*dLat*dLat - 32.391*dLon*dLon*dLon,
    y: 463000 + 309056.544*dLat + 60940.388*dLon*dLon*dLat - 9.941*dLon*dLon - 2.340*dLat*dLat*dLat,
  };
}

// RD New → WGS84 [lng, lat] (GeoJSON-standaard)
function rdNaarLngLat(x, y) {
  const dX=(x-155000)/100000, dY=(y-463000)/100000;
  const lat = 52.15517440+(3235.65389*dY-32.58297*dX*dX-0.24750*dY*dY
    -0.84978*dX*dX*dY-0.06550*dY*dY*dY-0.01709*dX*dX*dY*dY-0.00738*dX)/3600;
  const lng = 5.38720621+(5260.52916*dX+105.94684*dX*dY+2.45656*dX*dY*dY
    -0.81885*dX*dX*dX+0.05594*dX*dY*dY*dY-0.05607*dX*dX*dX*dY
    +0.01199*dY-0.00256*dX*dX*dX*dY*dY+0.00128*dX*dY*dY*dY*dY)/3600;
  return [lng, lat];
}

// Detecteer CRS en converteer geometrie naar [lng,lat] GeoJSON
function getFirstCoord(c) { while(Array.isArray(c[0]))c=c[0]; return c; }

function normaliseerGeometrie(geom) {
  if(!geom?.coordinates) return geom;
  const [x,y] = getFirstCoord(geom.coordinates);

  // Detectie op basis van coördinaat-grootte (NL):
  //   EPSG:28992 meters:   x ≈ 0–300.000  y ≈ 300.000–650.000  → x>1000
  //   WGS84 [lng,lat]:     x ≈ 3–8°       y ≈ 50–54°            → x<20
  //   WGS84 [lat,lng]:     x ≈ 50–54°     y ≈ 3–8°              → x>40 && y<20
  const isRd      = Math.abs(x) > 1000 || Math.abs(y) > 1000;
  const isLatLng  = x > 40 && y < 20;   // volgorde omgedraaid

  function convertPunt(c) {
    if(isRd)     return rdNaarLngLat(c[0], c[1]);        // RD → [lng,lat]
    if(isLatLng) return [c[1], c[0]];                    // swap lat,lng → lng,lat
    return [c[0], c[1]];                                  // al correct [lng,lat]
  }
  function convertRing(ring) { return ring.map(convertPunt); }

  const t = geom.type;
  if(t==='Point')           return {...geom, coordinates: convertPunt(geom.coordinates)};
  if(t==='LineString'||t==='MultiPoint')
                            return {...geom, coordinates: geom.coordinates.map(convertPunt)};
  if(t==='Polygon'||t==='MultiLineString')
                            return {...geom, coordinates: geom.coordinates.map(convertRing)};
  if(t==='MultiPolygon')    return {...geom, coordinates: geom.coordinates.map(p=>p.map(convertRing))};
  return geom;
}

export async function GET(request) {
  const {searchParams} = new URL(request.url);
  const lat = parseFloat(searchParams.get("lat")??"");
  const lng = parseFloat(searchParams.get("lng")??"");
  if(isNaN(lat)||isNaN(lng)) return Response.json({error:"Ongeldige lat/lng"},{status:400});

  const {x:rdX, y:rdY} = latLngNaarRD(lat, lng);
  const delta = 20;
  // bbox: minX,minY,maxX,maxY in EPSG:28992 (X=easting, Y=northing — PDOK praktijk-conventie)
  const bbox = `${rdX-delta},${rdY-delta},${rdX+delta},${rdY+delta}`;

  const features = [];
  await Promise.allSettled(BGT_COLLECTIES.map(async(col) => {
    try {
      // Geen crs= parameter → server-default → auto-detecteer in normaliseerGeometrie()
      const url = `${BGT_BASE}/collections/${col}/items?f=json`
        + `&bbox-crs=${encodeURIComponent(CRS_RD)}&bbox=${bbox}&limit=3`;
      const res = await fetch(url, {signal:AbortSignal.timeout(5000)});
      if(!res.ok) return;
      const data = await res.json();
      for(const f of (data?.features??[])){
        // Normaliseer geometrie naar [lng,lat] voor L.geoJSON
        features.push({...f, geometry: normaliseerGeometrie(f.geometry), _bgtLaag:col});
      }
    } catch{}
  }));

  const PRIO=["wegdeel","spoor","onbegroeidterreindeel","waterdeel",
               "begroeidterreindeel","pand","kunstwerkdeel","overigbouwwerk","scheiding"];
  features.sort((a,b)=>PRIO.indexOf(a._bgtLaag)-PRIO.indexOf(b._bgtLaag));
  return Response.json({features, total:features.length, rdX, rdY});
}
