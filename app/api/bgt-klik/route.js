// app/api/bgt-klik/route.js
const BGT_COLLECTIES=["wegdeel","onbegroeidterreindeel","begroeidterreindeel","waterdeel","spoor","pand","kunstwerkdeel","overigbouwwerk","scheiding"];
const BGT_BASE="https://api.pdok.nl/lv/bgt/ogc/v1_0";
const CRS_RD="http://www.opengis.net/def/crs/EPSG/0/28992";
const CRS_WGS84="http://www.opengis.net/def/crs/OGC/1.3/CRS84";
function latLngNaarRD(lat,lng){const dLat=0.36*(lat-52.15517440),dLon=0.36*(lng-5.38720621);return{x:155000+190094.945*dLon-11832.228*dLon*dLat-114.221*dLon*dLat*dLat-32.391*dLon*dLon*dLon,y:463000+309056.544*dLat+60940.388*dLon*dLon*dLat-9.941*dLon*dLon-2.340*dLat*dLat*dLat};}
function rdNaarLngLat(e,n){const dX=(e-155000)/100000,dY=(n-463000)/100000;const lat=52.15517440+(3235.65389*dY-32.58297*dX*dX-0.24750*dY*dY-0.84978*dX*dX*dY-0.06550*dY*dY*dY-0.01709*dX*dX*dY*dY-0.00738*dX)/3600;const lng=5.38720621+(5260.52916*dX+105.94684*dX*dY+2.45656*dX*dY*dY-0.81885*dX*dX*dX+0.05594*dX*dY*dY*dY-0.05607*dX*dX*dX*dY+0.01199*dY-0.00256*dX*dX*dX*dY*dY+0.00128*dX*dY*dY*dY*dY)/3600;return[lng,lat];}
function getFirst(c){while(Array.isArray(c[0]))c=c[0];return c;}
function normaliseer(geom){
  if(!geom?.coordinates)return geom;
  const[x,y]=getFirst(geom.coordinates);
  const isRd=Math.abs(x)>1000||Math.abs(y)>1000;
  const isLatLng=!isRd&&x>40&&y<20;
  function pt(c){if(isRd)return rdNaarLngLat(Math.min(c[0],c[1]),Math.max(c[0],c[1]));if(isLatLng)return[c[1],c[0]];return[c[0],c[1]];}
  function ring(r){return r.map(pt);}
  const t=geom.type;
  if(t==='Point')return{...geom,coordinates:pt(geom.coordinates)};
  if(t==='LineString'||t==='MultiPoint')return{...geom,coordinates:geom.coordinates.map(pt)};
  if(t==='Polygon'||t==='MultiLineString')return{...geom,coordinates:geom.coordinates.map(ring)};
  if(t==='MultiPolygon')return{...geom,coordinates:geom.coordinates.map(p=>p.map(ring))};
  return geom;
}
export async function GET(request){
  const{searchParams}=new URL(request.url);
  const lat=parseFloat(searchParams.get("lat")??""),lng=parseFloat(searchParams.get("lng")??"");
  if(isNaN(lat)||isNaN(lng))return Response.json({error:"Ongeldige lat/lng"},{status:400});
  const{x:rdX,y:rdY}=latLngNaarRD(lat,lng);
  const delta=20,bbox=`${rdX-delta},${rdY-delta},${rdX+delta},${rdY+delta}`;
  const features=[],debug={};
  await Promise.allSettled(BGT_COLLECTIES.map(async(col)=>{
    try{
      const url=`${BGT_BASE}/collections/${col}/items?f=json&crs=${encodeURIComponent(CRS_WGS84)}&bbox-crs=${encodeURIComponent(CRS_RD)}&bbox=${bbox}&limit=3`;
      const res=await fetch(url,{signal:AbortSignal.timeout(5000)});
      if(!res.ok)return;
      debug.contentCrs=res.headers.get("content-crs")||debug.contentCrs||"?";
      const data=await res.json();
      for(const f of(data?.features??[])){
        if(!debug.rawFirstCoord&&f.geometry?.coordinates){const fc=getFirst(f.geometry.coordinates);debug.rawFirstCoord=fc.slice(0,2);debug.col=col;}
        features.push({...f,geometry:normaliseer(f.geometry),_bgtLaag:col});
      }
    }catch{}
  }));
  const PRIO=["wegdeel","spoor","onbegroeidterreindeel","waterdeel","begroeidterreindeel","pand","kunstwerkdeel","overigbouwwerk","scheiding"];
  features.sort((a,b)=>PRIO.indexOf(a._bgtLaag)-PRIO.indexOf(b._bgtLaag));
  return Response.json({features,total:features.length,rdX:Math.round(rdX),rdY:Math.round(rdY),debug});
}
