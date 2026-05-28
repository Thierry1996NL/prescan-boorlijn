"use client";
import { useState, useEffect, useRef } from "react";

// ─── WMS lagen (alleen bevestigd werkende PDOK URLs + laagnamen) ──────────────
// Bron: GetCapabilities + bevestigd in productie-app
const BRO_WMS = [
  {
    id: "bodemkaart",
    label: "BRO Bodemkaart (SGM)",
    subtitel: "Bodemopbouw tot 1.2m · 1:50.000",
    kleur: "#84CC16", icon: "🌱",
    wmsUrl: "https://service.pdok.nl/tno/bro-bodemkaart/wms/v1_0",
    layer:  "soilarea",          // ✓ bevestigd via GetCapabilities
    opacity: 0.6,
    beschrijving: "Bodemtype, grondsoort en profiel tot 1.2m diepte. Relevant voor insteekhelling en grondsoort bij HDD.",
  },
  {
    id: "grondwater",
    label: "BRO Peilbuizen (GMW)",
    subtitel: "Grondwatermonitoring",
    kleur: "#3B82F6", icon: "💧",
    wmsUrl: "https://service.pdok.nl/tno/bro-grondwatermonitoring-in-samenhang-karakteristieken/wms/v1_0",
    layer:  "grondwatermonitoringput", // ✓ bevestigd
    opacity: 0.9,
    beschrijving: "BRO peilbuislocaties (GMW). Hoge grondwaterstand = opbarstrisico en verminderde boorstabiliteit.",
  },
  {
    id: "geomorfologie",
    label: "BRO Geomorfologie (GMM)",
    subtitel: "Landvormen en processen",
    kleur: "#059669", icon: "🏔️",
    wmsUrl: "https://service.pdok.nl/bzk/bro-geomorfologischekaart/wms/v2_0",
    layer:  "GMM_vlakken",       // meest waarschijnlijk — tileload-error geeft feedback
    opacity: 0.6,
    beschrijving: "Geomorfologische kaart: stuwwallen, duinen, dekzand, veengebieden. Contextueel voor tracé-risico.",
  },
  {
    id: "ahn",
    label: "AHN Hoogtemodel",
    subtitel: "Actueel maaiveld · AHN4",
    kleur: "#F97316", icon: "📏",
    wmsUrl: "https://service.pdok.nl/rws/actueel-hoogtebestand-nederland/wms/v1_0",
    layer:  "dtm_05m",           // ✓ bevestigd
    opacity: 0.5,
    beschrijving: "AHN4 maaiveld. Hoogteverschil bepaalt de vereiste boring-diepte en inslaghoek.",
  },
];

// ─── Download-only lagen (geen WMS beschikbaar op PDOK) ──────────────────────
const BRO_DOWNLOAD = [
  {
    id: "geotop",
    label: "BRO GeoTOP v1.6.1",
    subtitel: "3D voxelmodel ondergrond",
    kleur: "#7C3AED", icon: "🧭",
    atomUrl: "https://service.pdok.nl/bzk/brogtm/atom/v1_1/brogtm.xml",
    broloketUrl: "https://www.broloket.nl/ondergrondmodellen",
    dinoloketUrl: "https://www.dinoloket.nl/ondergrondmodellen/kaart",
    beschrijving: "3D bodemopbouw per 0.5m voxel tot 50m NAP. Zand/klei/veen-verdeling voor de hele boring.",
    tip: "Bekijk GeoTOP op DINOloket → Ondergrondmodellen → GeoTOP, of download via PDOK ATOM.",
  },
  {
    id: "regis",
    label: "BRO REGIS II v2.2.3",
    subtitel: "Hydrogeologisch model",
    kleur: "#0891B2", icon: "💎",
    broloketUrl: "https://www.broloket.nl/ondergrondmodellen",
    dinoloketUrl: "https://www.dinoloket.nl/ondergrondmodellen/kaart",
    beschrijving: "Hydraulische doorlatendheid per laag. Hoge k-waarde = kans op spoeling-verlies bij HDD.",
    tip: "REGIS II is beschikbaar op DINOloket en BROloket als interactieve kaart en download.",
  },
  {
    id: "dgm",
    label: "BRO DGM v2.2.1",
    subtitel: "Digitaal Geologisch Model",
    kleur: "#B45309", icon: "🪨",
    broloketUrl: "https://www.broloket.nl/ondergrondmodellen",
    dinoloketUrl: "https://www.dinoloket.nl/ondergrondmodellen/kaart",
    beschrijving: "Geologische laagvolgorde en formaties. Geeft inzicht in diepe ondergrondopbouw.",
    tip: "DGM is beschikbaar op DINOloket en als download via BROloket.",
  },
  {
    id: "gwsd",
    label: "BRO Grondwaterspiegeldiepte",
    subtitel: "GHG/GLG model",
    kleur: "#0EA5E9", icon: "🌊",
    broloketUrl: "https://www.broloket.nl/ondergrondmodellen",
    beschrijving: "Gemiddeld hoogste en laagste grondwaterstand. Kritisch voor opbarst-risico en stabiele boring.",
    tip: "Beschikbaar op BROloket als download en interactieve kaart.",
  },
];

// ─── Puntdata typen ───────────────────────────────────────────────────────────
const PUNT_TYPEN = {
  cpt: { label: "Sonderingen (CPT)", kleur: "#F97316", icon: "▼", omschrijving: "Kegelweerstand per diepte" },
  bhr: { label: "Boorprofielen",     kleur: "#7C3AED", icon: "⬡", omschrijving: "Grondlagen per diepte" },
  gmw: { label: "Peilbuizen (GMW)",  kleur: "#2563EB", icon: "◉", omschrijving: "Grondwaterstand monitoring" },
};

export default function BROlaag({ project, kaartRef }) {
  const [puntData,    setPuntData]    = useState(null);
  const [bezig,       setBezig]       = useState(false);
  const [fout,        setFout]        = useState(null);
  const [wmsAan,      setWmsAan]      = useState({});
  const [laagFout,    setLaagFout]    = useState({});
  const [puntAan,     setPuntAan]     = useState({ cpt:true, bhr:true, gmw:true });
  const [geselecteerd,setGeselecteerd]= useState(null);
  const [sectie,      setSectie]      = useState("wms"); // wms | download | punten
  const wmsLagenRef = useRef({});
  const puntLagenRef = useRef({});

  // ── WMS toggle ────────────────────────────────────────────────────
  function toggleWms(laag) {
    const map = kaartRef?.current;
    if (!map || !window.L) return;
    const L = window.L;

    if (wmsLagenRef.current[laag.id]) {
      map.removeLayer(wmsLagenRef.current[laag.id]);
      delete wmsLagenRef.current[laag.id];
      setWmsAan(v => ({ ...v, [laag.id]: false }));
      return;
    }

    if (!map.getPane("broPane")) {
      map.createPane("broPane");
      map.getPane("broPane").style.zIndex = 280;
      map.getPane("broPane").style.pointerEvents = "auto";
    }

    const l = L.tileLayer.wms(laag.wmsUrl, {
      layers: laag.layer, format: "image/png", transparent: true,
      opacity: laag.opacity, version: "1.1.1", pane: "broPane",
    });
    l.on("tileerror", () => setLaagFout(v => ({ ...v, [laag.id]: true })));
    l.on("tileload",  () => setLaagFout(v => { const n={...v}; delete n[laag.id]; return n; }));
    l.addTo(map);
    wmsLagenRef.current[laag.id] = l;
    setWmsAan(v => ({ ...v, [laag.id]: true }));
  }

  // ── Puntdata ophalen ──────────────────────────────────────────────
  async function laadPuntData() {
    if (!project?.boortrace_geojson) { setFout("Teken eerst een boorlijn in stap 4"); return; }
    setBezig(true); setFout(null);
    verwijderPuntMarkers();
    try {
      const gj = typeof project.boortrace_geojson==="string" ? JSON.parse(project.boortrace_geojson) : project.boortrace_geojson;
      const coords = gj?.coordinates ?? [];
      if (coords.length < 2) { setFout("Boorlijn heeft te weinig punten"); return; }
      const lats=coords.map(c=>c[1]), lngs=coords.map(c=>c[0]);
      const params = new URLSearchParams({ minLat:Math.min(...lats), maxLat:Math.max(...lats), minLng:Math.min(...lngs), maxLng:Math.max(...lngs) });
      const res = await fetch(`/api/bro?${params}`);
      if (!res.ok) throw new Error("BRO API niet bereikbaar");
      const json = await res.json();
      if (json.error) throw new Error(json.error);
      setPuntData(json);
      Object.entries(json).forEach(([type, items]) => { if (items?.length) toonPuntMarkers(type, items); });
    } catch(e) {
      setFout(e.message);
    } finally {
      setBezig(false);
    }
  }

  function verwijderPuntMarkers() {
    const map = kaartRef?.current;
    if (!map) return;
    Object.values(puntLagenRef.current).forEach(lg => { try { map.removeLayer(lg); } catch {} });
    puntLagenRef.current = {};
  }

  function toonPuntMarkers(type, items) {
    const map = kaartRef?.current;
    if (!map || !window.L) return;
    const L = window.L;
    const cfg = PUNT_TYPEN[type];
    const markers = items.map(item => {
      const icon = L.divIcon({ className:"", html:`<div style="width:18px;height:18px;background:${cfg.kleur};border:2px solid white;border-radius:50%;box-shadow:0 2px 5px rgba(0,0,0,0.3);display:flex;align-items:center;justify-content:center;font-size:8px;color:white;font-weight:900">${cfg.icon}</div>`, iconSize:[18,18], iconAnchor:[9,9] });
      return L.marker([item.lat, item.lng], {icon})
        .on("click", () => setGeselecteerd({...item, type}))
        .bindTooltip(`<strong>${cfg.label}</strong><br/>${item.id}${item.diepte?`<br/>⬇ ${item.diepte}m`:""}`, {direction:"top"});
    });
    const groep = L.layerGroup(markers);
    if (puntAan[type]) groep.addTo(map);
    puntLagenRef.current[type] = groep;
  }

  function togglePuntType(type) {
    const map=kaartRef?.current, laag=puntLagenRef.current[type];
    const nieuw = !puntAan[type];
    setPuntAan(v=>({...v,[type]:nieuw}));
    if(!map||!laag) return;
    if(nieuw) laag.addTo(map); else map.removeLayer(laag);
  }

  useEffect(() => () => {
    verwijderPuntMarkers();
    const map = kaartRef?.current;
    if (map) Object.values(wmsLagenRef.current).forEach(l => { try { map.removeLayer(l); } catch {} });
  }, []);

  const totaalPunten = puntData ? Object.values(puntData).reduce((s,a)=>s+a.length,0) : 0;

  // ── Tabs ──────────────────────────────────────────────────────────
  const TABS = [
    { id:"wms",      label:"🗺️ Kaartlagen" },
    { id:"download", label:"📥 Modellen" },
    { id:"punten",   label:`📍 Puntdata${totaalPunten>0?` (${totaalPunten})`:""}` },
  ];

  return (
    <div style={{fontFamily:"system-ui,sans-serif",fontSize:12}}>

      {/* Tabs */}
      <div style={{display:"flex",gap:0,borderBottom:"1px solid #E5E7EB",marginBottom:14}}>
        {TABS.map(t=>(
          <button key={t.id} onClick={()=>setSectie(t.id)} style={{
            flex:1,padding:"7px 4px",fontSize:11,fontWeight:sectie===t.id?700:400,
            color:sectie===t.id?"#F97316":"#6B7280",background:"none",border:"none",cursor:"pointer",
            borderBottom:sectie===t.id?"2px solid #F97316":"2px solid transparent",marginBottom:-1,
          }}>{t.label}</button>
        ))}
      </div>

      {/* ── Kaartlagen (WMS) ─────────────────────────────── */}
      {sectie==="wms"&&(
        <div>
          <p style={{fontSize:11,color:"#6B7280",marginBottom:10}}>
            Klik om een laag aan/uit te zetten op de kaart. Laagnamen zijn bevestigd via GetCapabilities.
          </p>
          <div style={{display:"flex",flexDirection:"column",gap:6}}>
            {BRO_WMS.map(laag=>{
              const aan=!!wmsAan[laag.id], err=!!laagFout[laag.id];
              return(
                <div key={laag.id} style={{
                  display:"flex",alignItems:"center",gap:10,padding:"9px 12px",
                  background:aan&&!err?laag.kleur+"15":"white",
                  border:`1.5px solid ${aan&&!err?laag.kleur:err?"#FCA5A5":"#E5E7EB"}`,
                  borderRadius:8,cursor:"pointer",transition:"all 0.15s",
                }} onClick={()=>toggleWms(laag)}>
                  <span style={{fontSize:18}}>{laag.icon}</span>
                  <div style={{flex:1}}>
                    <div style={{fontWeight:600,fontSize:11,color:aan&&!err?laag.kleur:"#374151"}}>{laag.label}</div>
                    <div style={{fontSize:10,color:"#9CA3AF"}}>{laag.subtitel}</div>
                    {err&&<div style={{fontSize:10,color:"#DC2626",marginTop:1}}>⚠️ Laag niet beschikbaar — verifieer laagnaam via GetCapabilities</div>}
                    {aan&&!err&&<div style={{fontSize:10,color:laag.kleur,marginTop:1}}>✓ Zichtbaar op kaart · layer: <code style={{fontSize:9}}>{laag.layer}</code></div>}
                  </div>
                  <div style={{width:34,height:18,borderRadius:9,background:aan&&!err?laag.kleur:"#E5E7EB",position:"relative",flexShrink:0,transition:"background 0.15s"}}>
                    <div style={{width:14,height:14,borderRadius:"50%",background:"white",position:"absolute",top:2,left:aan?18:2,transition:"left 0.15s",boxShadow:"0 1px 3px rgba(0,0,0,0.2)"}}/>
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      )}

      {/* ── Download-only modellen ──────────────────────── */}
      {sectie==="download"&&(
        <div>
          <p style={{fontSize:11,color:"#6B7280",marginBottom:10}}>
            Deze BRO-modellen hebben geen WMS. Bekijk ze op DINOloket of download via BROloket.
          </p>
          <div style={{display:"flex",flexDirection:"column",gap:8}}>
            {BRO_DOWNLOAD.map(m=>(
              <div key={m.id} style={{border:`1px solid ${m.kleur}30`,borderLeft:`4px solid ${m.kleur}`,borderRadius:8,padding:"10px 12px"}}>
                <div style={{display:"flex",alignItems:"center",gap:8,marginBottom:4}}>
                  <span style={{fontSize:16}}>{m.icon}</span>
                  <div>
                    <div style={{fontWeight:700,fontSize:11,color:m.kleur}}>{m.label}</div>
                    <div style={{fontSize:10,color:"#9CA3AF"}}>{m.subtitel}</div>
                  </div>
                </div>
                <p style={{fontSize:11,color:"#374151",marginBottom:8,lineHeight:1.4}}>{m.beschrijving}</p>
                <p style={{fontSize:10,color:"#6B7280",marginBottom:8,fontStyle:"italic"}}>{m.tip}</p>
                <div style={{display:"flex",gap:6"}}>
                  {m.dinoloketUrl&&<a href={m.dinoloketUrl} target="_blank" rel="noopener noreferrer" style={{fontSize:10,padding:"4px 8px",background:m.kleur,color:"white",borderRadius:6,textDecoration:"none",fontWeight:600}}>🌐 DINOloket</a>}
                  {m.broloketUrl&&<a href={m.broloketUrl} target="_blank" rel="noopener noreferrer" style={{fontSize:10,padding:"4px 8px",background:"#F9FAFB",color:m.kleur,border:`1px solid ${m.kleur}50`,borderRadius:6,textDecoration:"none",fontWeight:600}}>📥 BROloket</a>}
                  {m.atomUrl&&<a href={m.atomUrl} target="_blank" rel="noopener noreferrer" style={{fontSize:10,padding:"4px 8px",background:"#F9FAFB",color:"#6B7280",border:"1px solid #E5E7EB",borderRadius:6,textDecoration:"none"}}>⬇ ATOM download</a>}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* ── Puntdata ─────────────────────────────────────── */}
      {sectie==="punten"&&(
        <div>
          <div style={{display:"flex",justifyContent:"space-between",alignItems:"center",marginBottom:10}}>
            <div>
              <div style={{fontWeight:700,fontSize:12,color:"#1F2937"}}>BRO Puntdata nabij tracé</div>
              <div style={{fontSize:10,color:"#6B7280"}}>Sonderingen · Boorprofielen · Peilbuizen (~800m straal)</div>
            </div>
            <button onClick={laadPuntData} disabled={bezig} style={{padding:"6px 12px",background:bezig?"#E5E7EB":"#F97316",color:bezig?"#9CA3AF":"white",border:"none",borderRadius:7,cursor:bezig?"default":"pointer",fontWeight:600,fontSize:11,display:"flex",alignItems:"center",gap:5}}>
              {bezig?<><div style={{width:10,height:10,border:"2px solid rgba(0,0,0,0.1)",borderTop:"2px solid #9CA3AF",borderRadius:"50%",animation:"spin 0.8s linear infinite"}}/>Laden...</>:"🔍 Ophalen"}
            </button>
          </div>

          {fout&&<div style={{background:"#FEF2F2",border:"1px solid #FCA5A5",borderRadius:8,padding:"8px 12px",color:"#DC2626",marginBottom:8,fontSize:11}}>
            ❌ {fout}<div style={{marginTop:4,fontSize:10,color:"#9CA3AF"}}>PDOK WFS diensten zijn soms tijdelijk niet beschikbaar. Probeer opnieuw of controleer <a href="https://www.pdok.nl/status-overzicht" target="_blank" rel="noopener noreferrer" style={{color:"#3B82F6"}}>PDOK status</a>.</div>
          </div>}

          {puntData&&(
            <>
              <div style={{display:"flex",gap:5,flexWrap:"wrap",marginBottom:10}}>
                {Object.entries(PUNT_TYPEN).map(([type,cfg])=>{
                  const n=puntData[type]?.length??0, aan=puntAan[type];
                  return(
                    <button key={type} onClick={()=>n>0&&togglePuntType(type)} style={{display:"flex",alignItems:"center",gap:4,padding:"4px 8px",background:aan&&n>0?cfg.kleur+"15":"#F9FAFB",border:`1.5px solid ${aan&&n>0?cfg.kleur:"#E5E7EB"}`,borderRadius:6,cursor:n>0?"pointer":"default",opacity:n>0?1:0.6}}>
                      <div style={{width:7,height:7,borderRadius:"50%",background:n>0?cfg.kleur:"#D1D5DB"}}/>
                      <span style={{fontSize:10,fontWeight:600,color:n>0?cfg.kleur:"#9CA3AF"}}>{cfg.label}</span>
                      <span style={{fontSize:9,fontWeight:700,background:n>0?cfg.kleur:"#E5E7EB",color:n>0?"white":"#9CA3AF",borderRadius:8,padding:"0 4px"}}>{n}</span>
                    </button>
                  );
                })}
              </div>
              {totaalPunten===0&&<div style={{fontSize:11,color:"#9CA3AF",fontStyle:"italic",padding:"8px 0"}}>Geen puntdata gevonden in dit gebied. Niet elke locatie heeft sonderingen of boringen in BRO geregistreerd. Controleer ook <a href="https://www.dinoloket.nl/ondergrondgegevens" target="_blank" rel="noopener noreferrer" style={{color:"#3B82F6"}}>DINOloket</a> voor historische data.</div>}
              {Object.entries(PUNT_TYPEN).map(([type,cfg])=>{
                const items=puntData[type]??[];
                if(!items.length) return null;
                return(
                  <div key={type} style={{marginBottom:10,border:`1px solid ${cfg.kleur}30`,borderLeft:`4px solid ${cfg.kleur}`,borderRadius:8,overflow:"hidden"}}>
                    <div style={{background:cfg.kleur+"15",padding:"6px 10px",fontWeight:700,fontSize:11,color:cfg.kleur}}>{cfg.icon} {cfg.label} ({items.length})</div>
                    <div style={{maxHeight:150,overflowY:"auto"}}>
                      {items.map(item=>(
                        <div key={item.id} onClick={()=>setGeselecteerd({...item,type})} style={{padding:"5px 10px",borderBottom:"1px solid #F9FAFB",cursor:"pointer",background:geselecteerd?.id===item.id?cfg.kleur+"10":"white"}}>
                          <div style={{display:"flex",justifyContent:"space-between"}}>
                            <span style={{fontWeight:600,color:"#374151",fontSize:11}}>{item.id}</span>
                            <span style={{fontSize:10,color:"#9CA3AF"}}>{item.diepte?`⬇ ${item.diepte}m`:""}</span>
                          </div>
                          {item.bronhouder&&<div style={{fontSize:10,color:"#9CA3AF"}}>🏢 {item.bronhouder}</div>}
                        </div>
                      ))}
                    </div>
                  </div>
                );
              })}
            </>
          )}
          {!puntData&&!bezig&&<div style={{textAlign:"center",padding:"20px 0",color:"#9CA3AF",fontSize:11}}>Klik "Ophalen" voor sonderingen, boringen en peilbuizen nabij het tracé</div>}

          {geselecteerd&&(
            <div style={{marginTop:10,border:"1.5px solid #E5E7EB",borderRadius:10,overflow:"hidden"}}>
              <div style={{background:PUNT_TYPEN[geselecteerd.type]?.kleur+"15",padding:"8px 12px",display:"flex",justifyContent:"space-between"}}>
                <span style={{fontWeight:700,color:PUNT_TYPEN[geselecteerd.type]?.kleur,fontSize:12}}>{geselecteerd.id}</span>
                <div style={{display:"flex",gap:8}}>
                  <a href={`https://www.dinoloket.nl/ondergrondgegevens?zoekveld=${geselecteerd.id}`} target="_blank" rel="noopener noreferrer" style={{fontSize:10,color:"#7C3AED",textDecoration:"none",fontWeight:600}}>🔗 DINOloket</a>
                  <a href={`https://www.broloket.nl/ondergrondgegevens?zoekveld=${geselecteerd.id}`} target="_blank" rel="noopener noreferrer" style={{fontSize:10,color:"#3B82F6",textDecoration:"none",fontWeight:600}}>🔗 BROloket</a>
                  <button onClick={()=>setGeselecteerd(null)} style={{background:"none",border:"none",cursor:"pointer",color:"#9CA3AF"}}>✕</button>
                </div>
              </div>
              <div style={{padding:"10px 12px",display:"grid",gridTemplateColumns:"1fr 1fr",gap:6}}>
                {geselecteerd.diepte&&<KV label="Diepte" waarde={`${geselecteerd.diepte}m`}/>}
                {geselecteerd.kwaliteit&&<KV label="Kwaliteit" waarde={geselecteerd.kwaliteit}/>}
                {geselecteerd.bronhouder&&<KV label="Bronhouder" waarde={geselecteerd.bronhouder}/>}
                {geselecteerd.datum&&<KV label="Datum" waarde={geselecteerd.datum.split("T")[0]}/>}
              </div>
            </div>
          )}
        </div>
      )}

      <style>{`@keyframes spin{to{transform:rotate(360deg)}}`}</style>
    </div>
  );
}

function KV({label,waarde}){
  return(
    <div style={{background:"#F9FAFB",borderRadius:6,padding:"4px 8px"}}>
      <div style={{fontSize:9,color:"#9CA3AF",textTransform:"uppercase"}}>{label}</div>
      <div style={{fontWeight:600,color:"#1F2937",fontSize:11}}>{waarde}</div>
    </div>
  );
}
