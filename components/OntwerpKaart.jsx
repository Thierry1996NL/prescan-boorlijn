"use client";
import { useState, useEffect, useRef } from "react";
import { updateProject } from "@/lib/supabase-queries";

// ─── NEN-1775 thema configuratie ─────────────────────────────────
const THEMA = {
  laagspanning:              { label: "Laagspanning (LS)",   kleur: "#7B00AA" },
  middenspanning:            { label: "Middenspanning (MS)", kleur: "#00CCFF" },
  hoogspanning:              { label: "Hoogspanning (HS)",   kleur: "#FF4400" },
  gasLageDruk:               { label: "Gas lage druk (LD)", kleur: "#FFFF00" },
  gasHogeDruk:               { label: "Gas hoge druk (HD)", kleur: "#FF0000" },
  water:                     { label: "Water",               kleur: "#000080" },
  datatransport:             { label: "Data / Telecom",      kleur: "#00CC00" },
  rioolVrijverval:           { label: "Riool (vrijverval)",  kleur: "#AA00CC" },
  rioolOnderOverOfOnderdruk: { label: "Riool (druk)",        kleur: "#AA00CC" },
  warmte:                    { label: "Warmte",              kleur: "#FF6600" },
  overig:                    { label: "Overig",              kleur: "#888888" },
  // Speciale constructies — dik + gestreept weergegeven
  mantelbuis:                { label: "Mantelbuis",           kleur: "#4B5563" }, // donkergrijs
  kabelbed:                  { label: "Kabelbed",             kleur: "#111827" }, // bijna zwart
  duct:                      { label: "Duct / geleiding",     kleur: "#374151" }, // antraciet
};

const TYPE_KLEUR = { LS:"#7B00AA",MS:"#00CCFF",Gas:"#FFFF00",Water:"#000080",Data:"#00CC00",KLIC:"#FF0000" };

const ACHTERGROND = [
  // ── PDOK (EPSG:28992 native WMTS) ──
  { id:"brt_standaard", groep:"PDOK",    label:"BRT Standaard",     url:"https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/standaard/EPSG:28992/{z}/{x}/{y}.png", opties:{minZoom:0,maxNativeZoom:13,maxZoom:22,tileSize:256,attribution:"© PDOK BRT, © Kadaster"} },
  { id:"brt_grijs",     groep:"PDOK",    label:"BRT Grijs",         url:"https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/grijs/EPSG:28992/{z}/{x}/{y}.png",     opties:{minZoom:0,maxNativeZoom:13,maxZoom:22,tileSize:256,attribution:"© PDOK BRT, © Kadaster"} },
  { id:"brt_pastel",    groep:"PDOK",    label:"BRT Pastel",        url:"https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/pastel/EPSG:28992/{z}/{x}/{y}.png",    opties:{minZoom:0,maxNativeZoom:13,maxZoom:22,tileSize:256,attribution:"© PDOK BRT, © Kadaster"} },
  { id:"luchtfoto",     groep:"PDOK",    label:"Luchtfoto (PDOK)",  wms:true, url:"https://service.pdok.nl/hwh/luchtfotorgb/wms/v1_0", layers:"Actueel_ortho25", opties:{format:"image/jpeg",transparent:false,maxZoom:22,attribution:"© PDOK, Beeldmateriaal NL"} },
  // ── Esri Nederland (EPSG:28992 · L.GridLayer /export) ──
  { id:"esri_topo_rd",    groep:"Esri NL", label:"Esri Topo RD",       url:"https://services.arcgisonline.nl/arcgis/rest/services/Basiskaarten/Topo/MapServer",       opties:{attribution:"© Esri Nederland, Community Maps"} },
  { id:"esri_open_topo",  groep:"Esri NL", label:"Esri Open Topo",     url:"https://services.arcgisonline.nl/arcgis/rest/services/Basiskaarten/Open_Topo/MapServer",  opties:{attribution:"© Esri Nederland"} },
  { id:"esri_luchtfoto",  groep:"Esri NL", label:"Esri Luchtfoto (HR)",url:"https://services.arcgisonline.nl/arcgis/rest/services/Basiskaarten/Luchtfoto/MapServer",  opties:{attribution:"© Esri Nederland"} },
  { id:"esri_waterkaart", groep:"Esri NL", label:"Esri Waterkaart",    url:"https://services.arcgisonline.com/arcgis/rest/services/Ocean/World_Ocean_Base/MapServer", opties:{attribution:"© Esri, GEBCO, NOAA, NGS"} },
];

const OVERLAYS = [
  { id:"kadaster",   label:"Kadastrale percelen",    url:"https://service.pdok.nl/kadaster/kadastralekaart/wms/v5_0",   layers:"Perceel",                                              kleur:"#8B4513" },
  { id:"bag_panden", label:"BAG Panden",             url:"https://service.pdok.nl/lv/bag/wms/v2_0",                      layers:"pand",                                                 kleur:"#dc2626" },
  { id:"bag_adres",  label:"BAG Adressen",           url:"https://service.pdok.nl/lv/bag/wms/v2_0",                      layers:"ligplaats,standplaats,verblijfsobject",                 kleur:"#2563eb" },
  { id:"bgt",        label:"BGT (oppervlakten)",     url:"https://service.pdok.nl/lv/bgt/wms/v1_0",                      layers:"wegdeel,waterdeel,ondersteunendwegdeel,begroeidterreindeel", kleur:"#16a34a" },
];

// ─── CDN loaders ─────────────────────────────────────────────────
async function laadLeaflet() {
  if (typeof window==="undefined") return null;
  if (window.L?.version) return window.L;
  if (!document.querySelector('link[href*="leaflet"]')) {
    const l=document.createElement("link"); l.rel="stylesheet";
    l.href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css"; document.head.appendChild(l);
  }
  await new Promise((ok,err)=>{if(window.L?.version)return ok();const s=document.createElement("script");s.src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js";s.onload=ok;s.onerror=err;document.head.appendChild(s);});
  return window.L;
}
async function laadProj4() {
  if (!window.proj4) await new Promise((ok,err)=>{const s=document.createElement("script");s.src="https://cdnjs.cloudflare.com/ajax/libs/proj4js/2.9.0/proj4.js";s.onload=ok;s.onerror=err;document.head.appendChild(s);});
  if (!window.L?.Proj) await new Promise((ok,err)=>{const s=document.createElement("script");s.src="https://cdnjs.cloudflare.com/ajax/libs/proj4leaflet/1.0.2/proj4leaflet.js";s.onload=ok;s.onerror=err;document.head.appendChild(s);});
}
async function laadJSZip() {
  if (window.JSZip) return window.JSZip;
  await new Promise((ok,err)=>{const s=document.createElement("script");s.src="https://cdnjs.cloudflare.com/ajax/libs/jszip/3.10.1/jszip.min.js";s.onload=ok;s.onerror=err;document.head.appendChild(s);});
  return window.JSZip;
}

// ─── Esri GridLayer via ArcGIS REST /export ──────────────────
// Gebruikt /export?bbox=... zodat geen tile-schema mismatch mogelijk is
function maakEsriExportLayer(L, serviceUrl, attrib) {
  const EsriExport = L.GridLayer.extend({
    createTile(coords, done) {
      const img = document.createElement("img");
      img.alt = "";
      const ts = this.getTileSize();
      const nwPx = coords.scaleBy(ts);
      const sePx = nwPx.add([ts.x, ts.y]);
      const crs  = this._map.options.crs;
      const nwRD  = crs.project(this._map.unproject(nwPx, coords.z));
      const seRD  = crs.project(this._map.unproject(sePx, coords.z));
      const xMin = Math.min(nwRD.x, seRD.x).toFixed(3);
      const yMin = Math.min(nwRD.y, seRD.y).toFixed(3);
      const xMax = Math.max(nwRD.x, seRD.x).toFixed(3);
      const yMax = Math.max(nwRD.y, seRD.y).toFixed(3);
      const url  = `${serviceUrl}/export?bbox=${xMin},${yMin},${xMax},${yMax}`
                 + `&bboxSR=28992&size=${ts.x},${ts.y}&imageSR=28992`
                 + `&format=png32&transparent=false&f=image`;
      img.onload  = () => done(null, img);
      img.onerror = (e) => done(e, img);
      img.src = url;
      return img;
    }
  });
  return new EsriExport({ attribution: attrib ?? "© Esri Nederland", zIndex:1 });
}

// ─── RD CRS ──────────────────────────────────────────────────────
function maakRdCrs(L) {
  return new L.Proj.CRS("EPSG:28992",
    "+proj=sterea +lat_0=52.15517440 +lon_0=5.38720621 +k=0.9999079 +x_0=155000 +y_0=463000 +ellps=bessel +towgs84=565.417,50.3319,465.552,-0.398957,0.343988,-1.8774,4.0725 +units=m +no_defs",
    {
      resolutions:[3440.640,1720.320,860.160,430.080,215.040,107.520,53.760,26.880,13.440,6.720,3.360,1.680,0.840,0.420,0.210,0.105,0.0525,0.02625,0.013125,0.00656,0.00328,0.00164,0.00082],
      origin:[-285401.920,903401.920],
      bounds:L.bounds([-285401.920,22598.080],[595401.920,903401.920]),
    }
  );
}
const rdNaarLatLng = (L)=>([x,y])=>{
  if(typeof window!=="undefined"&&window.proj4){try{const w=proj4("EPSG:28992","EPSG:4326",[x,y]);return L.latLng(w[1],w[0]);}catch{}}
  const dX=(x-155000)/100000,dY=(y-463000)/100000;
  const sumN=3235.65389*dY-32.58297*dX*dX-0.24750*dY*dY-0.84978*dX*dX*dY-0.06550*dY*dY*dY;
  const sumE=5260.52916*dX+105.94684*dX*dY+2.45656*dX*dY*dY-0.81885*dX*dX*dX;
  return L.latLng(52.15517440+sumN/3600,5.38720621+sumE/3600);
};
function rdNaarWgs84(x,y) {
  if(typeof window!=="undefined"&&window.proj4){try{const w=proj4("EPSG:28992","EPSG:4326",[x,y]);return[w[0],w[1]];}catch{}}
  const dX=(x-155000)/100000,dY=(y-463000)/100000;
  const sumN=3235.65389*dY-32.58297*dX*dX-0.24750*dY*dY-0.84978*dX*dX*dY-0.06550*dY*dY*dY;
  const sumE=5260.52916*dX+105.94684*dX*dY+2.45656*dX*dY*dY-0.81885*dX*dX*dX;
  return[5.38720621+sumE/3600,52.15517440+sumN/3600];
}

// ─── Waarde humanizer ────────────────────────────────────────────
function humanize(val){
  if(!val||val==="[]"||val==="") return "—";
  const v=String(val);
  const MAP={functional:"Functioneel",inUse:"In gebruik",disused:"Buiten gebruik",underground:"Ondergronds",aboveground:"Bovengronds",underground_aboveground:"Ondergronds/Bovengronds",distribution:"Distributie",transmission:"Transport",service:"Aansluiting",potable:"Drinkwater",nonPotable:"Niet-drinkbaar",waste:"Afvalwater",tot100cm:"Tot 100 cm",tot50cm:"Tot 50 cm",tot20cm:"Tot 20 cm",nauwkeurig:"Nauwkeurig"};
  const key=v.split("/").pop().split("#").pop().split(":").pop();
  return MAP[key]??key;
}
function formatDate(val){if(!val)return"—";try{return new Date(val).toLocaleDateString("nl-NL");}catch{return val;}}

// ─── IMKL volledige parser ────────────────────────────────────────
function parseImkl(xmlTekst, onVoortgang) {
  const doc = new DOMParser().parseFromString(xmlTekst, "text/xml");
  const tag = el => el.tag?.split?.("}")?.pop?.() ?? el.tagName?.split?.(":")?.[1] ?? el.tagName;

  function getAttr(el, name) {
    return el.getAttribute(name) || el.getAttribute(`gml:${name}`) || el.getAttributeNS?.("http://www.opengis.net/gml/3.2", name.replace("gml:","")) || "";
  }
  function getXlink(el) { return el.getAttributeNS?.("http://www.w3.org/1999/xlink","href")||el.getAttribute("xlink:href")||""; }
  function childText(el, tagName) {
    const c = el.querySelector(tagName); return c?.textContent?.trim() || "";
  }
  function childXlink(el, tagName) {
    const c = el.querySelector(tagName); return c ? getXlink(c) : "";
  }

  // 1. Netbeheerder lookup: bronhoudercode → naam
  const netbeheerder = {}; // bronhoudercode → naam
  doc.querySelectorAll("Beheerder").forEach(b => {
    const code = childText(b,"bronhoudercode");
    const website = childText(b,"websiteKlic");
    let naam = code;
    if(website.includes("liander")) naam="Liander N.V.";
    else if(website.includes("vitens")) naam="Vitens";
    else if(website.includes("ziggo")) naam="Ziggo";
    else if(website.includes("kpn")||code==="KL1051") naam="KPN";
    else if(code.startsWith("GM")) naam=`Gemeente ${code}`;
    else if(code.startsWith("WS")) naam=`Waterschap ${code}`;
    netbeheerder[code] = naam;
  });

  // 2. Netwerk → thema + bronhoudercode
  const netInfo = {}; // netId → { thema, code }
  doc.querySelectorAll("Utiliteitsnet").forEach(net => {
    const id = getAttr(net,"id");
    const themaHref = childXlink(net,"thema");
    const thema = themaHref.split("/").pop();
    const code = id.split(".")[1]?.replace?.("imkl-","") ?? "";
    const broncode = code.match(/^[A-Z]{2}\d+|^[A-Z]{2}\d+/)?.[0] ?? code.substring(0,5);
    netInfo[id] = { thema, broncode };
  });

  // 3. Documenten: ExtraDetailinfo + Bijlage
  const documenten = [];
  doc.querySelectorAll("ExtraDetailinfo").forEach(d => {
    const netRef = childXlink(d,"inNetwork").replace(/^#/,"");
    const info = netInfo[netRef] || {};
    const pad = childText(d,"bestandLocatie");
    const typeHref = childXlink(d,"extraInfoType") || childText(d,"extraInfoType");
    const docType = typeHref.split("/").pop() || "document";
    if(pad) documenten.push({ pad, type: docType, thema: info.thema||"onbekend", broncode: info.broncode||"", naam: pad.split("/").pop() });
  });
  doc.querySelectorAll("Bijlage").forEach(b => {
    const pad = childText(b,"bestandLocatie");
    const typeHref = childXlink(b,"bijlageType") || childText(b,"bijlageType");
    const docType = typeHref.split("/").pop() || "bijlage";
    const id = childText(b,"bestandIdentificator");
    const code = id.split(".")[1]?.replace("imkl-","")?.substring(0,5) ?? "";
    if(pad) documenten.push({ pad, type: docType, thema:"algemeen", broncode:code, naam:pad.split("/").pop() });
  });

  // 4. Appurtenance (knooppunten/kleppen als punten)
  const appurtenances = [];
  doc.querySelectorAll("Appurtenance").forEach(app => {
    const netRef = childXlink(app,"inNetwork").replace(/^#/,"");
    const info = netInfo[netRef] || {};
    const appType = humanize(childXlink(app,"appurtenanceType"));
    const geomEl = app.querySelector("geometry Point pos, geometry Point coordinates, Geometry Point pos");
    if(!geomEl) return;
    const coords = geomEl.textContent.trim().split(/\s+/).map(Number);
    if(coords.length<2||isNaN(coords[0])) return;
    appurtenances.push({
      coords: [coords[0],coords[1]],
      properties: { featuretype:"Appurtenance", appurtenanceType:appType, thema:info.thema||"overig", broncode:info.broncode||"", netbeheerderNaam:netbeheerder[info.broncode]||info.broncode||"" }
    });
  });
  onVoortgang?.("Appurtenances gelezen…");

  // 5. Leidingen
  const themalagen = {};
  const LEIDING_TYPES = ["Elektriciteitskabel","Waterleiding","OlieGasChemicalienPijpleiding","Rioolleiding","Telecommunicatiekabel","Mantelbuis","Kabelbed","Duct","Overig"];
  const links = doc.querySelectorAll("UtilityLink");
  let teller=0;

  links.forEach(link => {
    teller++;
    if(teller%100===0) onVoortgang?.(`${teller}/${links.length} leidingen…`);
    const linkId = getAttr(link,"id");
    const netRef = childXlink(link,"inNetwork").replace(/^#/,"");
    const info = netInfo[netRef] || {};
    const thema = info.thema || "overig";
    const posListEl = link.querySelector("posList");
    if(!posListEl) return;
    const nums = posListEl.textContent.trim().split(/\s+/).map(Number);
    const coords=[];
    for(let i=0;i+1<nums.length;i+=2) coords.push([nums[i],nums[i+1]]);
    if(coords.length<2) return;

    // Zoek de bijhorende leiding voor eigenschappen
    const leidingProps = linkId2props(doc,linkId,netRef,info,netbeheerder,thema);

    // Mantelbuis/Kabelbed/Duct krijgen eigen groep zodat ze visueel onderscheidend zijn
    const SPECIALE_FEATURETYPES=["Mantelbuis","Kabelbed","Duct"];
    const groeperingKey=SPECIALE_FEATURETYPES.includes(leidingProps.featuretype)
      ? leidingProps.featuretype.toLowerCase()
      : thema;

    if(!themalagen[groeperingKey]) themalagen[groeperingKey]=[];
    themalagen[groeperingKey].push({ type:"Feature", geometry:{type:"LineString",coordinates:coords}, properties:leidingProps });
  });

  const lagen={};
  for(const[thema,features]of Object.entries(themalagen)) lagen[thema]={type:"FeatureCollection",features};
  return { lagen, documenten, appurtenances };
}

function linkId2props(doc,linkId,netRef,netInfo,netbeheerder,thema){
  // Zoek leiding die dit link gebruikt
  const TYPES=["Elektriciteitskabel","Waterleiding","OlieGasChemicalienPijpleiding","Rioolleiding","Telecommunicatiekabel","Mantelbuis","Kabelbed","Overig"];
  let props = { featuretype:"Leiding", thema, broncode:netInfo.broncode||"", netbeheerderNaam:netbeheerder[netInfo.broncode]||netInfo.broncode||"" };
  for(const t of TYPES){
    const found = [...doc.querySelectorAll(t)].find(el=>{
      const linkHref = el.querySelector("link")?.getAttributeNS?.("http://www.w3.org/1999/xlink","href")||el.querySelector("link")?.getAttribute?.("xlink:href")||"";
      return linkHref.includes(linkId.split(".").pop());
    });
    if(found){
      props.featuretype = t;
      const xlink = el => el.getAttributeNS?.("http://www.w3.org/1999/xlink","href")||el.getAttribute?.("xlink:href")||el.textContent?.trim()||"";
      found.querySelectorAll("*").forEach(child=>{
        const n=child.tagName?.split?.(":")?.[1]??child.tagName;
        if(!n||n===t) return;
        const v=child.children?.length>0?"":child.textContent?.trim();
        const h=xlink(child);
        if(v||h) props[n]=h?humanize(h):v;
      });
      break;
    }
  }
  return props;
}


// ─── Liang-Barsky lijnclipping naar filterbox ─────────────────────
function liangBarsky(x1,y1,x2,y2,xMin,xMax,yMin,yMax){
  const dx=x2-x1,dy=y2-y1;
  const p=[-dx,dx,-dy,dy];
  const q=[x1-xMin,xMax-x1,y1-yMin,yMax-y1];
  let t0=0,t1=1;
  for(let i=0;i<4;i++){
    if(p[i]===0){if(q[i]<0)return null;}
    else{const t=q[i]/p[i];if(p[i]<0)t0=Math.max(t0,t);else t1=Math.min(t1,t);}
  }
  if(t0>t1)return null;
  return[x1+t0*dx,y1+t0*dy,x1+t1*dx,y1+t1*dy];
}

// Clip een array van [lng,lat] of [lat,lng] coördinaten naar box
// Geeft array van segmenten terug (elk segment = array van [lat,lng])
function clipPolylineNaarBox(latlngs, box){
  // latlngs = [{lat,lng}] of [[lat,lng]]
  const xMin=box.lng1,xMax=box.lng2,yMin=box.lat1,yMax=box.lat2;
  const segmenten=[];
  let huidig=null;
  for(let i=0;i<latlngs.length-1;i++){
    const p1=latlngs[i],p2=latlngs[i+1];
    const lat1=p1.lat??p1[0],lng1=p1.lng??p1[1];
    const lat2=p2.lat??p2[0],lng2=p2.lng??p2[1];
    const c=liangBarsky(lng1,lat1,lng2,lat2,xMin,xMax,yMin,yMax);
    if(!c){if(huidig){segmenten.push(huidig);huidig=null;}continue;}
    const[cx1,cy1,cx2,cy2]=c;
    if(!huidig){huidig=[[cy1,cx1],[cy2,cx2]];}
    else{
      const last=huidig[huidig.length-1];
      if(Math.abs(last[0]-cy1)<1e-9&&Math.abs(last[1]-cx1)<1e-9){huidig.push([cy2,cx2]);}
      else{segmenten.push(huidig);huidig=[[cy1,cx1],[cy2,cx2]];}
    }
  }
  if(huidig)segmenten.push(huidig);
  return segmenten;
}

// ─── Standaard instellingen ───────────────────────────────────────
function standaardInst(type){return{zichtbaar:true,kleur:TYPE_KLEUR[type]??"#3b82f6",dikte:2,helderheid:0.85};}
function standaardThemaInst(t){
  const DIKTES={mantelbuis:7,kabelbed:9,duct:5};
  return{zichtbaar:true,kleur:THEMA[t]?.kleur??"#6b7280",dikte:DIKTES[t]??2,helderheid:DIKTES[t]?0.9:0.85};
}

// ════════════════════════════════════════════════════════════════
// ─── Snapshot helper (gedeeld via CDN html2canvas) ───────────────────────────
async function maakKaartOpname(containerEl, projectId, stapNr, setStatus) {
  if (!containerEl) return;
  setStatus('saving');
  try {
    if (!window.html2canvas) {
      await new Promise((ok, err) => {
        const s = document.createElement('script');
        s.src = 'https://cdnjs.cloudflare.com/ajax/libs/html2canvas/1.4.1/html2canvas.min.js';
        s.onload = ok; s.onerror = err;
        document.head.appendChild(s);
      });
    }
    const canvas = await window.html2canvas(containerEl, {
      useCORS: true, allowTaint: false, scale: 1.2,
      imageTimeout: 12000, logging: false,
    });
    const imgData = canvas.toDataURL('image/jpeg', 0.82);
    localStorage.setItem(`bv_snap_${projectId}_${stapNr}`, imgData);
    localStorage.setItem(`bv_snap_${projectId}_${stapNr}_datum`,
      new Date().toLocaleString('nl-NL', {day:'2-digit',month:'short',year:'numeric',hour:'2-digit',minute:'2-digit'})
    );
    setStatus('saved');
    setTimeout(() => setStatus(null), 3000);
  } catch(e) {
    console.error('Opname mislukt:', e);
    setStatus('error');
    setTimeout(() => setStatus(null), 4000);
  }
}

function SnapKnop({ status, onClick }) {
  return (
    <button
      onClick={onClick}
      disabled={status === 'saving'}
      title="Sla de huidige kaartweergave op voor het eindrapport"
      className={`absolute bottom-3 right-3 z-[400] flex items-center gap-1.5 px-2.5 py-1.5 text-xs font-medium rounded-lg shadow-md transition-all border ${
        status === 'saved'  ? 'bg-[#007A5A] text-white border-[#007A5A]' :
        status === 'error'  ? 'bg-red-500 text-white border-red-500' :
        status === 'saving' ? 'bg-white text-[#587080] border-[#DEE6EA]' :
        'bg-white text-[#587080] border-[#DEE6EA] hover:bg-[#F5F7F9] hover:text-[#1B2B35]'
      }`}
    >
      {status === 'saving' ? <>⏳ Opname…</> :
       status === 'saved'  ? <>✓ Opgeslagen voor rapport</> :
       status === 'error'  ? <>✗ Mislukt — probeer opnieuw</> :
       <><svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round"><path d="M23 19a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4l2-3h6l2 3h4a2 2 0 0 1 2 2z"/><circle cx="12" cy="13" r="4"/></svg>Opslaan voor rapport</>}
    </button>
  );
}

export default function OntwerpKaart({ project, projectId, onOpgeslagen }) {
  const mapElRef  = useRef(null);
  const kaartRef  = useRef(null);
  const snapContainerRef = useRef(null);
  const LRef      = useRef(null);
  const lagenRef  = useRef({});
  const basisLaagRef = useRef(null);
  const overlayRefs  = useRef({});
  const featureKlikRef = useRef(null);

  const bestanden = (()=>{try{return JSON.parse(project.bestanden_meta||"[]");}catch{return[];}})();
  const opgeslagenInst=(()=>{try{return JSON.parse(project.laag_instellingen||"{}");}catch{return{};}})();
  const initInst={};
  for(const b of bestanden) initInst[b.id]=opgeslagenInst[b.id]??standaardInst(b.type);
  for(const[k,v]of Object.entries(opgeslagenInst)){
    if(k.startsWith("klic_")){const t=k.replace("klic_","");initInst[k]={...standaardThemaInst(t),...v,kleur:THEMA[t]?.kleur??v.kleur};}
  }

  const [snapStatus,     setSnapStatus]      = useState(null); // null|'saving'|'saved'|'error'
  const [instellingen,   setInstellingen]   = useState(initInst);
  const [klicLagen,      setKlicLagen]       = useState({});
  const [documenten,     setDocumenten]      = useState([]); // [{naam,type,thema,broncode,blobUrl}]
  const [bestandStatus,  setBestandStatus]   = useState({});
  const [opslaanActief,  setOpslaanActief]   = useState(false);
  const [ingeslagen,     setIngeslagen]       = useState(false);
  const [foutmelding,    setFoutmelding]      = useState(null);
  const [rdCursor,       setRdCursor]         = useState(null);
  const mapLS3 = () => { try { return JSON.parse(localStorage.getItem(`map_s_${project?.id}_3`)||'null'); } catch { return null; } };
  const mapSave3 = (p) => { try { const SK=`map_s_${project?.id}_3`; const cur=JSON.parse(localStorage.getItem(SK)||'{}'); localStorage.setItem(SK,JSON.stringify({...cur,...p})); } catch {} };
  const _ls3 = mapLS3();
  const [actieveAchtergrond, setActieveAchtergrond] = useState(_ls3?.ag ?? opgeslagenInst.__achtergrond ?? "brt_standaard");
  const [actieveOverlays,    setActieveOverlays]    = useState(_ls3?.ov ?? opgeslagenInst.__overlays ?? []);
  useEffect(() => { mapSave3({ag: actieveAchtergrond}); }, [actieveAchtergrond]);
  useEffect(() => { mapSave3({ov: actieveOverlays}); }, [actieveOverlays]);
  const [vergrendeld,        setVergrendeld]         = useState({});
  const [resetConfirm,       setResetConfirm]        = useState(null);
  const [geselecteerdFeature,setGeselecteerdFeature] = useState(null);
  const [actieveTab,         setActieveTab]           = useState("lagen"); // "lagen" | "docs"
  const [zipData,            setZipData]              = useState(null);
  const [isKaartLaden,       setIsKaartLaden]         = useState(false);
  const [kaartLaadBericht,   setKaartLaadBericht]     = useState("Laden…");
  const [kaartBox,           setKaartBox]             = useState(opgeslagenInst.__kaartBox??null);
  const [tekenModus,         setTekenModus]           = useState(false);
  const tekenModusRef  = useRef(false);
  const boxRectRef     = useRef(null);   // L.Rectangle op de kaart
  const boxStartRef    = useRef(null);   // eerste hoekpunt tijdens tekenen
  const appMarkersRef  = useRef([]);     // Appurtenance circleMarkers voor filterbox

  featureKlikRef.current = setGeselecteerdFeature;
  tekenModusRef.current  = tekenModus;

  // ── Kaart init ────────────────────────────────────────────────
  useEffect(()=>{
    let actief=true;
    (async()=>{
      if(typeof window==="undefined"||!mapElRef.current||kaartRef.current)return;
      const L=await laadLeaflet();
      if(!L||!actief||!mapElRef.current)return;
      await laadProj4();
      LRef.current=L;
      delete L.Icon.Default.prototype._getIconUrl;
      L.Icon.Default.mergeOptions({iconRetinaUrl:"https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon-2x.png",iconUrl:"https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon.png",shadowUrl:"https://unpkg.com/leaflet@1.9.4/dist/images/marker-shadow.png"});
      const rdCrs=maakRdCrs(L);
      const pos=opgeslagenInst.__kaartPositie;
      const _ls=mapLS3();
      const initCenter=_ls?.c??(pos?.lat?[pos.lat,pos.lng]:[52.156,5.387]);
      const initZoom=_ls?.z??pos?.zoom??8;
      const kaart=L.map(mapElRef.current,{crs:rdCrs,zoomControl:true,preferCanvas:true,maxZoom:22}).setView(initCenter,initZoom);
      kaartRef.current=kaart;
      kaart.on("moveend zoomend",()=>{const c=kaart.getCenter();mapSave3({z:kaart.getZoom(),c:[c.lat,c.lng]});});

      // Cursor RD coords
      kaart.on("mousemove",e=>{
        try{if(window.proj4){const rd=proj4("EPSG:4326","EPSG:28992",[e.latlng.lng,e.latlng.lat]);setRdCursor({x:Math.round(rd[0]),y:Math.round(rd[1])});}}catch{}
        // Live box preview tijdens tekenen
        if(tekenModusRef.current&&boxStartRef.current){
          if(boxRectRef.current)kaart.removeLayer(boxRectRef.current);
          boxRectRef.current=L.rectangle([boxStartRef.current,e.latlng],{color:"#6b7280",weight:1.5,fillColor:"transparent",fillOpacity:0,dashArray:"6 3"});
          boxRectRef.current.addTo(kaart);
        }
      });
      kaart.on("mouseout",()=>setRdCursor(null));
      kaart.on("click",(e)=>{
        if(tekenModusRef.current){
          if(!boxStartRef.current){
            // Eerste klik: startpunt
            boxStartRef.current=e.latlng;
          } else {
            // Tweede klik: box afronden
            const b=L.latLngBounds(boxStartRef.current,e.latlng);
            const box={lat1:b.getSouth(),lng1:b.getWest(),lat2:b.getNorth(),lng2:b.getEast()};
            boxStartRef.current=null;
            setTekenModus(false);
            tekenModusRef.current=false;
            setKaartBox(box);
            kaart.getContainer().style.cursor="";
            // Teken definitieve box
            if(boxRectRef.current)kaart.removeLayer(boxRectRef.current);
            boxRectRef.current=L.rectangle([[box.lat1,box.lng1],[box.lat2,box.lng2]],{color:"#6b7280",weight:2,fillColor:"transparent",fillOpacity:0,dashArray:null});
            boxRectRef.current.addTo(kaart);
            // Filter toepassen via ref zodat we huidige state hebben
            setTimeout(()=>pasBoxFilterToeRef.current?.(box),100);
          }
        } else {
          featureKlikRef.current?.(null);
        }
      });

      // Achtergrond functions
      function voegAchtergrondToe(id){
        if(basisLaagRef.current){try{kaart.removeLayer(basisLaagRef.current);}catch{}basisLaagRef.current=null;}
        const cfg=ACHTERGROND.find(a=>a.id===id)??ACHTERGROND[0];
        if(cfg.groep==="Esri NL"){
          // Esri NL via ArcGIS REST /export — perfecte RD-bounding-box per tile, geen tile-schema conflict
          try{
            const laag = maakEsriExportLayer(L, cfg.url, cfg.opties?.attribution);
            laag.addTo(kaart);
            basisLaagRef.current = laag;
          }catch(e){
            console.warn("Esri export layer mislukt, fallback naar BRT:",e);
            const fb=L.tileLayer(ACHTERGROND[0].url,{...ACHTERGROND[0].opties,zIndex:1});
            fb.addTo(kaart);basisLaagRef.current=fb;
          }
        } else if(cfg.wms){
          const laag=L.tileLayer.wms(cfg.url,{layers:cfg.layers,...cfg.opties,zIndex:1});
          laag.addTo(kaart);basisLaagRef.current=laag;
        } else {
          const laag=L.tileLayer(cfg.url,{...cfg.opties,zIndex:1});
          laag.addTo(kaart);basisLaagRef.current=laag;
        }
      }
      function voegOverlayToe(id){
        if(overlayRefs.current[id])return;
        const c=OVERLAYS.find(o=>o.id===id);if(!c)return;
        const laag=L.tileLayer.wms(c.url,{layers:c.layers,format:"image/png",transparent:true,opacity:0.8,attribution:"© PDOK",zIndex:200});
        laag.addTo(kaart);overlayRefs.current[id]=laag;
      }
      function verwijderOverlay(id){if(overlayRefs.current[id]){kaart.removeLayer(overlayRefs.current[id]);delete overlayRefs.current[id];}}
      kaart._voegAchtergrondToe=voegAchtergrondToe;
      kaart._voegOverlayToe=voegOverlayToe;
      kaart._verwijderOverlay=verwijderOverlay;
      voegAchtergrondToe(opgeslagenInst.__achtergrond??"brt_standaard");
      // Gebruik ook localStorage-waarden zodat UI-state en kaart-state synchroon zijn
      const _initOvIds = _ls3?.ov ?? opgeslagenInst.__overlays ?? [];
      for(const id of _initOvIds) voegOverlayToe(id);

      // Herstel opgeslagen filterbox op de kaart
      if(opgeslagenInst.__kaartBox){
        const b=opgeslagenInst.__kaartBox;
        boxRectRef.current=L.rectangle([[b.lat1,b.lng1],[b.lat2,b.lng2]],{
          color:"#6b7280",weight:2,fillColor:"transparent",fillOpacity:0,dashArray:null
        });
        boxRectRef.current.addTo(kaart);
        // Filter toepassen na laden van lagen
        setTimeout(()=>pasBoxFilterToeRef.current?.(b),1500);
      }

      // Laad bestanden
      const instSnap={...initInst};
      let eersteGeladen=false;
      for(const bestand of bestanden){
        const ext=bestand.naam.split(".").pop().toLowerCase();
        const inst=instSnap[bestand.id]??standaardInst(bestand.type);
        if(ext==="zip"){await laadKlicBestand(bestand,inst,instSnap);}
        else{
          const laag=await laadEnkelBestand(bestand,inst);
          if(laag){if(inst.zichtbaar)laag.addTo(kaart);lagenRef.current[bestand.id]=laag;if(!eersteGeladen){try{kaart.fitBounds(laag.getBounds().pad(0.1));eersteGeladen=true;}catch{}}}
        }
      }
    })();
    return()=>{actief=false;if(kaartRef.current){kaartRef.current.remove();kaartRef.current=null;}};
  },[]);

  // ── KLIC ZIP laden + parsen (met sessionStorage cache) ──────────
  async function laadKlicBestand(bestand,inst,instSnap){
    if(!bestand.url)return;

    // ─ Cache check ─────────────────────────────────────────────────
    // Sla geparsede IMKL data op in sessionStorage zodat het maar 1x hoeft
    const cacheSleutel=`klic_parsed_${bestand.id}`;
    let lagen=null, docs=[], appurtenances=[];
    let zipRef=null; // houd zip object voor PDF extractie

    const gecached=sessionStorage.getItem(cacheSleutel);
    if(gecached){
      try{
        const parsed=JSON.parse(gecached);
        lagen=parsed.lagen;appurtenances=parsed.appurtenances??[];
        docs=parsed.docs??[];
        setBestandStatus(s=>({...s,[bestand.id]:"Uit cache laden…"}));
        setKaartLaadBericht("KLIC uit cache…");
      }catch{lagen=null;}
    }

    if(!lagen){
      // Eerste keer: download + parse
      setBestandStatus(s=>({...s,[bestand.id]:"ZIP ophalen…"}));
      setIsKaartLaden(true);setKaartLaadBericht("ZIP ophalen…");
      const res=await fetch(bestand.url);if(!res.ok)throw new Error(`HTTP ${res.status}`);
      const blob=await res.blob();
      setBestandStatus(s=>({...s,[bestand.id]:"Uitpakken…"}));
      const JSZip=await laadJSZip();
      const zip=await JSZip.loadAsync(blob);
      zipRef=zip;
      setZipData(zip);
      const xmlNaam=Object.keys(zip.files).find(n=>n.includes("GI_gebiedsinformatie")&&n.endsWith(".xml"));
      if(!xmlNaam)throw new Error("Geen IMKL XML in ZIP");
      setBestandStatus(s=>({...s,[bestand.id]:"IMKL parsen…"}));
      setKaartLaadBericht("IMKL parsen…");
      const xmlTekst=await zip.files[xmlNaam].async("string");
      const parsed=parseImkl(xmlTekst,msg=>{setBestandStatus(s=>({...s,[bestand.id]:msg}));setKaartLaadBericht(msg);});
      lagen=parsed.lagen;appurtenances=parsed.appurtenances;
      docs=parsed.documenten;

      // Sla op in sessionStorage (lagen + appurtenances + doc metadata, geen blob URLs)
      try{
        sessionStorage.setItem(cacheSleutel,JSON.stringify({
          lagen,appurtenances,
          docs:docs.map(({blobUrl,...rest})=>rest), // blobUrls zijn ephemeral, niet opslaan
        }));
      }catch(e){console.warn("sessionStorage vol:",e);}
    }

    setKlicLagen(lagen);

    // Documenten: haal PDF blob URLs op uit ZIP (ook na cache hit)
    // ZIP opnieuw ophalen als we vanuit cache laadden
    const docList=[];
    try{
      let zip=zipRef;
      if(!zip){
        setBestandStatus(s=>({...s,[bestand.id]:"PDFs laden…"}));
        const JSZip=await laadJSZip();
        const res2=await fetch(bestand.url);
        zip=await JSZip.loadAsync(await res2.blob());
        setZipData(zip);
      }
      for(const doc of docs){
        const zipPad=Object.keys(zip.files).find(p=>p.includes(doc.pad.split("/").pop()));
        if(zipPad&&!zip.files[zipPad].dir){
          try{
            const data=await zip.files[zipPad].async("uint8array");
            const blobUrl=URL.createObjectURL(new Blob([data],{type:"application/pdf"}));
            docList.push({...doc,blobUrl,grootte:data.length});
          }catch{}
        }
      }
      const liPad=Object.keys(zip.files).find(p=>p.includes("LI_")&&p.endsWith(".pdf"));
      if(liPad){try{const data=await zip.files[liPad].async("uint8array");const blobUrl=URL.createObjectURL(new Blob([data],{type:"application/pdf"}));docList.unshift({naam:liPad.split("/").pop(),type:"leveringsbrief",thema:"algemeen",broncode:"",blobUrl,grootte:data.length});}catch{}}
    }catch(docErr){console.warn("PDF laden:",docErr);}
    setDocumenten(docList);

    // Render lagen op kaart
    try{
      const L=LRef.current;const kaart=kaartRef.current;if(!L||!kaart)return;
      const nieuweInst={...instellingen};
      let eersteGeladen=false;
      for(const[thema,geoJson]of Object.entries(lagen)){
        const lagId=`klic_${thema}`;
        const inst2=instSnap[lagId]??standaardThemaInst(thema);
        nieuweInst[lagId]=inst2;
        const SPEC_DASH={mantelbuis:"10 5",kabelbed:"12 4",duct:"8 4"};
        const dashArray=SPEC_DASH[thema]??null;
        const laag=L.geoJSON(geoJson,{
          coordsToLatLng:rdNaarLatLng(L),
          style:()=>({color:inst2.kleur,weight:inst2.dikte,opacity:inst2.helderheid,fillOpacity:inst2.helderheid*0.2,...(dashArray?{dashArray}:{})}),
          onEachFeature:(feature,layer)=>{
            try{
              const rdC=feature.geometry?.coordinates??[];
              layer._origWgs84=rdC.map(([x,y])=>{const[lng,lat]=rdNaarWgs84(x,y);return[lat,lng];});
            }catch{}
            layer.on("click",(e)=>{L.DomEvent.stopPropagation(e);featureKlikRef.current?.(feature.properties);});
            layer.on("mouseover",()=>{layer.setStyle({weight:(inst2.dikte+2),opacity:1});});
            layer.on("mouseout",()=>{layer.setStyle({weight:inst2.dikte,opacity:inst2.helderheid});});
          }
        });
        if(inst2.zichtbaar)laag.addTo(kaart);
        lagenRef.current[lagId]=laag;
        if(!eersteGeladen){try{kaart.fitBounds(laag.getBounds().pad(0.05));eersteGeladen=true;}catch{}}
      }
      appMarkersRef.current.forEach(m=>{try{kaart.removeLayer(m);}catch{}});
      appMarkersRef.current=[];
      appurtenances.forEach(app=>{
        try{
          const ll=rdNaarLatLng(L)(app.coords);
          const kleur=THEMA[app.properties.thema]?.kleur??"#888";
          const marker=L.circleMarker(ll,{radius:4,color:kleur,fillColor:kleur,fillOpacity:0.9,weight:1.5,zIndex:250});
          marker._appLatLng={lat:ll.lat,lng:ll.lng};
          marker.on("click",(e)=>{L.DomEvent.stopPropagation(e);featureKlikRef.current?.(app.properties);});
          marker.addTo(kaart);
          appMarkersRef.current.push(marker);
        }catch{}
      });
      setInstellingen(nieuweInst);
      const totaal=Object.values(lagen).reduce((s,g)=>s+g.features.length,0);
      setBestandStatus(s=>({...s,[bestand.id]:`✓ ${totaal} objecten · ${Object.keys(lagen).length} lagen`}));
    }catch(err){console.error("KLIC render:",err);setBestandStatus(s=>({...s,[bestand.id]:`✗ ${err.message}`}));}
    finally{setIsKaartLaden(false);}
  }

  // ── Enkel DXF/GML bestand ────────────────────────────────────
  async function laadEnkelBestand(bestand,inst){
    if(!bestand.url)return null;
    setBestandStatus(s=>({...s,[bestand.id]:"Laden…"}));
    try{
      const res=await fetch(bestand.url);if(!res.ok)throw new Error(`HTTP ${res.status}`);
      const ext=bestand.naam.split(".").pop().toLowerCase();
      let geoJson=null;
      if(ext==="dxf") geoJson=parseDxf(await res.text());
      else if(ext==="gml"||ext==="xml") geoJson=parseGml(await res.text());
      else if(ext==="geojson"||ext==="json") geoJson=await res.json();
      if(!geoJson?.features?.length){setBestandStatus(s=>({...s,[bestand.id]:"Geen geometrieën"}));return null;}
      const L=LRef.current;if(!L)return null;
      const laag=L.geoJSON(geoJson,{
        coordsToLatLng:rdNaarLatLng(L),
        style:()=>({color:inst.kleur,weight:inst.dikte,opacity:inst.helderheid,fillOpacity:inst.helderheid*0.2}),
        onEachFeature:(feature,layer)=>{
          try{const rdC=feature.geometry?.coordinates??[];layer._origWgs84=rdC.map(([x,y])=>{const[lng,lat]=rdNaarWgs84(x,y);return[lat,lng];});}catch{}
          layer.on("click",(e)=>{L.DomEvent.stopPropagation(e);featureKlikRef.current?.({...feature.properties,featuretype:bestand.type,naam:bestand.naam});});
        }
      });
      setBestandStatus(s=>({...s,[bestand.id]:`✓ ${geoJson.features.length} objecten`}));
      return laag;
    }catch(err){setBestandStatus(s=>({...s,[bestand.id]:`✗ ${err.message}`}));return null;}
  }

  function parseDxf(tekst){
    try{const features=[];const regels=tekst.split(/\r?\n/);let i=0;
    while(i<regels.length){if(regels[i].trim()!=="0"){i++;continue;}const type=regels[i+1]?.trim();let einde=i+2;while(einde<regels.length&&regels[einde].trim()!=="0")einde++;
    const blok=regels.slice(i,einde);const getW=c=>{for(let k=0;k<blok.length-1;k++)if(blok[k].trim()===String(c))return parseFloat(blok[k+1].trim())||0;return 0;};
    try{if(type==="LINE")features.push({type:"Feature",geometry:{type:"LineString",coordinates:[[getW(10),getW(20)],[getW(11),getW(21)]]},properties:{}});
    else if(type==="LWPOLYLINE"||type==="POLYLINE"){const coords=[];for(let k=0;k<blok.length-3;k++)if(blok[k].trim()==="10"&&blok[k+2]?.trim()==="20"){const x=parseFloat(blok[k+1].trim()),y=parseFloat(blok[k+3].trim());if(!isNaN(x)&&!isNaN(y))coords.push([x,y]);}if(coords.length>=2)features.push({type:"Feature",geometry:{type:"LineString",coordinates:coords},properties:{}});}}catch{}
    i=einde;}return{type:"FeatureCollection",features};}catch{return null;}}

  function parseGml(tekst){
    try{const doc=new DOMParser().parseFromString(tekst,"text/xml");const features=[];
    doc.querySelectorAll("LineString, gml\\:LineString").forEach(el=>{const raw=(el.querySelector("posList, gml\\:posList")||el.querySelector("coordinates"))?.textContent?.trim();if(!raw)return;const nums=raw.split(/[\s,]+/).map(Number).filter(n=>!isNaN(n));const coords=[];for(let i=0;i<nums.length-1;i+=2)coords.push([nums[i],nums[i+1]]);if(coords.length>=2)features.push({type:"Feature",geometry:{type:"LineString",coordinates:coords},properties:{}});});
    return{type:"FeatureCollection",features};}catch{return null;}}

  // ── Laag instelling wijzigen ──────────────────────────────────
  function wijzig(lagId,sleutel,waarde){
    setInstellingen(prev=>{
      const isK=lagId.startsWith("klic_");
      const huidig=prev[lagId]??(isK?standaardThemaInst(lagId.replace("klic_","")):{});
      const nieuw={...prev,[lagId]:{...huidig,[sleutel]:waarde}};
      const kaart=kaartRef.current;const laag=lagenRef.current[lagId];
      if(kaart&&laag){
        if(sleutel==="zichtbaar"){if(waarde){if(!kaart.hasLayer(laag))kaart.addLayer(laag);}else{if(kaart.hasLayer(laag))kaart.removeLayer(laag);}}
        else{const i=nieuw[lagId];laag.setStyle({color:i.kleur,weight:i.dikte,opacity:i.helderheid,fillOpacity:i.helderheid*0.2});}
      }
      return nieuw;
    });
  }

  function wisselAchtergrond(id){setActieveAchtergrond(id);kaartRef.current?._voegAchtergrondToe?.(id);}
  function toggleOverlay(id){setActieveOverlays(prev=>{const aan=prev.includes(id);if(aan){kaartRef.current?._verwijderOverlay?.(id);return prev.filter(o=>o!==id);}else{kaartRef.current?._voegOverlayToe?.(id);return[...prev,id];}});}

  function voerResetUit(lagId){
    const isK=lagId.startsWith("klic_");const thema=lagId.replace("klic_","");
    const std=isK?standaardThemaInst(thema):standaardInst(bestanden.find(b=>b.id===lagId)?.type??"");
    setInstellingen(prev=>({...prev,[lagId]:{...(prev[lagId]??std),...std}}));
    const laag=lagenRef.current[lagId];if(laag)laag.setStyle({color:std.kleur,weight:std.dikte,opacity:std.helderheid,fillOpacity:std.helderheid*0.2});
    setResetConfirm(null);
  }

  // ── Box filter met nauwkeurige lijnclipping (Liang-Barsky) ─────
  // Alias zodat pasBoxFilterToe clipeerLijn kan aanroepen
  function clipeerLijn(latLngs, box) {
    return clipPolylineNaarBox(latLngs, box);
  }

  const pasBoxFilterToeRef = useRef(null);
  const extraClipLagenRef  = useRef({}); // lagId → [extra L.polyline instances voor multi-segment clips]

  function pasBoxFilterToe(box) {
    const L=LRef.current; const kaart=kaartRef.current; if(!L||!kaart)return;

    Object.entries(lagenRef.current).forEach(([lagId,laag])=>{
      const inst=instellingen[lagId]??{};
      if(!laag?.eachLayer)return;

      // Verwijder vorige extra clip-lagen
      (extraClipLagenRef.current[lagId]||[]).forEach(l=>{try{kaart.removeLayer(l);}catch{}});
      extraClipLagenRef.current[lagId]=[];

      laag.eachLayer(fl=>{
        const h=inst.helderheid??0.85;
        if(!inst.zichtbaar){fl.setStyle?.({opacity:0,fillOpacity:0});return;}

        if(!box){
          // Geen filterbox → herstel originele coördinaten
          if(fl._origWgs84&&fl._origWgs84.length>=2){
            try{fl.setLatLngs(fl._origWgs84);}catch{}
          }
          fl.setStyle?.({opacity:h,fillOpacity:h*0.2});
          return;
        }

        // Point markers: simpel inside/outside
        if(fl.getLatLng){
          const ll=fl.getLatLng();
          const inBox=ll.lat>=box.lat1&&ll.lat<=box.lat2&&ll.lng>=box.lng1&&ll.lng<=box.lng2;
          fl.setStyle?.({opacity:inBox?h:0,fillOpacity:inBox?h:0});
          return;
        }

        // Lijnen: clip met Cohen-Sutherland
        const orig=fl._origWgs84;
        if(!orig||orig.length<2){fl.setStyle?.({opacity:0,fillOpacity:0});return;}

        const segments=clipeerLijn(orig,box);

        if(segments.length===0){
          // Volledig buiten box
          fl.setStyle?.({opacity:0,fillOpacity:0});
        } else {
          // Eerste segment op bestaande feature laag
          try{fl.setLatLngs(segments[0]);}catch{}
          fl.setStyle?.({opacity:h,fillOpacity:h*0.2});
          // Extra segmenten (lijn snijdt box-rand meerdere keren) als losse polylines
          for(let s=1;s<segments.length;s++){
            if(segments[s].length<2)continue;
            const extra=L.polyline(segments[s],{
              color:inst.kleur??"#888",
              weight:inst.dikte??2,
              opacity:h,
              fillOpacity:h*0.2,
              interactive:false,
            }).addTo(kaart);
            if(!extraClipLagenRef.current[lagId])extraClipLagenRef.current[lagId]=[];extraClipLagenRef.current[lagId].push(extra);
          }
        }
      });
    });
    // Appurtenance markers (los van geoJSON lagen)
    appMarkersRef.current.forEach(m=>{
      const ll=m._appLatLng??m.getLatLng?.();
      if(!ll)return;
      if(!box){m.setStyle?.({opacity:0.9,fillOpacity:0.9});return;}
      const inBox=ll.lat>=box.lat1&&ll.lat<=box.lat2&&ll.lng>=box.lng1&&ll.lng<=box.lng2;
      m.setStyle?.({opacity:inBox?0.9:0,fillOpacity:inBox?0.9:0});
    });
  }
  pasBoxFilterToeRef.current=pasBoxFilterToe;

  function resetBox(){
    setKaartBox(null);
    if(boxRectRef.current&&kaartRef.current){kaartRef.current.removeLayer(boxRectRef.current);boxRectRef.current=null;}
    pasBoxFilterToe(null);
  }

  function startTekenBox(){
    setTekenModus(true);tekenModusRef.current=true;
    boxStartRef.current=null;
    if(kaartRef.current)kaartRef.current.getContainer().style.cursor="crosshair";
    if(boxRectRef.current&&kaartRef.current){kaartRef.current.removeLayer(boxRectRef.current);boxRectRef.current=null;}
    setKaartBox(null);
  }

  async function handleOpslaan(){
    setOpslaanActief(true);setFoutmelding(null);
    try{
      const t={...instellingen};
      if(kaartRef.current){const c=kaartRef.current.getCenter();t.__kaartPositie={lat:c.lat,lng:c.lng,zoom:kaartRef.current.getZoom()};}
      t.__achtergrond=actieveAchtergrond;t.__overlays=actieveOverlays;
      if(kaartBox)t.__kaartBox=kaartBox;
      await updateProject(projectId,{laag_instellingen:JSON.stringify(t)});
      onOpgeslagen?.();setIngeslagen(true);setTimeout(()=>setIngeslagen(false),2500);
    }catch(err){console.error(err);setFoutmelding(err?.message??"Onbekende fout");setTimeout(()=>setFoutmelding(null),6000);}
    finally{setOpslaanActief(false);}
  }

  // ── Sub-components ────────────────────────────────────────────
  function Toggle({lagId,inst}){return(<button onClick={()=>wijzig(lagId,"zichtbaar",!inst.zichtbaar)} className={`relative inline-flex flex-shrink-0 rounded-full transition-colors ${inst.zichtbaar?"bg-[#007A5A]":"bg-gray-200"}`} style={{width:36,height:20}}><span className={`inline-block w-4 h-4 bg-white rounded-full shadow transform transition-transform ${inst.zichtbaar?"translate-x-4":"translate-x-0.5"}`} style={{marginTop:2}}/></button>);}

  function SlotIcoon({lagId}){const isOpen=vergrendeld[lagId]===true;return(<button onClick={()=>setVergrendeld(v=>({...v,[lagId]:!isOpen}))} title={isOpen?"Vergrendelen":"Bewerken"} className={`w-6 h-6 flex items-center justify-center rounded transition-colors flex-shrink-0 ${isOpen?"text-[#007A5A] hover:text-[#007A5A]":"text-gray-300 hover:text-gray-500 hover:bg-gray-100"}`}>{isOpen?(<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round"><rect x="3" y="11" width="18" height="11" rx="2"/><path d="M7 11V7a5 5 0 0 1 9.9-1"/></svg>):(<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round"><rect x="3" y="11" width="18" height="11" rx="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/></svg>)}</button>);}

  function ResetKnop({lagId}){return(<button onClick={()=>setResetConfirm(lagId)} title="Terugzetten naar standaard" className="w-6 h-6 flex items-center justify-center rounded text-gray-300 hover:text-[#007A5A] hover:bg-[#F0FAF5] flex-shrink-0 transition-colors"><svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round"><path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8"/><path d="M3 3v5h5"/></svg></button>);}

  function ResetBevestiging({lagId}){
    if(resetConfirm!==lagId)return null;
    const isK=lagId.startsWith("klic_");const thema=lagId.replace("klic_","");
    const std=isK?standaardThemaInst(thema):standaardInst(bestanden.find(b=>b.id===lagId)?.type??"");
    return(<div className="mt-1.5 flex items-center gap-2 bg-[#E5F3EC] border border-[#007A5A]/20 rounded-lg px-3 py-2"><div className="w-3 h-3 rounded-full flex-shrink-0" style={{background:std.kleur}}/><span className="text-xs text-[#007A5A] flex-1">Terugzetten naar standaard?</span><button onClick={()=>setResetConfirm(null)} className="text-xs text-gray-400 hover:text-gray-600 px-1.5 py-0.5 rounded hover:bg-white transition-colors">Nee</button><button onClick={()=>voerResetUit(lagId)} className="text-xs text-white bg-[#007A5A] hover:bg-[#00915F] px-2 py-0.5 rounded transition-colors font-medium">Ja</button></div>);
  }

  function LaagControls({lagId,inst}){
    if(vergrendeld[lagId]!==true)return null;
    return(<div className={`mt-2 space-y-2 px-2 py-2 bg-gray-50 rounded-lg ${!inst.zichtbaar?"opacity-40 pointer-events-none":""}`}>
      <div className="flex items-center gap-2"><span className="text-xs text-gray-500 w-16 flex-shrink-0">Kleur</span><input type="color" value={inst.kleur} onChange={e=>wijzig(lagId,"kleur",e.target.value)} className="w-7 h-5 rounded cursor-pointer border-0 p-0 flex-shrink-0"/><span className="text-xs text-gray-400 font-mono">{inst.kleur}</span></div>
      <div className="flex items-center gap-2"><span className="text-xs text-gray-500 w-16 flex-shrink-0">Dikte</span><input type="range" min="0.5" max="8" step="0.5" value={inst.dikte} onChange={e=>wijzig(lagId,"dikte",Number(e.target.value))} className="flex-1 accent-[#007A5A] h-1 min-w-0"/><span className="text-xs text-gray-400 w-8 text-right flex-shrink-0">{inst.dikte}px</span></div>
      <div className="flex items-center gap-2"><span className="text-xs text-gray-500 w-16 flex-shrink-0">Helderheid</span><input type="range" min="0.1" max="1" step="0.05" value={inst.helderheid} onChange={e=>wijzig(lagId,"helderheid",Number(e.target.value))} className="flex-1 accent-[#007A5A] h-1 min-w-0"/><span className="text-xs text-gray-400 w-8 text-right flex-shrink-0">{Math.round(inst.helderheid*100)}%</span></div>
    </div>);
  }

  // ── Feature detail panel ──────────────────────────────────────
  function FeatureDetail(){
    if(!geselecteerdFeature)return null;
    const f=geselecteerdFeature;
    const kleur=THEMA[f.thema]?.kleur??"#888";
    const themaLabel=THEMA[f.thema]?.label??f.thema??"—";
    const VELD_LABELS={featuretype:"Featuretype",thema:"Thema",netbeheerderNaam:"Netbeheerder",broncode:"Bronhoudercode",currentStatus:"Huidige status",validFrom:"Aanlegdatum",verticalPosition:"Verticale positie",operatingVoltage:"Bedrijfsspanning",nominalVoltage:"Nominale spanning",pipeDiameter:"Diameter",waterType:"Watertype",label:"Label",utilityDeliveryType:"Leveringsnetwerk",warningType:"Waarschuwing",geoNauwkeurigheidXY:"Geo. nauwkeurigheid",appurtenanceType:"Type appendage"};
    const SKIP=new Set(["broncode","inNetwork","link","beginLifespanVersion","inspireId"]);
    const velden=Object.entries(f).filter(([k,v])=>!SKIP.has(k)&&v&&v!=="—"&&v!=="[]");
    return(
      <div className="border-t border-gray-100 bg-white flex-shrink-0" style={{maxHeight:240,overflowY:"auto"}}>
        <div className="flex items-center justify-between px-4 py-2 border-b border-gray-100 sticky top-0 bg-white">
          <div className="flex items-center gap-2">
            <div className="w-3 h-3 rounded-full flex-shrink-0" style={{background:kleur}}/>
            <span className="text-xs font-semibold text-gray-800">{f.featuretype||"Leiding"}</span>
            <span className="text-xs text-gray-400">{themaLabel}</span>
          </div>
          <button onClick={()=>setGeselecteerdFeature(null)} className="text-gray-400 hover:text-gray-600 text-sm">×</button>
        </div>
        <div className="divide-y divide-gray-50">
          {velden.map(([k,v])=>(
            <div key={k} className="flex items-start justify-between gap-4 px-4 py-1.5">
              <span className="text-xs text-gray-400 flex-shrink-0 w-36">{VELD_LABELS[k]??k}</span>
              <span className="text-xs text-gray-800 text-right break-all">{String(v).includes("V")&&k.includes("oltage")?`${v} V`:String(v).includes("mm")&&k==="pipeDiameter"?`${v} mm`:v}</span>
            </div>
          ))}
        </div>
      </div>
    );
  }

  // ── Documenten panel ─────────────────────────────────────────
  function DocumentenLijst(){
    if(documenten.length===0)return(<div className="p-6 text-center space-y-2"><div className="text-2xl">📄</div><p className="text-xs text-gray-400">Geen documenten gevonden.<br/>Upload een KLIC ZIP in stap 2.</p></div>);
    const DOC_TYPE_LABELS={profielschets:"Profielschets",leveringsbrief:"Leveringsbrief",algemeen:"Algemeen",boring:"Boring",waterkruisingen:"Waterkruisingen",bijlage:"Bijlage"};
    const gegroepeerd={};
    for(const doc of documenten){const k=doc.broncode||"algemeen";if(!gegroepeerd[k])gegroepeerd[k]=[];gegroepeerd[k].push(doc);}
    return(
      <div className="divide-y divide-gray-100">
        {Object.entries(gegroepeerd).map(([code,docs])=>(
          <div key={code}>
            <div className="px-4 py-2 bg-gray-50">
              <span className="text-xs font-semibold text-gray-600">{code||"Algemeen"}</span>
            </div>
            {docs.map((doc,i)=>(
              <a key={i} href={doc.blobUrl} target="_blank" rel="noopener noreferrer" className="flex items-center gap-3 px-4 py-2.5 hover:bg-blue-50 transition-colors group">
                <div className="w-7 h-7 rounded bg-red-100 flex items-center justify-center flex-shrink-0">
                  <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="#dc2626" strokeWidth="2" strokeLinecap="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>
                </div>
                <div className="flex-1 min-w-0">
                  <div className="text-xs font-medium text-gray-700 group-hover:text-[#007A5A] truncate">{DOC_TYPE_LABELS[doc.type]||doc.type}</div>
                  <div className="text-xs text-gray-400 truncate">{doc.naam}</div>
                </div>
                <div className="text-xs text-gray-300 flex-shrink-0">{doc.grootte?(doc.grootte/1024).toFixed(0)+" KB":""}</div>
                <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="#9ca3af" strokeWidth="2" strokeLinecap="round" className="flex-shrink-0"><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/><polyline points="15 3 21 3 21 9"/><line x1="10" y1="14" x2="21" y2="3"/></svg>
              </a>
            ))}
          </div>
        ))}
      </div>
    );
  }

  const gewoneLagen   = bestanden.filter(b=>!b.naam.toLowerCase().endsWith(".zip"));
  const klicBestanden = bestanden.filter(b=>b.naam.toLowerCase().endsWith(".zip"));
  const klicThemas    = Object.keys(klicLagen).length>0?Object.keys(klicLagen):Object.keys(instellingen).filter(k=>k.startsWith("klic_")).map(k=>k.replace("klic_",""));

  const kaartHoogte = geselecteerdFeature ? "calc(100vh - 168px - 240px)" : "calc(100vh - 168px)";

  return(
    <div className="flex gap-4 min-h-0" style={{height:"calc(100vh - 168px)"}}>

      {/* ── Lagenpaneel ─────────────────────────────────────── */}
      <div className="flex-shrink-0 bg-white border border-[#DEE6EA] rounded-xl flex flex-col overflow-hidden" style={{width:300}}>

        {/* ── Header rij 1: tabs + opslaan ── */}
        <div className="flex items-center gap-2 px-3 py-2.5 border-b border-[#DEE6EA] flex-shrink-0">
          <div className="flex gap-0.5 bg-[#F5F7F9] rounded-lg p-0.5">
            {["lagen","docs"].map(t=>(
              <button key={t} onClick={()=>setActieveTab(t)}
                className={`px-3 py-1 text-xs rounded-md font-medium transition-all ${
                  actieveTab===t ? "bg-white text-[#1B2B35] shadow-sm" : "text-[#8FA6B2] hover:text-[#587080]"
                }`}>
                {t==="lagen"?"Lagen":"Docs"}{t==="docs"&&documenten.length>0?` (${documenten.length})`:""}
              </button>
            ))}
          </div>
          <div className="flex-1"/>
          {!tekenModus&&!kaartBox&&(
            <button onClick={startTekenBox} title="Teken filterbox"
              className="flex items-center gap-1 px-2 py-1 text-xs border border-[#DEE6EA] rounded-lg hover:bg-[#E5F3EC] hover:border-[#007A5A] text-[#8FA6B2] hover:text-[#007A5A] transition-colors font-medium">
              <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round"><rect x="3" y="3" width="18" height="18" rx="2"/></svg>
              Filter
            </button>
          )}
          {tekenModus&&(
            <span className="px-2 py-1 text-xs bg-[#E5F3EC] text-[#007A5A] rounded-lg font-medium animate-pulse">
              {boxStartRef.current?"2e hoek…":"1e hoek…"}
            </span>
          )}
          {kaartBox&&!tekenModus&&(
            <div className="flex gap-1">
              <button onClick={startTekenBox}
                className="flex items-center gap-1 px-2 py-1 text-xs border border-[#007A5A]/30 text-[#007A5A] rounded-lg hover:bg-[#E5F3EC] transition-colors font-medium">
                <svg width="9" height="9" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5"><rect x="3" y="3" width="18" height="18" rx="2"/></svg>
                Opnieuw
              </button>
              <button onClick={resetBox}
                className="w-6 h-6 flex items-center justify-center text-xs border border-[#DEE6EA] text-[#8FA6B2] rounded-lg hover:bg-red-50 hover:border-red-200 hover:text-red-500 transition-colors">×</button>
            </div>
          )}
          <button onClick={handleOpslaan} disabled={opslaanActief}
            className={`flex items-center gap-1 px-2.5 py-1 text-xs rounded-lg font-semibold transition-colors flex-shrink-0 ${
              ingeslagen ? "bg-[#007A5A] text-white" : "bg-[#007A5A] text-white hover:bg-[#00915F] disabled:opacity-50"
            }`}>
            {ingeslagen?(
              <><svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3" strokeLinecap="round"><polyline points="20 6 9 17 4 12"/></svg>Opgeslagen</>
            ):(
              <><svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round"><path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/><polyline points="17 21 17 13 7 13 7 21"/><polyline points="7 3 7 8 15 8"/></svg>{opslaanActief?"Opslaan…":"Opslaan"}</>
            )}
          </button>
        </div>

        {/* Inhoud */}
        <div className="flex-1 overflow-y-auto">
          {actieveTab==="docs"?(
            <DocumentenLijst/>
          ):(
            <>
              {/* ── ACHTERGROND BOX ── */}
              <div className="m-3 mb-2 border border-[#DEE6EA] rounded-lg overflow-hidden">
                <div className="px-3 py-2 bg-[#F5F7F9] border-b border-[#DEE6EA] flex items-center justify-between">
                  <span className="text-xs font-semibold text-[#587080] uppercase tracking-wide">Achtergrond</span>
                  <div className="flex items-center gap-1.5">
                    <span className="text-[10px] text-[#8FA6B2]">EPSG:28992</span>
                    <div className="w-2 h-2 rounded-full bg-[#007A5A]"/>
                  </div>
                </div>
                <div className="p-2">
                  {["PDOK","Esri NL"].map(groep=>{
                    const lagen=ACHTERGROND.filter(a=>(a.groep??'PDOK')===groep);
                    return(
                      <div key={groep}>
                        <div className="flex items-center gap-2 px-1 pt-1.5 pb-0.5">
                          <span className="text-[10px] font-bold uppercase tracking-wider"
                            style={{color: groep==="Esri NL"?"#5B7FA6":"#8FA6B2"}}>{groep}</span>
                          {groep==="Esri NL"&&<span className="text-[9px] bg-[#EEF5FF] text-[#5B7FA6] border border-[#C5DEFF] rounded-full px-1.5 font-semibold">WMS</span>}
                          <div className="flex-1 h-px bg-[#F0F4F6]"/>
                        </div>
                        <div className="space-y-0.5">
                          {lagen.map(a=>(
                            <button key={a.id} onClick={()=>wisselAchtergrond(a.id)}
                              className={`flex items-center gap-2.5 w-full px-2.5 py-1.5 rounded-lg text-left transition-colors ${
                                actieveAchtergrond===a.id ? "bg-[#E5F3EC] text-[#007A5A]" : "text-[#1B2B35] hover:bg-[#F5F7F9]"
                              }`}>
                              <div className={`w-3.5 h-3.5 rounded-full border-2 flex-shrink-0 transition-colors ${
                                actieveAchtergrond===a.id ? "border-[#007A5A] bg-[#007A5A]" : "border-[#DEE6EA]"
                              }`}/>
                              <span className="text-xs font-medium flex-1">{a.label}</span>
                              {actieveAchtergrond===a.id&&<span className="text-[10px] text-[#007A5A] font-semibold">actief</span>}
                            </button>
                          ))}
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>

              {/* ── OVERLAYS BOX ── */}
              <div className="mx-3 mb-2 border border-[#DEE6EA] rounded-lg overflow-hidden">
                <div className="px-3 py-2 bg-[#F5F7F9] border-b border-[#DEE6EA] flex items-center justify-between">
                  <span className="text-xs font-semibold text-[#587080] uppercase tracking-wide">Overlays</span>
                  {actieveOverlays.length>0&&(
                    <span className="text-[10px] font-semibold bg-[#007A5A] text-white px-1.5 py-0.5 rounded-full">{actieveOverlays.length}</span>
                  )}
                </div>
                <div className="p-2 space-y-0.5">
                  {OVERLAYS.map(o=>{
                    const aan=actieveOverlays.includes(o.id);
                    return(
                      <button key={o.id} onClick={()=>toggleOverlay(o.id)}
                        className={`flex items-center gap-2.5 w-full px-2.5 py-1.5 rounded-lg text-left transition-colors ${aan?"bg-[#E5F3EC]":"hover:bg-[#F5F7F9]"}`}>
                        <div className="w-3.5 h-3.5 rounded flex-shrink-0 border transition-colors"
                          style={{background:aan?o.kleur:"transparent",borderColor:aan?o.kleur:"#DEE6EA"}}/>
                        <span className={`text-xs font-medium ${aan?"text-[#007A5A]":"text-[#1B2B35]"}`}>{o.label}</span>
                        {aan&&<span className="ml-auto text-[10px] text-[#007A5A] font-semibold">aan</span>}
                      </button>
                    );
                  })}
                </div>
              </div>

              {/* ── LAGEN BOX ── */}
              {(gewoneLagen.length>0||klicBestanden.length>0)&&(
                <div className="mx-3 mb-2 border border-[#DEE6EA] rounded-lg overflow-hidden">
                  <div className="px-3 py-2 bg-[#F5F7F9] border-b border-[#DEE6EA]">
                    <span className="text-xs font-semibold text-[#587080] uppercase tracking-wide">Lagen</span>
                  </div>
                  <div className="divide-y divide-[#F0F4F6]">
                    {gewoneLagen.map(b=>{const inst=instellingen[b.id]??standaardInst(b.type);return(
                      <div key={b.id} className="px-3 py-2.5 space-y-1">
                        <div className="flex items-center gap-2">
                          <div className="w-3 h-3 rounded-full flex-shrink-0 border border-white shadow-sm" style={{background:inst.kleur}}/>
                          <div className="flex-1 min-w-0">
                            <div className="text-xs font-medium text-[#1B2B35] truncate">{b.naam}</div>
                            <div className="text-[11px] text-[#8FA6B2]">{bestandStatus[b.id]||b.type}</div>
                          </div>
                          <div className="flex items-center gap-1 flex-shrink-0">
                            <ResetKnop lagId={b.id}/><SlotIcoon lagId={b.id}/><Toggle lagId={b.id} inst={inst}/>
                          </div>
                        </div>
                        <ResetBevestiging lagId={b.id}/>
                        <LaagControls lagId={b.id} inst={inst}/>
                      </div>
                    );})}
                    {klicBestanden.map(b=>(
                      <div key={b.id}>
                        <div className="px-3 py-2 bg-[#F5F7F9]">
                          <div className="flex items-center gap-2">
                            <span className="text-base leading-none">🗂️</span>
                            <div className="flex-1 min-w-0">
                              <div className="text-xs font-semibold text-[#1B2B35] truncate">{b.naam}</div>
                              <div className="text-[11px] text-[#8FA6B2]">KLIC-melding · IMKL</div>
                            </div>
                          </div>
                          {bestandStatus[b.id]&&(
                            <div className={`text-[11px] mt-1 ml-7 font-medium ${
                              bestandStatus[b.id].startsWith("✓")?"text-[#007A5A]":bestandStatus[b.id].startsWith("✗")?"text-red-500":"text-[#587080]"
                            }`}>{bestandStatus[b.id]}</div>
                          )}
                        </div>
                        {klicThemas.length>0 ? klicThemas.map(thema=>{
                          const lagId=`klic_${thema}`;
                          const inst=instellingen[lagId]??standaardThemaInst(thema);
                          const config=THEMA[thema]??{label:thema};
                          const n=klicLagen[thema]?.features?.length;
                          return(
                            <div key={lagId} className="px-3 py-2 space-y-1 border-t border-[#F0F4F6]">
                              <div className="flex items-center gap-2">
                                <div className="w-2.5 h-2.5 rounded-full flex-shrink-0 ml-2 border border-white shadow-sm" style={{background:inst.kleur}}/>
                                <div className="flex-1 min-w-0">
                                  <div className="text-xs font-medium text-[#1B2B35]">{config.label}</div>
                                  {n&&<div className="text-[11px] text-[#8FA6B2]">{n} objecten</div>}
                                </div>
                                <div className="flex items-center gap-1 flex-shrink-0">
                                  <ResetKnop lagId={lagId}/><SlotIcoon lagId={lagId}/><Toggle lagId={lagId} inst={inst}/>
                                </div>
                              </div>
                              <ResetBevestiging lagId={lagId}/>
                              <LaagControls lagId={lagId} inst={inst}/>
                            </div>
                          );
                        }) : (
                          <div className="px-4 py-3 text-xs text-[#8FA6B2] italic pl-8">
                            {bestandStatus[b.id]?"Laden…":"Geen lagen geladen"}
                          </div>
                        )}
                      </div>
                    ))}
                  </div>
                </div>
              )}

              {bestanden.length===0&&(
                <div className="p-6 text-center space-y-2">
                  <div className="text-2xl">📂</div>
                  <p className="text-sm font-medium text-[#1B2B35]">Geen bestanden</p>
                  <p className="text-xs text-[#8FA6B2]">Upload ontwerpen in stap 2.</p>
                </div>
              )}
            </>
          )}
        </div>

        {/* Footer */}
        <div className="border-t border-[#DEE6EA] px-3 py-2 space-y-1 flex-shrink-0">
          {rdCursor&&(
            <div className="text-[11px] font-mono text-[#587080] bg-[#F5F7F9] rounded px-2 py-1">
              RD X: {rdCursor.x.toLocaleString("nl-NL")} · Y: {rdCursor.y.toLocaleString("nl-NL")}
            </div>
          )}
          {foutmelding&&<p className="text-xs text-red-500 font-medium">✗ {foutmelding}</p>}
          <p className="text-[11px] text-[#8FA6B2]">EPSG:28992 · instellingen gelden ook in stap 4 t/m 8.</p>
        </div>
      </div>

      {/* ── Kaart + feature detail ───────────────────────────── */}
      <div ref={snapContainerRef} className="flex-1 flex flex-col min-w-0 bg-white border border-gray-200 rounded-xl overflow-hidden">
        {/* Kaart met overlay */}
        <div className="flex-1 min-h-0 relative" style={{minHeight:300}}>
          <div ref={mapElRef} className="w-full h-full"/>
          <SnapKnop status={snapStatus} onClick={() => maakKaartOpname(snapContainerRef.current, projectId, 3, setSnapStatus)}/>

          {/* Laadspinner overlay */}
          {isKaartLaden&&(
            <div className="absolute inset-0 bg-white/70 flex flex-col items-center justify-center z-[400] pointer-events-none">
              <div className="w-12 h-12 border-4 border-[#E5F3EC] border-t-[#007A5A] rounded-full animate-spin mb-3"/>
              <p className="text-sm text-gray-700 font-medium">{kaartLaadBericht}</p>
              <p className="text-xs text-gray-400 mt-1">Even geduld…</p>
            </div>
          )}

          {/* Tekenmodus aanwijzing op kaart */}
          {tekenModus&&(
            <div className="absolute top-3 left-1/2 -translate-x-1/2 z-[300] px-4 py-2 bg-[#007A5A] text-white rounded-xl shadow-lg text-sm font-medium pointer-events-none">
              {boxStartRef.current?"Klik voor de 2e hoek":"Klik voor de 1e hoek van de filterbox"}
            </div>
          )}
          {kaartBox&&!tekenModus&&(
            <div className="absolute top-3 left-1/2 -translate-x-1/2 z-[300] px-3 py-1.5 bg-white border border-orange-300 rounded-lg shadow text-xs text-[#007A5A] font-medium pointer-events-none">
              Filterbox actief — alleen dit gebied is zichtbaar
            </div>
          )}
        </div>
        <FeatureDetail/>
      </div>
    </div>
  );
}
