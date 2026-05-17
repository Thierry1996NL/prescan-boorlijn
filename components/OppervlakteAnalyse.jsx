"use client";
import { useEffect, useRef, useState } from "react";

// ─── BGT oppervlak typen ─────────────────────────────────────────
const BGT_TYPEN = {
  "gesloten verharding":  { label:"Gesloten verharding",  kleur:"#6b7280", risico:"Hoog"   },
  "open verharding":      { label:"Open verharding",      kleur:"#f59e0b", risico:"Middel" },
  "half verhard":         { label:"Half verhard",         kleur:"#fbbf24", risico:"Middel" },
  "onverhard":            { label:"Onverhard",             kleur:"#d97706", risico:"Middel" },
  "groenvoorziening":     { label:"Groenvoorziening",      kleur:"#16a34a", risico:"Laag"   },
  "gras":                 { label:"Groenvoorziening",      kleur:"#16a34a", risico:"Laag"   },
  "water":                { label:"Water",                  kleur:"#2563eb", risico:"Hoog"   },
  "spoor":                { label:"Spoor",                  kleur:"#dc2626", risico:"Hoog"   },
  "beton":                { label:"Gesloten verharding",  kleur:"#6b7280", risico:"Hoog"   },
  "asfalt":               { label:"Gesloten verharding",  kleur:"#6b7280", risico:"Hoog"   },
};
const OVERIG = { label:"Overig", kleur:"#9ca3af", risico:"?" };
function matchBgt(rawType) {
  if(!rawType) return OVERIG;
  const raw = rawType.toLowerCase();
  for(const [k,v] of Object.entries(BGT_TYPEN)) if(raw.includes(k)) return v;
  if(raw.includes("verhard")) return BGT_TYPEN["gesloten verharding"];
  if(raw.includes("groen")||raw.includes("gras")||raw.includes("berm")) return BGT_TYPEN["groenvoorziening"];
  if(raw.includes("water")||raw.includes("sloot")||raw.includes("vijver")) return BGT_TYPEN["water"];
  return OVERIG;
}

// ─── BGT classificatie op basis van properties (betrouwbaarder dan feature-ID) ──
// Elke BGT-laag heeft unieke properties → detecteer laagtype hierop
// Detecteer BGT laagtype op basis van properties
// Ondersteunt zowel WFS camelCase als OGC API snake_case properties
function detecteerBgtLaag(p) {
  const has = (...keys) => keys.some(k => k in p);
  const get = (...keys) => { for(const k of keys) if(p[k]) return String(p[k]); return ""; };

  if (has("typeWater","type_water")) return "waterdeel";
  if (has("transporttype","transport_type","spoorbreedte")) return "spoor";
  if (has("verhardingstype","verharding_type")) return "wegdeel";
  if (has("fysiekVoorkomen","fysiek_voorkomen")) {
    const fv = get("fysiekVoorkomen","fysiek_voorkomen").toLowerCase();
    if (fv.includes("verhard")||fv.includes("onverhard")||fv.includes("erf")||fv.includes("half"))
      return "onbegroeid";
    return "begroeid";
  }
  return "onbekend";
}

function detecteerViaId(featureId) {
  const id = (featureId || "").toLowerCase();
  // Mogelijke formaten: "wegdeel.uuid", "wegdeelv2.uuid", "bgt_wegdeel.uuid" etc.
  if (/waterdeel/.test(id))            return "waterdeel";
  if (/spoor/.test(id))                return "spoor";
  if (/wegdeel/.test(id))              return "wegdeel";
  if (/onbegroeid/.test(id))           return "onbegroeid";
  if (/begroeid/.test(id))             return "begroeid";
  return "onbekend";
}

function classificeerBgt(feat) {
  const p = feat.properties || {};
  // Eerst op property-aanwezigheid (meest betrouwbaar)
  let laag = detecteerBgtLaag(p);
  // Fallback op feature-ID als property-detectie faalt
  if (laag === "onbekend") laag = detecteerViaId(feat.id);

  switch (laag) {
    case "waterdeel": return BGT_TYPEN["water"];
    case "spoor":     return BGT_TYPEN["spoor"];
    case "begroeid":  return BGT_TYPEN["groenvoorziening"];

    case "wegdeel": {
      // WFS: verhardingstype, OGC: verhardingstype (zelfde gelukkig)
      const v = (p.verhardingstype||p.verharding_type||"").toLowerCase();
      if (v.includes("gesloten"))  return BGT_TYPEN["gesloten verharding"];
      if (v.includes("open"))      return BGT_TYPEN["open verharding"];
      if (v.includes("half"))      return BGT_TYPEN["half verhard"];
      if (v.includes("onverhard")) return BGT_TYPEN["onverhard"];
      return BGT_TYPEN["gesloten verharding"]; // default: asfaltzand
    }

    case "onbegroeid": {
      // WFS: fysiekVoorkomen, OGC: fysiek_voorkomen
      const fv = (p.fysiekVoorkomen||p.fysiek_voorkomen||"").toLowerCase();
      if (fv.includes("gesloten"))  return BGT_TYPEN["gesloten verharding"];
      if (fv.includes("open"))      return BGT_TYPEN["open verharding"];
      if (fv.includes("half"))      return BGT_TYPEN["half verhard"];
      if (fv.includes("onverhard")) return BGT_TYPEN["onverhard"];
      if (fv.includes("erf"))       return BGT_TYPEN["open verharding"];
      return BGT_TYPEN["onverhard"];
    }

    default: {
      // Fallback: match op alle property-waarden
      const allVals = Object.values(p).join(" ").toLowerCase();
      if (allVals.includes("water")||allVals.includes("sloot")||allVals.includes("waterloop")) return BGT_TYPEN["water"];
      if (allVals.includes("gesloten verharding")) return BGT_TYPEN["gesloten verharding"];
      if (allVals.includes("groenvoorziening")||allVals.includes("gras")) return BGT_TYPEN["groenvoorziening"];
      if (allVals.includes("verhard")) return BGT_TYPEN["open verharding"];
      return OVERIG;
    }
  }
}

// Haal BGT-oppervlak op met robuuste classificatie + debug logging
// haalOppervlakOp roept de server-side /api/bgt proxy aan (geen CORS-probleem)
async function haalOppervlakOp(lat, lng) {
  try {
    const res = await fetch(`/api/bgt?lat=${lat}&lng=${lng}`);
    if (!res.ok) return OVERIG;
    const data = await res.json();
    if (!data.features?.length) return OVERIG;

    // Selecteer meest relevante feature
    const PRIO = ["waterdeel","spoor","wegdeel","onbegroeid","begroeid"];
    let beste = null;
    for (const t of PRIO) {
      beste = data.features.find(f => {
        const laag = detecteerBgtLaag(f.properties||{});
        return laag === t || detecteerViaId(f.id) === t;
      });
      if (beste) break;
    }
    if (!beste) beste = data.features[0];
    return classificeerBgt(beste);
  } catch (err) {
    console.warn("BGT proxy fout:", err.message);
    return OVERIG;
  }
}

// ─── Afstand ─────────────────────────────────────────────────────
function afstandM([lat1,lng1],[lat2,lng2]){
  const R=6371000,dLat=(lat2-lat1)*Math.PI/180,dLng=(lng2-lng1)*Math.PI/180;
  const a=Math.sin(dLat/2)**2+Math.cos(lat1*Math.PI/180)*Math.cos(lat2*Math.PI/180)*Math.sin(dLng/2)**2;
  return R*2*Math.atan2(Math.sqrt(a),Math.sqrt(1-a));
}

// ─── Steekproefpunten langs boorlijn ─────────────────────────────
function genereerPunten(coords,stapM=5){
  if(!coords||coords.length<2)return[];
  const punten=[];
  let cumul=0;
  punten.push({lat:coords[0][0],lng:coords[0][1],positieM:0});
  for(let i=0;i<coords.length-1;i++){
    const segLen=afstandM(coords[i],coords[i+1]);
    if(segLen<0.01){cumul+=segLen;continue;}
    let rest=stapM;
    while(rest<segLen){
      const t=rest/segLen;
      punten.push({lat:coords[i][0]+t*(coords[i+1][0]-coords[i][0]),lng:coords[i][1]+t*(coords[i+1][1]-coords[i][1]),positieM:cumul+rest});
      rest+=stapM;
    }
    cumul+=segLen;
  }
  const last=coords[coords.length-1];
  if(punten[punten.length-1].positieM<cumul-0.5)
    punten.push({lat:last[0],lng:last[1],positieM:cumul});
  return punten;
}

// ─── RD New CRS ──────────────────────────────────────────────────
function maakRdCrs(L){
  return new L.Proj.CRS("EPSG:28992",
    "+proj=sterea +lat_0=52.15517440 +lon_0=5.38720621 +k=0.9999079 +x_0=155000 +y_0=463000 +ellps=bessel +towgs84=565.417,50.3319,465.552,-0.398957,0.343988,-1.8774,4.0725 +units=m +no_defs",
    {resolutions:[3440.640,1720.320,860.160,430.080,215.040,107.520,53.760,26.880,13.440,6.720,3.360,1.680,0.840,0.420,0.210,0.105,0.0525,0.02625,0.013125,0.00656,0.00328,0.00164,0.00082],
     origin:[-285401.920,903401.920],bounds:L.bounds([-285401.920,22598.080],[595401.920,903401.920])}
  );
}
function rdNaarLatLng(L,x,y){
  if(typeof window!=="undefined"&&window.proj4){try{const w=proj4("EPSG:28992","EPSG:4326",[x,y]);return L.latLng(w[1],w[0]);}catch{}}
  const dX=(x-155000)/100000,dY=(y-463000)/100000;
  const N=3235.65389*dY-32.58297*dX*dX-0.24750*dY*dY-0.84978*dX*dX*dY-0.06550*dY*dY*dY;
  const E=5260.52916*dX+105.94684*dX*dY+2.45656*dX*dY*dY-0.81885*dX*dX*dX;
  return L.latLng(52.15517440+N/3600,5.38720621+E/3600);
}

// ─── Achtergrond en overlay config ───────────────────────────────
const ACHTERGRONDEN=[
  {id:"brt_standaard",label:"BRT Standaard",url:"https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/standaard/EPSG:28992/{z}/{x}/{y}.png"},
  {id:"brt_grijs",label:"BRT Grijs",url:"https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/grijs/EPSG:28992/{z}/{x}/{y}.png"},
  {id:"brt_pastel",label:"BRT Pastel",url:"https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/pastel/EPSG:28992/{z}/{x}/{y}.png"},
  {id:"luchtfoto",label:"Luchtfoto",url:null,wms:true},
];
const OVERLAYS=[
  {id:"kadaster",label:"Kadastrale percelen",kleur:"#f59e0b",url:"https://service.pdok.nl/kadaster/kadastralekaart/wms/v5_0",layers:"Perceel"},
  {id:"bag_panden",label:"BAG Panden",kleur:"#dc2626",url:"https://service.pdok.nl/lv/bag/wms/v2_0",layers:"pand"},
  {id:"bgt",label:"BGT oppervlakten",kleur:"#16a34a",url:"https://service.pdok.nl/lv/bgt/wms/v1_0",layers:"wegdeel,waterdeel,ondersteunendwegdeel,begroeidterreindeel"},
  {id:"buisleid",label:"Buisleidingen",kleur:"#f97316",url:"https://service.pdok.nl/kadaster/buisleidingen/wms/v1_0",layers:"buisleiding"},
];

// ════════════════════════════════════════════════════════════════
export default function OppervlakteAnalyse({ project, onAnalyseOpgeslagen }) {
  const mapRef       = useRef(null);
  const kaartRef     = useRef(null);
  const klicRef      = useRef([]);
  const punterMkRef  = useRef([]); // analyse-punt markers op kaart
  const basisLaagRef = useRef(null);
  const overlayRefs  = useRef({});

  const s3=(() => { try { return JSON.parse(project?.laag_instellingen||"{}"); } catch { return {}; } })();
  const [actieveAchtergrond, setActieveAchtergrond] = useState(s3.__achtergrond??"brt_standaard");
  const [actieveOverlays,    setActieveOverlays]    = useState(s3.__overlays??[]);
  const actOvRef = useRef(s3.__overlays??[]);
  actOvRef.current = actieveOverlays;

  const [analysePunten, setAnalysePunten] = useState(() => {
    try { const s=project?.analyse_punten; if(s)return typeof s==="string"?JSON.parse(s):s; } catch {}
    return [];
  });
  const [bezig,      setBezig]      = useState(false);
  const [voortgang,  setVoortgang]  = useState(0);
  const [totaalPunten,setTotaalPunten]=useState(0);
  const [opgeslagen, setOpgeslagen] = useState(false);
  const [stapGrootte,setStagGrootte]= useState(5);
  const [legendaOpen,setLegendaOpen]= useState(true);
  const [geselecteerdPunt, setGeselecteerdPunt] = useState(null);
  const [bgtDebug, setBgtDebug] = useState(null); // raw API response for debugging

  const boorCoords = (() => {
    try { const g=project?.boortrace_geojson;if(!g)return[];const p=typeof g==="string"?JSON.parse(g):g;return p.coordinates?.map(([lng,lat])=>[lat,lng])??[]; } catch { return []; }
  })();
  const totaalM = boorCoords.length>=2 ? boorCoords.reduce((s,_,i)=>i===0?0:s+afstandM(boorCoords[i-1],boorCoords[i]),0) : 0;
  const box = s3.__kaartBox??null;

  // ── Kaart init ──────────────────────────────────────────────────
  useEffect(() => {
    if(typeof window==="undefined"||kaartRef.current)return;
    let actief=true;
    (async()=>{
      if(!document.querySelector('link[href*="leaflet"]')){const c=document.createElement("link");c.rel="stylesheet";c.href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css";document.head.appendChild(c);}
      const ls=src=>new Promise((ok,er)=>{if(document.querySelector(`script[src="${src}"]`))return ok();const s=document.createElement("script");s.src=src;s.onload=ok;s.onerror=er;document.head.appendChild(s);});
      await ls("https://unpkg.com/leaflet@1.9.4/dist/leaflet.js");
      await ls("https://cdnjs.cloudflare.com/ajax/libs/proj4js/2.9.0/proj4.js");
      await ls("https://cdnjs.cloudflare.com/ajax/libs/proj4leaflet/1.0.2/proj4leaflet.js");
      if(!actief||!mapRef.current)return;
      const L=window.L;
      let rdCrs;try{rdCrs=maakRdCrs(L);}catch{}
      const pos=s3.__kaartPositie;
      const center=pos?[pos.lat,pos.lng]:(boorCoords[0]??[52.15,5.39]);
      const kaart=L.map(mapRef.current,{...(rdCrs?{crs:rdCrs}:{}),center,zoom:pos?.zoom??14,maxZoom:22,zoomControl:true});
      kaartRef.current=kaart;

      // Helpers voor achtergrond/overlays
      function zetAchtergrond(id){
        if(basisLaagRef.current){kaart.removeLayer(basisLaagRef.current);basisLaagRef.current=null;}
        if(id==="luchtfoto")basisLaagRef.current=L.tileLayer.wms("https://service.pdok.nl/hwh/luchtfotorgb/wms/v1_0",{layers:"Actueel_ortho25",format:"image/jpeg",transparent:false,maxZoom:22,attribution:"© PDOK",zIndex:1}).addTo(kaart);
        else{const c=ACHTERGRONDEN.find(a=>a.id===id)??ACHTERGRONDEN[0];basisLaagRef.current=L.tileLayer(c.url,{maxZoom:22,maxNativeZoom:13,tileSize:256,attribution:"© PDOK BRT, © Kadaster",zIndex:1}).addTo(kaart);}
      }
      function zetOverlay(id,aan){
        if(aan){if(overlayRefs.current[id])return;const c=OVERLAYS.find(o=>o.id===id);if(!c)return;overlayRefs.current[id]=L.tileLayer.wms(c.url,{layers:c.layers,format:"image/png",transparent:true,opacity:0.7,zIndex:200,attribution:"© PDOK"}).addTo(kaart);}
        else{if(overlayRefs.current[id]){kaart.removeLayer(overlayRefs.current[id]);delete overlayRefs.current[id];}}
      }
      kaart._zetAchtergrond=zetAchtergrond;
      kaart._zetOverlay=zetOverlay;
      zetAchtergrond(s3.__achtergrond??"brt_standaard");
      (s3.__overlays??[]).forEach(id=>zetOverlay(id,true));

      // Filterbox
      if(box)L.rectangle([[box.lat1,box.lng1],[box.lat2,box.lng2]],{color:"#6b7280",weight:2,fillOpacity:0,interactive:false}).addTo(kaart);

      // Boorlijn (vast)
      if(boorCoords.length>=2){
        const lijn=L.polyline(boorCoords,{color:"#2563eb",weight:5,opacity:0.95,interactive:false}).addTo(kaart);
        const mk=(nr,kleur)=>L.divIcon({className:"",html:`<div style="width:20px;height:20px;background:${kleur};border:2px solid white;border-radius:50%;display:flex;align-items:center;justify-content:center;font-size:9px;font-weight:700;color:white;box-shadow:0 1px 4px rgba(0,0,0,.4)">${nr}</div>`,iconSize:[20,20],iconAnchor:[10,10]});
        L.marker(boorCoords[0],{icon:mk("S","#15803d"),interactive:false}).addTo(kaart);
        L.marker(boorCoords[boorCoords.length-1],{icon:mk("E","#dc2626"),interactive:false}).addTo(kaart);
        try{kaart.fitBounds(lijn.getBounds().pad(0.2));}catch{}
      }

      // KLIC achtergrond
      const bestanden=(()=>{try{return JSON.parse(project?.bestanden_meta||"[]");}catch{return[];}})();
      const KLIC_K={laagspanning:"#7B00AA",middenspanning:"#00CCFF",water:"#000080",datatransport:"#00CC00",gasLageDruk:"#FFFF00",rioolVrijverval:"#AA00CC",overig:"#888"};
      for(const b of bestanden){
        if(!b.url)continue;
        const ext=b.naam.split(".").pop().toLowerCase();
        if(ext==="zip"){
          try{
            const cache=sessionStorage.getItem(`klic_parsed_${b.id}`);
            if(!cache)continue;
            const{lagen}=JSON.parse(cache);
            for(const[thema,gj]of Object.entries(lagen??{})){
              const li=s3[`klic_${thema}`]??{};if(li.zichtbaar===false)continue;
              const kleur=li.kleur??KLIC_K[thema]??"#888";
              for(const feat of(gj?.features??[])){
                const coords=(feat.geometry?.coordinates??[]).map(([x,y])=>{const ll=rdNaarLatLng(L,x,y);return[ll.lat,ll.lng];});
                if(coords.length<2)continue;
                L.polyline(coords,{color:kleur,weight:li.dikte??2,opacity:(li.helderheid??0.75)*0.8,interactive:false,zIndexOffset:-500}).addTo(kaart);
              }
            }
          }catch{}
        }
      }
    })();
    return()=>{actief=false;if(kaartRef.current){kaartRef.current.remove();kaartRef.current=null;}};
  },[]);

  // ── Teken analyse-punt markers op kaart ─────────────────────────
  useEffect(() => {
    const L=window.L;const kaart=kaartRef.current;if(!L||!kaart||analysePunten.length===0)return;
    punterMkRef.current.forEach(m=>{try{kaart.removeLayer(m);}catch{}});
    punterMkRef.current=[];
    analysePunten.forEach((p,i)=>{
      const kleur=p.oppervlak?.kleur??"#9ca3af";
      const isEinde=i===analysePunten.length-1;const isStart=i===0;
      const nr=isStart?"S":isEinde?"E":String(i);
      const ic=L.divIcon({className:"",html:`<div style="width:16px;height:16px;background:${kleur};border:1.5px solid white;border-radius:${isStart||isEinde?"50%":"3px"};display:flex;align-items:center;justify-content:center;font-size:7px;font-weight:700;color:white;box-shadow:0 1px 3px rgba(0,0,0,.4);cursor:pointer">${nr}</div>`,iconSize:[16,16],iconAnchor:[8,8]});
      const m=L.marker([p.lat,p.lng],{icon:ic,zIndexOffset:300}).addTo(kaart);
      m.on("click",()=>setGeselecteerdPunt(i));
      punterMkRef.current.push(m);
    });
  },[analysePunten]);

  // ── Achtergrond/overlay handlers ─────────────────────────────────
  function wisselAchtergrond(id){setActieveAchtergrond(id);kaartRef.current?._zetAchtergrond?.(id);}
  function toggleOverlay(id){
    const aan=!actOvRef.current.includes(id);
    const nieuw=aan?[...actOvRef.current,id]:actOvRef.current.filter(o=>o!==id);
    actOvRef.current=nieuw;setActieveOverlays(nieuw);
    kaartRef.current?._zetOverlay?.(id,aan);
  }

  // ── Analyse uitvoeren — één bulk-request voor het gehele tracé ───
  async function voerAnalyseUit(){
    if(boorCoords.length<2){alert("Geen boorlijn. Teken eerst in stap 4.");return;}
    setBezig(true);setVoortgang(0);setGeselecteerdPunt(null);
    const punten=genereerPunten(boorCoords,stapGrootte);
    setTotaalPunten(punten.length);

    try{
      // Eén bulk-request voor de hele boorlijn bbox
      const lats=boorCoords.map(c=>c[0]),lngs=boorCoords.map(c=>c[1]);
      const bulkUrl=`/api/bgt?minLat=${Math.min(...lats)}&maxLat=${Math.max(...lats)}&minLng=${Math.min(...lngs)}&maxLng=${Math.max(...lngs)}&lat=${lats[0]}&lng=${lngs[0]}`;
      setVoortgang(10);
      const res=await fetch(bulkUrl);
      const bulkData=await res.json();
      setVoortgang(60);
      const bgtFeatures=bulkData.features??[];
      console.log(`[BGT bulk] ${bgtFeatures.length} features voor heel tracé (bron: ${bulkData._source})`);

      // Per punt: vind beste BGT-feature via prioriteit en afstand
      const resultaten=punten.map((p,i)=>{
        const opp=classificeerVoorPunt(p.lat,p.lng,bgtFeatures);
        return{...p,oppervlak:opp,id:`ap_${i}`};
      });
      setVoortgang(90);
      setAnalysePunten(resultaten);
      await onAnalyseOpgeslagen?.(resultaten).catch(console.error);
      setOpgeslagen(true);setTimeout(()=>setOpgeslagen(false),3000);
    }catch(err){
      console.error("Analyse fout:",err);
      alert("Analyse mislukt: "+err.message);
    }finally{
      setBezig(false);setVoortgang(0);
    }
  }

  // ── Point-in-polygon (ray casting) voor GeoJSON ─────────────────
  function ptInRing(lat, lng, ring) {
    // ring = [[lng,lat], ...] in GeoJSON-volgorde
    let inside = false;
    for(let i=0,j=ring.length-1;i<ring.length;j=i++){
      const xi=ring[i][0],yi=ring[i][1];
      const xj=ring[j][0],yj=ring[j][1];
      if(((yi>lat)!==(yj>lat))&&(lng<(xj-xi)*(lat-yi)/(yj-yi)+xi))
        inside=!inside;
    }
    return inside;
  }

  function ptInGeometry(lat, lng, geom) {
    if(!geom) return false;
    const t=geom.type, c=geom.coordinates;
    if(t==="Polygon")     return ptInRing(lat,lng,c[0]);
    if(t==="MultiPolygon")return c.some(poly=>ptInRing(lat,lng,poly[0]));
    return false; // LineString/Point: geen polygon
  }

  // Vind het meest relevante BGT-feature voor een punt
  // Eerst: features die het punt BEVATTEN (point-in-polygon)
  // Dan:   dichtsbijzijnde centroid als fallback
  function classificeerVoorPunt(lat, lng, features) {
    if(!features.length) return OVERIG;

    // 1. Welke features bevatten het punt?
    const bevattend = features.filter(f => ptInGeometry(lat,lng,f.geometry));

    // 2. Als meerdere: kies op laagtype-prioriteit
    //    Wegdeel/onbegroeid/spoor vóór water/begroeid (specifiekere informatie)
    const PRIO = {"spoor":0,"wegdeel":1,"onbegroeid":2,"begroeid":3,"waterdeel":4};

    if(bevattend.length > 0) {
      bevattend.sort((a,b)=>{
        const ta=detecteerViaId(a.id)||detecteerBgtLaag(a.properties||{});
        const tb=detecteerViaId(b.id)||detecteerBgtLaag(b.properties||{});
        return (PRIO[ta]??5)-(PRIO[tb]??5);
      });
      return classificeerBgt(bevattend[0]);
    }

    // 3. Geen enkel polygon bevat het punt — gebruik dichtstbijzijnde centroid
    function centroidDist(f) {
      try{
        const pts=f.geometry?.coordinates?.flat(10)??[];
        const lngs=pts.filter((_,i)=>i%2===0);
        const lats=pts.filter((_,i)=>i%2===1);
        const cLng=lngs.reduce((s,v)=>s+v,0)/lngs.length;
        const cLat=lats.reduce((s,v)=>s+v,0)/lats.length;
        return Math.sqrt((lat-cLat)**2+(lng-cLng)**2);
      }catch{return Infinity;}
    }
    const nearest=[...features].sort((a,b)=>centroidDist(a)-centroidDist(b))[0];
    if(centroidDist(nearest)<0.003) return classificeerBgt(nearest); // < 300m
    return OVERIG;
  }

  // ── BGT directe test (debug) ─────────────────────────────────────
  async function testBgtQuery() {
    if (boorCoords.length < 1) return;
    const [lat, lng] = boorCoords[Math.floor(boorCoords.length / 2)];
    setBgtDebug({ status: "laden via /api/bgt...", lat, lng });
    try {
      const url = `/api/bgt?lat=${lat}&lng=${lng}`;
      const res = await fetch(url);
      if (!res.ok) { setBgtDebug({ status: `HTTP ${res.status} - ${res.statusText}`, lat, lng, url }); return; }
      const data = await res.json();
      const feats = data.features || [];
      console.log("[BGT DEBUG volledige response]", data);
      setBgtDebug({
        status: `${feats.length} features gevonden`,
        lat: Math.round(lat*10000)/10000,
        lng: Math.round(lng*10000)/10000,
        rdX: data._debug?.rdX,
        rdY: data._debug?.rdY,
        error: data.error,
        rawStart: data._debug?.rawStart,
        tried: data._debug?.tried,
        locatieserver: data._locatieserver?.adres,
        perCollectie: data._debug?.perCollectie,
        url: `/api/bgt?lat=${Math.round(lat*10000)/10000}&lng=${Math.round(lng*10000)/10000}`,
        features: feats.slice(0,3).map(f => ({
          id: f.id,
          properties: Object.fromEntries(
            Object.entries(f.properties||{}).filter(([,v])=>v!=null&&v!=="").slice(0,8)
          ),
          classificatie: classificeerBgt(f)?.label ?? "Overig",
        })),
      });
    } catch(e) {
      setBgtDebug({ status: `Fout: ${e.message}`, lat, lng });
    }
  }

  // ── Statistieken ─────────────────────────────────────────────────
  const stats=analysePunten.length>=2?(() => {
    const tot=analysePunten[analysePunten.length-1]?.positieM??0;
    const gr={};
    for(let i=0;i<analysePunten.length-1;i++){
      const seg=analysePunten[i+1].positieM-analysePunten[i].positieM;
      const k=analysePunten[i].oppervlak?.label??"Overig";
      gr[k]=(gr[k]??0)+seg;
    }
    return Object.entries(gr).sort((a,b)=>b[1]-a[1]).map(([label,m])=>({label,m:Math.round(m),pct:Math.round(m/(tot||1)*100),kleur:Object.values(BGT_TYPEN).find(t=>t.label===label)?.kleur??"#9ca3af"}));
  })():[];

  // ── Profiel SVG ──────────────────────────────────────────────────
  function AnalyseProfiel(){
    if(analysePunten.length<2)return null;
    const W=900,H=140,PAD={top:16,right:10,bottom:38,left:30};
    const plotW=W-PAD.left-PAD.right,plotH=H-PAD.top-PAD.bottom;
    const totM=analysePunten[analysePunten.length-1]?.positieM??1;
    const xPos=m=>PAD.left+(m/totM)*plotW;
    const midY=PAD.top+plotH/2;
    const segmenten=[];
    for(let i=0;i<analysePunten.length-1;i++){
      const p=analysePunten[i],n=analysePunten[i+1];
      segmenten.push({x1:xPos(p.positieM),x2:xPos(n.positieM),kleur:p.oppervlak?.kleur??"#9ca3af"});
    }
    const ticks=[0,0.25,0.5,0.75,1].map(f=>({x:xPos(f*totM),m:Math.round(f*totM)}));
    return(
      <svg viewBox={`0 0 ${W} ${H}`} className="w-full rounded-lg border border-gray-100 bg-gray-50" style={{cursor:"default"}}>
        {/* Achtergrond */}
        <rect x={PAD.left} y={PAD.top} width={plotW} height={plotH} fill="#f9fafb" rx="4"/>
        {/* Kleurvlakken verharding */}
        {segmenten.map((s,i)=>(
          <rect key={i} x={s.x1} y={PAD.top} width={Math.max(1,s.x2-s.x1)} height={plotH} fill={s.kleur} opacity={0.8}/>
        ))}
        {/* Analyse-punt nummers */}
        {analysePunten.map((p,i)=>{
          const x=xPos(p.positieM);
          const isStart=i===0,isEinde=i===analysePunten.length-1;
          const gesel=geselecteerdPunt===i;
          const nr=isStart?"S":isEinde?"E":String(i);
          return(
            <g key={i} style={{cursor:"pointer"}} onClick={()=>setGeselecteerdPunt(gesel?null:i)}>
              <line x1={x} y1={PAD.top} x2={x} y2={PAD.top+plotH} stroke={gesel?"#1d4ed8":"rgba(0,0,0,0.3)"} strokeWidth={gesel?2:0.8}/>
              <circle cx={x} cy={midY} r={gesel?6:4} fill={p.oppervlak?.kleur??"#9ca3af"} stroke="white" strokeWidth="1.5"/>
              {(i%3===0||isStart||isEinde||gesel)&&(
                <text x={x} y={PAD.top+plotH+12} textAnchor="middle" fontSize="8" fill="#6b7280">{nr}</text>
              )}
            </g>
          );
        })}
        {/* Tracé lijn */}
        <line x1={PAD.left} y1={midY} x2={W-PAD.right} y2={midY} stroke="#1d4ed8" strokeWidth="2" strokeDasharray="8 3" opacity="0.6"/>
        {/* X-as ticks */}
        {ticks.map(({x,m})=>(
          <g key={m}>
            <line x1={x} y1={H-PAD.bottom} x2={x} y2={H-PAD.bottom+4} stroke="#9ca3af" strokeWidth="1"/>
            <text x={x} y={H-8} textAnchor="middle" fontSize="10" fill="#9ca3af">{m}m</text>
          </g>
        ))}
        <text x={W/2} y={H-1} textAnchor="middle" fontSize="10" fill="#9ca3af">Afstand langs boorlijn (m)</text>
      </svg>
    );
  }

  // ── Render ───────────────────────────────────────────────────────
  return(
    <div className="space-y-3">
      <div className="flex gap-4" style={{height:"calc(100vh - 240px)",minHeight:420}}>

        {/* ── Linkerpaneel ───────────────────────────────────── */}
        <div className="w-72 flex-shrink-0 bg-white border border-gray-200 rounded-xl flex flex-col overflow-hidden">
          <div className="flex items-center justify-between px-4 py-2.5 border-b border-gray-100">
            <span className="text-sm font-semibold text-gray-800">Oppervlakteanalyse</span>
            <button onClick={()=>setLegendaOpen(o=>!o)} className="text-xs text-gray-400 hover:text-gray-600">{legendaOpen?"▲":"▼"}</button>
          </div>

          <div className="flex-1 overflow-y-auto">
            {/* Boorlijn status */}
            {boorCoords.length>=2?(
              <div className="px-4 py-3 border-b border-gray-100">
                <div className="bg-blue-50 rounded-lg px-3 py-2 flex justify-between text-xs">
                  <span className="text-blue-600 font-medium">✓ Boorlijn</span>
                  <span className="text-blue-500 font-mono">{Math.round(totaalM)} m · {boorCoords.length} pts</span>
                </div>
              </div>
            ):(
              <div className="px-4 py-4 text-center text-sm text-gray-400">Geen boorlijn — teken in stap 4.</div>
            )}

            {/* Interval instelling */}
            <div className="px-4 py-3 border-b border-gray-100">
              <div className="text-xs font-semibold text-gray-400 uppercase tracking-wide mb-2">Steekproef interval</div>
              <div className="flex items-center gap-2">
                <input type="range" min="2" max="20" step="1" value={stapGrootte} onChange={e=>setStagGrootte(Number(e.target.value))} className="flex-1 accent-orange-500 h-1"/>
                <span className="text-xs font-mono text-gray-600 w-12 text-right">elke {stapGrootte}m</span>
              </div>
              <p className="text-xs text-gray-400 mt-1">~{boorCoords.length>=2?Math.round(totaalM/stapGrootte)+2:"-"} BGT-queries</p>
            </div>

            {/* Analyse knop */}
            <div className="px-4 py-3 border-b border-gray-100 space-y-2">
              <button onClick={voerAnalyseUit} disabled={bezig||boorCoords.length<2}
                className="w-full flex items-center justify-center gap-2 px-3 py-2 text-sm font-semibold rounded-lg transition-colors disabled:opacity-50 bg-orange-500 text-white hover:bg-orange-600">
                {bezig?(<><div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin"/>Bezig…</>):(<><svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round"><polyline points="22 12 18 12 15 21 9 3 6 12 2 12"/></svg>{analysePunten.length>0?"Heranalyseren":"Analyse uitvoeren"}</>)}
              </button>
              {bezig&&(<><div className="w-full bg-gray-100 rounded-full h-1.5 overflow-hidden"><div className="h-1.5 bg-orange-500 rounded-full transition-all" style={{width:`${voortgang}%`}}/></div><p className="text-xs text-gray-400 text-center">{voortgang}% · {Math.round(voortgang/100*totaalPunten)}/{totaalPunten}</p></>)}
              {opgeslagen&&<p className="text-xs text-green-600 text-center">✓ Opgeslagen</p>}
              <button onClick={testBgtQuery}
                className="w-full px-3 py-1.5 text-xs border border-gray-200 rounded-lg text-gray-500 hover:bg-gray-50 transition-colors">
                🔍 Test BGT API (middelpunt)
              </button>
              {bgtDebug&&(
                <div className="text-xs bg-gray-50 rounded-lg p-2 space-y-1 border border-gray-200 max-h-48 overflow-y-auto">
                  <div className={`font-semibold ${bgtDebug.features?.length?'text-green-700':'text-red-600'}`}>
                    {bgtDebug.status}
                  </div>
                  <div className="text-gray-400">lat={bgtDebug.lat} lng={bgtDebug.lng}</div>
                  {bgtDebug.rdX&&<div className="text-gray-400 font-mono">RD X={bgtDebug.rdX} Y={bgtDebug.rdY}</div>}
                  {bgtDebug.error&&<div className="text-red-600 font-medium">Fout: {bgtDebug.error}</div>}
                  {bgtDebug.rawStart&&<div className="text-gray-500 font-mono text-xs break-all">{bgtDebug.rawStart}</div>}
                  {bgtDebug.locatieserver&&<div className="text-blue-600">📍 Adres: {bgtDebug.locatieserver}</div>}
                  {bgtDebug.tried&&<div className="text-gray-400 text-xs">Geprobeerd: {bgtDebug.tried.join(", ")}</div>}
                  {bgtDebug.perCollectie&&(
                    <div className="text-xs space-y-0.5">
                      {bgtDebug.perCollectie.map(({col,n})=>(
                        <div key={col} className={n>0?"text-green-600":"text-gray-300"}>{n>0?"✓":"○"} {col}: {n} features</div>
                      ))}
                    </div>
                  )}
                  {bgtDebug.features?.length===0&&!bgtDebug.error&&(
                    <div className="text-orange-600 font-medium">⚠ 0 features — geen BGT-data gevonden</div>
                  )}
                  {bgtDebug.features?.map((f,i)=>(
                    <div key={i} className="border-t border-gray-200 pt-1">
                      <div className="text-blue-600 font-mono text-xs">{f.id}</div>
                      {Object.entries(f.properties).map(([k,v])=>(
                        <div key={k}><span className="text-gray-400">{k}:</span> <span className="text-gray-700">{String(v)}</span></div>
                      ))}
                    </div>
                  ))}
                </div>
              )}
            </div>

            {/* Geselecteerd punt detail */}
            {geselecteerdPunt!==null&&analysePunten[geselecteerdPunt]&&(()=>{
              const p=analysePunten[geselecteerdPunt];
              return(
                <div className="px-4 py-3 border-b border-gray-100 bg-blue-50">
                  <div className="flex items-center justify-between mb-1">
                    <span className="text-xs font-semibold text-blue-700">Punt {geselecteerdPunt===0?"S":geselecteerdPunt===analysePunten.length-1?"E":geselecteerdPunt}</span>
                    <button onClick={()=>setGeselecteerdPunt(null)} className="text-blue-400 hover:text-blue-600 text-sm">×</button>
                  </div>
                  <div className="flex items-center gap-2 text-xs">
                    <div className="w-3 h-3 rounded-sm flex-shrink-0" style={{background:p.oppervlak?.kleur??"#9ca3af"}}/>
                    <span className="text-blue-800 font-medium">{p.oppervlak?.label??"Overig"}</span>
                  </div>
                  <div className="text-xs text-blue-500 mt-1">{Math.round(p.positieM)} m langs tracé</div>
                </div>
              );
            })()}

            {/* Samenvatting */}
            {stats.length>0&&(
              <div className="px-4 py-3 border-b border-gray-100">
                <div className="text-xs font-semibold text-gray-400 uppercase tracking-wide mb-2">Samenvatting</div>
                <div className="space-y-1.5">
                  {stats.map(({label,m,pct,kleur})=>(
                    <div key={label}>
                      <div className="flex items-center justify-between text-xs mb-0.5">
                        <div className="flex items-center gap-1.5"><div className="w-2.5 h-2.5 rounded-sm" style={{background:kleur}}/><span className="text-gray-700">{label}</span></div>
                        <span className="text-gray-400 font-mono">{m}m</span>
                      </div>
                      <div className="w-full bg-gray-100 rounded-full h-1 overflow-hidden">
                        <div className="h-1 rounded-full" style={{width:`${pct}%`,background:kleur}}/>
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            )}

            {legendaOpen&&(
              <>
                {/* Achtergrond */}
                <div className="px-4 py-3 border-b border-gray-100">
                  <div className="text-xs font-semibold text-gray-400 uppercase tracking-wide mb-2">Achtergrond</div>
                  <div className="space-y-1">
                    {ACHTERGRONDEN.map(a=>(
                      <button key={a.id} onClick={()=>wisselAchtergrond(a.id)}
                        className={`flex items-center gap-2 w-full px-2 py-1 rounded-lg text-left transition-colors ${actieveAchtergrond===a.id?"bg-orange-50 text-orange-700":"text-gray-600 hover:bg-gray-50"}`}>
                        <div className={`w-2.5 h-2.5 rounded-full border-2 flex-shrink-0 ${actieveAchtergrond===a.id?"border-orange-500 bg-orange-500":"border-gray-300"}`}/>
                        <span className="text-xs">{a.label}</span>
                      </button>
                    ))}
                  </div>
                </div>
                {/* Overlays */}
                <div className="px-4 py-3">
                  <div className="text-xs font-semibold text-gray-400 uppercase tracking-wide mb-2">Overlays</div>
                  <div className="space-y-1">
                    {OVERLAYS.map(o=>{const aan=actieveOverlays.includes(o.id);return(
                      <button key={o.id} onClick={()=>toggleOverlay(o.id)}
                        className={`flex items-center gap-2 w-full px-2 py-1 rounded-lg text-left transition-colors ${aan?"bg-blue-50":"hover:bg-gray-50"}`}>
                        <div className="w-2.5 h-2.5 rounded border border-gray-200 flex-shrink-0" style={{background:aan?o.kleur:"transparent"}}/>
                        <span className={`text-xs ${aan?"text-blue-700":"text-gray-600"}`}>{o.label}</span>
                      </button>
                    );})}
                  </div>
                </div>
              </>
            )}
          </div>
        </div>

        {/* ── Kaart ──────────────────────────────────────────── */}
        <div className="flex-1 min-w-0 rounded-xl border border-gray-200 overflow-hidden shadow-sm">
          <div ref={mapRef} className="w-full h-full"/>
        </div>
      </div>

      {/* ── Profiel ─────────────────────────────────────────── */}
      {analysePunten.length>=2&&(
        <div className="bg-white border border-gray-200 rounded-xl p-4">
          <div className="flex items-center justify-between mb-2">
            <h3 className="text-sm font-semibold text-gray-900">BGT Verhardingsprofiel langs boorlijn</h3>
            <div className="flex items-center gap-3 flex-wrap">
              {stats.map(({label,kleur})=>(
                <div key={label} className="flex items-center gap-1 text-xs text-gray-600">
                  <div className="w-3 h-3 rounded-sm" style={{background:kleur}}/>{label}
                </div>
              ))}
            </div>
          </div>
          <AnalyseProfiel/>
          <p className="text-xs text-gray-400 mt-1.5 text-center">
            {analysePunten.length} meetpunten · elke {stapGrootte}m · klik op punt in profiel voor detail
          </p>
        </div>
      )}
    </div>
  );
}
