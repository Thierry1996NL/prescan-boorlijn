// app/api/bag3d/[...path]/route.js
// Proxy voor 3D BAG → Cesium 3D Tiles
// - /api/bag3d/tileset.json   → https://api.3dbag.nl/collections/pand/3dtiles  (root tileset)
// - /api/bag3d/subtiles/...   → https://api.3dbag.nl/collections/pand/3dtiles/subtiles/...

const BAG_BASE = "https://api.3dbag.nl/collections/pand/3dtiles";

export async function GET(request, { params }) {
  const pathParts = (await params).path ?? [];
  const pathStr   = pathParts.join("/");

  // "tileset.json" of leeg pad → root tileset ophalen
  const upstream = (pathStr === "tileset.json" || pathStr === "")
    ? BAG_BASE
    : `${BAG_BASE}/${pathStr}`;

  // Querystring doorsturen
  const qs = new URL(request.url).searchParams.toString();
  const url = qs ? `${upstream}?${qs}` : upstream;

  try {
    const res = await fetch(url, {
      headers: {
        "Accept": "application/json, application/octet-stream, */*",
        "User-Agent": "PrescanBoorlijn/1.0",
      },
      next: { revalidate: 3600 },
    });

    if (!res.ok) {
      return new Response(`3D BAG upstream ${res.status}: ${url}`, { status: res.status });
    }

    const ct = res.headers.get("content-type") ?? "application/octet-stream";

    // JSON: herschrijf absolute 3D BAG URLs → proxy-paden
    if (ct.includes("json")) {
      let text = await res.text();
      text = text.replaceAll(
        "https://api.3dbag.nl/collections/pand/3dtiles/",
        "/api/bag3d/"
      );
      return new Response(text, {
        headers: {
          "Content-Type": "application/json",
          "Access-Control-Allow-Origin": "*",
          "Cache-Control": "public, max-age=3600",
        },
      });
    }

    // Binair (glb, b3dm, …): direct doorsturen
    const body = await res.arrayBuffer();
    return new Response(body, {
      headers: {
        "Content-Type": ct,
        "Access-Control-Allow-Origin": "*",
        "Cache-Control": "public, max-age=3600",
      },
    });
  } catch (e) {
    return new Response(`Proxy fout: ${e.message}`, { status: 502 });
  }
}
