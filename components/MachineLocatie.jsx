"use client";
import BoorLabel, { LockButton } from "@/components/BoorLabel";
import { useEffect, useRef, useState, useCallback, useMemo } from "react";
import KlicAchtergrond from "@/components/KlicAchtergrond";

// ─── RD/WGS84 helpers ────────────────────────────────────────────
function rdNaarLatLng(x,y){
  const dX=(x-155000)/100000,dY=(y-463000)/100000;
  const lat=52.15517440+(3235.65389*dY-32.58297*dX*dX-0.24750*dY*dY-0.84978*dX*dX*dY-0.06550*dY*dY*dY)/3600;
  const lng=5.38720621+(5260.52916*dX+105.94684*dX*dY+2.45656*dX*dY*dY-0.81885*dX*dX*dX)/3600;
  return [lng,lat];
}
function latLngNaarRD(lat,lng){
  const dLat=0.36*(lat-52.15517440),dLon=0.36*(lng-5.38720621);
  return{x:155000+190094.945*dLon-11832.228*dLon*dLat-114.221*dLon*dLat*dLat-32.391*dLon*dLon*dLon,
         y:463000+309056.544*dLat+60940.388*dLon*dLon*dLat-9.941*dLon*dLon-2.340*dLat*dLat*dLat};
}

function berekenBearing(p1,p2){
  const lat1=p1[0]*Math.PI/180,lat2=p2[0]*Math.PI/180;
  const dLon=(p2[1]-p1[1])*Math.PI/180;
  const x=Math.sin(dLon)*Math.cos(lat2),y=Math.cos(lat1)*Math.sin(lat2)-Math.sin(lat1)*Math.cos(lat2)*Math.cos(dLon);
  return((Math.atan2(x,y)*180/Math.PI)+360)%360;
}

// Maak rechthoek-polygoon in Leaflet [lat,lng][] geroteerd langs de bore-richting
function maakRechthoekCoords(centerRD,lengteM,breedteM,bearingDeg){
  const B=bearingDeg*Math.PI/180,sinB=Math.sin(B),cosB=Math.cos(B);
  const hl=lengteM/2,hb=breedteM/2;
  return[[hl,hb],[hl,-hb],[-hl,-hb],[-hl,hb]].map(([along,perp])=>{
    const rdX=centerRD.x+along*sinB+perp*cosB;
    const rdY=centerRD.y+along*cosB-perp*sinB;
    const[lng,lat]=rdNaarLatLng(rdX,rdY);
    return[lat,lng];
  });
}

function maakRdCrs(L){
  return new L.Proj.CRS("EPSG:28992","+proj=sterea +lat_0=52.15517440 +lon_0=5.38720621 +k=0.9999079 +x_0=155000 +y_0=463000 +ellps=bessel +towgs84=565.417,50.3319,465.552,-0.398957,0.343988,-1.8774,4.0725 +units=m +no_defs",
    {resolutions:[3440.640,1720.320,860.160,430.080,215.040,107.520,53.760,26.880,13.440,6.720,3.360,1.680,0.840,0.420,0.210,0.105,0.0525,0.02625,0.013125,0.00656,0.00328,0.00164,0.00082],
     origin:[-285401.920,903401.920],bounds:L.bounds([-285401.920,22598.080],[595401.920,903401.920])});
}

// ─── Machine configuratie ─────────────────────────────────────────
const MACHINE_CONFIG={
  boormachine:{label:"HDD Boormachine",kleur:"#3b82f6",kleurFill:"rgba(59,130,246,0.15)",icon:"🏗️"},
  bentoniet:  {label:"Bentoniet & opvangput",kleur:"#f97316",kleurFill:"rgba(249,115,22,0.15)",icon:"🛢️"},
};

// ─── Hoofd-component ──────────────────────────────────────────────
export default function MachineLocatie({project,onSave,boringConfig}){
  const mapRef=useRef(null);
  const kaartRef=useRef(null);
  const [kaartInstantie,setKaartInstantie]=useState(null);
  const [locked,setLocked]=useState(() => {
    try { const s = localStorage.getItem(`boor_lock_${project?.id}_7`); return s ? JSON.parse(s) : false; } catch { return false; }
  });
  useEffect(() => {
    try { localStorage.setItem(`boor_lock_${project?.id}_7`, JSON.stringify(locked)); } catch {}
  }, [locked]);

  const [boorCoords]=useState(()=>{
    try{const g=project?.boortrace_geojson;if(!g)return[];const p=typeof g==="string"?JSON.parse(g):g;return p.coordinates?.map(([lng,lat])=>[lat,lng])??[];}catch{return[];}
  });

  const bearing=useMemo(()=>boorCoords.length>=2?berekenBearing(boorCoords[0],boorCoords[boorCoords.length-1]):0,[boorCoords]);
  const [geroteerd,setGeroteerd]=useState(true);
  const rotatieDeg=useMemo(()=>{let r=(90-bearing+360)%360;if(r>180)r-=360;return r;},[bearing]);

  // Machine-afmetingen en posities
  const [machines,setMachines]=useState(()=>{
    try{
      const s=project?.machine_locaties;
      if(s){const p=typeof s==="string"?JSON.parse(s):s;if(p)return p;}
    }catch{}
    return{
      boormachine:{centerRD:null,lengte:6,breedte:3},
      bentoniet:  {centerRD:null,lengte:4,breedte:2.5},
    };
  });

  const [plaatsModus,setPlaatsModus]=useState(null); // "boormachine"|"bentoniet"|null
  const mapLS7 = () => { try { return JSON.parse(localStorage.getItem(`map_s_${project?.id}_7`)||'null'); } catch { return null; } };
  const mapSave7 = (p) => { try { const SK=`map_s_${project?.id}_7`; const cur=JSON.parse(localStorage.getItem(SK)||'{}'); localStorage.setItem(SK,JSON.stringify({...cur,...p})); } catch {} };
  const _ls7 = mapLS7();
  const [actieveAchtergrond,setActieveAchtergrond]=useState(_ls7?.ag??"luchtfoto");
  const [actieveOverlays,setActieveOverlays]=useState(_ls7?.ov??{klic:true,kadaster:false,bgt:false});
  useEffect(()=>{ mapSave7({ag:actieveAchtergrond}); },[actieveAchtergrond]);
  useEffect(()=>{ mapSave7({ov:actieveOverlays}); },[actieveOverlays]);
  const [opslaanStatus,setOpslaanStatus]=useState(null);

  const plaatsModusRef=useRef(null);
  plaatsModusRef.current=plaatsModus;
  const machinesRef=useRef(machines);
  machinesRef.current=machines;
  const bearingRef=useRef(bearing);
  bearingRef.current=bearing;
  const rotDegRef=useRef(0);
  rotDegRef.current=geroteerd?rotatieDeg:0;
  const prevGeroteerdRef=useRef(false); // herstel rotatie na plaatsen

  // Auto-switch naar Noord-omhoog bij plaatsen (correcte klikpositie)
  const startPlaatsen=(key)=>{
    if(geroteerd){prevGeroteerdRef.current=true;setGeroteerd(false);}
    else{prevGeroteerdRef.current=false;}
    setPlaatsModus(key);
  };

  // ── Kaart init ────────────────────────────────────────────────
  useEffect(()=>{
    if(typeof window==="undefined"||kaartRef.current||!mapRef.current)return;
    let actief=true;
    (async()=>{
      const ls=src=>new Promise((ok,er)=>{if(document.querySelector(`script[src="${src}"]`))return ok();const s=document.createElement("script");s.src=src;s.onload=ok;s.onerror=er;document.head.appendChild(s);});
      if(!document.querySelector('link[href*="leaflet"]')){const c=document.createElement("link");c.rel="stylesheet";c.href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css";document.head.appendChild(c);}
      await ls("https://unpkg.com/leaflet@1.9.4/dist/leaflet.js");
      await ls("https://cdnjs.cloudflare.com/ajax/libs/proj4js/2.9.0/proj4.js");
      await ls("https://cdnjs.cloudflare.com/ajax/libs/proj4leaflet/1.0.2/proj4leaflet.js");
      if(!actief||!mapRef.current)return;
      const L=window.L;
      const crs=maakRdCrs(L);
      const _ls=mapLS7();
      const center=_ls?.c??boorCoords[0]??[52.15,5.39];
      const kaart=L.map(mapRef.current,{crs,center,zoom:_ls?.z??15,maxZoom:22,zoomControl:true});
      kaart.on("moveend zoomend",()=>{const c=kaart.getCenter();mapSave7({z:kaart.getZoom(),c:[c.lat,c.lng]});});
      kaartRef.current=kaart;
      setKaartInstantie(kaart);

      // Achtergrondlagen
      const ACH={
        standaard:L.tileLayer("https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/standaard/EPSG:28992/{z}/{x}/{y}.png",{maxZoom:22,maxNativeZoom:13,tileSize:256,attribution:"© PDOK BRT",zIndex:1}),
        grijs:    L.tileLayer("https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/grijs/EPSG:28992/{z}/{x}/{y}.png",{maxZoom:22,maxNativeZoom:13,tileSize:256,attribution:"© PDOK BRT",zIndex:1}),
        luchtfoto:L.tileLayer("https://service.pdok.nl/hwh/luchtfotorgb/wmts/v1_0/2023_orthoHR/EPSG:28992/{z}/{x}/{y}.jpeg",{maxZoom:22,maxNativeZoom:13,tileSize:256,attribution:"© PDOK Luchtfoto",zIndex:1}),
      };
      ACH.luchtfoto.addTo(kaart);
      kaart._wisselAchtergrond=(id)=>{Object.values(ACH).forEach(l=>kaart.hasLayer(l)&&kaart.removeLayer(l));ACH[id]?.addTo(kaart);};

      // Overlays
      const OVL={
        kadaster:L.tileLayer.wms("https://service.pdok.nl/kadaster/kadastralekaart/wms/v5_0",{layers:"Perceel",format:"image/png",transparent:true,opacity:0.7,zIndex:11}),
        bgt:L.tileLayer.wms("https://service.pdok.nl/lv/bgt/wms/v1_0",{layers:"wegdeel,waterdeel,pand,begroeidterreindeel",format:"image/png",transparent:true,opacity:0.5,zIndex:12}),
      };
      kaart._toggleOverlay=(id,aan)=>{if(aan)OVL[id]?.addTo(kaart);else if(kaart.hasLayer(OVL[id]))kaart.removeLayer(OVL[id]);};

      // Read-only boorlijn
      if(boorCoords.length>=2){
        const boorGewicht = boringConfig?.boringD ? Math.max(4, Math.min(18, Math.round(boringConfig.boringD / 25))) : 4;
        L.polyline(boorCoords,{color:"#f97316",weight:boorGewicht,opacity:0.8,interactive:false}).addTo(kaart);
        L.circleMarker(boorCoords[0],{radius:8,fillColor:"#16a34a",fillOpacity:1,color:"white",weight:2,interactive:false}).addTo(kaart);
        L.circleMarker(boorCoords[boorCoords.length-1],{radius:8,fillColor:"#dc2626",fillOpacity:1,color:"white",weight:2,interactive:false}).addTo(kaart);
        try{kaart.fitBounds(L.latLngBounds(boorCoords).pad(0.25),{maxZoom:16});}catch{}
      }

      // Machine-rechthoek lagen — gedeelde drag-state voor schone event-afhandeling
      const machLagen={};
      const machDotMarkers={};
      const machLabelMarkers={};
      Object.keys(MACHINE_CONFIG).forEach(key=>{machLagen[key]=null;machDotMarkers[key]=null;machLabelMarkers[key]=null;});

      // Gedeelde drag-state (1 handler op de kaart)
      const ds={active:false,key:null,startLL:null,startCenterRD:null,lengte:0,breedte:0,bear:0};
      kaart.on("mousemove",(e)=>{
        if(!ds.active)return;
        const sRD=latLngNaarRD(ds.startLL.lat,ds.startLL.lng);
        const dRD=latLngNaarRD(e.latlng.lat,e.latlng.lng);
        const nc={x:ds.startCenterRD.x+(dRD.x-sRD.x),y:ds.startCenterRD.y+(dRD.y-sRD.y)};
        machLagen[ds.key]?.setLatLngs(maakRechthoekCoords(nc,ds.lengte,ds.breedte,ds.bear));
        const[lo,la]=rdNaarLatLng(nc.x,nc.y);
        machDotMarkers[ds.key]?.setLatLng([la,lo]);
        machLabelMarkers[ds.key]?.setLatLng([la,lo]);
      });
      kaart.on("mouseup",(e)=>{
        if(!ds.active)return;
        ds.active=false;
        kaart.dragging.enable();
        kaart._container.style.cursor="";
        const sRD=latLngNaarRD(ds.startLL.lat,ds.startLL.lng);
        const dRD=latLngNaarRD(e.latlng.lat,e.latlng.lng);
        const nc={x:ds.startCenterRD.x+(dRD.x-sRD.x),y:ds.startCenterRD.y+(dRD.y-sRD.y)};
        setMachines(prev=>({...prev,[ds.key]:{...prev[ds.key],centerRD:nc}}));
      });

      function updateRechthoek(key,centerRD,lengte,breedte,bear,zoomNaar=false,rotDeg=0){
        // Verwijder oude lagen
        [machLagen,machDotMarkers,machLabelMarkers].forEach(obj=>{
          if(obj[key]){try{kaart.removeLayer(obj[key]);}catch{}obj[key]=null;}
        });
        if(!centerRD)return;

        const cfg=MACHINE_CONFIG[key];
        const coords=maakRechthoekCoords(centerRD,lengte,breedte,bear);
        const[lo,la]=rdNaarLatLng(centerRD.x,centerRD.y);

        // 1. Polygoon — sleepbaar via mousedown
        machLagen[key]=L.polygon(coords,{
          color:cfg.kleur,weight:4,opacity:1,
          fillColor:cfg.kleurFill,fillOpacity:0.45,
          interactive:true,
        }).addTo(kaart);
        machLagen[key].on("mousedown",(e)=>{
          L.DomEvent.stop(e);
          kaart.dragging.disable();
          kaart._container.style.cursor="grabbing";
          Object.assign(ds,{active:true,key,startLL:e.latlng,startCenterRD:{...centerRD},lengte,breedte,bear});
        });

        // 2. Dot IN het midden van de rechthoek
        machDotMarkers[key]=L.circleMarker([la,lo],{
          radius:8,fillColor:cfg.kleur,fillOpacity:1,
          color:"white",weight:2.5,interactive:false,zIndexOffset:500,
        }).addTo(kaart);

        // 3. Label — counter-geroteerd, naast het midden
        const rot=`rotate(${-rotDeg}deg)`;
        const labelIcon=L.divIcon({
          html:`<div style="transform:${rot};transform-origin:8px 50%;display:inline-flex;align-items:center;gap:5px;pointer-events:none">
            <div style="width:0;height:0"></div>
            <div style="background:white;color:${cfg.kleur};border:2px solid ${cfg.kleur};border-radius:6px;padding:3px 8px;font-size:10px;font-weight:700;white-space:nowrap;box-shadow:0 2px 5px rgba(0,0,0,.3)">${cfg.icon} ${lengte}×${breedte}m</div>
          </div>`,
          className:"",iconSize:[1,1],iconAnchor:[0,0],
        });
        machLabelMarkers[key]=L.marker([la,lo],{icon:labelIcon,interactive:false,zIndexOffset:550}).addTo(kaart);

        if(zoomNaar){
          try{kaart.fitBounds(machLagen[key].getBounds().pad(4),{maxZoom:19,animate:true});}catch{}
        }
      }
      kaart._updateRechthoek=updateRechthoek;

      // Klik op kaart = plaatsen (kaart staat al Noord-omhoog, geen rotatie-offset)
      kaart.on("click",(e)=>{
        const key=plaatsModusRef.current;
        if(!key)return;
        const rd=latLngNaarRD(e.latlng.lat,e.latlng.lng);
        const m=machinesRef.current?.[key]??{lengte:6,breedte:3};
        updateRechthoek(key,rd,m.lengte,m.breedte,bearingRef.current??0,true,0);
        setMachines(prev=>{const n={...prev,[key]:{...prev[key],centerRD:rd}};machinesRef.current=n;return n;});
        setPlaatsModus(null);
        if(prevGeroteerdRef.current){setTimeout(()=>setGeroteerd(true),500);}
      });

      // Noord-pijl overlay
      const npiDiv=document.createElement("div");
      npiDiv.style.cssText="position:absolute;top:12px;right:12px;z-index:500;pointer-events:none";
      npiDiv.innerHTML=`<div style="background:white;border:1px solid #e5e7eb;border-radius:50%;width:48px;height:48px;display:flex;align-items:center;justify-content:center;box-shadow:0 1px 4px rgba(0,0,0,.2)"><svg viewBox="0 0 40 40" width="36" height="36"><polygon points="20,4 24,20 20,17 16,20" fill="#dc2626"/><polygon points="20,36 24,20 20,23 16,20" fill="#374151"/><circle cx="20" cy="20" r="3" fill="white" stroke="#9ca3af" strokeWidth="1"/><text x="20" y="3" textAnchor="middle" fontSize="7" fill="#dc2626" fontWeight="700">N</text></svg></div>`;
      mapRef.current.appendChild(npiDiv);
      kaart._noordPijlEl=npiDiv;
      kaart._zetNoordPijlRotatie=(deg)=>{
        npiDiv.querySelector("div").style.transform=`rotate(${-deg}deg)`;
        npiDiv.querySelector("div").style.transition="transform 0.5s ease";
      };
    })();
    return()=>{actief=false;if(kaartRef.current){try{kaartRef.current.remove();}catch{}kaartRef.current=null;}};
  // eslint-disable-next-line react-hooks/exhaustive-deps
  },[]);

  // Sync achtergrond/overlays
  useEffect(()=>{kaartRef.current?._wisselAchtergrond?.(actieveAchtergrond);},[actieveAchtergrond]);
  useEffect(()=>{Object.entries(actieveOverlays).forEach(([id,aan])=>kaartRef.current?._toggleOverlay?.(id,aan));},[actieveOverlays]);

  // Vergrendeling
  useEffect(()=>{
    const map=kaartRef.current; if(!map)return;
    ["dragging","scrollWheelZoom","doubleClickZoom","boxZoom","keyboard","touchZoom"]
      .forEach(m=>{if(map[m])locked?map[m].disable():map[m].enable();});
  },[locked]);

  // Sync rotatie
  useEffect(()=>{
    kaartRef.current?._zetNoordPijlRotatie?.(geroteerd?rotatieDeg:0);
    const t=setTimeout(()=>{try{kaartRef.current?.invalidateSize({animate:false});}catch{}},600);
    return()=>clearTimeout(t);
  },[geroteerd,rotatieDeg]);

  // Sync machine-rechthoeken naar kaart
  useEffect(()=>{
    if(!kaartInstantie?._updateRechthoek)return;
    const rotDeg=geroteerd?rotatieDeg:0;
    Object.keys(machines).forEach(key=>{
      const m=machines[key];
      if(m.centerRD)kaartInstantie._updateRechthoek(key,m.centerRD,m.lengte,m.breedte,bearing,false,rotDeg);
    });
  },[kaartInstantie,machines,bearing,geroteerd,rotatieDeg]);

  // Cursor bij plaatsmodus
  useEffect(()=>{
    if(!mapRef.current)return;
    mapRef.current.style.cursor=plaatsModus?"crosshair":"";
  },[plaatsModus]);

  // Opslaan
  const handleOpslaan=useCallback(async()=>{
    if(!onSave)return;
    setOpslaanStatus("bezig");
    try{await onSave({machine_locaties:machines});setOpslaanStatus("ok");setTimeout(()=>setOpslaanStatus(null),3000);}
    catch(e){setOpslaanStatus("fout");console.error(e);}
  },[machines,onSave]);

  return(
    <div className="space-y-4">
      {kaartInstantie&&<KlicAchtergrond kaart={kaartInstantie} project={project}/>}
      <div className="flex gap-4" style={{height:"calc(100vh - 200px)",minHeight:480}}>

        {/* Sidebar */}
        <div className="w-72 flex-shrink-0 bg-white border border-gray-200 rounded-xl overflow-y-auto flex flex-col">
          <div className="px-4 py-2.5 border-b border-gray-100">
            <span className="text-sm font-semibold text-gray-900">7. Machine- & bentonietlocatie</span>
            <div className="text-xs text-gray-400">Klik op de kaart om te plaatsen</div>
          </div>
          <div className="flex-1 overflow-y-auto px-4 py-3 space-y-4">

            {/* Machine blokken */}
            {Object.entries(MACHINE_CONFIG).map(([key,cfg])=>{
              const m=machines[key];
              const isActief=plaatsModus===key;
              return(
                <div key={key} className="border rounded-xl overflow-hidden" style={{borderColor:cfg.kleur+"44"}}>
                  <div className="px-3 py-2 flex items-center gap-2" style={{background:cfg.kleurFill}}>
                    <span className="text-base">{cfg.icon}</span>
                    <span className="text-xs font-semibold" style={{color:cfg.kleur}}>{cfg.label}</span>
                    <div className="ml-auto w-3 h-3 rounded-full" style={{background:m.centerRD?cfg.kleur:"#d1d5db"}}/>
                  </div>
                  <div className="px-3 py-2 space-y-2">
                    <button onClick={()=>{
                        if(isActief){
                          setPlaatsModus(null);
                          if(prevGeroteerdRef.current)setGeroteerd(true);
                        } else{ startPlaatsen(key); }
                      }}
                      className={`w-full py-2 rounded-lg text-xs font-semibold transition-all ${isActief?"text-white":"border"}`}
                      style={isActief?{background:cfg.kleur}:{borderColor:cfg.kleur+"66",color:cfg.kleur}}>
                      {isActief?"✕ Annuleer":"📍 "+(m.centerRD?"Herplaats":"Plaats op kaart")}
                    </button>
                    {isActief&&<div className="text-xs text-center" style={{color:cfg.kleur}}>↑ Noord-omhoog voor nauwkeurig klikken</div>}
                    {/* Afmetingen */}
                    <div className="grid grid-cols-2 gap-1.5">
                      <div>
                        <div className="text-xs text-gray-400 mb-0.5">Lengte (m)</div>
                        <input type="number" min={1} max={30} step={0.5} value={m.lengte}
                          onChange={e=>setMachines(prev=>({...prev,[key]:{...prev[key],lengte:+e.target.value}}))}
                          className="w-full border border-gray-200 rounded-lg px-2 py-1 text-xs text-center focus:outline-none focus:ring-1"
                          style={{"--tw-ring-color":cfg.kleur}}/>
                      </div>
                      <div>
                        <div className="text-xs text-gray-400 mb-0.5">Breedte (m)</div>
                        <input type="number" min={1} max={20} step={0.5} value={m.breedte}
                          onChange={e=>setMachines(prev=>({...prev,[key]:{...prev[key],breedte:+e.target.value}}))}
                          className="w-full border border-gray-200 rounded-lg px-2 py-1 text-xs text-center focus:outline-none focus:ring-1"/>
                      </div>
                    </div>
                    {m.centerRD&&(
                      <div className="text-xs text-gray-400">
                        {m.lengte}×{m.breedte}m = {(m.lengte*m.breedte).toFixed(1)}m² · Sleep label om te verplaatsen
                      </div>
                    )}
                  </div>
                </div>
              );
            })}

            {/* Rotatie */}
            <button onClick={()=>setGeroteerd(v=>!v)}
              className={`w-full py-2 rounded-xl text-xs font-semibold border transition-all ${geroteerd?"bg-indigo-600 text-white border-indigo-600":"bg-white text-indigo-600 border-indigo-300 hover:bg-indigo-50"}`}>
              {geroteerd?"↑ Noord-omhoog":"↺ Bore-richting (horizontaal)"}
            </button>

            {/* Achtergrond */}
            <div className="border-t border-gray-100 pt-3">
              <div className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-2">Achtergrond</div>
              {[["standaard","BRT Standaard"],["grijs","BRT Grijs"],["luchtfoto","Luchtfoto (HR)"]].map(([id,label])=>(
                <label key={id} className="flex items-center gap-2 cursor-pointer mb-1">
                  <input type="radio" name="ach7" checked={actieveAchtergrond===id} onChange={()=>setActieveAchtergrond(id)} className="accent-orange-500"/>
                  <span className="text-xs text-gray-700">{label}</span>
                </label>
              ))}
            </div>

            {/* Overlays */}
            <div className="border-t border-gray-100 pt-3">
              <div className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-2">Overlays</div>
              {[["klic","KLIC leidingen"],["kadaster","Kadastrale percelen"],["bgt","BGT oppervlakken"]].map(([id,label])=>(
                <label key={id} className="flex items-center gap-2 cursor-pointer mb-1">
                  <input type="checkbox" checked={!!actieveOverlays[id]} onChange={e=>setActieveOverlays(p=>({...p,[id]:e.target.checked}))} className="accent-orange-500"/>
                  <span className="text-xs text-gray-700">{label}</span>
                </label>
              ))}
            </div>

            {/* Opslaan */}
            <div className="border-t border-gray-100 pt-3">
              <button onClick={handleOpslaan} disabled={!onSave||opslaanStatus==="bezig"}
                className={`w-full py-2.5 rounded-xl text-sm font-semibold transition-all ${
                  opslaanStatus==="ok"?"bg-green-500 text-white":opslaanStatus==="fout"?"bg-red-500 text-white":"bg-blue-600 hover:bg-blue-700 text-white shadow-sm"}`}>
                {opslaanStatus==="bezig"?"Opslaan…":opslaanStatus==="ok"?"✓ Opgeslagen!":opslaanStatus==="fout"?"✗ Fout":"💾 Locaties opslaan"}
              </button>
            </div>
          </div>
        </div>

        {/* Kaart */}
        <div className="flex-1 min-w-0 rounded-xl border border-gray-200 overflow-hidden shadow-sm relative bg-gray-100">
          <div style={{position:"absolute",width:"200%",height:"200%",top:"-50%",left:"-50%",
            transform:geroteerd?`rotate(${rotatieDeg}deg)`:"none",
            transition:"transform 0.5s ease",transformOrigin:"center center"}}>
            <div ref={mapRef} style={{width:"100%",height:"100%"}}/>
          </div>
          <BoorLabel boringConfig={boringConfig} boorlengte={project?.boorlengte_m} traceGeojson={project?.boortrace_geojson} leafletMapRef={kaartRef} projectId={project?.id} step="7" initialPos={{x:16,y:60}}/>
          <LockButton locked={locked} onToggle={()=>setLocked(l=>!l)}/>
          {plaatsModus&&(
            <div className="absolute top-3 left-1/2 -translate-x-1/2 z-[500] pointer-events-none">
              <div className="bg-white/95 backdrop-blur-sm rounded-full px-4 py-2 text-xs font-semibold shadow border border-gray-200" style={{color:MACHINE_CONFIG[plaatsModus]?.kleur}}>
                {MACHINE_CONFIG[plaatsModus]?.icon} Klik om {MACHINE_CONFIG[plaatsModus]?.label} te plaatsen
              </div>
            </div>
          )}
          <div className="absolute bottom-3 left-1/2 -translate-x-1/2 z-[500] pointer-events-none">
            <div className="bg-white/85 backdrop-blur-sm rounded-full px-3 py-1 text-xs text-gray-500 shadow border border-gray-100">
              🟢 Start · 🔴 Einde · Sleep label om rechthoek te verplaatsen
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
