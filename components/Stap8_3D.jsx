"use client";
import { useEffect, useRef, useState, useMemo } from "react";

// ─── RD → WGS84 ──────────────────────────────────────────────────
function rdNaarWgs84(x, y) {
  const dX=(x-155000)/100000, dY=(y-463000)/100000;
  const lat=52.15517440+(3235.65389*dY-32.58297*dX*dX-0.24750*dY*dY-0.84978*dX*dX*dY-0.06550*dY*dY*dY)/3600;
  const lng=5.38720621+(5260.52916*dX+105.94684*dX*dY+2.45656*dX*dY*dY-0.81885*dX*dX*dX)/3600;
  return [lng, lat];
}

// NAP → hoogte boven WGS84 ellipsoïde (NL gemiddeld: +44.1m)
const NAP_OFFSET = 44.1;
function napNaarCesium(nap) { return nap + NAP_OFFSET; }

// Bereken positie op afstand langs polyline
function positieOpAfstand(coords, afstand) {
  let cumul = 0;
  for (let i = 0; i < coords.length - 1; i++) {
    const [lat1,lng1]=coords[i],[lat2,lng2]=coords[i+1];
    const R=6371000, toRad=d=>d*Math.PI/180;
    const dLat=toRad(lat2-lat1), dLng=toRad(lng2-lng1);
    const a=Math.sin(dLat/2)**2+Math.cos(toRad(lat1))*Math.cos(toRad(lat2))*Math.sin(dLng/2)**2;
    const d=R*2*Math.atan2(Math.sqrt(a),Math.sqrt(1-a));
    if (cumul + d >= afstand) {
      const t = (afstand - cumul) / d;
      return [lat1 + t*(lat2-lat1), lng1 + t*(lng2-lng1)];
    }
    cumul += d;
  }
  return coords[coords.length-1];
}

// Interpoleer hoogte op afstand uit profielPunten
function interpoleerHoogte(profielPunten, afstand) {
  if (!profielPunten?.length) return 0;
  for (let i = 0; i < profielPunten.length - 1; i++) {
    const a = profielPunten[i], b = profielPunten[i+1];
    if (afstand >= a.afstand && afstand <= b.afstand) {
      const t = (afstand - a.afstand) / (b.afstand - a.afstand);
      return a.hoogte + t * (b.hoogte - a.hoogte);
    }
  }
  return profielPunten[profielPunten.length-1]?.hoogte ?? 0;
}

// Interpoleer diepte op afstand uit dieptePunten
function interpoleerDiepte(dieptePunten, afstand) {
  if (!dieptePunten?.length) return 0;
  const s = [...dieptePunten].sort((a,b)=>a.afstand-b.afstand);
  if (afstand <= s[0].afstand) return s[0].diepte;
  if (afstand >= s[s.length-1].afstand) return s[s.length-1].diepte;
  for (let i = 0; i < s.length-1; i++) {
    const a=s[i], b=s[i+1];
    if (afstand >= a.afstand && afstand <= b.afstand) {
      const t=(afstand-a.afstand)/(b.afstand-a.afstand);
      return a.diepte + t*(b.diepte-a.diepte);
    }
  }
  return 0;
}
function parseImklMini(xmlTekst) {
  const doc = new DOMParser().parseFromString(xmlTekst, "text/xml");
  const netThema = {};
  doc.querySelectorAll("Utiliteitsnet").forEach(net => {
    const id = net.getAttribute("gml:id") || net.getAttributeNS?.("http://www.opengis.net/gml/3.2","id") || "";
    const href = net.querySelector("thema")?.getAttributeNS?.("http://www.w3.org/1999/xlink","href") || "";
    if (id) netThema[id] = href.split("/").pop() || "overig";
  });
  const themalagen = {};
  doc.querySelectorAll("UtilityLink").forEach(link => {
    const netHref = (link.querySelector("inNetwork")?.getAttributeNS?.("http://www.w3.org/1999/xlink","href") || "").replace(/^#/,"");
    const thema = netThema[netHref] || "overig";
    const posListEl = link.querySelector("posList");
    if (!posListEl) return;
    const nums = posListEl.textContent.trim().split(/\s+/).map(Number);
    const coords = [];
    for (let i = 0; i+1 < nums.length; i+=2) {
      const x=nums[i],y=nums[i+1];
      const [lng,lat]=(Math.abs(x)>180||Math.abs(y)>90)?rdNaarWgs84(x,y):[x,y];
      coords.push([lng, lat]);
    }
    if (coords.length >= 2) { if (!themalagen[thema]) themalagen[thema]=[]; themalagen[thema].push(coords); }
  });
  return themalagen;
}

const THEMA_CESIUM_KLEUR = {
  laagspanning:"#7B00AA", middenspanning:"#00CCFF", hoogspanning:"#FF4400",
  gasLageDruk:"#FFFF00", gasHogeDruk:"#FF0000", water:"#0000CC",
  datatransport:"#00CC00", rioolVrijverval:"#AA00CC", rioolOnderOverOfOnderdruk:"#AA00CC",
  warmte:"#FF6600", overig:"#888888",
};

// ════════════════════════════════════════════════════════════════════
export default function Stap8_3D({ project }) {
  const containerRef = useRef(null);
  const viewerRef = useRef(null);
  const [ionToken, setIonToken] = useState(() => {
    try { return localStorage.getItem("cesium_ion_token") || ""; } catch { return ""; }
  });
  const [tokenInput, setTokenInput] = useState(ionToken);
  const [tokenOpgeslagen, setTokenOpgeslagen] = useState(false);

  const handleTokenInput = (val) => {
    setTokenInput(val);
    try { localStorage.setItem("cesium_ion_token", val); } catch {}
    setTokenOpgeslagen(true);
    setTimeout(() => setTokenOpgeslagen(false), 2000);
  };
  const [googleToken, setGoogleToken] = useState(() => {
    try { return localStorage.getItem("google_maps_token") || ""; } catch { return ""; }
  });
  const [status, setStatus] = useState("init");
  const [lagen, setLagen] = useState({ boorlijn:true, klic:true, machines:true, gebouwen:false, fotorealistisch:false });

  // Parse project data
  const boorCoords = useMemo(() => {
    try { const g=project?.boortrace_geojson; if(!g)return[]; const p=typeof g==="string"?JSON.parse(g):g; return p.coordinates?.map(([lng,lat])=>[lat,lng])??[]; } catch{return[];}
  }, [project]);

  const ahnProfiel = useMemo(() => {
    try { const r=project?.ahn_profiel; if(!r)return null; const p=typeof r==="string"?JSON.parse(r):r; return Array.isArray(p)?{profielPunten:p,dieptePunten:[]}:p; } catch{return null;}
  }, [project]);

  const machineLocaties = useMemo(() => {
    try { const r=project?.machine_locaties; if(!r)return null; return typeof r==="string"?JSON.parse(r):r; } catch{return null;}
  }, [project]);

  const bestandenMeta = useMemo(() => {
    try { return JSON.parse(project?.bestanden_meta||"[]"); } catch{return[];}
  }, [project]);

  // ── Init Cesium ────────────────────────────────────────────────
  useEffect(() => {
    if (typeof window==="undefined" || viewerRef.current || !containerRef.current) return;
    let actief = true;
    setStatus("laden");

    (async () => {
      // Laad Cesium CSS + JS
      if (!document.querySelector('link[href*="cesium"]')) {
        const link = document.createElement("link");
        link.rel="stylesheet"; link.href="https://cesium.com/downloads/cesiumjs/releases/1.120/Build/Cesium/Widgets/widgets.css";
        document.head.appendChild(link);
      }
      if (!window.Cesium) {
        await new Promise((ok,er)=>{ const s=document.createElement("script"); s.src="https://cesium.com/downloads/cesiumjs/releases/1.120/Build/Cesium/Cesium.js"; s.onload=ok; s.onerror=er; document.head.appendChild(s); });
      }
      if (!actief || !containerRef.current) return;
      const C = window.Cesium;

      // Ion token
      if (ionToken) C.Ion.defaultAccessToken = ionToken;

      // Terrein
      let terrein;
      try {
        if (ionToken) {
          terrein = await C.CesiumTerrainProvider.fromIonAssetId(1);
        } else {
          terrein = new C.EllipsoidTerrainProvider();
        }
      } catch { terrein = new C.EllipsoidTerrainProvider(); }

      if (!actief || !containerRef.current) return;

      // Viewer
      const viewer = new C.Viewer(containerRef.current, {
        terrainProvider: terrein,
        baseLayerPicker: false,
        navigationHelpButton: false,
        animation: false,
        timeline: false,
        geocoder: false,
        sceneModePicker: true,
        homeButton: false,
        infoBox: true,
        selectionIndicator: false,
        fullscreenButton: false,
        creditContainer: document.createElement("div"),
      });
      viewerRef.current = viewer;

      // PDOK luchtfoto als imagery
      viewer.imageryLayers.removeAll();
      viewer.imageryLayers.addImageryProvider(
        new C.WebMapServiceImageryProvider({
          url: "https://service.pdok.nl/hwh/luchtfotorgb/wms/v1_0",
          layers: "2023_orthoHR",
          parameters: { format:"image/jpeg", transparent:false },
          rectangle: C.Rectangle.fromDegrees(3.2,50.7,7.3,53.7),
        })
      );

      viewer.scene.globe.depthTestAgainstTerrain = true;
      viewer.scene.screenSpaceCameraController.enableCollisionDetection = false;

      // ── Boorlijn ────────────────────────────────────────────────
      if (boorCoords.length >= 2) {
        const pp = ahnProfiel?.profielPunten ?? [];
        const dp = ahnProfiel?.dieptePunten ?? [];
        const sorted = [...dp].sort((a,b)=>a.afstand-b.afstand);

        // Oppervlak-lijn — geclamped, altijd op maaiveld
        viewer.entities.add({
          name:"Boorlijn maaiveld",
          polyline:{
            positions: C.Cartesian3.fromDegreesArray(
              boorCoords.flatMap(([lat,lng])=>[lng,lat])
            ),
            width:4, clampToGround:true,
            material: C.Color.fromCssColorString("#f97316"),
          }
        });

        // 3D bore — gebruik ALLE profielPunten met geïnterpoleerde diepte
        // Zo krijg je 18+ punten ipv 2-4 → echte curve
        if (pp.length >= 2 && sorted.length >= 2) {
          const bore3DPts = pp.flatMap(profPt => {
            const diepte = interpoleerDiepte(sorted, profPt.afstand);
            // AHN hoogte (NAP) - diepte = bore NAP hoogte → + offset = boven WGS84 ellipsoïde
            const absH = napNaarCesium(profPt.hoogte - diepte);
            const [lat,lng] = positieOpAfstand(boorCoords, profPt.afstand);
            return [lng, lat, absH];
          });

          // Ondergrondse buis — gloeiend oranje, gestippeld achter terrein
          viewer.entities.add({
            name:"Boorlijn diepteprofiel",
            polyline:{
              positions: C.Cartesian3.fromDegreesArrayHeights(bore3DPts),
              width:8,
              material: new C.PolylineGlowMaterialProperty({
                glowPower:0.3, taperPower:0.0,
                color:C.Color.fromCssColorString("#f97316"),
              }),
              depthFailMaterial: new C.PolylineDashMaterialProperty({
                color:C.Color.fromCssColorString("#fb923c").withAlpha(0.7),
                dashLength:18, dashPattern:255,
              }),
              arcType: C.ArcType.NONE,
            }
          });

          // Verticale diepte-lijnen van bore → maaiveld bij elke waypoint
          sorted.forEach(pt=>{
            const [lat,lng]=positieOpAfstand(boorCoords,pt.afstand);
            const mvH=napNaarCesium(interpoleerHoogte(pp,pt.afstand));
            const boorH=napNaarCesium(interpoleerHoogte(pp,pt.afstand)-pt.diepte);
            if(pt.diepte < 0.1)return;
            viewer.entities.add({ polyline:{
              positions:C.Cartesian3.fromDegreesArrayHeights([lng,lat,boorH,lng,lat,mvH]),
              width:1.5,
              material:new C.PolylineDashMaterialProperty({
                color:C.Color.fromCssColorString("#f97316").withAlpha(0.45),dashLength:10
              }),
            }});
            // Diepte-label
            viewer.entities.add({ position:C.Cartesian3.fromDegrees(lng,lat,boorH-1),
              label:{text:`-${pt.diepte.toFixed(1)}m\n${(interpoleerHoogte(pp,pt.afstand)-pt.diepte).toFixed(2)}m NAP`,
                font:"bold 11px sans-serif",fillColor:C.Color.WHITE,
                outlineColor:C.Color.BLACK,outlineWidth:2,
                style:C.LabelStyle.FILL_AND_OUTLINE,
                verticalOrigin:C.VerticalOrigin.TOP,
                pixelOffset:new C.Cartesian2(0,5),
                disableDepthTestDistance:1000,
                scale:0.9}});
          });
        }

        // Start/einde markers
        const [sLat,sLng]=boorCoords[0],[eLat,eLng]=boorCoords[boorCoords.length-1];
        const startH=napNaarCesium(ahnProfiel?.profielPunten?.[0]?.hoogte??0);
        const eindeH=napNaarCesium(ahnProfiel?.profielPunten?.[ahnProfiel.profielPunten.length-1]?.hoogte??0);
        viewer.entities.add({position:C.Cartesian3.fromDegrees(sLng,sLat,startH+5),
          point:{pixelSize:12,color:C.Color.fromCssColorString("#16a34a"),outlineColor:C.Color.WHITE,outlineWidth:2},
          label:{text:"S",font:"bold 14px sans-serif",fillColor:C.Color.fromCssColorString("#16a34a"),
            outlineColor:C.Color.BLACK,outlineWidth:2,style:C.LabelStyle.FILL_AND_OUTLINE,
            verticalOrigin:C.VerticalOrigin.BOTTOM,pixelOffset:new C.Cartesian2(0,-8)}});
        viewer.entities.add({position:C.Cartesian3.fromDegrees(eLng,eLat,eindeH+5),
          point:{pixelSize:12,color:C.Color.fromCssColorString("#dc2626"),outlineColor:C.Color.WHITE,outlineWidth:2},
          label:{text:"E",font:"bold 14px sans-serif",fillColor:C.Color.fromCssColorString("#dc2626"),
            outlineColor:C.Color.BLACK,outlineWidth:2,style:C.LabelStyle.FILL_AND_OUTLINE,
            verticalOrigin:C.VerticalOrigin.BOTTOM,pixelOffset:new C.Cartesian2(0,-8)}});
      }

      // ── Machine-locaties ────────────────────────────────────────
      if (machineLocaties) {
        const MACH_CFG={
          boormachine:{kleur:"#3b82f6",icon:"🏗️",hoogte:3,label:"HDD Boormachine"},
          bentoniet:  {kleur:"#f97316",icon:"🛢️",hoogte:2.5,label:"Bentoniet & opvangput"},
        };
        Object.entries(machineLocaties).forEach(([key,m])=>{
          if (!m?.centerRD) return;
          const cfg=MACH_CFG[key]; if(!cfg)return;
          const [lo,la]=rdNaarWgs84(m.centerRD.x,m.centerRD.y);

          // Zoek dichtstbijzijnde profielPunt voor de hoogte
          const pp2=ahnProfiel?.profielPunten??[];
          let bestH=napNaarCesium(1); // fallback: 1m NAP
          if(pp2.length>0){
            // Gebruik eerste of laatste profielPunt afhankelijk van machine type
            const refPt = key==="boormachine" ? pp2[0] : pp2[pp2.length-1];
            bestH = napNaarCesium(refPt?.hoogte??1);
          }

          // Bearing langs boorlijn voor oriëntatie rechthoek
          const bear=boorCoords.length>=2 ?
            Math.atan2(
              boorCoords[boorCoords.length-1][1]-boorCoords[0][1],
              boorCoords[boorCoords.length-1][0]-boorCoords[0][0]
            ) : 0;

          const hl=m.lengte/2, hb=m.breedte/2;
          const sinB=Math.sin(bear+Math.PI/2), cosB=Math.cos(bear+Math.PI/2);
          const corners=[[-hl,hb],[-hl,-hb],[hl,-hb],[hl,hb]].map(([a,p])=>{
            const [clo,cla]=rdNaarWgs84(m.centerRD.x+a*cosB-p*sinB, m.centerRD.y+a*sinB+p*cosB);
            return C.Cartesian3.fromDegrees(clo,cla,bestH);
          });

          // 3D box — lage opake rand, subtiele fill
          viewer.entities.add({
            name:`${cfg.icon} ${cfg.label}`,
            polygon:{
              hierarchy: new C.PolygonHierarchy(corners),
              height: bestH,
              extrudedHeight: bestH + cfg.hoogte,
              material: C.Color.fromCssColorString(cfg.kleur).withAlpha(0.35),
              outline:true,
              outlineColor: C.Color.fromCssColorString(cfg.kleur),
              outlineWidth:3,
              closeTop:true, closeBottom:true,
            }
          });
          // Label boven de box
          viewer.entities.add({
            position:C.Cartesian3.fromDegrees(lo,la,bestH+cfg.hoogte+3),
            label:{
              text:`${cfg.icon} ${cfg.label}\n${m.lengte}×${m.breedte}m`,
              font:"bold 11px sans-serif",
              fillColor:C.Color.fromCssColorString(cfg.kleur),
              outlineColor:C.Color.BLACK,outlineWidth:2,
              style:C.LabelStyle.FILL_AND_OUTLINE,
              disableDepthTestDistance:2000,
              horizontalOrigin:C.HorizontalOrigin.CENTER,
            }
          });
        });
      }

      // ── KLIC leidingen ──────────────────────────────────────────
      const zipBestanden = bestandenMeta.filter(b=>b.naam?.toLowerCase().endsWith(".zip")&&b.url);
      if (zipBestanden.length > 0) {
        try {
          const JSZip = await (async ()=>{ if(window.JSZip)return window.JSZip; await new Promise((ok,er)=>{const s=document.createElement("script");s.src="https://cdnjs.cloudflare.com/ajax/libs/jszip/3.10.1/jszip.min.js";s.onload=ok;s.onerror=er;document.head.appendChild(s);}); return window.JSZip; })();
          for (const bestand of zipBestanden) {
            if (!actief) break;
            try {
              const res=await fetch(bestand.url); if(!res.ok)continue;
              const blob=await res.blob();
              const zip=await JSZip.loadAsync(blob);
              const xmlNaam=Object.keys(zip.files).find(n=>n.includes("GI_gebiedsinformatie")&&n.endsWith(".xml"));
              if (!xmlNaam) continue;
              const xml=await zip.files[xmlNaam].async("string");
              const themalagen=parseImklMini(xml);
              Object.entries(themalagen).forEach(([thema,lijnen])=>{
                const kleur=THEMA_CESIUM_KLEUR[thema]??"#888888";
                lijnen.forEach(lijn=>{
                  const pts=lijn.flatMap(([lo,la])=>[lo,la,napNaarCesium(0.5)]);
                  if(pts.length<6)return;
                  viewer.entities.add({ polyline:{
                    positions:C.Cartesian3.fromDegreesArrayHeights(pts),
                    width:3, clampToGround:true,
                    material:C.Color.fromCssColorString(kleur),
                  }});
                });
              });
            } catch(e){ console.warn("KLIC 3D:",e.message); }
          }
        } catch(e){ console.warn("KLIC 3D laden:",e); }
      }

      // ── OSM 3D Gebouwen (gratis via Ion) ───────────────────────
      let osmTileset = null;
      try {
        osmTileset = await C.createOsmBuildingsAsync();
        osmTileset.show = false; // standaard uit
        viewer.scene.primitives.add(osmTileset);
        kaart._osmTileset = osmTileset;
      } catch(e) { console.warn("OSM buildings:", e.message); }

      // ── Google Photorealistisch (gebouwen + bomen) ──────────────
      let googleTileset = null;
      if (googleToken) {
        try {
          googleTileset = await C.createGooglePhotorealistic3DTileset(googleToken);
          googleTileset.show = false;
          viewer.scene.primitives.add(googleTileset);
          kaart._googleTileset = googleTileset;
          // Schakel terrein en eigen imagery uit bij Google tiles
          viewer.scene.globe.show = !googleTileset.show;
        } catch(e) { console.warn("Google 3D tiles:", e.message); }
      }
      kaart._googleToken = googleToken;
      if (boorCoords.length >= 2) {
        const mid=boorCoords[Math.floor(boorCoords.length/2)];
        viewer.camera.flyTo({
          destination: C.Cartesian3.fromDegrees(mid[1], mid[0], 200),
          orientation:{ heading:C.Math.toRadians(0), pitch:C.Math.toRadians(-35), roll:0 },
          duration: 2,
        });
      }

      setStatus("klaar");
    })().catch(e=>{ console.error("Cesium init:",e); setStatus("fout"); });

    return () => {
      actief=false;
      if (viewerRef.current) { try { viewerRef.current.destroy(); } catch {} viewerRef.current=null; }
    };
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ionToken]);

  // Laag-toggles
  useEffect(() => {
    const v=viewerRef.current; if(!v)return;
    // OSM gebouwen
    if(v._osmTileset) v._osmTileset.show = lagen.gebouwen && !lagen.fotorealistisch;
    // Google fotorealistisch — verbergt globe, toont alles
    if(v._googleTileset){
      v._googleTileset.show = lagen.fotorealistisch;
      v.scene.globe.show = !lagen.fotorealistisch;
      v.imageryLayers.show = !lagen.fotorealistisch;
    }
    // OSM uit als fotorealistisch aan
    if(lagen.fotorealistisch && v._osmTileset) v._osmTileset.show = false;
  }, [lagen]);

  const slaTokenOp = () => {
    try { localStorage.setItem("cesium_ion_token", tokenInput); } catch {}
    if (viewerRef.current) { try { viewerRef.current.destroy(); } catch {} viewerRef.current=null; }
    setStatus("init");
    setIonToken(tokenInput);
  };

  return (
    <div className="space-y-3">
      {/* Ion token banner */}
      <div className="bg-white border border-gray-200 rounded-xl px-4 py-3 flex flex-wrap items-center gap-3">
        <div className="flex-1 min-w-0">
          <div className="text-xs font-semibold text-gray-700">🌍 Cesium Ion token <span className="text-gray-400 font-normal">(optioneel — voor echt 3D terrein)</span></div>
          <div className="text-xs text-gray-400 mt-0.5">Gratis token via <a href="https://ion.cesium.com" target="_blank" className="text-blue-500 underline">ion.cesium.com</a> → "Access Tokens" → kopieer de default token</div>
        </div>
        <div className="flex gap-2">
          <input value={tokenInput} onChange={e=>handleTokenInput(e.target.value)} placeholder="eyJhbGci..."
            className="border border-gray-200 rounded-lg px-3 py-1.5 text-xs w-52 focus:outline-none focus:ring-1 focus:ring-indigo-400 font-mono"/>
          <span className={`text-xs transition-opacity duration-500 ${tokenOpgeslagen?"text-green-500 opacity-100":"opacity-0"}`}>✓ opgeslagen</span>
          <button onClick={slaTokenOp} className="px-3 py-1.5 bg-indigo-600 text-white text-xs font-semibold rounded-lg hover:bg-indigo-700 whitespace-nowrap">
            {ionToken?"↺ Herladen":"Activeren"}
          </button>
        </div>
      </div>

      {/* Kaart + laag-panel */}
      <div className="flex gap-3" style={{height:"calc(100vh - 220px)",minHeight:500}}>
        {/* Laag-controls */}
        <div className="w-52 flex-shrink-0 bg-white border border-gray-200 rounded-xl p-4 flex flex-col gap-3">
          <div className="text-xs font-semibold text-gray-700 uppercase tracking-wide">Lagen</div>
          {[
            ["boorlijn","🟠 Boorlijn & diepteprofiel"],
            ["klic","🔌 KLIC leidingen"],
            ["machines","🏗️ Machine-locaties"],
          ].map(([key,label])=>(
            <label key={key} className="flex items-center gap-2 cursor-pointer">
              <input type="checkbox" checked={!!lagen[key]} onChange={e=>setLagen(p=>({...p,[key]:e.target.checked}))} className="accent-indigo-600"/>
              <span className="text-xs text-gray-700">{label}</span>
            </label>
          ))}

          <div className="border-t border-gray-100 pt-2">
            <div className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-2">3D Omgeving</div>
            <label className="flex items-center gap-2 cursor-pointer mb-1">
              <input type="checkbox" checked={!!lagen.gebouwen} onChange={e=>setLagen(p=>({...p,gebouwen:e.target.checked,fotorealistisch:false}))} className="accent-indigo-600"/>
              <span className="text-xs text-gray-700">🏠 OSM Gebouwen <span className="text-gray-400">(Ion)</span></span>
            </label>
            <label className="flex items-center gap-2 cursor-pointer">
              <input type="checkbox" checked={!!lagen.fotorealistisch} disabled={!googleToken} onChange={e=>setLagen(p=>({...p,fotorealistisch:e.target.checked,gebouwen:false}))} className="accent-indigo-600 disabled:opacity-40"/>
              <span className={`text-xs ${googleToken?"text-gray-700":"text-gray-400"}`}>🌳 Google Fotorealistisch <span className="text-gray-400">(bomen+huizen)</span></span>
            </label>
            {!googleToken&&(
              <div className="mt-1.5 text-xs text-amber-600 leading-tight">Voer Google Maps API key in om in te schakelen →</div>
            )}
          </div>

          <div className="border-t border-gray-100 pt-2">
            <div className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-1.5">Google Maps API key</div>
            <div className="text-xs text-gray-400 mb-1.5 leading-tight">Voor 3D bomen & gebouwen. Gratis via <a href="https://console.cloud.google.com" target="_blank" className="text-blue-500 underline">console.cloud.google.com</a> → "Map Tiles API"</div>
            <input value={googleToken} onChange={e=>setGoogleToken(e.target.value)} placeholder="AIza..."
              className="w-full border border-gray-200 rounded px-2 py-1 text-xs font-mono focus:outline-none focus:ring-1 focus:ring-indigo-400 mb-1"/>
            <button onClick={()=>{
              try{localStorage.setItem("google_maps_token",googleToken);}catch{}
              if(viewerRef.current){try{viewerRef.current.destroy();}catch{}viewerRef.current=null;}
              setStatus("init"); setIonToken(t=>t);
            }} className="w-full py-1 text-xs bg-gray-100 hover:bg-gray-200 rounded border border-gray-200 text-gray-600">Toepassen & herladen</button>
          </div>
          <div className="border-t border-gray-100 pt-3 space-y-1.5">
            <div className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-1">Navigatie</div>
            <div className="text-xs text-gray-400 leading-relaxed">🖱️ Klik+sleep = draaien<br/>⚙️ Rechts+sleep = kantelen<br/>🔍 Scroll = zoom</div>
          </div>
          <div className="mt-auto">
            {status==="laden"&&<div className="text-xs text-indigo-600 animate-pulse">⏳ Laden…</div>}
            {status==="klaar"&&<div className="text-xs text-green-600">✓ Klaar</div>}
            {status==="fout"&&<div className="text-xs text-red-500">✗ Fout bij laden</div>}
            {!ionToken&&status==="klaar"&&<div className="text-xs text-amber-500 mt-1">⚠ Geen terrein — voer Ion token in voor AHN</div>}
          </div>
        </div>

        {/* Cesium container */}
        <div className="flex-1 min-w-0 rounded-xl overflow-hidden border border-gray-200 shadow-sm bg-black relative">
          <div ref={containerRef} style={{width:"100%",height:"100%"}}/>
          {status==="laden"&&(
            <div className="absolute inset-0 flex items-center justify-center bg-gray-900/80 z-10">
              <div className="text-center">
                <div className="text-white text-lg font-semibold mb-2">3D wereld laden…</div>
                <div className="text-gray-300 text-sm">CesiumJS · PDOK luchtfoto · KLIC leidingen</div>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
