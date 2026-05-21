// app/api/bag3d/[...path]/route.js
// Proxy voor 3D BAG API (CORS-bypass)

export async function GET(request, { params }) {
  const path = (await params).path?.join("/") ?? "";
  const { searchParams } = new URL(request.url);
  const query = searchParams.toString();
  const upstream = `https://api.3dbag.nl/collections/pand/3dtiles/${path}${query ? "?" + query : ""}`;

  try {
    const res = await fetch(upstream, {
      headers: {
        "User-Agent": "PrescanBoorlijn/1.0",
        "Accept": "application/json, application/octet-stream, */*",
      },
      next: { revalidate: 3600 }, // cache 1 uur
    });

    if (!res.ok) {
      return new Response(`3D BAG upstream error: ${res.status}`, { status: res.status });
    }

    const contentType = res.headers.get("content-type") ?? "application/octet-stream";
    const body = await res.arrayBuffer();

    return new Response(body, {
      status: 200,
      headers: {
        "Content-Type": contentType,
        "Access-Control-Allow-Origin": "*",
        "Cache-Control": "public, max-age=3600",
      },
    });
  } catch (e) {
    return new Response(`Proxy fout: ${e.message}`, { status: 502 });
  }
}
