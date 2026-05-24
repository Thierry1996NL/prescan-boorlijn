"use client";
import BoorLabel, { LockButton } from "@/components/BoorLabel";
import { useEffect, useRef, useState, useMemo } from "react";

// ─── RD New → WGS84 ──────────────────────────────────────────────
function rdNaarWgs84(x, y) {
  const dX=(x-155000)/100000, dY=(y-463000)/100000;
  const lat=52.15517440+(3235.65389*dY-32.58297*dX*dX-0.24750*dY*dY-0.84978*dX*dX*dY-0.06550*dY*dY*dY)/3600;
  const lng=5.38720621+(5260.52916*dX+105.94684*dX*dY+2.45656*dX*dY*dY-0.81885*dX*dX*dX)/3600;
  return [lng, lat];
}
const NAP_OFFSET = 44.1; // NL gemiddeld boven WGS84 ellipsoïde
const napH = nap => nap + NAP_OFFSET;

// Lineaire interpolatie van hoogte op afstand
function hoogtOpAfstand(punten, afstand) {
  if (!punten?.length) return 0;
  for (let i=0;i<punten.length-1;i++) {
    const a=punten[i], b=punten[i+1];
    if (afstand>=a.afstand && afstand<=b.afstand) {
      const t=(afstand-a.afstand)/(b.afstand-a.afstand);
      return a.hoogte+t*(b.hoogte-a.hoogte);
    }
  }
  return punten[punten.length-1]?.hoogte ?? 0;
}
function diepteOpAfstand(punten, afstand) {
  if (!punten?.length) return 0;
  const s=[...punten].sort((a,b)=>a.afstand-b.afstand);
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
function positieOpLijn(coords, afstand) {
  let cumul=0;
  for (let i=0;i<coords.length-1;i++) {
    const [la1,lo1]=coords[i], [la2,lo2]=coords[i+1];
    const R=6371000, r=d=>d*Math.PI/180;
    const a=Math.sin(r(la2-la1)/2)**2+Math.cos(r(la1))*Math.cos(r(la2))*Math.sin(r(lo2-lo1)/2)**2;
    const d=R*2*Math.atan2(Math.sqrt(a),Math.sqrt(1-a));
    if (cumul+d>=afstand) { const t=(afstand-cumul)/d; return [la1+t*(la2-la1),lo1+t*(lo2-lo1)]; }
    cumul+=d;
  }
  return coords[coords.length-1];
}

// IMKL/KLIC parser
function parseImkl(xml) {
  const doc=new DOMParser().parseFromString(xml,"text/xml");
  const netThema={};
  doc.querySelectorAll("Utiliteitsnet").forEach(n=>{
    const id=n.getAttribute("gml:id")||n.getAttributeNS?.("http://www.opengis.net/gml/3.2","id")||"";
    const href=n.querySelector("thema")?.getAttributeNS?.("http://www.w3.org/1999/xlink","href")||"";
    if(id) netThema[id]=href.split("/").pop()||"overig";
  });
  const lagen={};
  doc.querySelectorAll("UtilityLink").forEach(link=>{
    const thema=netThema[(link.querySelector("inNetwork")?.getAttributeNS?.("http://www.w3.org/1999/xlink","href")||"").replace(/^#/,"")]||"overig";
    const pl=link.querySelector("posList");
    if(!pl)return;
    const nums=pl.textContent.trim().split(/\s+/).map(Number);
    const coords=[];
    for(let i=0;i+1<nums.length;i+=2){
      const x=nums[i],y=nums[i+1];
      const [lo,la]=(Math.abs(x)>180||Math.abs(y)>90)?rdNaarWgs84(x,y):[x,y];
      coords.push([lo,la]);
    }
    if(coords.length>=2){if(!lagen[thema])lagen[thema]=[];lagen[thema].push(coords);}
  });
  return lagen;
}

const KLIC_KLEUREN={"laagspanning":"#7B00AA","middenspanning":"#00CCFF","hoogspanning":"#FF4400","gasLageDruk":"#FFFF00","gasHogeDruk":"#FF0000","water":"#0000CC","datatransport":"#00CC00","rioolVrijverval":"#AA00CC","rioolOnderOverOfOnderdruk":"#AA00CC","warmte":"#FF6600","overig":"#888888"};

// ════════════════════════════════════════════════════════════════════
export default function Stap8_3D({ project, boringConfig }) {
  const containerRef = useRef(null);
  const viewerRef    = useRef(null);

  const [ionToken, setIonToken] = useState(()=>{ try{return localStorage.getItem("cesium_ion_token")||"";}catch{return "";} });
  const [tokenInput, setTokenInput] = useState(ionToken);
  const [tokenSaved, setTokenSaved] = useState(false);
  const [status, setStatus] = useState("init");
  const [locked, setLocked] = useState(() => {
    try { const s = localStorage.getItem(`boor_lock_${project?.id}_8`); return s ? JSON.parse(s) : false; } catch { return false; }
  });
  useEffect(() => {
    try { localStorage.setItem(`boor_lock_${project?.id}_8`, JSON.stringify(locked)); } catch {}
  }, [locked]);
  const mapLS8 = () => { try { return JSON.parse(localStorage.getItem(`map_s_${project?.id}_8`)||'null'); } catch { return null; } };
  const mapSave8 = (p) => { try { const SK=`map_s_${project?.id}_8`; const cur=JSON.parse(localStorage.getItem(SK)||'{}'); localStorage.setItem(SK,JSON.stringify({...cur,...p})); } catch {} };
  const _ls8 = mapLS8();
  const [lagen, setLagen] = useState(_ls8?.lagen ?? { boorlijn:true, klic:true, machines:true, bag3d:true });
  useEffect(() => { mapSave8({lagen}); }, [lagen]);
  const [bag3dStatus, setBag3dStatus] = useState(null);
  const [bag3dTeller, setBag3dTeller] = useState(0);

  // Project data parsers
  const boorCoords = useMemo(()=>{
    try{ const g=project?.boortrace_geojson; if(!g)return[];
      const p=typeof g==="string"?JSON.parse(g):g;
      return p.coordinates?.map(([lo,la])=>[la,lo])??[]; }catch{return[];}
  },[project]);

  const ahnProfiel = useMemo(()=>{
    try{ const r=project?.ahn_profiel; if(!r)return null;
      const p=typeof r==="string"?JSON.parse(r):r;
      return Array.isArray(p)?{profielPunten:p,dieptePunten:[]}:p; }catch{return null;}
  },[project]);

  const machineLocaties = useMemo(()=>{
    try{ const r=project?.machine_locaties; if(!r)return null;
      return typeof r==="string"?JSON.parse(r):r; }catch{return null;}
  },[project]);

  const bestandenMeta = useMemo(()=>{
    try{return JSON.parse(project?.bestanden_meta||"[]");}catch{return[];}
  },[project]);

  const handleToken = val => {
    setTokenInput(val);
    try{localStorage.setItem("cesium_ion_token",val);}catch{}
    setTokenSaved(true); setTimeout(()=>setTokenSaved(false),2000);
  };

  // ── Cesium init ────────────────────────────────────────────────
  useEffect(()=>{
    if(typeof window==="undefined"||viewerRef.current||!containerRef.current)return;
    let actief=true;
    setStatus("laden");
    (async()=>{
      // Laad CesiumJS
      if(!document.querySelector('link[href*="cesium"]')){
        const l=document.createElement("link");l.rel="stylesheet";
        l.href="https://cesium.com/downloads/cesiumjs/releases/1.120/Build/Cesium/Widgets/widgets.css";
        document.head.appendChild(l);
      }
      if(!window.Cesium) await new Promise((ok,er)=>{
        const s=document.createElement("script");
        s.src="https://cesium.com/downloads/cesiumjs/releases/1.120/Build/Cesium/Cesium.js";
        s.onload=ok; s.onerror=er; document.head.appendChild(s);
      });
      if(!actief||!containerRef.current)return;
      const C=window.Cesium;
      if(ionToken) C.Ion.defaultAccessToken=ionToken;

      // Viewer — geen terrainProvider/imageryProvider in constructor (verwijderd in v1.103)
      const viewer=new C.Viewer(containerRef.current,{
        baseLayerPicker:false, navigationHelpButton:false, animation:false,
        timeline:false, geocoder:false, sceneModePicker:true, homeButton:false,
        infoBox:false, selectionIndicator:false, fullscreenButton:false,
        creditContainer:document.createElement("div"),
      });
      viewerRef.current=viewer;

      // Terrain via Ion (AHN4)
      if(ionToken){
        try{ viewer.scene.setTerrain(C.Terrain.fromWorldTerrain({requestVertexNormals:false})); }
        catch(e){ console.warn("Terrain:",e.message); }
      }

      // PDOK luchtfoto (async factory — nieuwe Cesium API)
      viewer.imageryLayers.removeAll();
      try{
        const pdok=await C.WebMapServiceImageryProvider.fromUrl(
          "https://service.pdok.nl/hwh/luchtfotorgb/wms/v1_0",
          {layers:"2023_orthoHR",parameters:{format:"image/jpeg",transparent:false}}
        );
        viewer.imageryLayers.addImageryProvider(pdok);
      }catch{
        try{ viewer.imageryLayers.addImageryProvider(new C.OpenStreetMapImageryProvider({url:"https://tile.openstreetmap.org/"})); }catch{}
      }

      // Camera: volledig vrij in alle richtingen
      viewer.scene.globe.depthTestAgainstTerrain=true;
      const ctrl=viewer.scene.screenSpaceCameraController;
      ctrl.enableCollisionDetection=false;
      ctrl.minimumZoomDistance=2;
      ctrl.enableTilt=true; ctrl.enableRotate=true;
      ctrl.enableTranslate=true; ctrl.enableZoom=true; ctrl.enableLook=true;

      // Entity-groepen voor laag-toggles
      viewer._boorlijnEntities=[]; viewer._klicEntities=[]; viewer._machineEntities=[]; viewer._bag3dEntities=[];
      const addB=e=>{const r=viewer.entities.add(e);viewer._boorlijnEntities.push(r);return r;};
      const addK=e=>{const r=viewer.entities.add(e);viewer._klicEntities.push(r);return r;};
      const addM=e=>{const r=viewer.entities.add(e);viewer._machineEntities.push(r);return r;};

      const pp=ahnProfiel?.profielPunten??[];
      const dp=ahnProfiel?.dieptePunten??[];

      // ── Boorlijn op maaiveld ────────────────────────────────────
      if(boorCoords.length>=2){
        addB({polyline:{positions:C.Cartesian3.fromDegreesArray(boorCoords.flatMap(([la,lo])=>[lo,la])),
          width:4,clampToGround:true,material:C.Color.fromCssColorString("#f97316")}});

        // 3D boreprofiel
        if(pp.length>=2&&dp.length>=2){
          const pts=pp.flatMap(p=>{
            const d=diepteOpAfstand(dp,p.afstand);
            const [la,lo]=positieOpLijn(boorCoords,p.afstand);
            return[lo,la,napH(p.hoogte-d)];
          });
          const cesiumBoorWidth = boringConfig?.boringD ? Math.max(5, Math.min(22, Math.round(boringConfig.boringD / 18))) : 7;
          addB({polyline:{positions:C.Cartesian3.fromDegreesArrayHeights(pts),width:cesiumBoorWidth,
            material:new C.PolylineGlowMaterialProperty({glowPower:0.25,color:C.Color.fromCssColorString("#f97316")}),
            depthFailMaterial:new C.PolylineDashMaterialProperty({color:C.Color.fromCssColorString("#f97316").withAlpha(0.65),dashLength:16}),
            arcType:C.ArcType.NONE}});
          // Dieptepunten
          [...dp].sort((a,b)=>a.afstand-b.afstand).forEach(d=>{
            if(d.diepte<0.1)return;
            const [la,lo]=positieOpLijn(boorCoords,d.afstand);
            const mvH=napH(hoogtOpAfstand(pp,d.afstand));
            const bH=napH(hoogtOpAfstand(pp,d.afstand)-d.diepte);
            addB({polyline:{positions:C.Cartesian3.fromDegreesArrayHeights([lo,la,bH,lo,la,mvH]),width:1.5,
              material:new C.PolylineDashMaterialProperty({color:C.Color.fromCssColorString("#f97316").withAlpha(0.4),dashLength:10})}});
            addB({position:C.Cartesian3.fromDegrees(lo,la,bH-1),label:{
              text:`-${d.diepte.toFixed(1)}m\n${(hoogtOpAfstand(pp,d.afstand)-d.diepte).toFixed(2)}m NAP`,
              font:"bold 11px sans-serif",fillColor:C.Color.WHITE,outlineColor:C.Color.BLACK,outlineWidth:2,
              style:C.LabelStyle.FILL_AND_OUTLINE,verticalOrigin:C.VerticalOrigin.TOP,
              pixelOffset:new C.Cartesian2(0,5),disableDepthTestDistance:1000,scale:0.85}});
          });
        }
        // S/E markers
        const [sLa,sLo]=boorCoords[0],[eLa,eLo]=boorCoords[boorCoords.length-1];
        const sH=napH(pp[0]?.hoogte??0), eH=napH(pp[pp.length-1]?.hoogte??0);
        const mkr=(lo,la,h,kleur,letter)=>{
          addB({position:C.Cartesian3.fromDegrees(lo,la,h+4),
            point:{pixelSize:12,color:C.Color.fromCssColorString(kleur),outlineColor:C.Color.WHITE,outlineWidth:2},
            label:{text:letter,font:"bold 14px sans-serif",fillColor:C.Color.fromCssColorString(kleur),
              outlineColor:C.Color.BLACK,outlineWidth:2,style:C.LabelStyle.FILL_AND_OUTLINE,
              verticalOrigin:C.VerticalOrigin.BOTTOM,pixelOffset:new C.Cartesian2(0,-8)}});
        };
        mkr(sLo,sLa,sH,"#16a34a","S");
        mkr(eLo,eLa,eH,"#dc2626","E");
      }

      // ── Machine-locaties ────────────────────────────────────────
      if(machineLocaties){
        const CFG={
          boormachine:{kleur:"#3b82f6",hoogte:3,label:"HDD Boormachine"},
          bentoniet:  {kleur:"#f97316",hoogte:2.5,label:"Bentoniet & opvangput"},
        };
        // Bearing uit bore-lijn (in RD New richting, betrouwbaarder dan lat/lng)
        Object.entries(machineLocaties).forEach(([key,m])=>{
          if(!m?.centerRD)return;
          const cfg=CFG[key]; if(!cfg)return;
          const [lo,la]=rdNaarWgs84(m.centerRD.x,m.centerRD.y);
          const refPt=key==="boormachine"?pp[0]:pp[pp.length-1];
          const bH=napH(refPt?.hoogte??1);
          // Bearing uit bore-startpunt en eindpunt (RD, accurater)
          let bear=0;
          if(boorCoords.length>=2){
            const [la1,lo1]=boorCoords[0],[la2,lo2]=boorCoords[boorCoords.length-1];
            bear=Math.atan2(lo2-lo1,la2-la1); // hoek in geografische ruimte
          }
          const hl=m.lengte/2, hb=m.breedte/2;
          const sB=Math.sin(bear), cB=Math.cos(bear);
          // Rechthoekhoeken in RD + omrekenen naar WGS84
          const hoeken=[[-hl,-hb],[-hl,hb],[hl,hb],[hl,-hb]].map(([a,p])=>{
            const rdX=m.centerRD.x+a*cB-p*sB, rdY=m.centerRD.y+a*sB+p*cB;
            const [clo,cla]=rdNaarWgs84(rdX,rdY);
            return C.Cartesian3.fromDegrees(clo,cla,bH);
          });
          addM({name:cfg.label, polygon:{
            hierarchy:new C.PolygonHierarchy(hoeken),
            height:bH, extrudedHeight:bH+cfg.hoogte,
            material:C.Color.fromCssColorString(cfg.kleur).withAlpha(0.35),
            outline:true, outlineColor:C.Color.fromCssColorString(cfg.kleur), outlineWidth:3,
            closeTop:true, closeBottom:true,
          }});
          addM({position:C.Cartesian3.fromDegrees(lo,la,bH+cfg.hoogte+3), label:{
            text:`${cfg.label}\n${m.lengte}×${m.breedte}m`,
            font:"bold 11px sans-serif", fillColor:C.Color.fromCssColorString(cfg.kleur),
            outlineColor:C.Color.BLACK, outlineWidth:2, style:C.LabelStyle.FILL_AND_OUTLINE,
            disableDepthTestDistance:2000, horizontalOrigin:C.HorizontalOrigin.CENTER,
          }});
        });
      }

      // ── KLIC leidingen ──────────────────────────────────────────
      const zipBestanden=bestandenMeta.filter(b=>b.naam?.toLowerCase().endsWith(".zip")&&b.url);
      if(zipBestanden.length>0){
        if(!window.JSZip){
          await new Promise((ok,er)=>{const s=document.createElement("script");
            s.src="https://cdnjs.cloudflare.com/ajax/libs/jszip/3.10.1/jszip.min.js";
            s.onload=ok;s.onerror=er;document.head.appendChild(s);});
        }
        for(const b of zipBestanden){
          if(!actief)break;
          try{
            const res=await fetch(b.url);if(!res.ok)continue;
            const zip=await window.JSZip.loadAsync(await res.blob());
            const xmlNaam=Object.keys(zip.files).find(n=>n.includes("GI_gebiedsinformatie")&&n.endsWith(".xml"));
            if(!xmlNaam)continue;
            Object.entries(parseImkl(await zip.files[xmlNaam].async("string"))).forEach(([thema,lijnen])=>{
              const kleur=KLIC_KLEUREN[thema]??"#888888";
              lijnen.forEach(lijn=>{
                const pts=lijn.flatMap(([lo,la])=>[lo,la,NAP_OFFSET+0.3]);
                if(pts.length<6)return;
                addK({polyline:{positions:C.Cartesian3.fromDegreesArrayHeights(pts),
                  width:3,clampToGround:true,material:C.Color.fromCssColorString(kleur)}});
              });
            });
          }catch(e){console.warn("KLIC:",e.message);}
        }
      }

      // ── 3D BAG via PDOK WFS (geen proxy, CORS ingeschakeld) ────
      setBag3dStatus("laden");
      if(boorCoords.length>=2){
        const lats=boorCoords.map(([la])=>la), lons=boorCoords.map(([,lo])=>lo);
        const m=0.006;
        const bbox=`${Math.min(...lons)-m},${Math.min(...lats)-m},${Math.max(...lons)+m},${Math.max(...lats)+m}`;
        fetch(`https://service.pdok.nl/lv/bag/wfs/v2_0?SERVICE=WFS&VERSION=2.0.0&REQUEST=GetFeature&TYPENAMES=bag:pand&BBOX=${bbox},urn:ogc:def:crs:OGC:1.3:CRS84&OUTPUTFORMAT=application/json&COUNT=500`)
          .then(r=>{if(!r.ok)throw new Error(r.status);return r.json();})
          .then(data=>{
            const baseH=napH(pp[Math.floor(pp.length/2)]?.hoogte??0);
            let n=0;
            (data.features??[]).forEach(feat=>{
              const geom=feat.geometry;if(!geom?.coordinates)return;
              const ring=geom.type==="Polygon"?geom.coordinates[0]:geom.coordinates?.[0]?.[0];
              if(!ring||ring.length<3)return;
              const pos=ring.map(([lo,la])=>C.Cartesian3.fromDegrees(lo,la,baseH));
              const ent=viewer.entities.add({polygon:{
                hierarchy:new C.PolygonHierarchy(pos),perPositionHeight:true,
                extrudedHeight:baseH+7,material:C.Color.fromBytes(215,190,155,210),
                outline:true,outlineColor:C.Color.fromBytes(110,80,40,255),outlineWidth:1,
                closeTop:true,closeBottom:true,
              }});
              viewer._bag3dEntities.push(ent);n++;
            });
            setBag3dTeller(n);
            setBag3dStatus(n>0?"klaar":"leeg");
          })
          .catch(e=>{console.error("3D BAG:",e.message);setBag3dStatus("fout");});
      }else{setBag3dStatus("leeg");}

      // Camera fly-to — herstel opgeslagen positie of vlieg naar boorlijn
      const savedCam = mapLS8()?.cam;
      if (savedCam) {
        viewer.camera.setView({ destination: new C.Cartesian3(savedCam.x, savedCam.y, savedCam.z),
          orientation: { heading:savedCam.h, pitch:savedCam.p, roll:savedCam.r } });
      }
      viewer._vliegNaarBoor=()=>{
        if(!boorCoords.length)return;
        const mid=boorCoords[Math.floor(boorCoords.length/2)];
        viewer.camera.flyTo({destination:C.Cartesian3.fromDegrees(mid[1],mid[0],300),
          orientation:{heading:C.Math.toRadians(0),pitch:C.Math.toRadians(-40),roll:0},duration:1.5});
      };
      viewer._vogel=()=>{
        if(!boorCoords.length)return;
        const mid=boorCoords[Math.floor(boorCoords.length/2)];
        viewer.camera.flyTo({destination:C.Cartesian3.fromDegrees(mid[1],mid[0],600),
          orientation:{heading:C.Math.toRadians(0),pitch:C.Math.toRadians(-90),roll:0},duration:1.5});
      };
      if (!savedCam) viewer._vliegNaarBoor();
      // Sla camerapositie op bij bewegen
      viewer.camera.moveEnd.addEventListener(() => {
        const cam = viewer.camera; const pos = cam.position;
        mapSave8({ cam:{ x:pos.x, y:pos.y, z:pos.z, h:cam.heading, p:cam.pitch, r:cam.roll } });
      });
      setStatus("klaar");
    })().catch(e=>{console.error("Cesium:",e);setStatus("fout");});
    return()=>{actief=false;if(viewerRef.current){try{viewerRef.current.destroy();}catch{}viewerRef.current=null;}};
  // eslint-disable-next-line react-hooks/exhaustive-deps
  },[ionToken]);

  // Laag-toggles
  useEffect(()=>{
    const v=viewerRef.current;if(!v)return;
    v._boorlijnEntities?.forEach(e=>{e.show=lagen.boorlijn;});
    v._klicEntities?.forEach(e=>{e.show=lagen.klic;});
    v._machineEntities?.forEach(e=>{e.show=lagen.machines;});
    v._bag3dEntities?.forEach(e=>{e.show=lagen.bag3d;});
  },[lagen]);

  // Vergrendeling CesiumJS
  useEffect(()=>{
    const ctrl=viewerRef.current?.scene?.screenSpaceCameraController; if(!ctrl)return;
    ctrl.enableRotate=!locked;ctrl.enableZoom=!locked;
    ctrl.enableTranslate=!locked;ctrl.enableTilt=!locked;ctrl.enableLook=!locked;
  },[locked]);

  const herlaad=()=>{if(viewerRef.current){try{viewerRef.current.destroy();}catch{}viewerRef.current=null;}setStatus("init");setIonToken(tokenInput);};

  return(
    <div className="space-y-3">
      {/* Token */}
      <div className="bg-white border border-gray-200 rounded-xl px-4 py-2.5 flex flex-wrap items-center gap-3">
        <div className="flex-1 min-w-0">
          <div className="text-xs font-semibold text-gray-700">🌍 Cesium Ion token <span className="font-normal text-gray-400">(optioneel — voor AHN4 terrein)</span></div>
          <div className="text-xs text-gray-400">Gratis via <a href="https://ion.cesium.com" target="_blank" className="text-blue-500 underline">ion.cesium.com</a> → Access Tokens → default token</div>
        </div>
        <div className="flex items-center gap-2">
          <input value={tokenInput} onChange={e=>handleToken(e.target.value)} placeholder="eyJhbGci..."
            className="border border-gray-200 rounded-lg px-3 py-1.5 text-xs w-52 font-mono focus:outline-none focus:ring-1 focus:ring-indigo-400"/>
          <span className={`text-xs transition-opacity ${tokenSaved?"text-green-500 opacity-100":"opacity-0"}`}>✓</span>
          <button onClick={herlaad} className="px-3 py-1.5 bg-indigo-600 text-white text-xs font-semibold rounded-lg hover:bg-indigo-700">↺ Herladen</button>
        </div>
      </div>

      <div className="flex gap-3" style={{height:"calc(100vh - 200px)",minHeight:500}}>
        {/* Zijpaneel */}
        <div className="w-48 flex-shrink-0 bg-white border border-gray-200 rounded-xl p-4 flex flex-col gap-2.5 text-xs">
          <div className="font-semibold text-gray-600 uppercase tracking-wide">Lagen</div>
          {[["boorlijn","🟠 Boorlijn & profiel"],["klic","🔌 KLIC leidingen"],["machines","🏗️ Machines"]].map(([k,l])=>(
            <label key={k} className="flex items-center gap-2 cursor-pointer">
              <input type="checkbox" checked={!!lagen[k]} onChange={e=>setLagen(p=>({...p,[k]:e.target.checked}))} className="accent-indigo-600"/>
              <span className="text-gray-700">{l}</span>
            </label>
          ))}
          <div className="border-t border-gray-100 pt-2">
            <label className="flex items-center gap-1.5 cursor-pointer">
              <input type="checkbox" checked={!!lagen.bag3d} onChange={e=>setLagen(p=>({...p,bag3d:e.target.checked}))} className="accent-indigo-600"/>
              <span className="text-gray-700 flex-1">🏠 3D BAG gebouwen<span className="text-gray-400 block">PDOK · AHN4 hoogtes</span></span>
              {bag3dStatus==="laden"&&<span className="text-indigo-500 animate-pulse">⏳</span>}
              {bag3dStatus==="klaar"&&<span className="text-green-500">✓ {bag3dTeller}</span>}
              {bag3dStatus==="leeg"&&<span className="text-amber-500">⚠ 0</span>}
              {bag3dStatus==="fout"&&<span className="text-red-400">✗</span>}
            </label>
          </div>
          <div className="border-t border-gray-100 pt-2">
            <div className="font-semibold text-gray-500 uppercase tracking-wide mb-1.5">Camera</div>
            <button onClick={()=>viewerRef.current?._vliegNaarBoor?.()} className="w-full mb-1 py-1.5 bg-gray-50 hover:bg-gray-100 rounded-lg border border-gray-200 text-gray-700">📍 Vlieg naar boorlijn</button>
            <button onClick={()=>viewerRef.current?._vogel?.()} className="w-full py-1.5 bg-gray-50 hover:bg-gray-100 rounded-lg border border-gray-200 text-gray-700">🔭 Vogelperspectief</button>
          </div>
          <div className="border-t border-gray-100 pt-2">
            <div className="font-semibold text-gray-500 uppercase tracking-wide mb-1">Navigatie</div>
            <div className="text-gray-400 leading-relaxed">🖱 Links+sleep = draaien<br/>⚙ Rechts+sleep = kantelen<br/>🔍 Scroll = zoom</div>
          </div>
          <div className="mt-auto">
            {status==="laden"&&<span className="text-indigo-500 animate-pulse">⏳ Laden…</span>}
            {status==="klaar"&&<span className="text-green-600">✓ Klaar</span>}
            {status==="fout"&&<span className="text-red-500">✗ Fout — zie console</span>}
          </div>
        </div>

        {/* Cesium container */}
        <div className="flex-1 min-w-0 rounded-xl overflow-hidden border border-gray-200 shadow-sm bg-black relative">
          <div ref={containerRef} style={{width:"100%",height:"100%"}}/>
          <BoorLabel boringConfig={boringConfig} boorlengte={project?.boorlengte_m} traceGeojson={project?.boortrace_geojson} projectId={project?.id} step="8" />
          <LockButton locked={locked} onToggle={()=>setLocked(l=>!l)}/>
          {status==="laden"&&(
            <div className="absolute inset-0 flex items-center justify-center bg-gray-900/80 z-10">
              <div className="text-center">
                <div className="text-white text-lg font-semibold mb-1">3D ontwerp laden…</div>
                <div className="text-gray-300 text-sm">CesiumJS · PDOK luchtfoto · KLIC · 3D BAG</div>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
