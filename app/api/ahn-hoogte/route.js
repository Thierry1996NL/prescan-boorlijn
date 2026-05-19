// app/api/ahn-hoogte/route.js
// Haalt AHN4 maaiveldhoogte (DTM 0.5m) op voor een lijst RD New coördinaten
// via WMS GetFeatureInfo — server-side (geen CORS probleem)

const AHN_WMS = "https://service.pdok.nl/rws/actueel-hoogtebestand-nederland/wms/v1_0";
const DELTA = 3; // half-breedte van de query-bbox in meters (6x6m vak)

async function haalEenHoogte(rdX, rdY) {
  // WMS GetFeatureInfo: vraag de rasterwaarde op in een klein vakje rondom het punt
  const bbox = `${rdX-DELTA},${rdY-DELTA},${rdX+DELTA},${rdY+DELTA}`;
  const url = AHN_WMS +
    "?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetFeatureInfo" +
    "&LAYERS=dtm_05m&QUERY_LAYERS=dtm_05m" +
    "&INFO_FORMAT=application/json" +
    `&I=50&J=50&WIDTH=101&HEIGHT=101` +
    `&BBOX=${bbox}&CRS=EPSG:28992` +
    "&FEATURE_COUNT=1";

  const res = await fetch(url, { signal: AbortSignal.timeout(6000) });
  if (!res.ok) return null;

  const contentType = res.headers.get("content-type") ?? "";

  // Probeer JSON te parsen
  if (contentType.includes("json")) {
    const data = await res.json();
    // GeoJSON FeatureCollection of Feature
    const features = data.features ?? (data.type === "Feature" ? [data] : []);
    for (const f of features) {
      const props = f.properties ?? {};
      // AHN WMS geeft de hoogte terug als "GRAY_INDEX", "value", "dtm_05m" of numerieke property
      const kandidaten = [
        props.GRAY_INDEX, props.value, props.dtm_05m, props.hoogte,
        ...Object.values(props).filter(v => typeof v === "number"),
      ];
      for (const v of kandidaten) {
        const n = parseFloat(v);
        if (!isNaN(n) && n > -30 && n < 350) return +n.toFixed(3); // geldig NL hoogte-bereik
      }
    }
    return null;
  }

  // Fallback: XML/GML parsen (AHN geeft soms GML terug)
  const text = await res.text();
  // Zoek naar een getal achter "GRAY_INDEX" of "value" in de XML
  const matchers = [
    /<GRAY_INDEX>([\-\d.]+)<\/GRAY_INDEX>/,
    /<value>([\-\d.]+)<\/value>/,
    />([\-]?\d+\.\d+)<\/[^>]+>/,
  ];
  for (const re of matchers) {
    const m = text.match(re);
    if (m) {
      const n = parseFloat(m[1]);
      if (!isNaN(n) && n > -30 && n < 350) return +n.toFixed(3);
    }
  }
  return null;
}

export async function POST(request) {
  let body;
  try { body = await request.json(); } catch {
    return Response.json({ error: "Ongeldige JSON body" }, { status: 400 });
  }

  const punten = body?.punten;
  if (!Array.isArray(punten) || punten.length === 0) {
    return Response.json({ error: "Geen punten meegegeven" }, { status: 400 });
  }

  // Max 200 punten per request om rate-limits te vermijden
  const slice = punten.slice(0, 200);

  // Haal hoogtes op — max 10 tegelijk om de WMS niet te overbelasten
  const BATCH = 10;
  const hoogtes = new Array(slice.length).fill(null);

  for (let i = 0; i < slice.length; i += BATCH) {
    const batch = slice.slice(i, i + BATCH);
    const results = await Promise.allSettled(
      batch.map(p => haalEenHoogte(p.x, p.y))
    );
    results.forEach((r, j) => {
      hoogtes[i + j] = r.status === "fulfilled" ? r.value : null;
    });
  }

  return Response.json({ hoogtes, n: hoogtes.length, geldig: hoogtes.filter(h => h !== null).length });
}
