// app/api/bgt/route.js
// Server-side BGT proxy — RD New + grote buffer voor betrouwbare resultaten

// Iteratieve WGS84 → RD New (gecalibreerd op Nederlandse coördinaten)
function wgs84NaarRd(lat, lng) {
  // Startschatting via lineaire benadering
  let x = 155000 + (lng - 5.38720621) * 67000;
  let y = 463000 + (lat - 52.15517440) * 111300;

  // Verfijn via 5 iteraties met de precieze RD→WGS84 formule
  for (let i = 0; i < 5; i++) {
    const dX = (x - 155000) / 100000;
    const dY = (y - 463000) / 100000;
    const sumN = 3235.65389*dY - 32.58297*dX*dX - 0.24750*dY*dY - 0.84978*dX*dX*dY - 0.06550*dY*dY*dY;
    const sumE = 5260.52916*dX + 105.94684*dX*dY + 2.45656*dX*dY*dY - 0.81885*dX*dX*dX;
    const latCur = 52.15517440 + sumN / 3600;
    const lngCur = 5.38720621  + sumE / 3600;
    x += (lng - lngCur) * 67000;
    y += (lat - latCur) * 111300;
  }
  return { x: Math.round(x), y: Math.round(y) };
}

export async function GET(request) {
  const { searchParams } = new URL(request.url);
  const lat = parseFloat(searchParams.get("lat"));
  const lng = parseFloat(searchParams.get("lng"));

  if (isNaN(lat) || isNaN(lng)) {
    return Response.json({ error: "Ongeldige coördinaten", features: [] }, { status: 400 });
  }

  const rd  = wgs84NaarRd(lat, lng);
  const buf = 300; // 300m buffer — ruim genoeg voor smalle wegen en sloten
  const bbox = `${rd.x - buf},${rd.y - buf},${rd.x + buf},${rd.y + buf}`;

  const lagen = "bgt:wegdeel,bgt:onbegroeidterreindeel,bgt:begroeidterreindeel,bgt:waterdeel,bgt:spoor";
  const url = `https://service.pdok.nl/lv/bgt/wfs/v1_0?service=WFS&version=2.0.0&request=GetFeature`
    + `&typeName=${lagen}`
    + `&outputFormat=application/json`
    + `&bbox=${bbox},EPSG:28992`
    + `&srsName=EPSG:28992`
    + `&count=10`;

  let rawText = "";
  try {
    const res = await fetch(url, {
      headers: { "User-Agent": "PrescanAI/1.0 Vercel" },
      cache: "no-store",
    });

    rawText = await res.text();

    if (!res.ok) {
      return Response.json({
        features: [], error: `HTTP ${res.status}`,
        _debug: { rdX: rd.x, rdY: rd.y, bbox, url, rawStart: rawText.substring(0, 300) }
      });
    }

    let data;
    try { data = JSON.parse(rawText); }
    catch {
      return Response.json({
        features: [], error: "JSON parse fout",
        _debug: { rdX: rd.x, rdY: rd.y, bbox, rawStart: rawText.substring(0, 500) }
      });
    }

    return Response.json({
      ...data,
      _debug: { rdX: rd.x, rdY: rd.y, bbox, count: data.features?.length ?? 0 }
    });

  } catch (err) {
    return Response.json({
      features: [], error: err.message,
      _debug: { rdX: rd.x, rdY: rd.y, bbox, url }
    });
  }
}
