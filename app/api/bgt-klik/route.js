// app/api/bgt-klik/route.js
const BGT_COLLECTIES = ["wegdeel","onbegroeidterreindeel","begroeidterreindeel","waterdeel","spoor","pand","kunstwerkdeel","overigbouwwerk","scheiding"];
const BGT_BASE  = "https://api.pdok.nl/lv/bgt/ogc/v1_0";
const CRS_RD    = "http://www.opengis.net/def/crs/EPSG/0/28992";
const CRS_WGS84 = "http://www.opengis.net/def/crs/OGC/1.3/CRS84";
function latLngNaarRD(lat,lng){const dLat=0.36*(lat-52.15517440),dLon=0.36*(lng-5.38720621);return{x:155000+190094.945*dLon-11832.228*dLon*dLat-114.221*dLon*dLat*dLat-32.391*dLon*dLon*dLon-0.705*dLon,y:463000+309056.544*dLat+60940.388*dLon*dLon*dLat-9.941*dLon*dLon-2.340*dLat*dLat*dLat-0.133*dLon*dLon*dLon*dLon};}
export async function GET(request){
  const{searchParams}=new URL(request.url);
  const lat=parseFloat(searchParams.get("lat")??""),lng=parseFloat(searchParams.get("lng")??"");
  if(isNaN(lat)||isNaN(lng))return Response.json({error:"Ongeldige lat/lng"},{status:400});
  const{x:rdX,y:rdY}=latLngNaarRD(lat,lng);
  const delta=4,bbox=`${rdX-delta},${rdY-delta},${rdX+delta},${rdY+delta}`;
  const features=[];
  await Promise.allSettled(BGT_COLLECTIES.map(async(col)=>{
    try{
      const url=`${BGT_BASE}/collections/${col}/items?f=json&crs=${encodeURIComponent(CRS_WGS84)}&bbox-crs=${encodeURIComponent(CRS_RD)}&bbox=${bbox}&limit=3`;
      const res=await fetch(url,{signal:AbortSignal.timeout(5000)});
      if(!res.ok)return;
      const data=await res.json();
      for(const f of(data?.features??[]))features.push({...f,_bgtLaag:col});
    }catch{}
  }));
  const PRIO=["wegdeel","spoor","onbegroeidterreindeel","waterdeel","begroeidterreindeel","pand","kunstwerkdeel","overigbouwwerk","scheiding"];
  features.sort((a,b)=>PRIO.indexOf(a._bgtLaag)-PRIO.indexOf(b._bgtLaag));
  return Response.json({features,total:features.length,rdX,rdY});
}
