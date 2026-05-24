"use client";
import { useState, useRef, useEffect, useCallback } from "react";

// ─── Kleur helpers ────────────────────────────────────────────────────────────
const TUBE_COLORS = ["#1D4ED8","#047857","#B45309","#6D28D9","#374151","#B91C1C"];
const CAT_COLORS  = { ls:"#DC2626", ms:"#7C3AED", gf:"#D97706", water:"#2563EB", gas:"#F59E0B" };
const CAT_KEYS    = {
  "YMVK":"ls","12 kV":"ms","Microduct":"gf","GF kabel":"gf",
  "PE32 water":"water","PE40 water":"water","PE50 water":"water",
  "PE63 water":"water","PE90 water":"water",
  "PE32 gas":"gas","PE40 gas":"gas","PE50 gas":"gas","PE63 gas":"gas",
};

function itemColor(item, idx) {
  if (item.type === "mb") return TUBE_COLORS[idx % TUBE_COLORS.length];
  const key = Object.keys(CAT_KEYS).find(k => item.label?.startsWith(k));
  return key ? CAT_COLORS[CAT_KEYS[key]] : "#6B7280";
}

function itemLabel(item) {
  if (item.type === "mb") return `PE${item.dn}`;
  // Shorten: "YMVK 4x95 mm2" → "4x95", "12 kV 3x95 mm2" → "12kV 3x95"
  const l = item.label || "";
  if (l.startsWith("YMVK"))    return l.replace("YMVK ","").replace(" mm2","");
  if (l.startsWith("12 kV"))   return l.replace("12 kV ","12kV ").replace(" mm2","");
  if (l.startsWith("Microduct"))return l.replace("Microduct ","μ");
  if (l.startsWith("GF kabel"))return l.replace("GF kabel ","GF ");
  if (l.includes(" water"))    return l.replace(" water","w");
  if (l.includes(" gas"))      return l.replace(" gas","g");
  return l.slice(0,8);
}

// ─── Tracélengte uit GeoJSON ──────────────────────────────────────────────────
function berekenTraceLengte(geojson) {
  try {
    const coords = geojson?.features?.[0]?.geometry?.coordinates
      ?? geojson?.geometry?.coordinates;
    if (!coords || coords.length < 2) return null;
    let d = 0;
    for (let i = 1; i < coords.length; i++) {
      const [lng1,lat1] = coords[i-1], [lng2,lat2] = coords[i];
      const R = 6371000, f = Math.PI/180;
      const dLat = (lat2-lat1)*f, dLng = (lng2-lng1)*f;
      const a = Math.sin(dLat/2)**2 + Math.cos(lat1*f)*Math.cos(lat2*f)*Math.sin(dLng/2)**2;
      d += R * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1-a));
    }
    return Math.round(d);
  } catch { return null; }
}

// ─── Drag helper ─────────────────────────────────────────────────────────────
function useDrag(initialPos, onChange) {
  const ref   = useRef(null);
  const start = useRef(null);

  const onDown = useCallback((e) => {
    e.stopPropagation(); e.preventDefault();
    start.current = { cx: e.clientX - ref.current.x, cy: e.clientY - ref.current.y };
    const onMove = ev => {
      const nx = ev.clientX - start.current.cx;
      const ny = ev.clientY - start.current.cy;
      ref.current = { x: nx, y: ny };
      onChange({ x: nx, y: ny });
    };
    const onUp = () => { window.removeEventListener("mousemove", onMove); window.removeEventListener("mouseup", onUp); };
    window.addEventListener("mousemove", onMove);
    window.addEventListener("mouseup", onUp);
  }, [onChange]);

  // init
  if (!ref.current) ref.current = initialPos;

  return onDown;
}

// ─── HTML BoorLabel (Leaflet kaarten, stap 4 5 7 8) ──────────────────────────
export default function BoorLabel({ boringConfig, boorlengte, traceGeojson, initialPos, initialAnchor }) {
  const [lPos,   setLPos]   = useState(initialPos   ?? { x: 20, y: 20 });
  const [anchor, setAnchor] = useState(initialAnchor ?? { x: 220, y: 160 });
  const labelRef = useRef(null);
  const [labelH, setLabelH] = useState(80);

  // Actuele tracélengte
  const traceLengte = traceGeojson ? berekenTraceLengte(traceGeojson) : null;
  const lengte = traceLengte ?? boorlengte;

  useEffect(() => {
    if (labelRef.current) setLabelH(labelRef.current.offsetHeight);
  });

  // Label drag
  const lDrag = useRef(null);
  function onLabelDown(e) {
    if (e.target.closest("[data-anchor]")) return;
    lDrag.current = { sx: e.clientX - lPos.x, sy: e.clientY - lPos.y };
    const onMove = ev => setLPos({ x: ev.clientX - lDrag.current.sx, y: ev.clientY - lDrag.current.sy });
    const onUp   = () => { window.removeEventListener("mousemove", onMove); window.removeEventListener("mouseup", onUp); };
    window.addEventListener("mousemove", onMove);
    window.addEventListener("mouseup", onUp);
  }

  // Anchor drag
  const aDrag = useRef(null);
  function onAnchorDown(e) {
    e.stopPropagation(); e.preventDefault();
    aDrag.current = { sx: e.clientX - anchor.x, sy: e.clientY - anchor.y };
    const onMove = ev => setAnchor({ x: ev.clientX - aDrag.current.sx, y: ev.clientY - aDrag.current.sy });
    const onUp   = () => { window.removeEventListener("mousemove", onMove); window.removeEventListener("mouseup", onUp); };
    window.addEventListener("mousemove", onMove);
    window.addEventListener("mouseup", onUp);
  }

  if (!boringConfig?.boringD) return null;
  const { items = [], boringD } = boringConfig;

  // Leader connection: bottom-center of label
  const LW = 160;
  const connX = lPos.x + LW / 2;
  const connY = lPos.y + labelH;

  return (
    <>
      {/* SVG leader overlay */}
      <svg style={{
        position:"absolute", inset:0, width:"100%", height:"100%",
        pointerEvents:"none", zIndex:998, overflow:"visible",
      }}>
        {/* Gestippelde leader lijn */}
        <line x1={connX} y1={connY} x2={anchor.x} y2={anchor.y}
              stroke="#F97316" strokeWidth={1.5} strokeDasharray="6,3" opacity={0.75}/>
        {/* Ankerpunt (versleepbaar) */}
        <g data-anchor style={{ pointerEvents:"all", cursor:"crosshair" }}
           onMouseDown={onAnchorDown}>
          <circle cx={anchor.x} cy={anchor.y} r={9}
                  fill="#F97316" fillOpacity={0.15} stroke="#F97316" strokeWidth={1.5}/>
          <circle cx={anchor.x} cy={anchor.y} r={4}
                  fill="#F97316" stroke="white" strokeWidth={1.5}/>
          {/* Kruis-cursor hint */}
          <line x1={anchor.x-7} y1={anchor.y} x2={anchor.x+7} y2={anchor.y}
                stroke="#F97316" strokeWidth={1} opacity={0.5}/>
          <line x1={anchor.x} y1={anchor.y-7} x2={anchor.x} y2={anchor.y+7}
                stroke="#F97316" strokeWidth={1} opacity={0.5}/>
        </g>
      </svg>

      {/* Label kaart */}
      <div ref={labelRef} onMouseDown={onLabelDown} style={{
        position:"absolute", left:lPos.x, top:lPos.y, zIndex:999,
        width:LW, cursor:"grab", userSelect:"none", touchAction:"none",
        background:"white", border:"1.5px solid #FDBA74",
        borderRadius:8, boxShadow:"0 2px 10px rgba(0,0,0,0.12)",
        fontSize:10,
      }}>
        {/* Header */}
        <div style={{
          display:"flex", alignItems:"center", gap:5,
          padding:"5px 8px", borderBottom:"1px solid #FEF3C7",
          background:"#FFFBEB", borderRadius:"6px 6px 0 0",
        }}>
          <div style={{width:6,height:6,borderRadius:"50%",background:"#F97316",flexShrink:0}}/>
          <span style={{fontWeight:700,color:"#B45309",fontSize:9,lineHeight:1}}>Boring</span>
          <span style={{marginLeft:"auto",color:"#D1D5DB",fontSize:11,lineHeight:1,cursor:"grab"}}>⠿</span>
        </div>

        {/* Diameter + lengte */}
        <div style={{display:"flex",gap:0,padding:"5px 8px 4px"}}>
          <div style={{flex:1}}>
            <div style={{fontSize:8,color:"#9CA3AF",lineHeight:1,marginBottom:1}}>Diameter</div>
            <div style={{fontSize:12,fontWeight:800,color:"#1F2937",lineHeight:1}}>
              Ø{boringD}<span style={{fontSize:8,fontWeight:500}}> mm</span>
            </div>
          </div>
          {lengte && (
            <div style={{flex:1,borderLeft:"1px solid #F3F4F6",paddingLeft:8}}>
              <div style={{fontSize:8,color:"#9CA3AF",lineHeight:1,marginBottom:1}}>Tracélengte</div>
              <div style={{fontSize:12,fontWeight:800,color:"#1F2937",lineHeight:1}}>
                {lengte}<span style={{fontSize:8,fontWeight:500}}> m</span>
              </div>
            </div>
          )}
        </div>

        {/* Inhoud */}
        {items.length > 0 && (
          <div style={{
            padding:"4px 8px 6px",
            borderTop:"1px solid #F3F4F6",
            display:"flex", flexWrap:"wrap", gap:2,
          }}>
            {items.map((item, idx) => (
              <div key={item.id ?? idx} style={{
                display:"flex", alignItems:"center", gap:2,
                background:"#F9FAFB", border:"1px solid #E5E7EB",
                borderRadius:20, padding:"1.5px 5px",
              }}>
                <div style={{width:5,height:5,borderRadius:"50%",background:itemColor(item,idx),flexShrink:0}}/>
                <span style={{fontSize:8.5,color:"#374151",whiteSpace:"nowrap"}}>{itemLabel(item)}</span>
                {item.type === "mb" && item.contents?.length > 0 && (
                  <div style={{display:"flex",gap:1.5,marginLeft:1}}>
                    {item.contents.map((c,ci)=>{
                      const k = Object.keys(CAT_KEYS).find(k=>c.label?.startsWith(k));
                      return <div key={ci} style={{width:4,height:4,borderRadius:"50%",background:k?CAT_COLORS[CAT_KEYS[k]]:"#9CA3AF"}}/>;
                    })}
                  </div>
                )}
              </div>
            ))}
          </div>
        )}

        {/* Connector dot onderaan midden */}
        <div style={{
          position:"absolute", bottom:-5, left:"50%", transform:"translateX(-50%)",
          width:8,height:8,borderRadius:"50%",
          background:"white",border:"1.5px solid #F97316",
          pointerEvents:"none",
        }}/>
      </div>
    </>
  );
}

// ─── SVG label (Diepteligging profiel, stap 6) ───────────────────────────────
export function BoorLabelSVG({ boringConfig, boorlengte, traceGeojson, x: ix = 20, y: iy = 20 }) {
  const [lPos,   setLPos]   = useState({ x: ix, y: iy });
  const [anchor, setAnchor] = useState({ x: ix + 200, y: iy + 40 });

  const traceLengte = traceGeojson ? berekenTraceLengte(traceGeojson) : null;
  const lengte = traceLengte ?? boorlengte;

  if (!boringConfig?.boringD) return null;
  const { items = [], boringD } = boringConfig;
  const W = 130;
  const rows = Math.ceil(items.length / 3);
  const H = 52 + (items.length > 0 ? 6 + rows * 14 : 0);
  const connX = lPos.x + W/2, connY = lPos.y + H;

  function startLabelDrag(e) {
    e.stopPropagation(); e.preventDefault();
    const sx = e.clientX - lPos.x, sy = e.clientY - lPos.y;
    const onMove = ev => setLPos({ x: ev.clientX - sx, y: ev.clientY - sy });
    const onUp = () => { window.removeEventListener("mousemove", onMove); window.removeEventListener("mouseup", onUp); };
    window.addEventListener("mousemove", onMove);
    window.addEventListener("mouseup", onUp);
  }

  function startAnchorDrag(e) {
    e.stopPropagation(); e.preventDefault();
    const sx = e.clientX - anchor.x, sy = e.clientY - anchor.y;
    const onMove = ev => setAnchor({ x: ev.clientX - sx, y: ev.clientY - sy });
    const onUp = () => { window.removeEventListener("mousemove", onMove); window.removeEventListener("mouseup", onUp); };
    window.addEventListener("mousemove", onMove);
    window.addEventListener("mouseup", onUp);
  }

  return (
    <g>
      {/* Leader */}
      <line x1={connX} y1={connY} x2={anchor.x} y2={anchor.y}
            stroke="#f97316" strokeWidth={1.5} strokeDasharray="5,3" opacity={0.75}/>
      {/* Anker op boorlijn */}
      <g onMouseDown={startAnchorDrag} style={{cursor:"crosshair"}}>
        <circle cx={anchor.x} cy={anchor.y} r={8} fill="#f97316" fillOpacity={0.15} stroke="#f97316" strokeWidth={1.5}/>
        <circle cx={anchor.x} cy={anchor.y} r={3.5} fill="#f97316" stroke="white" strokeWidth={1.5}/>
        <line x1={anchor.x-6} y1={anchor.y} x2={anchor.x+6} y2={anchor.y} stroke="#f97316" strokeWidth={1} opacity={0.5}/>
        <line x1={anchor.x} y1={anchor.y-6} x2={anchor.x} y2={anchor.y+6} stroke="#f97316" strokeWidth={1} opacity={0.5}/>
      </g>

      {/* Label */}
      <g transform={`translate(${lPos.x},${lPos.y})`} onMouseDown={startLabelDrag} style={{cursor:"grab"}}>
        <rect x={0} y={0} width={W} height={H} rx={6} fill="white" stroke="#FDBA74" strokeWidth={1.5}
              filter="drop-shadow(0px 2px 4px rgba(0,0,0,0.1))"/>
        {/* Header */}
        <rect x={0} y={0} width={W} height={18} rx={6} fill="#FFFBEB"/>
        <rect x={0} y={12} width={W} height={6} fill="#FFFBEB"/>
        <circle cx={11} cy={9} r={3.5} fill="#F97316"/>
        <text x={19} y={13} fontSize={8} fontWeight="700" fill="#B45309">Boring</text>
        <text x={W-6} y={13} fontSize={10} fill="#D1D5DB" textAnchor="end">⠿</text>
        {/* Separator */}
        <line x1={6} y1={20} x2={W-6} y2={20} stroke="#FEF3C7" strokeWidth={1}/>
        {/* Diameter */}
        <text x={8} y={31} fontSize={7} fill="#9CA3AF">Diameter</text>
        <text x={8} y={43} fontSize={12} fontWeight="800" fill="#1F2937">Ø{boringD}</text>
        <text x={8+String(boringD).length*7+2} y={43} fontSize={7} fill="#6B7280">mm</text>
        {/* Lengte */}
        {lengte && <>
          <line x1={W/2} y1={22} x2={W/2} y2={48} stroke="#F3F4F6" strokeWidth={1}/>
          <text x={W/2+6} y={31} fontSize={7} fill="#9CA3AF">Lengte</text>
          <text x={W/2+6} y={43} fontSize={12} fontWeight="800" fill="#1F2937">{lengte}</text>
          <text x={W/2+6+String(lengte).length*7+2} y={43} fontSize={7} fill="#6B7280">m</text>
        </>}
        {/* Items */}
        {items.length > 0 && <>
          <line x1={6} y1={52} x2={W-6} y2={52} stroke="#F3F4F6" strokeWidth={1}/>
          {items.map((item, idx) => {
            const col = idx % 3, row = Math.floor(idx / 3);
            const tx = 10 + col * 40, ty = 62 + row * 14;
            const clr = itemColor(item, idx);
            return (
              <g key={item.id ?? idx} transform={`translate(${tx},${ty})`}>
                <circle cx={3.5} cy={-3} r={3} fill={clr}/>
                <text x={9} y={0} fontSize={7.5} fill="#374151">{itemLabel(item)}</text>
              </g>
            );
          })}
        </>}
        {/* Connector stipje onderaan */}
        <circle cx={W/2} cy={H} r={4} fill="white" stroke="#F97316" strokeWidth={1.5}/>
      </g>
    </g>
  );
}
