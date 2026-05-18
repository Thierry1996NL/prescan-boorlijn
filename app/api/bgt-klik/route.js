// app/api/bgt-klik/route.js
// FIX: PDOK BGT retourneert EPSG:28992 in [Northing, Easting] (Y,X) volgorde.
// In Nederland geldt altijd Easting (0–300,000m) < Northing (289,000–629,000m).
// Oplossing: min(x,y) = Easting, max(x,y) = Northing — werkt voor beide assenvolgordes.

const BGT_COLLECTIES = ["wegdeel","onbegroeidterreindeel","begroeidterreindeel",
                        "waterdeel","spoor","pand","kunstwerkdeel","overigbouwwerk","scheiding"];
const BGT_BASE = "https://api.pdok.nl/lv/bgt/ogc/v1_0";
const CRS_RD   = "http://www.opengis.net/def/crs/EPSG/0/28992";

function latLngNaarRD(lat, lng) {
  const dLat=0.36*(lat-52.15517440), dLon=0.36*(lng-5.38720621);
  return {
    x: 155000+190094.945*dLon-11832.228*dLon*dLat-114.221*dLon*dLat*dLat-32.391*dLon*dLon*dLon,
    y: 463000+309056.544*dLat+60940.388*dLon*dLon*dLat-9.941*dLon*dLon-2.340*dLat*dLat*dLat,
  };
}

// RD New [easting, northing] → WGS84 [longitude, latitude] (GeoJSON-standaard)
function rdNaarLngLat(easting, northing) {
  const dX=(easting-155000)/100000, dY=(northing-463000)/100000;
  const lat=52.15517440+(3235.65389*dY-32.58297*dX*dX-0.24750*dY*dY
    -0.84978*dX*dX*dY-0.06550*dY*dY*dY-0.01709*dX*dX*dY*dY-0.00738*dX)/3600;
  const lng=5.38720621+(5260.52916*dX+105.94684*dX*dY+2.45656*dX*dY*dY
    -0.81885*dX*dX*dX+0.05594*dX*dY*dY*dY-0.05607*dX*dX*dX*dY
    +0.01199*dY-0.00256*dX*dX*dX*dY*dY+0.00128*dX*dY*dY*dY*dY)/3600;
  return [lng, lat];
}

function getFirstCoord(c) { while(Array.isArray(c[0])) c=c[0]; return c; }

// Detecteer CRS en converteer naar GeoJSON [lng, lat]
function normaliseerGeometrie(geom) {
  if(!geom?.coordinates) return geom;
  const [x, y] = getFirstCoord(geom.coordinates);

  // EPSG:28992: coördinaten zijn in meters (x of y > 1000)
  const isRd = Math.abs(x) > 1000 || Math.abs(y) > 1000;
  // WGS84 lat-eerst: x≈52, y≈6 (lat>40 en lng<20)
  const isLatLng = !isRd && x > 40 && y < 20;

  function convertPunt(c) {
    if (isRd) {
      // Nederland: Easting (0–300,000) ALTIJD kleiner dan Northing (289,000–629,000)
      // min(x,y) = Easting, max(x,y) = Northing — ongeacht Y,X of X,Y asvolgorde
      const rdEasting  = Math.min(c[0], c[1]);
      const rdNorthing = Math.max(c[0], c[1]);
      return rdNaarLngLat(rdEasting, rdNorthing);
    }
    if (isLatLng) return [c[1], c[0]]; // swap lat,lng → lng,lat
    return [c[0], c[1]];               // al correct [lng,lat]
  }

  function convertRing(ring) { return ring.map(convertPunt); }
  const t = geom.type;
  if(t==='Point')            return {...geom, coordinates: convertPunt(geom.coordinates)};
  if(t==='LineString'||t==='MultiPoint')
                             return {...geom, coordinates: geom.coordinates.map(convertPunt)};
  if(t==='Polygon'||t==='MultiLineString')
                             return {...geom, coordinates: geom.coordinates.map(convertRing)};
  if(t==='MultiPolygon')     return {...geom, coordinates: geom.coordinates.map(p=>p.map(convertRing))};
  return geom;
}

export async function GET(request) {
  const {searchParams} = new URL(request.url);
  const lat = parseFloat(searchParams.get("lat")??"");
  const lng = parseFloat(searchParams.get("lng")??"");
  if(isNaN(lat)||isNaN(lng)) return Response.json({error:"Ongeldige lat/lng"},{status:400});

  const {x:rdX, y:rdY} = latLngNaarRD(lat, lng);
  const delta = 20;
  // bbox: minX,minY,maxX,maxY — PDOK gebruikt praktijk X,Y (Easting, Northing) voor bbox
  const bbox = `${rdX-delta},${rdY-delta},${rdX+delta},${rdY+delta}`;

  const features = [];
  await Promise.allSettled(BGT_COLLECTIES.map(async (col) => {
    try {
      const url = `${BGT_BASE}/collections/${col}/items?f=json`
        + `&bbox-crs=${encodeURIComponent(CRS_RD)}&bbox=${bbox}&limit=3`;
      const res = await fetch(url, {signal:AbortSignal.timeout(5000)});
      if(!res.ok) return;
      const data = await res.json();
      for(const f of (data?.features??[])) {
        features.push({...f, geometry: normaliseerGeometrie(f.geometry), _bgtLaag: col});
      }
    } catch {}
  }));

  const PRIO=["wegdeel","spoor","onbegroeidterreindeel","waterdeel",
              "begroeidterreindeel","pand","kunstwerkdeel","overigbouwwerk","scheiding"];
  features.sort((a,b)=>PRIO.indexOf(a._bgtLaag)-PRIO.indexOf(b._bgtLaag));
  return Response.json({features, total:features.length, rdX, rdY});
}
