"use client";
import BoorLabel, { LockButton } from "@/components/BoorLabel";
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
  const stap=Math.max(1,stapM); // minimaal 1m
  const punten=[];
  let cumul=0;
  punten.push({lat:coords[0][0],lng:coords[0][1],positieM:0});
  for(let i=0;i<coords.length-1;i++){
    const segLen=afstandM(coords[i],coords[i+1]);
    if(segLen<0.001){cumul+=segLen;continue;}
    let rest=stap;
    while(rest<segLen){
      const t=rest/segLen;
      punten.push({
        lat:coords[i][0]+t*(coords[i+1][0]-coords[i][0]),
        lng:coords[i][1]+t*(coords[i+1][1]-coords[i][1]),
        positieM:cumul+rest
      });
      rest+=stap;
    }
    cumul+=segLen;
  }
  const last=coords[coords.length-1];
  if(punten[punten.length-1].positieM<cumul-0.1)
    punten.push({lat:last[0],lng:last[1],positieM:cumul});
  return punten;
}

// ─── RD New CRS ──────────────────────────────────────────────────
// lat_0=52.15517440 / lon_0=5.38720621 zijn de wiskundige expansiepunten van de
// PDOK polynoombenaderingsformule — NIET gewijzigd naar de officiële projectieparameters
// omdat alle andere stappen (stap 3, 4, enz.) en de /api/bgt-klik route dezelfde
// referentiewaarden gebruiken. Inconsistentie tussen stappen veroorzaakt een visuele
// offset van ~110m van de boorlijn.
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
  {id:"brt_standaard",label:"BRT Standaard",  url:"https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/standaard/EPSG:28992/{z}/{x}/{y}.png"},
  {id:"brt_grijs",    label:"BRT Grijs",       url:"https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/grijs/EPSG:28992/{z}/{x}/{y}.png"},
  {id:"brt_pastel",   label:"BRT Pastel",      url:"https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/pastel/EPSG:28992/{z}/{x}/{y}.png"},
  {id:"luchtfoto",    label:"Luchtfoto (25cm)", wms:true,
   wmsUrl:"https://service.pdok.nl/hwh/luchtfotorgb/wms/v1_0",
   wmsLayers:"Actueel_ortho25",wmsFormat:"image/jpeg",attribution:"© PDOK Beeldmateriaal"},
  {id:"luchtfoto_hr", label:"Satelliet HR (8cm)",wms:true,
   wmsUrl:"https://service.pdok.nl/hwh/luchtfotorgb/wms/v1_0",
   wmsLayers:"Actueel_orthoHR",wmsFormat:"image/jpeg",attribution:"© PDOK Beeldmateriaal HR"},
  // Esri kaarten: custom GridLayer — per tile WGS84 bbox → Esri WMS EPSG:4326
  // Correcte WMS URL = /arcgis/services/[service]/MapServer/WMSServer (GEEN /rest/ of /exts/)
  {id:"hybride",label:"Satelliet hybride (Esri)",compound:[
    {esriWms:true,wmsUrl:"https://server.arcgisonline.com/arcgis/services/World_Imagery/MapServer/WMSServer",wmsLayers:"0",zIndex:1,attribution:"© Esri, Airbus DS, USGS"},
    {esriWms:true,wmsUrl:"https://server.arcgisonline.com/arcgis/services/Reference/World_Boundaries_and_Places/MapServer/WMSServer",wmsLayers:"0",zIndex:2,attribution:"© Esri"},
  ]},
  {id:"topografisch",label:"Topografisch (Esri)",esriWms:true,
   wmsUrl:"https://server.arcgisonline.com/arcgis/services/World_Topo_Map/MapServer/WMSServer",
   wmsLayers:"0",attribution:"© Esri, HERE, Garmin, © OpenStreetMap contributors"},
];
const OVERLAYS=[
  {id:"kadaster",label:"Kadastrale percelen",kleur:"#f59e0b",url:"https://service.pdok.nl/kadaster/kadastralekaart/wms/v5_0",layers:"Perceel"},
  {id:"bag_panden",label:"BAG Panden",kleur:"#dc2626",url:"https://service.pdok.nl/lv/bag/wms/v2_0",layers:"pand"},
  {id:"bgt",label:"BGT oppervlakten",kleur:"#16a34a",url:"https://service.pdok.nl/lv/bgt/wms/v1_0",layers:"wegdeel,waterdeel,ondersteunendwegdeel,begroeidterreindeel"},
  {id:"buisleid",label:"Buisleidingen",kleur:"#f97316",url:"https://service.pdok.nl/kadaster/buisleidingen/wms/v1_0",layers:"buisleiding"},
];

// ─── Ondergrond lagen: GeoTOP · REGIS II · Bodemkaart · Grondwater · AHN ────
// BRO is volledig open en gratis (CC0-licentie), geen login vereist.
// LET OP: PDOK heeft twee URL-patronen: korte aliassen (bzk/ahn) die redirecten naar
// canonieke URLs (tno/actueel-hoogtebestand). Gebruik ALTIJD de canonieke URL —
// Leaflet WMS-tiles volgen redirects op tile-niveau niet betrouwbaar.
//
//  GeoTOP & REGIS II: GEEN WMS op PDOK — enkel ATOM download → wmsAvailable:false
//  Overige drie: canonieke URLs + layer-namen bevestigd via GetCapabilities
const ONDERGROND_LAGEN = [
  {
    id:"geotop", label:"GeoTOP", subtitel:"3D Bodemopbouw", emoji:"🧭",
    kleur:"#a855f7",
    wmsAvailable:false,
    broUrl:"https://www.broloket.nl/",
    pdokUrl:"https://app.pdok.nl/viewer/",
    beschrijving:"3D bodemopbouw (zand/klei/veen) tot ~50 m diepte",
    risicoLabel:"Hoog", risicoKleur:"#dc2626",
    risicoTekst:"Veen- en kleilagen → spoelingverlies & instabiliteit HDD-traject",
  },
  {
    id:"regis", label:"REGIS II", subtitel:"Hydrogeologie", emoji:"🧱",
    kleur:"#06b6d4",
    wmsAvailable:false,
    broUrl:"https://www.broloket.nl/",
    pdokUrl:"https://app.pdok.nl/viewer/",
    beschrijving:"Klei/zandlagen met hydraulische eigenschappen (k-waarden)",
    risicoLabel:"Hoog", risicoKleur:"#dc2626",
    risicoTekst:"Hoge doorlatendheid → spoelingverlies, boorstabiliteit & drukopbouw",
  },
  {
    id:"bodemkaart", label:"Bodemkaart", subtitel:"1:50.000", emoji:"🌍",
    kleur:"#84cc16",
    wmsAvailable:true,
    // Canonieke URL (niet de bzk-alias die redirect veroorzaakt)
    url:"https://service.pdok.nl/tno/bro-bodemkaart/wms/v1_0",
    layers:"soilarea",       // ✓ bevestigd via GetCapabilities
    type:"wms", opacity:0.55, zIndex:211,
    beschrijving:"Landbodemtypes toplaag: klei · veen · zand",
    risicoLabel:"Middel", risicoKleur:"#f59e0b",
    risicoTekst:"Contextueel voor landzijde insteek — minder relevant waterbodems",
  },
  {
    id:"grondwater", label:"Grondwaterput (GMW)", subtitel:"BRO Peilbuizen", emoji:"💧",
    kleur:"#3b82f6",
    wmsAvailable:true,
    // Nieuwe canonieke URL (oude geodata.nationaalgeoregister.nl/brogmw is per dec 2024 offline)
    url:"https://service.pdok.nl/tno/bro-grondwatermonitoring-in-samenhang-karakteristieken/wms/v1_0",
    layers:"grondwatermonitoringput",
    type:"wms", opacity:0.85, zIndex:212,
    beschrijving:"Peilbuislocaties (BRO GMW) — grondwaterstanden & monitoring",
    risicoLabel:"Hoog", risicoKleur:"#dc2626",
    risicoTekst:"Hoge GWS → opbarstrisico boorgang & verminderde boorstabiliteit",
  },
  {
    id:"ahn", label:"AHN", subtitel:"Hoogtemodel", emoji:"🌊",
    kleur:"#f97316",
    wmsAvailable:true,
    // Canonieke URL (niet de ahn-alias die redirect veroorzaakt)
    url:"https://service.pdok.nl/rws/actueel-hoogtebestand-nederland/wms/v1_0",
    layers:"dtm_05m",        // ✓ bevestigd via WCS GetCapabilities (CoverageID=dtm_05m)
    type:"wms", opacity:0.50, zIndex:208,
    beschrijving:"Actueel Hoogtebestand Nederland — maaiveld, taluds, oevers (AHN4)",
    risicoLabel:"Middel", risicoKleur:"#f59e0b",
    risicoTekst:"Hoogteverschil beïnvloedt insteekhelling & vereiste boringsdiepte",
  },
];

// ─── Sub-stappen definitie ─────────────────────────────────────────────────
const SUB_STAPPEN = [
  { id:"5.1", label:"Oppervlakte",  emoji:"🛣️",  subtitel:"BGT verharding",    ondergrondId:null,       kleur:"#f97316" },
  { id:"5.2", label:"GeoTOP",       emoji:"🧭",  subtitel:"3D Bodemopbouw",    ondergrondId:"geotop",   kleur:"#a855f7" },
  { id:"5.3", label:"REGIS II",     emoji:"🧱",  subtitel:"Hydrogeologie",     ondergrondId:"regis",    kleur:"#06b6d4" },
  { id:"5.4", label:"Bodemkaart",   emoji:"🌍",  subtitel:"1:50.000",          ondergrondId:"bodemkaart",kleur:"#84cc16" },
  { id:"5.5", label:"Grondwater",   emoji:"💧",  subtitel:"BRO Peilbuizen",    ondergrondId:"grondwater",kleur:"#3b82f6" },
  { id:"5.6", label:"AHN",          emoji:"🌊",  subtitel:"Hoogtemodel",       ondergrondId:"ahn",      kleur:"#f97316" },
];
export default function OppervlakteAnalyse({ project, onAnalyseOpgeslagen, boringConfig }) {
  const mapRef       = useRef(null);
  const kaartRef     = useRef(null);
  const klicRef      = useRef([]);
  const punterMkRef  = useRef([]); // analyse-punt markers op kaart
  const basisLaagRef = useRef(null);
  const overlayRefs       = useRef({});
  const ondergrondOvRef   = useRef({});

  const s3=(() => { try { return JSON.parse(project?.laag_instellingen||"{}"); } catch { return {}; } })();
  const mapLS5 = () => { try { return JSON.parse(localStorage.getItem(`map_s_${project?.id}_5`)||'null'); } catch { return null; } };
  const mapSave5 = (p) => { try { const SK=`map_s_${project?.id}_5`; const cur=JSON.parse(localStorage.getItem(SK)||'{}'); localStorage.setItem(SK,JSON.stringify({...cur,...p})); } catch {} };
  const _ls5 = mapLS5();
  const [actieveAchtergrond, setActieveAchtergrond] = useState(_ls5?.ag ?? s3.__achtergrond ?? "brt_standaard");
  const [actieveOverlays,    setActieveOverlays]    = useState(_ls5?.ov ?? s3.__overlays ?? []);
  useEffect(() => { mapSave5({ag: actieveAchtergrond}); }, [actieveAchtergrond]);
  useEffect(() => { mapSave5({ov: actieveOverlays}); }, [actieveOverlays]);
  const [locked,             setLocked]             = useState(() => {
    try { const s = localStorage.getItem(`boor_lock_${project?.id}_5`); return s ? JSON.parse(s) : false; } catch { return false; }
  });
  useEffect(() => {
    try { localStorage.setItem(`boor_lock_${project?.id}_5`, JSON.stringify(locked)); lockedRef.current = locked; } catch {}
  }, [locked]);
  const lockedRef = useRef(locked);
  const actOvRef = useRef(s3.__overlays??[]);
  actOvRef.current = actieveOverlays;

  const [actieveOndergrondLagen, setActieveOndergrondLagen] = useState([]);
  const actOndergrondRef = useRef([]);
  actOndergrondRef.current = actieveOndergrondLagen;
  const [ondergrondSectieOpen, setOndergrondSectieOpen] = useState(true);
  const [actieveSubStap, setActieveSubStap] = useState("5.1");
  const actieveSubStapRef = useRef("5.1");   // stable ref voor closures (kaart click-handler)
  actieveSubStapRef.current = actieveSubStap;

  // BGT klik-info (GetFeatureInfo op klik)
  const [bgtKlikInfo,  setBgtKlikInfo]  = useState(null);
  const [bgtKlikPos,   setBgtKlikPos]   = useState({x:0,y:0});
  const [bgtKlikBezig, setBgtKlikBezig] = useState(false);
  const [actieveKlikIdx, setActieveKlikIdx] = useState(0);

  const [analysePunten, setAnalysePunten] = useState(() => {
    try { const s=project?.analyse_punten; if(s)return typeof s==="string"?JSON.parse(s):s; } catch {}
    return [];
  });
  const [bezig,      setBezig]      = useState(false);
  const [voortgang,  setVoortgang]  = useState(0);
  const [totaalPunten,setTotaalPunten]=useState(0);
  const [opgeslagen, setOpgeslagen] = useState(false);
  const [opslaanBezig, setOpslaanBezig] = useState(false);
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
      const _ls=mapLS5();
      const center=_ls?.c??(pos?[pos.lat,pos.lng]:(boorCoords[0]??[52.15,5.39]));
      const kaart=L.map(mapRef.current,{...(rdCrs?{crs:rdCrs}:{}),center,zoom:_ls?.z??pos?.zoom??14,maxZoom:22,zoomControl:true});
      kaart.on("moveend zoomend",()=>{const c=kaart.getCenter();mapSave5({z:kaart.getZoom(),c:[c.lat,c.lng]});});
      kaartRef.current=kaart;
      if(lockedRef.current){["dragging","scrollWheelZoom","doubleClickZoom","boxZoom","keyboard","touchZoom"].forEach(m=>{if(kaart[m])kaart[m].disable();});}

      // ── Esri via WMS+GridLayer: exacte WGS84 bbox per tile ─────────
      // Esri WMS ondersteunt EPSG:4326 (WGS84) maar NIET EPSG:28992.
      // L.GridLayer.createTile berekent de exacte LatLng bbox per PDOK-tile →
      // request naar Esri WMS in EPSG:4326 met die exacte bbox → pixel-perfect.
      function maakEsriWMSLaag(wmsUrl,wmsLayers,opts={}){
        const EsriWMS=L.GridLayer.extend({
          createTile(coords,done){
            const img=document.createElement("img");
            img.setAttribute("role","presentation");
            try{
              const b=this._tileCoordsToBounds(coords);
              const s=b.getSouth(),w=b.getWest(),n=b.getNorth(),e=b.getEast();
              const sz=this.getTileSize();
              // WMS 1.1.1 SRS=EPSG:4326: BBOX volgorde = minLon,minLat,maxLon,maxLat
              img.src=wmsUrl+
                "?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap"+
                "&LAYERS="+wmsLayers+"&STYLES="+
                "&SRS=EPSG:4326"+
                "&BBOX="+w+","+s+","+e+","+n+
                "&WIDTH="+sz.x+"&HEIGHT="+sz.y+
                "&FORMAT=image/jpeg&TRANSPARENT=FALSE";
              img.onload=()=>done(null,img);
              img.onerror=()=>done(new Error("tile"),img);
            }catch(err){done(err,img);}
            return img;
          }
        });
        return new EsriWMS({tileSize:256,opacity:opts.opacity??1,zIndex:opts.zIndex??1,attribution:opts.attribution??"",...opts});
      }
      kaart._maakEsriWMSLaag=maakEsriWMSLaag;
      // (maakEsriLaag blijft beschikbaar als fallback)

      // Helpers voor achtergrond/overlays
      function zetAchtergrond(id){
        if(basisLaagRef.current){
          const lagen=Array.isArray(basisLaagRef.current)?basisLaagRef.current:[basisLaagRef.current];
          lagen.forEach(l=>{try{kaart.removeLayer(l);}catch{}});
          basisLaagRef.current=null;
        }
        const c=ACHTERGRONDEN.find(a=>a.id===id)??ACHTERGRONDEN[0];
        function maakLaag(def,fallbackZ=1){
          if(def.esriWms) return maakEsriWMSLaag(def.wmsUrl,def.wmsLayers??"0",{zIndex:def.zIndex??fallbackZ,opacity:def.opacity??1,attribution:def.attribution??""});
          if(def.esri)    return maakEsriLaag?.(def.esriUrl,{zIndex:def.zIndex??fallbackZ,opacity:def.opacity??1,attribution:def.attribution??""})??L.tileLayer("",{});
          if(def.wms)     return L.tileLayer.wms(def.wmsUrl,{layers:def.wmsLayers??"0",format:def.wmsFormat??"image/jpeg",transparent:def.transparent??false,opacity:def.opacity??1,maxZoom:22,zIndex:def.zIndex??fallbackZ,attribution:def.attribution??""});
          return L.tileLayer(def.url,{maxZoom:22,maxNativeZoom:def.maxNativeZoom??13,tileSize:256,opacity:def.opacity??1,zIndex:def.zIndex??fallbackZ,attribution:def.attribution??""});
        }
        if(c.compound){
          basisLaagRef.current=c.compound.map((def,i)=>maakLaag(def,i+1).addTo(kaart));
        } else if(c.esriWms){
          basisLaagRef.current=maakEsriWMSLaag(c.wmsUrl,c.wmsLayers??"0",{zIndex:1,attribution:c.attribution??""}).addTo(kaart);
        } else if(c.esri){
          basisLaagRef.current=maakEsriLaag(c.esriUrl,{zIndex:1,attribution:c.attribution??""}).addTo(kaart);
        } else if(c.wms){
          basisLaagRef.current=L.tileLayer.wms(c.wmsUrl,{layers:c.wmsLayers??"0",format:c.wmsFormat??"image/jpeg",transparent:false,maxZoom:22,attribution:c.attribution??"",zIndex:1}).addTo(kaart);
        } else {
          basisLaagRef.current=L.tileLayer(c.url,{maxZoom:22,maxNativeZoom:c.maxNativeZoom??13,tileSize:256,attribution:c.attribution??"© PDOK BRT, © Kadaster",zIndex:1}).addTo(kaart);
        }
      }
      function zetOverlay(id,aan){
        if(aan){if(overlayRefs.current[id])return;const c=OVERLAYS.find(o=>o.id===id);if(!c)return;overlayRefs.current[id]=L.tileLayer.wms(c.url,{layers:c.layers,format:"image/png",transparent:true,opacity:0.7,zIndex:200,attribution:"© PDOK"}).addTo(kaart);}
        else{if(overlayRefs.current[id]){kaart.removeLayer(overlayRefs.current[id]);delete overlayRefs.current[id];}}
      }
      kaart._zetAchtergrond=zetAchtergrond;
      kaart._zetOverlay=zetOverlay;
      zetAchtergrond(s3.__achtergrond??"brt_standaard");
      (s3.__overlays??[]).forEach(id=>zetOverlay(id,true));

      // Ondergrond overlay beheer (GeoTOP, REGIS, Bodemkaart, Grondwater, AHN)
      function zetOndergrondOverlay(id,aan){
        if(aan){
          if(ondergrondOvRef.current[id])return;
          const laag=ONDERGROND_LAGEN.find(l=>l.id===id);
          if(!laag||!laag.wmsAvailable)return;  // geen WMS beschikbaar — skip
          let lyr;
          if(laag.type==="wmts"){
            lyr=L.tileLayer(laag.wmtsUrl,{maxZoom:22,maxNativeZoom:13,tileSize:256,opacity:laag.opacity??0.6,zIndex:laag.zIndex??210,attribution:"© PDOK AHN",crossOrigin:""});
          } else {
            lyr=L.tileLayer.wms(laag.url,{layers:laag.layers,format:"image/png",transparent:true,opacity:laag.opacity??0.65,zIndex:laag.zIndex??210,attribution:"© PDOK BRO"});
          }
          ondergrondOvRef.current[id]=lyr;
          lyr.addTo(kaart);
        } else {
          if(ondergrondOvRef.current[id]){kaart.removeLayer(ondergrondOvRef.current[id]);delete ondergrondOvRef.current[id];}
        }
      }
      kaart._zetOndergrondOverlay=zetOndergrondOverlay;

      // ── BGT klik-highlight (GeoJSON polygon op kaart) ────────────
      let klikHighlightLaag = null;
      let klikMarkerLaag    = null;

      function zetKlikHighlight(features, klikLatLng) {
        if(klikHighlightLaag){ kaart.removeLayer(klikHighlightLaag); klikHighlightLaag=null; }
        if(klikMarkerLaag)   { kaart.removeLayer(klikMarkerLaag);    klikMarkerLaag=null; }

        // Rode stip op exacte klikpositie
        if(klikLatLng){
          klikMarkerLaag=L.circleMarker([klikLatLng.lat,klikLatLng.lng],{
            radius:7,fillColor:"#ef4444",fillOpacity:1,
            color:"white",weight:2.5,interactive:false,zIndexOffset:9999,
          }).addTo(kaart);
        }

        // Zoek de eerste feature MET geldige geometrie
        const featureArr = Array.isArray(features) ? features : (features ? [features] : []);
        const feat = featureArr.find(f => f?.geometry?.coordinates?.length > 0);
        if(!feat) return;

        // Opvallende stijl: oranje/geel zodat het altijd zichtbaar is t.o.v. achtergrond
        klikHighlightLaag = L.geoJSON(feat, {
          style: {
            fillColor:"#f97316", fillOpacity:0.40,
            color:"#ea580c",     weight:4, opacity:1,
            dashArray:null,
          },
          pointToLayer:(_f,ll) => L.circleMarker(ll,{
            radius:10,fillColor:"#f97316",fillOpacity:0.7,color:"#ea580c",weight:3,
          }),
        }).addTo(kaart);

        // Zoom/pan naar de highlight zodat die altijd zichtbaar is
        try {
          const bounds = klikHighlightLaag.getBounds();
          if(bounds.isValid()) kaart.fitBounds(bounds.pad(0.4),{maxZoom:17});
        } catch{}
      }
      kaart._zetKlikHighlight = zetKlikHighlight;

      // ── BGT GetFeatureInfo op klik (tab 5.1) ─────────────────────
      // BGT bbox-selectie — stopPropagation voorkomt kaart-verschuiving
      let bgtDlCorner1 = null;
      let bgtDlRect    = null;
      kaart._resetBgtDl    = () => { if(bgtDlRect){try{kaart.removeLayer(bgtDlRect);}catch{}bgtDlRect=null;} bgtDlCorner1=null; };
      kaart._setBgtDlModus = (aan) => { bgtDlModusRef.current=aan; kaart._container.style.cursor=aan?"crosshair":""; };
      kaart.on("click", (e) => {
        if(!bgtDlModusRef.current) return;
        L.DomEvent.stopPropagation(e.originalEvent);
        if(!bgtDlCorner1){
          bgtDlCorner1=e.latlng;
          if(bgtDlRect){try{kaart.removeLayer(bgtDlRect);}catch{}}
          bgtDlRect=window.L.circleMarker(e.latlng,{radius:6,color:"#f97316",fillColor:"#f97316",fillOpacity:1,weight:2,interactive:false}).addTo(kaart);
        } else {
          const bounds=window.L.latLngBounds(bgtDlCorner1,e.latlng);
          if(bgtDlRect){try{kaart.removeLayer(bgtDlRect);}catch{}}
          bgtDlRect=window.L.rectangle(bounds,{color:"#f97316",weight:2.5,dashArray:"6,4",fillColor:"#f97316",fillOpacity:0.08,interactive:false}).addTo(kaart);
          setBgtDlHoek1(bgtDlCorner1); setBgtDlHoek2(e.latlng);
          setBgtDlStatus("gereed"); setBgtDlModus(false);
          bgtDlCorner1=null; kaart._container.style.cursor=""; bgtDlModusRef.current=false;
        }
      });

      // Filterbox
      if(box)L.rectangle([[box.lat1,box.lng1],[box.lat2,box.lng2]],{color:"#6b7280",weight:2,fillOpacity:0,interactive:false}).addTo(kaart);

      // Boorlijn (vast)
      if(boorCoords.length>=2){
        const boorGewicht = boringConfig?.boringD ? Math.max(4, Math.min(18, Math.round(boringConfig.boringD / 25))) : 5;
        const lijn=L.polyline(boorCoords,{color:"#2563eb",weight:boorGewicht,opacity:0.95,interactive:false}).addTo(kaart);
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

  // ── Vergrendeling ────────────────────────────────────────────────
  useEffect(()=>{
    const map=kaartRef.current; if(!map) return;
    ["dragging","scrollWheelZoom","doubleClickZoom","boxZoom","keyboard","touchZoom"]
      .forEach(m=>{if(map[m]) locked?map[m].disable():map[m].enable();});
  },[locked]);

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
  function toggleOndergrondLaag(id){
    const aan=!actOndergrondRef.current.includes(id);
    const nieuw=aan?[...actOndergrondRef.current,id]:actOndergrondRef.current.filter(o=>o!==id);
    setActieveOndergrondLagen(nieuw);
    kaartRef.current?._zetOndergrondOverlay?.(id,aan);
  }
  function wisselSubStap(nieuweStap){
    // Deactiveer vorige ondergrond-overlay
    const vorigeStap=SUB_STAPPEN.find(s=>s.id===actieveSubStap);
    if(vorigeStap?.ondergrondId){
      const laag=ONDERGROND_LAGEN.find(l=>l.id===vorigeStap.ondergrondId);
      if(laag?.wmsAvailable){
        kaartRef.current?._zetOndergrondOverlay?.(vorigeStap.ondergrondId,false);
        setActieveOndergrondLagen(prev=>prev.filter(id=>id!==vorigeStap.ondergrondId));
      }
    }
    // Activeer nieuwe ondergrond-overlay indien beschikbaar
    const nieuweStapDef=SUB_STAPPEN.find(s=>s.id===nieuweStap);
    if(nieuweStapDef?.ondergrondId){
      const laag=ONDERGROND_LAGEN.find(l=>l.id===nieuweStapDef.ondergrondId);
      if(laag?.wmsAvailable){
        kaartRef.current?._zetOndergrondOverlay?.(nieuweStapDef.ondergrondId,true);
        setActieveOndergrondLagen(prev=>prev.includes(nieuweStapDef.ondergrondId)?prev:[...prev,nieuweStapDef.ondergrondId]);
      }
    }
    setActieveSubStap(nieuweStap);
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

    // 3. Geen enkel polygon bevat het punt exact
    //    → probeer met minimale marge (±0.00001° ≈ 1m) om rand-gevallen op te vangen
    const MARGE = 0.00001;
    const bevattendMarge = features.filter(f =>
      ptInGeometry(lat + MARGE, lng, f.geometry) ||
      ptInGeometry(lat - MARGE, lng, f.geometry) ||
      ptInGeometry(lat, lng + MARGE, f.geometry) ||
      ptInGeometry(lat, lng - MARGE, f.geometry)
    );
    if (bevattendMarge.length > 0) {
      bevattendMarge.sort((a,b)=>{
        const ta=detecteerViaId(a.id)||detecteerBgtLaag(a.properties||{});
        const tb=detecteerViaId(b.id)||detecteerBgtLaag(b.properties||{});
        return (PRIO[ta]??5)-(PRIO[tb]??5);
      });
      return classificeerBgt(bevattendMarge[0]);
    }

    // Echt geen data voor dit exacte punt
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
  const actSubStapDef = SUB_STAPPEN.find(s=>s.id===actieveSubStap);
  const actOndergrondLaag = actSubStapDef?.ondergrondId
    ? ONDERGROND_LAGEN.find(l=>l.id===actSubStapDef.ondergrondId)
    : null;

  return(
    <div className="space-y-3">

      {/* ── Sub-navigatie 5.1 – 5.6 ────────────────────────────── */}
      <div className="bg-white border border-gray-200 rounded-xl px-3 py-2 flex items-center gap-1 flex-wrap">
        <span className="text-xs font-semibold text-gray-400 mr-2 flex-shrink-0">Stap 5 — Analyse:</span>
        {SUB_STAPPEN.map(s=>{
          const actief=actieveSubStap===s.id;
          const laag=s.ondergrondId?ONDERGROND_LAGEN.find(l=>l.id===s.ondergrondId):null;
          const heeftWms=!laag||laag.wmsAvailable;
          return(
            <button key={s.id} onClick={()=>wisselSubStap(s.id)}
              className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium transition-all flex-shrink-0 ${
                actief
                  ?"text-white shadow-sm"
                  :"bg-gray-50 text-gray-500 hover:bg-gray-100 hover:text-gray-700"
              }`}
              style={actief?{background:s.kleur}:{}}>
              <span>{s.emoji}</span>
              <span className="font-bold">{s.id}</span>
              <span>{s.label}</span>
              {!heeftWms&&<span className="ml-0.5 opacity-70 text-xs">↗</span>}
            </button>
          );
        })}
      </div>

      <div className="flex gap-4" style={{height:"calc(100vh - 280px)",minHeight:400}}>

        {/* ── Linkerpaneel ───────────────────────────────────── */}
        <div className="w-72 flex-shrink-0 bg-white border border-gray-200 rounded-xl flex flex-col overflow-hidden">
          <div className="flex items-center justify-between px-4 py-2.5 border-b border-gray-100">
            <div className="flex items-center gap-2">
              <span className="text-base leading-none">{actSubStapDef?.emoji}</span>
              <div>
                <span className="text-xs font-bold text-gray-400">{actieveSubStap} </span>
                <span className="text-sm font-semibold text-gray-800">{actSubStapDef?.label}</span>
                <div className="text-xs text-gray-400">{actSubStapDef?.subtitel}</div>
              </div>
            </div>
            <button onClick={()=>setLegendaOpen(o=>!o)} className="text-xs text-gray-400 hover:text-gray-600">{legendaOpen?"▲":"▼"}</button>
          </div>

          <div className="flex-1 overflow-y-auto">

            {/* ── 5.1 Oppervlakteanalyse (BGT) ─────────────────── */}
            {actieveSubStap==="5.1"&&(<>
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
                <input type="range" min="1" max="20" step="1" value={stapGrootte} onChange={e=>setStagGrootte(Number(e.target.value))} className="flex-1 accent-orange-500 h-1"/>
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
              {/* Opslaan knop */}
              {analysePunten.length > 0 && (
                <button
                  onClick={async () => {
                    if(opslaanBezig) return;
                    setOpslaanBezig(true);
                    try {
                      await onAnalyseOpgeslagen?.(analysePunten).catch(console.error);
                      setOpgeslagen(true); setTimeout(()=>setOpgeslagen(false), 3000);
                    } finally { setOpslaanBezig(false); }
                  }}
                  disabled={opslaanBezig}
                  className="w-full flex items-center justify-center gap-2 px-3 py-2 text-sm font-semibold rounded-lg transition-colors bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-50">
                  {opslaanBezig
                    ? <><div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin"/>Opslaan…</>
                    : <><svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round"><path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/><polyline points="17 21 17 13 7 13 7 21"/><polyline points="7 3 7 8 15 8"/></svg>Opslaan</>
                  }
                </button>
              )}
              {bezig&&(<><div className="w-full bg-gray-100 rounded-full h-1.5 overflow-hidden"><div className="h-1.5 bg-orange-500 rounded-full transition-all" style={{width:`${voortgang}%`}}/></div><p className="text-xs text-gray-400 text-center">{voortgang}% · {Math.round(voortgang/100*totaalPunten)}/{totaalPunten}</p></>)}
              {opgeslagen&&<p className="text-xs text-green-600 text-center">✓ Opgeslagen</p>}
              <button onClick={testBgtQuery}
                className="w-full px-3 py-1.5 text-xs border border-gray-200 rounded-lg text-gray-500 hover:bg-gray-50 transition-colors">
                🔍 Test BGT API (middelpunt)
              </button>
              {bgtDebug&&(
                <div className="text-xs bg-gray-50 rounded-lg p-2 space-y-1 border border-gray-200 max-h-48 overflow-y-auto">
                  <div className={`font-semibold ${bgtDebug.features?.length?'text-green-700':'text-red-600'}`}>{bgtDebug.status}</div>
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
            {/* Samenvatting BGT */}
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
                <div className="px-4 py-3 border-b border-gray-100">
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
            </>)}

            {/* ── 5.2 GeoTOP / 5.3 REGIS II (geen WMS) ─────────── */}
            {(actieveSubStap==="5.2"||actieveSubStap==="5.3")&&actOndergrondLaag&&(
              <div className="p-4 space-y-3">
                <div className="bg-orange-50 border border-orange-200 rounded-xl p-3 text-center">
                  <div className="text-2xl mb-1">{actSubStapDef?.emoji}</div>
                  <div className="text-sm font-bold text-orange-800">{actOndergrondLaag.label}</div>
                  <div className="text-xs text-orange-600 mt-0.5">{actOndergrondLaag.beschrijving}</div>
                  <div className="mt-2 text-xs text-orange-700 bg-orange-100 rounded-lg px-2 py-1">
                    Niet beschikbaar als WMS — enkel download
                  </div>
                </div>
                <div className="rounded-xl border border-gray-100 bg-gray-50 p-3 space-y-2">
                  <div className="text-xs font-semibold text-gray-500 uppercase tracking-wide">HDD Relevantie</div>
                  <div className="flex items-center gap-1.5">
                    <div className="w-2 h-2 rounded-full flex-shrink-0" style={{background:actOndergrondLaag.risicoKleur}}/>
                    <span className="text-xs font-semibold" style={{color:actOndergrondLaag.risicoKleur}}>{actOndergrondLaag.risicoLabel}</span>
                  </div>
                  <p className="text-xs text-gray-600 leading-snug">{actOndergrondLaag.risicoTekst}</p>
                </div>
                <a href={actOndergrondLaag.broUrl} target="_blank" rel="noopener noreferrer"
                  className="flex items-center justify-center gap-2 w-full py-2.5 rounded-xl bg-orange-500 text-white text-sm font-semibold hover:bg-orange-600 transition-colors">
                  📋 Open in BROloket
                </a>
                <a href={actOndergrondLaag.pdokUrl} target="_blank" rel="noopener noreferrer"
                  className="flex items-center justify-center gap-2 w-full py-2 rounded-xl border border-gray-200 text-gray-600 text-xs hover:bg-gray-50 transition-colors">
                  🗺️ PDOK Viewer
                </a>
                <p className="text-xs text-gray-400 text-center leading-snug">
                  Bekijk het model op BROloket voor de locatie van deze boring.
                </p>
              </div>
            )}

            {/* ── 5.4 / 5.5 / 5.6 (WMS beschikbaar) ────────────── */}
            {(actieveSubStap==="5.4"||actieveSubStap==="5.5"||actieveSubStap==="5.6")&&actOndergrondLaag&&(
              <div className="p-4 space-y-3">
                {/* Status kaart */}
                <div className="rounded-xl border p-3 space-y-1.5" style={{borderColor:actOndergrondLaag.kleur+"44",background:actOndergrondLaag.kleur+"08"}}>
                  <div className="flex items-center gap-2">
                    <span className="text-lg leading-none">{actSubStapDef?.emoji}</span>
                    <div>
                      <div className="text-sm font-bold text-gray-800">{actOndergrondLaag.label}</div>
                      <div className="text-xs text-gray-500">{actOndergrondLaag.beschrijving}</div>
                    </div>
                  </div>
                  <div className="flex items-center gap-1.5 mt-1">
                    <div className="w-2 h-2 rounded-full" style={{background:actOndergrondLaag.risicoKleur}}/>
                    <span className="text-xs font-semibold" style={{color:actOndergrondLaag.risicoKleur}}>{actOndergrondLaag.risicoLabel}</span>
                    <span className="text-xs text-gray-500">— {actOndergrondLaag.risicoTekst}</span>
                  </div>
                </div>
                {/* Toggle */}
                <div className="flex items-center justify-between bg-white border border-gray-200 rounded-xl px-3 py-2.5">
                  <span className="text-xs font-medium text-gray-700">Laag op kaart</span>
                  <button onClick={()=>toggleOndergrondLaag(actOndergrondLaag.id)}
                    className={`relative w-10 h-5 rounded-full transition-colors ${actieveOndergrondLagen.includes(actOndergrondLaag.id)?"bg-green-500":"bg-gray-300"}`}>
                    <div className={`absolute top-0.5 w-4 h-4 bg-white rounded-full shadow transition-transform ${actieveOndergrondLagen.includes(actOndergrondLaag.id)?"translate-x-5":"translate-x-0.5"}`}/>
                  </button>
                </div>
                {/* Opacity slider */}
                <div className="bg-white border border-gray-200 rounded-xl px-3 py-2.5 space-y-1.5">
                  <div className="flex justify-between text-xs text-gray-500">
                    <span>Doorzichtigheid</span>
                    <span className="font-mono">{Math.round((actOndergrondLaag.opacity??0.6)*100)}%</span>
                  </div>
                  <div className="w-full bg-gray-100 rounded h-1">
                    <div className="h-1 rounded" style={{width:`${(actOndergrondLaag.opacity??0.6)*100}%`,background:actOndergrondLaag.kleur}}/>
                  </div>
                </div>
                {/* Bron info */}
                <div className="bg-gray-50 border border-gray-100 rounded-xl px-3 py-2.5 space-y-1">
                  <div className="text-xs font-semibold text-gray-400 uppercase tracking-wide">Bron</div>
                  <div className="text-xs text-gray-600 font-mono break-all leading-snug">{actOndergrondLaag.url}</div>
                  <div className="text-xs text-gray-400">Layer: <span className="font-mono text-gray-600">{actOndergrondLaag.layers}</span></div>
                  <div className="text-xs text-gray-400">Licentie: CC0 · PDOK / BRO</div>
                </div>
                {actieveSubStap==="5.5"&&(
                  <p className="text-xs text-blue-600 bg-blue-50 rounded-lg px-2 py-1.5 leading-snug">
                    💡 Zoom in om peilbuizen te zien. Klik op een punt voor grondwaterstand.
                  </p>
                )}
                {actieveSubStap==="5.6"&&(
                  <p className="text-xs text-orange-600 bg-orange-50 rounded-lg px-2 py-1.5 leading-snug">
                    💡 AHN toont maaiveld. Waterdiepte is hierin niet opgenomen.
                  </p>
                )}
              </div>
            )}

          </div>
        </div>

        {/* ── Kaart ──────────────────────────────────────────── */}
        <div className="flex-1 min-w-0 rounded-xl border border-gray-200 overflow-hidden shadow-sm relative">
          <div ref={mapRef} className="w-full h-full"/>
          <BoorLabel boringConfig={boringConfig} boorlengte={project?.boorlengte_m} traceGeojson={project?.boortrace_geojson} leafletMapRef={kaartRef} projectId={project?.id} step="5" locked={locked} />
          <LockButton locked={locked} onToggle={()=>setLocked(l=>!l)}/>
        </div>
      </div>

      {/* 5.1 BGT Verhardingsprofiel */}
      {actieveSubStap==="5.1"&&analysePunten.length>=2&&(
        <div className="bg-white border border-gray-200 rounded-xl p-4">
          <div className="flex items-center justify-between mb-2">
            <h3 className="text-sm font-semibold text-gray-900">🛣️ 5.1 BGT Verhardingsprofiel langs boorlijn</h3>
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

      {/* 5.2 / 5.3 GeoTOP & REGIS info-kaart */}
      {(actieveSubStap==="5.2"||actieveSubStap==="5.3")&&actOndergrondLaag&&(
        <div className="bg-white border border-gray-200 rounded-xl p-4">
          <div className="flex items-center gap-3 mb-3">
            <span className="text-2xl">{actSubStapDef?.emoji}</span>
            <div>
              <h3 className="text-sm font-semibold text-gray-900">{actieveSubStap} {actOndergrondLaag.label} — {actOndergrondLaag.beschrijving}</h3>
              <p className="text-xs text-gray-500">{actOndergrondLaag.risicoTekst}</p>
            </div>
            <span className="ml-auto text-xs px-3 py-1 rounded-full font-semibold" style={{background:actOndergrondLaag.risicoKleur+"18",color:actOndergrondLaag.risicoKleur}}>
              {actOndergrondLaag.risicoLabel} HDD-risico
            </span>
          </div>
          <div className="bg-orange-50 border border-orange-200 rounded-xl p-4 text-center">
            <p className="text-sm text-orange-700 font-medium mb-2">
              {actOndergrondLaag.label} is niet als WMS beschikbaar via PDOK.
            </p>
            <p className="text-xs text-orange-600 mb-3">
              Bekijk het model op BROloket voor een dwarsdoorsnede ter plaatse van dit tracé.
            </p>
            <a href={actOndergrondLaag.broUrl} target="_blank" rel="noopener noreferrer"
              className="inline-flex items-center gap-2 px-4 py-2 rounded-lg bg-orange-500 text-white text-sm font-semibold hover:bg-orange-600 transition-colors">
              📋 Open BROloket
            </a>
          </div>
        </div>
      )}

      {/* 5.4 / 5.5 / 5.6 WMS lagen info */}
      {(actieveSubStap==="5.4"||actieveSubStap==="5.5"||actieveSubStap==="5.6")&&actOndergrondLaag&&(
        <div className="bg-white border border-gray-200 rounded-xl p-4">
          <div className="flex items-center gap-3 mb-2">
            <span className="text-2xl">{actSubStapDef?.emoji}</span>
            <div>
              <h3 className="text-sm font-semibold text-gray-900">{actieveSubStap} {actOndergrondLaag.label} — WMS overlay op kaart</h3>
              <p className="text-xs text-gray-500">{actOndergrondLaag.beschrijving} · {actOndergrondLaag.risicoTekst}</p>
            </div>
            <span className="ml-auto text-xs px-3 py-1 rounded-full font-semibold flex-shrink-0" style={{background:actOndergrondLaag.risicoKleur+"18",color:actOndergrondLaag.risicoKleur}}>
              {actOndergrondLaag.risicoLabel}
            </span>
          </div>
          <div className="bg-gray-50 rounded-xl p-3 text-xs text-gray-500 flex items-center gap-3">
            <div className="w-8 h-8 rounded-lg flex-shrink-0 flex items-center justify-center text-xl" style={{background:actOndergrondLaag.kleur+"22"}}>
              {actSubStapDef?.emoji}
            </div>
            <div>
              <span className="font-semibold text-gray-700">WMS actief op kaart</span>
              <span className="ml-2 font-mono">{actOndergrondLaag.url.replace("https://","")}</span>
              <br/>Layer: <span className="font-mono text-gray-700">{actOndergrondLaag.layers}</span>
              {actieveSubStap==="5.6"&&<span className="ml-3 text-orange-600">AHN toont maaiveld — geen waterdiepte</span>}
              {actieveSubStap==="5.5"&&<span className="ml-3 text-blue-600">Zoom in om peilbuispunten te zien</span>}
            </div>
          </div>
        </div>
      )}

    </div>
  );
}
