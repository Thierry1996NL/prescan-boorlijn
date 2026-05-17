// app/api/esri-tile/route.js
// Proxy die EPSG:28992 (RD New) tile-coördinaten omzet naar WebMercator Esri tiles.
// Waarom nodig: Esri tiles zijn EPSG:3857 (WebMercator) — incompatibel met de EPSG:28992 Leaflet kaart.
// Oplossing: voor elke PDOK-tile (z,x,y) → bereken RD center → WGS84 → WebMercator tile → fetch bij Esri.

const ESRI = {
  topo:   "https://server.arcgisonline.com/ArcGIS/rest/services/World_Topo_Map/MapServer/tile/{z}/{y}/{x}",
  hybrid: "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
  labels: "https://server.arcgisonline.com/ArcGIS/rest/services/Reference/World_Boundaries_and_Places/MapServer/tile/{z}/{y}/{x}",
};

// PDOK EPSG:28992 tiling scheme
const ORIGIN_X = -285401.920;  // RD New origin X (m)
const ORIGIN_Y =  903401.920;  // RD New origin Y (m)
const TILE_PX  = 256;
const RESOLUTIONS = [
  3440.640, 1720.320, 860.160, 430.080, 215.040, 107.520,
  53.760, 26.880, 13.440, 6.720, 3.360, 1.680, 0.840, 0.420,
  0.210, 0.105, 0.0525, 0.02625, 0.013125,
];

// Stap 1: PDOK tile (z,x,y) → RD New middelpunt (m)
function rdTileCenter(z, tx, ty) {
  const res = RESOLUTIONS[z] ?? RESOLUTIONS[RESOLUTIONS.length - 1];
  return {
    rdX: ORIGIN_X + (tx + 0.5) * TILE_PX * res,
    rdY: ORIGIN_Y - (ty + 0.5) * TILE_PX * res,
  };
}

// Stap 2: RD New (m) → WGS84 (°) — volledige polynoomformule
function rdToLatLng(rdX, rdY) {
  const dX = (rdX - 155000) / 100000;
  const dY = (rdY - 463000) / 100000;
  const lat =
    52.15517440 +
    (3235.65389 * dY
     - 32.58297 * dX * dX
     -  0.24750 * dY * dY
     -  0.84978 * dX * dX * dY
     -  0.06550 * dY * dY * dY
     -  0.01709 * dX * dX * dY * dY
     -  0.00738 * dX) / 3600;
  const lng =
    5.38720621 +
    (5260.52916 * dX
     + 105.94684 * dX * dY
     +   2.45656 * dX * dY * dY
     -   0.81885 * dX * dX * dX
     +   0.05594 * dX * dY * dY * dY
     -   0.05607 * dX * dX * dX * dY
     +   0.01199 * dY
     -   0.00256 * dX * dX * dX * dY * dY
     +   0.00128 * dX * dY * dY * dY * dY) / 3600;
  return { lat, lng };
}

// Stap 3: WGS84 → WebMercator tile-index (standaard Slippy Map)
function latLngToWMTile(lat, lng, z) {
  const n = Math.pow(2, z);
  const tx = Math.floor(((lng + 180) / 360) * n);
  const latRad = (lat * Math.PI) / 180;
  const ty = Math.floor(
    ((1 - Math.log(Math.tan(latRad) + 1 / Math.cos(latRad)) / Math.PI) / 2) * n
  );
  return { tx, ty };
}

// Zoomniveau-mapping: PDOK resolutie ≈ WebMercator zoom (NL @ lat 52°)
// PDOK z13 (13.44 m/px) ≈ WebMercator z13 — vrijwel identiek, geen aanpassing nodig.
function mapZoom(pdokZ) {
  // Voor NL zijn de zoom levels goed vergelijkbaar (max 1 niveau verschil)
  return Math.min(pdokZ, 18); // Esri max native: 18
}

export async function GET(request) {
  const { searchParams } = new URL(request.url);
  const type = searchParams.get("type") ?? "topo";
  const z    = parseInt(searchParams.get("z")  ?? "13", 10);
  const x    = parseInt(searchParams.get("x")  ?? "0",  10);
  const y    = parseInt(searchParams.get("y")  ?? "0",  10);

  const esriUrl = ESRI[type];
  if (!esriUrl) return new Response("Onbekend type", { status: 400 });

  // Coördinaatconversie
  const { rdX, rdY }  = rdTileCenter(z, x, y);
  const { lat, lng }  = rdToLatLng(rdX, rdY);
  const wmZ           = mapZoom(z);
  const { tx, ty }    = latLngToWMTile(lat, lng, wmZ);

  const url = esriUrl
    .replace("{z}", wmZ)
    .replace("{y}", ty)
    .replace("{x}", tx);

  try {
    const res = await fetch(url, {
      signal: AbortSignal.timeout(8000),
      headers: { "User-Agent": "PrescanBoorlijn/1.0 (+https://prescan.nl)" },
    });

    if (!res.ok) return new Response(`Esri tile ${res.status}`, { status: res.status });

    const buffer = await res.arrayBuffer();
    return new Response(buffer, {
      headers: {
        "Content-Type":  res.headers.get("content-type") ?? "image/jpeg",
        "Cache-Control": "public, max-age=86400, stale-while-revalidate=604800",
        "X-Tile-Coords": `pdok:${z}/${x}/${y} → wm:${wmZ}/${tx}/${ty} (${lat.toFixed(4)},${lng.toFixed(4)})`,
      },
    });
  } catch (err) {
    return new Response(`Tile ophalen mislukt: ${err.message}`, { status: 502 });
  }
}
