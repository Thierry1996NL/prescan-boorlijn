// app/api/ahn-hoogte/route.js
// AHN4 DTM 0.5m via WMS GetFeatureInfo — text/plain parsing (meest betrouwbaar)

const AHN_WMS = "https://service.pdok.nl/rws/actueel-hoogtebestand-nederland/wms/v1_0";

async function haalEenHoogte(rdX, rdY) {
  // Groter bbox (40x40m) voor betere kans op raster-hit
  const D = 20;
  const bbox = `${rdX-D},${rdY-D},${rdX+D},${rdY+D}`;

  // Probeer meerdere INFO_FORMAT opties
  const formats = ["text/plain", "application/json", "text/xml"];

  for (const fmt of formats) {
    try {
      const url = AHN_WMS +
        `?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetFeatureInfo` +
        `&LAYERS=dtm_05m&QUERY_LAYERS=dtm_05m` +
        `&INFO_FORMAT=${encodeURIComponent(fmt)}` +
        `&I=50&J=50&WIDTH=101&HEIGHT=101` +
        `&BBOX=${bbox}&CRS=EPSG:28992&FEATURE_COUNT=1`;

      const res = await fetch(url, { signal: AbortSignal.timeout(8000) });
      if (!res.ok) continue;

      const ct = res.headers.get("content-type") ?? "";
      const body = await res.text();

      let hoogte = null;

      if (ct.includes("json")) {
        // JSON: zoek GRAY_INDEX of numerieke property
        try {
          const data = JSON.parse(body);
          const features = data.features ?? (data.type==="Feature" ? [data] : []);
          for (const f of features) {
            const props = f.properties ?? {};
            const vals = [props.GRAY_INDEX, props.value, props.dtm_05m,
                         ...Object.values(props).filter(v => typeof v === "number")];
            for (const v of vals) {
              const n = parseFloat(v);
              if (!isNaN(n) && n > -30 && n < 350) { hoogte = n; break; }
            }
            if (hoogte !== null) break;
          }
        } catch {}
      }

      if (hoogte === null) {
        // text/plain of XML: regex naar elk getal in NL hoogtebereik
        const patterns = [
          /GRAY_INDEX[^\d\-]*([\-]?\d+\.?\d*)/i,
          /value[^\d\-]*([\-]?\d+\.?\d*)/i,
          /hoogte[^\d\-]*([\-]?\d+\.?\d*)/i,
          // Laatste fallback: elk decimaal getal dat op een hoogte lijkt
          /([\-]?\d{1,3}\.\d{1,4})/g,
        ];

        for (const re of patterns) {
          if (re.global) {
            // Voor de globale regex: zoek het eerste getal in NL hoogtebereik
            let m;
            re.lastIndex = 0;
            while ((m = re.exec(body)) !== null) {
              const n = parseFloat(m[1]);
              if (!isNaN(n) && n > -30 && n < 350) { hoogte = n; break; }
            }
          } else {
            const m = body.match(re);
            if (m) {
              const n = parseFloat(m[1]);
              if (!isNaN(n) && n > -30 && n < 350) hoogte = n;
            }
          }
          if (hoogte !== null) break;
        }
      }

      if (hoogte !== null) return +hoogte.toFixed(3);

      // Als we een respons hadden maar geen getal: log voor debugging
      if (body && body.length < 2000) console.log("AHN body sample:", body.slice(0, 200));

    } catch (err) {
      console.warn(`AHN fetch fout (${fmt}):`, err.message);
    }
  }

  return null;
}

export async function POST(request) {
  let body;
  try { body = await request.json(); }
  catch { return Response.json({ error: "Ongeldige JSON body" }, { status: 400 }); }

  const punten = body?.punten;
  if (!Array.isArray(punten) || !punten.length)
    return Response.json({ error: "Geen punten" }, { status: 400 });

  const slice = punten.slice(0, 200);
  const hoogtes = new Array(slice.length).fill(null);

  // Max 8 tegelijk (AHN WMS rate-limit voorkomen)
  const BATCH = 8;
  for (let i = 0; i < slice.length; i += BATCH) {
    const batch = slice.slice(i, i + BATCH);
    const results = await Promise.allSettled(batch.map(p => haalEenHoogte(p.x, p.y)));
    results.forEach((r, j) => { hoogtes[i + j] = r.status === "fulfilled" ? r.value : null; });
  }

  const geldig = hoogtes.filter(h => h !== null).length;
  return Response.json({ hoogtes, n: hoogtes.length, geldig });
}
