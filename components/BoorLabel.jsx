"use client";
import { useState, useRef, useCallback } from "react";

// ─── Kleur helpers ────────────────────────────────────────────────────────────
const TUBE_COLORS = ["#1D4ED8","#047857","#B45309","#6D28D9","#374151","#B91C1C"];
const CAT_COLORS  = { ls:"#DC2626", ms:"#7C3AED", gf:"#D97706", water:"#2563EB", gas:"#F59E0B" };
const CAT_KEYS = {
  "YMVK":"ls","12 kV":"ms","Microduct":"gf","GF kabel":"gf",
  "PE32 water":"water","PE40 water":"water","PE50 water":"water",
  "PE63 water":"water","PE90 water":"water",
  "PE32 gas":"gas","PE40 gas":"gas","PE50 gas":"gas","PE63 gas":"gas",
};

function cableCat(label) {
  const k = Object.keys(CAT_KEYS).find(k => label?.startsWith(k));
  return k ? CAT_COLORS[CAT_KEYS[k]] : "#6B7280";
}
function itemColor(item, idx) {
  return item.type === "mb" ? TUBE_COLORS[idx % TUBE_COLORS.length] : cableCat(item.label);
}
function shortLabel(label = "") {
  if (label.startsWith("YMVK"))     return label.replace("YMVK ","").replace(" mm2","");
  if (label.startsWith("12 kV"))    return label.replace("12 kV ","12kV ").replace(" mm2","");
  if (label.startsWith("Microduct"))return label.replace("Microduct ","μ");
  if (label.startsWith("GF kabel")) return label.replace("GF kabel ","GF ");
  if (label.includes(" water"))     return label.replace(" water","w");
  if (label.includes(" gas"))       return label.replace(" gas","g");
  return label.slice(0, 10);
}

// ─── Geometrie helpers ────────────────────────────────────────────────────────
function getTraceLatLngs(geojson) {
  if (!geojson) return [];
  // LineString direct (MapTrace formaat): {type:"LineString", coordinates:[[lng,lat],...]}
  const coords = geojson.coordinates
    ?? geojson.geometry?.coordinates
    ?? geojson.features?.[0]?.geometry?.coordinates;
  if (!coords?.length) return [];
  return coords.map(c => [c[1], c[0]]); // GeoJSON [lng,lat] → [lat,lng]
}

function traceLengteM(geojson) {
  const latlngs = getTraceLatLngs(geojson);
  if (latlngs.length < 2) return null;
  let d = 0;
  for (let i = 1; i < latlngs.length; i++) {
    const [lat1,lng1] = latlngs[i-1], [lat2,lng2] = latlngs[i];
    const R = 6371000, f = Math.PI/180;
    const dLat = (lat2-lat1)*f, dLng = (lng2-lng1)*f;
    const a = Math.sin(dLat/2)**2 + Math.cos(lat1*f)*Math.cos(lat2*f)*Math.sin(dLng/2)**2;
    d += R * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1-a));
  }
  return Math.round(d);
}

function closestOnSegment(px, py, ax, ay, bx, by) {
  const dx = bx-ax, dy = by-ay, len2 = dx*dx+dy*dy;
  if (len2 === 0) return {x:ax, y:ay};
  const t = Math.max(0, Math.min(1, ((px-ax)*dx+(py-ay)*dy)/len2));
  return {x: ax+t*dx, y: ay+t*dy};
}

function snapToPolyline(mx, my, screenPts) {
  let best = null, bestD = Infinity;
  for (let i = 1; i < screenPts.length; i++) {
    const [ax,ay] = screenPts[i-1], [bx,by] = screenPts[i];
    const pt = closestOnSegment(mx, my, ax, ay, bx, by);
    const d = Math.hypot(pt.x-mx, pt.y-my);
    if (d < bestD) { bestD = d; best = pt; }
  }
  return best ?? {x:mx, y:my};
}

// Zet lat/lng trace om naar container-pixels via Leaflet
function traceToScreen(leafletMap, traceGeojson) {
  const latlngs = getTraceLatLngs(traceGeojson);
  return latlngs.map(([lat,lng]) => {
    const pt = leafletMap.latLngToContainerPoint([lat,lng]);
    return [pt.x, pt.y];
  });
}

// ─── Chip component ───────────────────────────────────────────────────────────
function Chip({ color, label, children }) {
  return (
    <div style={{
      display:"flex", flexDirection:"column", gap:2,
      background:"#F9FAFB", border:"1px solid #E5E7EB",
      borderRadius:6, padding:"3px 6px",
    }}>
      <div style={{display:"flex",alignItems:"center",gap:3}}>
        <div style={{width:6,height:6,borderRadius:"50%",background:color,flexShrink:0}}/>
        <span style={{fontSize:9,fontWeight:600,color:"#374151",whiteSpace:"nowrap"}}>{label}</span>
      </div>
      {children}
    </div>
  );
}

function ContentChip({ label }) {
  return (
    <div style={{display:"flex",alignItems:"center",gap:2,paddingLeft:8}}>
      <div style={{width:4,height:4,borderRadius:"50%",background:cableCat(label),flexShrink:0}}/>
      <span style={{fontSize:8,color:"#6B7280",whiteSpace:"nowrap"}}>{shortLabel(label)}</span>
    </div>
  );
}

// ─── HTML BoorLabel (Leaflet kaarten stap 4 5 7 8) ───────────────────────────
export default function BoorLabel({
  boringConfig,
  boorlengte,
  traceGeojson,
  leafletMapRef,     // ref-object naar Leaflet L.Map instantie
  initialPos,
  initialAnchor,
}) {
  const [lPos,   setLPos]   = useState(initialPos   ?? { x:16, y:16 });
  const [anchor, setAnchor] = useState(initialAnchor ?? { x:240, y:160 });
  const labelRef = useRef(null);
  const lDrag    = useRef(null);
  const aDrag    = useRef(null);

  const lengte = traceGeojson ? (traceLengteM(traceGeojson) ?? boorlengte) : boorlengte;

  // ── Label drag ──────────────────────────────────────────────────────────────
  function onLabelDown(e) {
    if (e.target.closest("[data-anchor]")) return;
    e.stopPropagation();
    lDrag.current = { sx: e.clientX - lPos.x, sy: e.clientY - lPos.y };
    const move = ev => setLPos({ x: ev.clientX - lDrag.current.sx, y: ev.clientY - lDrag.current.sy });
    const up   = ()  => { window.removeEventListener("mousemove", move); window.removeEventListener("mouseup", up); };
    window.addEventListener("mousemove", move);
    window.addEventListener("mouseup",   up);
  }

  // ── Anchor drag — snapt naar boorlijn via Leaflet ──────────────────────────
  function onAnchorDown(e) {
    e.stopPropagation(); e.preventDefault();
    const map = leafletMapRef?.current;
    const container = map?.getContainer?.();

    function move(ev) {
      if (map && container && traceGeojson) {
        const rect = container.getBoundingClientRect();
        const mx = ev.clientX - rect.left;
        const my = ev.clientY - rect.top;
        const screenPts = traceToScreen(map, traceGeojson);
        if (screenPts.length >= 2) {
          const snapped = snapToPolyline(mx, my, screenPts);
          setAnchor(snapped);
          return;
        }
      }
      // Vrij slepen als geen trace of geen Leaflet
      if (!aDrag.current) aDrag.current = { sx: ev.clientX - anchor.x, sy: ev.clientY - anchor.y };
      setAnchor({ x: ev.clientX - aDrag.current.sx, y: ev.clientY - aDrag.current.sy });
    }

    aDrag.current = null;
    const up = () => { aDrag.current = null; window.removeEventListener("mousemove", move); window.removeEventListener("mouseup", up); };
    window.addEventListener("mousemove", move);
    window.addEventListener("mouseup",   up);
  }

  if (!boringConfig?.boringD) return null;
  const { items = [], boringD } = boringConfig;

  const LW = 172;
  const labelH = labelRef.current?.offsetHeight ?? 90;
  // Koppelpunt: onderkant midden van het label
  const connX = lPos.x + LW / 2;
  const connY = lPos.y + labelH;

  return (
    <>
      {/* SVG leader overlay — volledige container */}
      <svg style={{
        position:"absolute", inset:0, width:"100%", height:"100%",
        pointerEvents:"none", zIndex:998, overflow:"visible",
      }}>
        {/* Gestippelde lijn */}
        <line x1={connX} y1={connY} x2={anchor.x} y2={anchor.y}
              stroke="#F97316" strokeWidth={1.5} strokeDasharray="6,3" opacity={0.8}/>
        {/* Ankerpunt — versleepbaar langs boorlijn */}
        <g data-anchor style={{pointerEvents:"all",cursor:"ew-resize"}}
           onMouseDown={onAnchorDown}>
          <circle cx={anchor.x} cy={anchor.y} r={10}
                  fill="#F97316" fillOpacity={0.12} stroke="#F97316" strokeWidth={1.5}/>
          <circle cx={anchor.x} cy={anchor.y} r={4}
                  fill="#F97316" stroke="white" strokeWidth={2}/>
        </g>
      </svg>

      {/* Label */}
      <div ref={labelRef} onMouseDown={onLabelDown} style={{
        position:"absolute", left:lPos.x, top:lPos.y, zIndex:999,
        width:LW, cursor:"grab", userSelect:"none", touchAction:"none",
        background:"white", border:"1.5px solid #FDBA74",
        borderRadius:8, boxShadow:"0 2px 10px rgba(0,0,0,0.13)",
        fontSize:10,
      }}>
        {/* Header */}
        <div style={{
          display:"flex", alignItems:"center", gap:5,
          padding:"4px 8px", borderBottom:"1px solid #FEF3C7",
          background:"#FFFBEB", borderRadius:"6px 6px 0 0",
        }}>
          <div style={{width:6,height:6,borderRadius:"50%",background:"#F97316",flexShrink:0}}/>
          <span style={{fontWeight:700,color:"#B45309",fontSize:9}}>Boring</span>
          <span style={{marginLeft:"auto",fontSize:11,color:"#D1D5DB"}}>⠿</span>
        </div>

        {/* Diameter + Lengte */}
        <div style={{display:"flex",padding:"5px 8px 4px",gap:0}}>
          <div style={{flex:1}}>
            <div style={{fontSize:8,color:"#9CA3AF",lineHeight:1,marginBottom:1}}>Diameter</div>
            <div style={{fontSize:13,fontWeight:800,color:"#1F2937",lineHeight:1}}>
              Ø{boringD} <span style={{fontSize:8,fontWeight:500}}>mm</span>
            </div>
          </div>
          {lengte && (
            <div style={{flex:1,borderLeft:"1px solid #F3F4F6",paddingLeft:8}}>
              <div style={{fontSize:8,color:"#9CA3AF",lineHeight:1,marginBottom:1}}>Tracélengte</div>
              <div style={{fontSize:13,fontWeight:800,color:"#1F2937",lineHeight:1}}>
                {lengte} <span style={{fontSize:8,fontWeight:500}}>m</span>
              </div>
            </div>
          )}
        </div>

        {/* Inhoud — mantelbuizen met sub-items */}
        {items.length > 0 && (
          <div style={{
            padding:"4px 7px 6px",
            borderTop:"1px solid #F3F4F6",
            display:"flex", flexDirection:"column", gap:3,
          }}>
            {items.map((item, idx) => {
              if (item.type === "mb") {
                return (
                  <Chip key={item.id ?? idx} color={TUBE_COLORS[idx % TUBE_COLORS.length]} label={`PE${item.dn} mantelbuis`}>
                    {item.contents?.length > 0 && (
                      <div style={{display:"flex",flexDirection:"column",gap:1,marginTop:1}}>
                        {item.contents.map((c, ci) => (
                          <ContentChip key={c.id ?? ci} label={c.label}/>
                        ))}
                      </div>
                    )}
                  </Chip>
                );
              }
              return (
                <Chip key={item.id ?? idx} color={cableCat(item.label)} label={shortLabel(item.label)}/>
              );
            })}
          </div>
        )}

        {/* Koppelstip onderaan */}
        <div style={{
          position:"absolute", bottom:-5, left:"50%", transform:"translateX(-50%)",
          width:8, height:8, borderRadius:"50%",
          background:"white", border:"1.5px solid #F97316",
          pointerEvents:"none",
        }}/>
      </div>
    </>
  );
}


// ─── SVG BoorLabel (Diepteligging dwarsprofiel stap 6) ────────────────────────
export function BoorLabelSVG({ boringConfig, boorlengte, traceGeojson, boorPadPts, x:ix=680, y:iy=8 }) {
  const [lPos,   setLPos]   = useState({ x:ix, y:iy });
  const [anchor, setAnchor] = useState({ x:ix-80, y:iy+90 });

  const lengte = traceGeojson ? (traceLengteM(traceGeojson) ?? boorlengte) : boorlengte;

  if (!boringConfig?.boringD) return null;
  const { items=[], boringD } = boringConfig;

  const W = 150;
  const mbItems   = items.filter(i=>i.type==="mb");
  const dirItems  = items.filter(i=>i.type!=="mb");
  let contentRows = 0;
  mbItems.forEach(mb => { contentRows += 1 + (mb.contents?.length || 0); });
  contentRows += dirItems.length;
  const H = 52 + (items.length>0 ? 8 + contentRows*13 : 0);
  const connX = lPos.x + W/2, connY = lPos.y + H;

  function startLabel(e) {
    e.stopPropagation(); e.preventDefault();
    const sx=e.clientX-lPos.x, sy=e.clientY-lPos.y;
    const move = ev => setLPos({x:ev.clientX-sx,y:ev.clientY-sy});
    const up   = ()  => { window.removeEventListener("mousemove",move); window.removeEventListener("mouseup",up); };
    window.addEventListener("mousemove",move); window.addEventListener("mouseup",up);
  }

  function startAnchor(e) {
    e.stopPropagation(); e.preventDefault();
    const svg = e.currentTarget.ownerSVGElement;
    function move(ev) {
      if (svg && boorPadPts?.length >= 2) {
        // SVG coordinate conversie (correct bij CSS-scaling)
        const pt = svg.createSVGPoint();
        pt.x = ev.clientX; pt.y = ev.clientY;
        const svgPt = pt.matrixTransform(svg.getScreenCTM().inverse());
        const snapped = snapToPolyline(svgPt.x, svgPt.y, boorPadPts.map(p=>[p.x,p.y]));
        setAnchor(snapped);
      } else if (svg) {
        const pt = svg.createSVGPoint();
        pt.x = ev.clientX; pt.y = ev.clientY;
        const svgPt = pt.matrixTransform(svg.getScreenCTM().inverse());
        setAnchor({x:svgPt.x, y:svgPt.y});
      }
    }
    const up = () => { window.removeEventListener("mousemove",move); window.removeEventListener("mouseup",up); };
    window.addEventListener("mousemove",move); window.addEventListener("mouseup",up);
  }

  function renderItems() {
    const rows = [];
    let y = 60;
    items.forEach((item, idx) => {
      const color = itemColor(item, idx);
      if (item.type === "mb") {
        rows.push(
          <g key={`mb-${idx}`}>
            <circle cx={lPos.x+10} cy={lPos.y+y-3} r={3.5} fill={color}/>
            <text x={lPos.x+17} y={lPos.y+y} fontSize={8} fontWeight="600" fill="#374151">PE{item.dn} mantelbuis</text>
          </g>
        );
        y += 12;
        (item.contents||[]).forEach((c, ci) => {
          rows.push(
            <g key={`c-${idx}-${ci}`}>
              <circle cx={lPos.x+18} cy={lPos.y+y-3} r={2.5} fill={cableCat(c.label)}/>
              <text x={lPos.x+24} y={lPos.y+y} fontSize={7.5} fill="#6B7280">{shortLabel(c.label)}</text>
            </g>
          );
          y += 11;
        });
      } else {
        rows.push(
          <g key={`d-${idx}`}>
            <circle cx={lPos.x+10} cy={lPos.y+y-3} r={3} fill={color}/>
            <text x={lPos.x+17} y={lPos.y+y} fontSize={8} fill="#374151">{shortLabel(item.label)}</text>
          </g>
        );
        y += 12;
      }
    });
    return rows;
  }

  return (
    <g>
      <line x1={connX} y1={connY} x2={anchor.x} y2={anchor.y}
            stroke="#f97316" strokeWidth={1.5} strokeDasharray="5,3" opacity={0.8}/>
      <g onMouseDown={startAnchor} style={{cursor:"ew-resize"}}>
        <circle cx={anchor.x} cy={anchor.y} r={10} fill="#f97316" fillOpacity={0.12} stroke="#f97316" strokeWidth={1.5}/>
        <circle cx={anchor.x} cy={anchor.y} r={4} fill="#f97316" stroke="white" strokeWidth={2}/>
      </g>
      <g onMouseDown={startLabel} style={{cursor:"grab"}}>
        <rect x={lPos.x} y={lPos.y} width={W} height={H} rx={6}
              fill="white" stroke="#FDBA74" strokeWidth={1.5}
              style={{filter:"drop-shadow(0 2px 4px rgba(0,0,0,0.1))"}}/>
        <rect x={lPos.x} y={lPos.y} width={W} height={17} rx={6} fill="#FFFBEB"/>
        <rect x={lPos.x} y={lPos.y+11} width={W} height={6} fill="#FFFBEB"/>
        <circle cx={lPos.x+11} cy={lPos.y+9} r={3.5} fill="#F97316"/>
        <text x={lPos.x+19} y={lPos.y+13} fontSize={8} fontWeight="700" fill="#B45309">Boring</text>
        <text x={lPos.x+W-6} y={lPos.y+13} fontSize={10} fill="#D1D5DB" textAnchor="end">⠿</text>
        <line x1={lPos.x+6} y1={lPos.y+19} x2={lPos.x+W-6} y2={lPos.y+19} stroke="#FEF3C7" strokeWidth={1}/>
        <text x={lPos.x+8} y={lPos.y+30} fontSize={7} fill="#9CA3AF">Diameter</text>
        <text x={lPos.x+8} y={lPos.y+42} fontSize={12} fontWeight="800" fill="#1F2937">Ø{boringD} mm</text>
        {lengte && <>
          <line x1={lPos.x+W/2} y1={lPos.y+21} x2={lPos.x+W/2} y2={lPos.y+48} stroke="#F3F4F6"/>
          <text x={lPos.x+W/2+6} y={lPos.y+30} fontSize={7} fill="#9CA3AF">Lengte</text>
          <text x={lPos.x+W/2+6} y={lPos.y+42} fontSize={12} fontWeight="800" fill="#1F2937">{lengte} m</text>
        </>}
        {items.length > 0 && <>
          <line x1={lPos.x+6} y1={lPos.y+50} x2={lPos.x+W-6} y2={lPos.y+50} stroke="#F3F4F6"/>
          {renderItems()}
        </>}
        <circle cx={connX} cy={connY} r={4} fill="white" stroke="#F97316" strokeWidth={1.5}/>
      </g>
    </g>
  );
}
