// app/api/bag3d-wfs/route.js
// Proxy voor 3D BAG WFS (GeoJSON met gebouwpolygonen + AHN4-hoogtes)

export async function GET(request) {
  const { searchParams } = new URL(request.url);
  const bbox  = searchParams.get("bbox")  ?? "";
  const limit = searchParams.get("limit") ?? "500";

  const upstream = `https://api.3dbag.nl/collections/pand/items?bbox=${encodeURIComponent(bbox)}&f=json&limit=${limit}&crs=http%3A%2F%2Fwww.opengis.net%2Fdef%2Fcrs%2FOGC%2F1.3%2FCRS84`;

  try {
    const res = await fetch(upstream, {
      headers: { "Accept": "application/geo+json, application/json, */*" },
      next: { revalidate: 3600 },
    });

    if (!res.ok) {
      return new Response(`3D BAG WFS ${res.status}`, { status: res.status });
    }

    const data = await res.json();
    return Response.json(data, {
      headers: {
        "Access-Control-Allow-Origin": "*",
        "Cache-Control": "public, max-age=3600",
      },
    });
  } catch (e) {
    return new Response(`Proxy fout: ${e.message}`, { status: 502 });
  }
}
