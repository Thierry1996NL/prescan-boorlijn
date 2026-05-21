"use client";
import { useEffect, useRef, useState, useMemo } from "react";

// ─── RD → WGS84 ──────────────────────────────────────────────────
function rdNaarWgs84(x, y) {
  const dX=(x-155000)/100000, dY=(y-463000)/100000;
  const lat=52.15517440+(3235.65389*dY-32.58297*dX*dX-0.24750*dY*dY-0.84978*dX*dX*dY-0.06550*dY*dY*dY)/3600;
  const lng=5.38720621+(5260.52916*dX+105.94684*dX*dY+2.45656*dX*dY*dY-0.81885*dX*dX*dX)/3600;
  return [lng, lat];
}
const NAP_OFFSET = 44.1;
function napNaarCesium(nap) { return nap + NAP_OFFSET; }

function positieOpAfstand(coords, afstand) {
  let cumul = 0;
  for (let i = 0; i < coords.length - 1; i++) {
    const [lat1,lng1]=coords[i],[lat2,lng2]=coords[i+1];
    const R=6371000, toRad=d=>d*Math.PI/180;
    const dLat=toRad(lat2-lat1), dLng=toRad(lng2-lng1);
    const a=Math.sin(dLat/2)**2+Math.cos(toRad(lat1))*Math.cos(toRad(lat2))*Math.sin(dLng/2)**2;
    const d=R*2*Math.atan2(Math.sqrt(a),Math.sqrt(1-a));
    if (cumul+d >= afstand) { const t=(afstand-cumul)/d; return [lat1+t*(lat2-lat1),lng1+t*(lng2-lng1)]; }
    cumul += d;
  }
  return coords[coords.length-1];
}
function interpoleerHoogte(pp, afstand) {
  if (!pp?.length) return 0;
  for (let i=0;i<pp.length-1;i++) {
    const a=pp[i],b=pp[i+1];
    if (afstand>=a.afstand&&afstand<=b.afstand) return a.hoogte+(afstand-a.afstand)/(b.afstand-a.afstand)*(b.hoogte-a.hoogte);
  }
  return pp[pp.length-1]?.hoogte??0;
}
function interpoleerDiepte(dp, afstand) {
  if (!dp?.length) return 0;
  const s=[...dp].sort((a,b)=>a.afstand-b.afstand);
  if (afstand<=s[0].afstand) return s[0].diepte;
  if (afstand>=s[s.length-1].afstand) return s[s.length-1].diepte;
  for (let i=0;i<s.length-1;i++) {
    if (afstand>=s[i].afstand&&afstand<=s[i+1].afstand) {
      const t=(afstand-s[i].afstand)/(s[i+1].afstand-s[i].afstand);
      return s[i].diepte+t*(s[i+1].diepte-s[i].diepte);
    }
  }
  return 0;
}
function parseImklMini(xmlTekst) {
  const doc=new DOMParser().parseFromString(xmlTekst,"text/xml");
  const netThema={};
  doc.querySelectorAll("Utiliteitsnet").forEach(net=>{
    const id=net.getAttribute("gml:id")||net.getAttributeNS?.("http://www.opengis.net/gml/3.2","id")||"";
    const href=net.querySelector("thema")?.getAttributeNS?.("http://www.w3.org/1999/xlink","href")||"";
    if(id) netThema[id]=href.split("/").pop()||"overig";
  });
  const themalagen={};
  doc.querySelectorAll("UtilityLink").forEach(link=>{
    const netHref=(link.querySelector("inNetwork")?.getAttributeNS?.("http://www.w3.org/1999/xlink","href")||"").replace(/^#/,"");
    const thema=netThema[netHref]||"overig";
    const posListEl=link.querySelector("posList");
    if(!posListEl)return;
    const nums=posListEl.textContent.trim().split(/\s+/).map(Number);
    const coords=[];
    for(let i=0;i+1<nums.length;i+=2){
      const x=nums[i],y=nums[i+1];
      const [lng,lat]=(Math.abs(x)>180||Math.abs(y)>90)?rdNaarWgs84(x,y):[x,y];
      coords.push([lng,lat]);
    }
    if(coords.length>=2){if(!themalagen[thema])themalagen[thema]=[];themalagen[thema].push(coords);}
  });
  return themalagen;
}
const THEMA_KLEUR={"laagspanning":"#7B00AA","middenspanning":"#00CCFF","hoogspanning":"#FF4400","gasLageDruk":"#FFFF00","gasHogeDruk":"#FF0000","water":"#0000CC","datatransport":"#00CC00","rioolVrijverval":"#AA00CC","rioolOnderOverOfOnderdruk":"#AA00CC","warmte":"#FF6600","overig":"#888888"};

// ════════════════════════════════════════════════════════════════════
export default function Stap8_3D({ project }) {
  const containerRef = useRef(null);
  const viewerRef    = useRef(null);

  const [ionToken, setIonToken] = useState(()=>{ try{return localStorage.getItem("cesium_ion_token")||"";}catch{return "";} });
  const [tokenInput, setTokenInput] = useState(ionToken);
  const [tokenOpgeslagen, setTokenOpgeslagen] = useState(false);
  const [status, setStatus] = useState("init");
  const [lagen, setLagen] = useState({ boorlijn:true, klic:true, machines:true, bag3d:true });
  const [bag3dStatus, setBag3dStatus] = useState(null);

  const handleTokenInput = val => {
    setTokenInput(val);
    try{localStorage.setItem("cesium_ion_token",val);}catch{}
    setTokenOpgeslagen(true);
    setTimeout(()=>setTokenOpgeslagen(false),2000);
  };

  const boorCoords = useMemo(()=>{
    try{const g=project?.boortrace_geojson;if(!g)return[];const p=typeof g==="string"?JSON.parse(g):g;return p.coordinates?.map(([lng,lat])=>[lat,lng])??[];}catch{return[];}
  },[project]);
  const ahnProfiel = useMemo(()=>{
    try{const r=project?.ahn_profiel;if(!r)return null;const p=typeof r==="string"?JSON.parse(r):r;return Array.isArray(p)?{profielPunten:p,dieptePunten:[]}:p;}catch{return null;}
  },[project]);
  const machineLocaties = useMemo(()=>{
    try{const r=project?.machine_locaties;if(!r)return null;return typeof r==="string"?JSON.parse(r):r;}catch{return null;}
  },[project]);
  const bestandenMeta = useMemo(()=>{
    try{return JSON.parse(project?.bestanden_meta||"[]");}catch{return[];}
  },[project]);

  // ── Cesium init ────────────────────────────────────────────────
  useEffect(()=>{
    if(typeof window==="undefined"||viewerRef.current||!containerRef.current)return;
    let actief=true;
    setStatus("laden");
    (async()=>{
      if(!document.querySelector('link[href*="cesium"]')){
        const l=document.createElement("link");l.rel="stylesheet";
        l.href="https://cesium.com/downloads/cesiumjs/releases/1.120/Build/Cesium/Widgets/widgets.css";
        document.head.appendChild(l);
      }
      if(!window.Cesium){
        await new Promise((ok,er)=>{const s=document.createElement("script");s.src="https://cesium.com/downloads/cesiumjs/releases/1.120/Build/Cesium/Cesium.js";s.onload=ok;s.onerror=er;document.head.appendChild(s);});
      }
      if(!actief||!containerRef.current)return;
      const C=window.Cesium;
      if(ionToken) C.Ion.defaultAccessToken=ionToken;

      const viewer=new C.Viewer(containerRef.current,{
        baseLayerPicker:false, navigationHelpButton:false, animation:false,
        timeline:false, geocoder:false, sceneModePicker:true, homeButton:false,
        infoBox:true, selectionIndicator:false, fullscreenButton:false,
        creditContainer:document.createElement("div"),
      });
      viewerRef.current=viewer;

      // Terrain (AHN4 via Cesium World Terrain)
      if(ionToken){
        try{viewer.scene.setTerrain(C.Terrain.fromWorldTerrain({requestVertexNormals:false,requestWaterMask:false}));}
        catch(e){console.warn("Terrain:",e.message);}
      }

      // PDOK luchtfoto
      viewer.imageryLayers.removeAll();
      try{
        const pdok=await C.WebMapServiceImageryProvider.fromUrl(
          "https://service.pdok.nl/hwh/luchtfotorgb/wms/v1_0",
          {layers:"2023_orthoHR",parameters:{format:"image/jpeg",transparent:false}}
        );
        viewer.imageryLayers.addImageryProvider(pdok);
      }catch(e){
        console.warn("PDOK WMS:",e.message);
        try{viewer.imageryLayers.addImageryProvider(new C.OpenStreetMapImageryProvider({url:"https://tile.openstreetmap.org/"}));}catch{}
      }
      viewer.scene.globe.depthTestAgainstTerrain=true;
      viewer.scene.screenSpaceCameraController.enableCollisionDetection=false;

      // Entity-groepen
      viewer._boorlijnEntities=[];
      viewer._klicEntities=[];
      viewer._machineEntities=[];
      const addBoor=e=>{const r=viewer.entities.add(e);viewer._boorlijnEntities.push(r);return r;};
      const addKlic=e=>{const r=viewer.entities.add(e);viewer._klicEntities.push(r);return r;};
      const addMachine=e=>{const r=viewer.entities.add(e);viewer._machineEntities.push(r);return r;};

      // ── Boorlijn ────────────────────────────────────────────────
      if(boorCoords.length>=2){
        const pp=ahnProfiel?.profielPunten??[];
        const dp=ahnProfiel?.dieptePunten??[];
        const sorted=[...dp].sort((a,b)=>a.afstand-b.afstand);

        addBoor({polyline:{positions:C.Cartesian3.fromDegreesArray(boorCoords.flatMap(([lat,lng])=>[lng,lat])),width:4,clampToGround:true,material:C.Color.fromCssColorString("#f97316")}});

        if(pp.length>=2&&sorted.length>=2){
          const bore3D=pp.flatMap(pt=>{
            const diepte=interpoleerDiepte(sorted,pt.afstand);
            const [lat,lng]=positieOpAfstand(boorCoords,pt.afstand);
            return[lng,lat,napNaarCesium(pt.hoogte-diepte)];
          });
          addBoor({polyline:{positions:C.Cartesian3.fromDegreesArrayHeights(bore3D),width:8,
            material:new C.PolylineGlowMaterialProperty({glowPower:0.3,color:C.Color.fromCssColorString("#f97316")}),
            depthFailMaterial:new C.PolylineDashMaterialProperty({color:C.Color.fromCssColorString("#fb923c").withAlpha(0.7),dashLength:18}),
            arcType:C.ArcType.NONE}});
          sorted.forEach(pt=>{
            if(pt.diepte<0.1)return;
            const [lat,lng]=positieOpAfstand(boorCoords,pt.afstand);
            const mvH=napNaarCesium(interpoleerHoogte(pp,pt.afstand));
            const bH=napNaarCesium(interpoleerHoogte(pp,pt.afstand)-pt.diepte);
            addBoor({polyline:{positions:C.Cartesian3.fromDegreesArrayHeights([lng,lat,bH,lng,lat,mvH]),width:1.5,
              material:new C.PolylineDashMaterialProperty({color:C.Color.fromCssColorString("#f97316").withAlpha(0.4),dashLength:10})}});
            addBoor({position:C.Cartesian3.fromDegrees(lng,lat,bH-1),label:{
              text:`-${pt.diepte.toFixed(1)}m\n${(interpoleerHoogte(pp,pt.afstand)-pt.diepte).toFixed(2)}m NAP`,
              font:"bold 11px sans-serif",fillColor:C.Color.WHITE,outlineColor:C.Color.BLACK,outlineWidth:2,
              style:C.LabelStyle.FILL_AND_OUTLINE,verticalOrigin:C.VerticalOrigin.TOP,
              pixelOffset:new C.Cartesian2(0,5),disableDepthTestDistance:1000,scale:0.9}});
          });
        }
        const sH=napNaarCesium(pp[0]?.hoogte??0),eH=napNaarCesium(pp[pp.length-1]?.hoogte??0);
        const [sLat,sLng]=boorCoords[0],[eLat,eLng]=boorCoords[boorCoords.length-1];
        addBoor({position:C.Cartesian3.fromDegrees(sLng,sLat,sH+5),point:{pixelSize:12,color:C.Color.fromCssColorString("#16a34a"),outlineColor:C.Color.WHITE,outlineWidth:2},label:{text:"S",font:"bold 14px sans-serif",fillColor:C.Color.fromCssColorString("#16a34a"),outlineColor:C.Color.BLACK,outlineWidth:2,style:C.LabelStyle.FILL_AND_OUTLINE,verticalOrigin:C.VerticalOrigin.BOTTOM,pixelOffset:new C.Cartesian2(0,-8)}});
        addBoor({position:C.Cartesian3.fromDegrees(eLng,eLat,eH+5),point:{pixelSize:12,color:C.Color.fromCssColorString("#dc2626"),outlineColor:C.Color.WHITE,outlineWidth:2},label:{text:"E",font:"bold 14px sans-serif",fillColor:C.Color.fromCssColorString("#dc2626"),outlineColor:C.Color.BLACK,outlineWidth:2,style:C.LabelStyle.FILL_AND_OUTLINE,verticalOrigin:C.VerticalOrigin.BOTTOM,pixelOffset:new C.Cartesian2(0,-8)}});
      }

      // ── Machine-locaties ────────────────────────────────────────
      if(machineLocaties){
        const CFG={boormachine:{kleur:"#3b82f6",icon:"🏗️",hoogte:3,label:"HDD Boormachine"},bentoniet:{kleur:"#f97316",icon:"🛢️",hoogte:2.5,label:"Bentoniet & opvangput"}};
        const pp2=ahnProfiel?.profielPunten??[];
        Object.entries(machineLocaties).forEach(([key,m])=>{
          if(!m?.centerRD)return;
          const cfg=CFG[key];if(!cfg)return;
          const [lo,la]=rdNaarWgs84(m.centerRD.x,m.centerRD.y);
          const refPt=key==="boormachine"?pp2[0]:pp2[pp2.length-1];
          const bH=napNaarCesium(refPt?.hoogte??1);
          const bear=boorCoords.length>=2?Math.atan2(boorCoords[boorCoords.length-1][1]-boorCoords[0][1],boorCoords[boorCoords.length-1][0]-boorCoords[0][0]):0;
          const hl=m.lengte/2,hb=m.breedte/2,sinB=Math.sin(bear+Math.PI/2),cosB=Math.cos(bear+Math.PI/2);
          const corners=[[-hl,hb],[-hl,-hb],[hl,-hb],[hl,hb]].map(([a,p])=>{
            const [clo,cla]=rdNaarWgs84(m.centerRD.x+a*cosB-p*sinB,m.centerRD.y+a*sinB+p*cosB);
            return C.Cartesian3.fromDegrees(clo,cla,bH);
          });
          addMachine({name:`${cfg.icon} ${cfg.label}`,polygon:{hierarchy:new C.PolygonHierarchy(corners),height:bH,extrudedHeight:bH+cfg.hoogte,material:C.Color.fromCssColorString(cfg.kleur).withAlpha(0.35),outline:true,outlineColor:C.Color.fromCssColorString(cfg.kleur),outlineWidth:3,closeTop:true,closeBottom:true}});
          addMachine({position:C.Cartesian3.fromDegrees(lo,la,bH+cfg.hoogte+3),label:{text:`${cfg.icon} ${cfg.label}\n${m.lengte}×${m.breedte}m`,font:"bold 11px sans-serif",fillColor:C.Color.fromCssColorString(cfg.kleur),outlineColor:C.Color.BLACK,outlineWidth:2,style:C.LabelStyle.FILL_AND_OUTLINE,disableDepthTestDistance:2000,horizontalOrigin:C.HorizontalOrigin.CENTER}});
        });
      }

      // ── KLIC ────────────────────────────────────────────────────
      const zipBestanden=bestandenMeta.filter(b=>b.naam?.toLowerCase().endsWith(".zip")&&b.url);
      if(zipBestanden.length>0){
        try{
          if(!window.JSZip){await new Promise((ok,er)=>{const s=document.createElement("script");s.src="https://cdnjs.cloudflare.com/ajax/libs/jszip/3.10.1/jszip.min.js";s.onload=ok;s.onerror=er;document.head.appendChild(s);});}
          for(const b of zipBestanden){
            if(!actief)break;
            try{
              const res=await fetch(b.url);if(!res.ok)continue;
              const blob=await res.blob();
              const zip=await window.JSZip.loadAsync(blob);
              const xmlNaam=Object.keys(zip.files).find(n=>n.includes("GI_gebiedsinformatie")&&n.endsWith(".xml"));
              if(!xmlNaam)continue;
              const xml=await zip.files[xmlNaam].async("string");
              Object.entries(parseImklMini(xml)).forEach(([thema,lijnen])=>{
                const kleur=THEMA_KLEUR[thema]??"#888888";
                lijnen.forEach(lijn=>{
                  const pts=lijn.flatMap(([lo,la])=>[lo,la,NAP_OFFSET+0.5]);
                  if(pts.length<6)return;
                  addKlic({polyline:{positions:C.Cartesian3.fromDegreesArrayHeights(pts),width:3,clampToGround:true,material:C.Color.fromCssColorString(kleur)}});
                });
              });
            }catch(e){console.warn("KLIC:",e.message);}
          }
        }catch(e){console.warn("KLIC laden:",e);}
      }

      // ── 3D BAG via lokale proxy (CORS-bypass) ──────────────────
      setBag3dStatus("laden");
      try {
        // Proxy-route: /api/bag3d/... → https://api.3dbag.nl/collections/pand/3dtiles/...
        const bag3d = await C.Cesium3DTileset.fromUrl(
          "/api/bag3d/tileset.json",
          { maximumScreenSpaceError: 16 }
        );
        bag3d.style = new C.Cesium3DTileStyle({
          color: "color('rgba(210, 185, 150, 0.9)')",
        });
        viewer.scene.primitives.add(bag3d);
        viewer._bag3dTileset = bag3d;
        setBag3dStatus("klaar");
      } catch(e) {
        console.error("3D BAG:", e.message);
        setBag3dStatus("fout");
      }

      // Camera fly-to op de boorlijn
      viewer._vliegNaarBoor = () => {
        if (!boorCoords.length) return;
        const mid = boorCoords[Math.floor(boorCoords.length/2)];
        viewer.camera.flyTo({
          destination: C.Cartesian3.fromDegrees(mid[1], mid[0], 300),
          orientation: { heading: C.Math.toRadians(0), pitch: C.Math.toRadians(-45), roll: 0 },
          duration: 1.5,
        });
      };
      viewer._vliegVanBovenaf = () => {
        if (!boorCoords.length) return;
        const mid = boorCoords[Math.floor(boorCoords.length/2)];
        viewer.camera.flyTo({
          destination: C.Cartesian3.fromDegrees(mid[1], mid[0], 500),
          orientation: { heading: C.Math.toRadians(0), pitch: C.Math.toRadians(-90), roll: 0 },
          duration: 1.5,
        });
      };

      if (boorCoords.length >= 2) {
        viewer._vliegNaarBoor();
      }
      setStatus("klaar");
    })().catch(e=>{console.error("Cesium init:",e);setStatus("fout");});
    return()=>{actief=false;if(viewerRef.current){try{viewerRef.current.destroy();}catch{}viewerRef.current=null;}};
  // eslint-disable-next-line react-hooks/exhaustive-deps
  },[ionToken]);

  // Laag-toggles
  useEffect(()=>{
    const v=viewerRef.current;if(!v)return;
    v._boorlijnEntities?.forEach(e=>{e.show=lagen.boorlijn;});
    v._klicEntities?.forEach(e=>{e.show=lagen.klic;});
    v._machineEntities?.forEach(e=>{e.show=lagen.machines;});
    if(v._bag3dTileset) v._bag3dTileset.show=lagen.bag3d;
  },[lagen]);

  const herlaadViewer=()=>{
    if(viewerRef.current){try{viewerRef.current.destroy();}catch{}viewerRef.current=null;}
    setStatus("init");setIonToken(tokenInput);
  };

  return(
    <div className="space-y-3">
      {/* Token balk */}
      <div className="bg-white border border-gray-200 rounded-xl px-4 py-3 flex flex-wrap items-center gap-3">
        <div className="flex-1 min-w-0">
          <div className="text-xs font-semibold text-gray-700">🌍 Cesium Ion token <span className="text-gray-400 font-normal">(voor AHN4 terrein)</span></div>
          <div className="text-xs text-gray-400">Gratis via <a href="https://ion.cesium.com" target="_blank" className="text-blue-500 underline">ion.cesium.com</a> → Access Tokens → kopieer de default token</div>
        </div>
        <div className="flex items-center gap-2">
          <input value={tokenInput} onChange={e=>handleTokenInput(e.target.value)} placeholder="eyJhbGci..."
            className="border border-gray-200 rounded-lg px-3 py-1.5 text-xs w-52 font-mono focus:outline-none focus:ring-1 focus:ring-indigo-400"/>
          <span className={`text-xs transition-opacity ${tokenOpgeslagen?"text-green-500 opacity-100":"opacity-0"}`}>✓ opgeslagen</span>
          <button onClick={herlaadViewer} className="px-3 py-1.5 bg-indigo-600 text-white text-xs font-semibold rounded-lg hover:bg-indigo-700">↺ Herladen</button>
        </div>
      </div>

      <div className="flex gap-3" style={{height:"calc(100vh - 210px)",minHeight:500}}>
        {/* Zijpaneel */}
        <div className="w-48 flex-shrink-0 bg-white border border-gray-200 rounded-xl p-4 flex flex-col gap-2.5">
          <div className="text-xs font-semibold text-gray-600 uppercase tracking-wide">Lagen</div>

          {[["boorlijn","🟠 Boorlijn & profiel"],["klic","🔌 KLIC leidingen"],["machines","🏗️ Machines"]].map(([k,l])=>(
            <label key={k} className="flex items-center gap-2 cursor-pointer">
              <input type="checkbox" checked={!!lagen[k]} onChange={e=>setLagen(p=>({...p,[k]:e.target.checked}))} className="accent-indigo-600"/>
              <span className="text-xs text-gray-700">{l}</span>
            </label>
          ))}

          <div className="border-t border-gray-100 pt-2">
            <label className="flex items-center gap-2 cursor-pointer">
              <input type="checkbox" checked={!!lagen.bag3d} onChange={e=>setLagen(p=>({...p,bag3d:e.target.checked}))} className="accent-indigo-600"/>
              <span className="text-xs text-gray-700 flex-1">🏠 3D BAG gebouwen
                <span className="text-gray-400 block text-xs">NL — AHN4 hoogtes</span>
              </span>
              {bag3dStatus==="laden"&&<span className="text-xs text-indigo-500 animate-pulse">⏳</span>}
              {bag3dStatus==="klaar"&&<span className="text-xs text-green-500">✓</span>}
              {bag3dStatus==="fout"&&<span className="text-xs text-red-400">✗</span>}
            </label>
          </div>

          <div className="border-t border-gray-100 pt-2">
            <div className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-1.5">Camera</div>
            <button onClick={()=>viewerRef.current?._vliegNaarBoor?.()}
              className="w-full mb-1 py-1.5 text-xs bg-gray-50 hover:bg-gray-100 rounded-lg border border-gray-200 text-gray-700">
              📍 Vlieg naar boorlijn
            </button>
            <button onClick={()=>viewerRef.current?._vliegVanBovenaf?.()}
              className="w-full py-1.5 text-xs bg-gray-50 hover:bg-gray-100 rounded-lg border border-gray-200 text-gray-700">
              🔭 Verticaal (vogelperspectief)
            </button>
          </div>

          <div className="border-t border-gray-100 pt-2">
            <div className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-1">Navigatie</div>
            <div className="text-xs text-gray-400 leading-relaxed">🖱 Klik+sleep = draaien<br/>⚙ Rechts+sleep = kantelen<br/>🔍 Scroll = zoom</div>
          </div>

          <div className="mt-auto text-xs">
            {status==="laden"&&<span className="text-indigo-500 animate-pulse">⏳ Laden…</span>}
            {status==="klaar"&&<span className="text-green-600">✓ Klaar</span>}
            {status==="fout"&&<span className="text-red-500">✗ Fout</span>}
            {!ionToken&&status==="klaar"&&<div className="text-amber-500 mt-1">⚠ Geen terrain-token</div>}
          </div>
        </div>

        {/* Cesium viewer */}
        <div className="flex-1 min-w-0 rounded-xl overflow-hidden border border-gray-200 shadow-sm bg-black relative">
          <div ref={containerRef} style={{width:"100%",height:"100%"}}/>
          {status==="laden"&&(
            <div className="absolute inset-0 flex items-center justify-center bg-gray-900/80 z-10">
              <div className="text-center">
                <div className="text-white text-lg font-semibold mb-2">3D ontwerp laden…</div>
                <div className="text-gray-300 text-sm">CesiumJS · PDOK luchtfoto · 3D BAG gebouwen</div>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
