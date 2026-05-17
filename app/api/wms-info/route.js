// app/api/wms-info/route.js
// Proxy voor WMS GetFeatureInfo requests (CORS-bypass)
// Alleen PDOK / BRO domeinen toegestaan

const TOEGESTANE_HOSTS = [
  "service.pdok.nl",
  "geodata.nationaalgeoregister.nl",
];

export async function GET(request) {
  const { searchParams } = new URL(request.url);
  const targetUrl = searchParams.get("url");

  if (!targetUrl) {
    return Response.json({ error: "Missing url parameter" }, { status: 400 });
  }

  // Veiligheidscheck: alleen PDOK/BRO domeinen
  let parsed;
  try {
    parsed = new URL(targetUrl);
  } catch {
    return Response.json({ error: "Invalid url" }, { status: 400 });
  }

  if (!TOEGESTANE_HOSTS.includes(parsed.hostname)) {
    return Response.json(
      { error: `Domain ${parsed.hostname} not allowed` },
      { status: 403 }
    );
  }

  try {
    const res = await fetch(targetUrl, {
      headers: { Accept: "application/json, text/xml" },
    });

    const contentType = res.headers.get("content-type") ?? "";

    if (contentType.includes("json")) {
      const data = await res.json();
      return Response.json(data);
    } else {
      // XML fallback (sommige WMS services retourneren GML/XML)
      const text = await res.text();
      return new Response(text, {
        headers: { "Content-Type": contentType },
      });
    }
  } catch (err) {
    return Response.json(
      { error: `Upstream fetch failed: ${err.message}` },
      { status: 502 }
    );
  }
}
