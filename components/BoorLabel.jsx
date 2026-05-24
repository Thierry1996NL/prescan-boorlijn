"use client";
import { useState, useRef } from "react";

const TUBE_COLORS = ["#1D4ED8","#047857","#B45309","#6D28D9","#374151","#B91C1C"];
const CAT_COLORS  = { ls:"#DC2626", ms:"#7C3AED", gf:"#D97706", water:"#2563EB", gas:"#F59E0B" };
const CAT_LOOKUP  = {
  "YMVK":"ls","12 kV":"ms","Microduct":"gf","GF kabel":"gf",
  "PE32 water":"water","PE40 water":"water","PE50 water":"water","PE63 water":"water","PE90 water":"water",
  "PE32 gas":"gas","PE40 gas":"gas","PE50 gas":"gas","PE63 gas":"gas",
};

function itemColor(item, idx) {
  if (item.type === "mb") return TUBE_COLORS[idx % TUBE_COLORS.length];
  const key = Object.keys(CAT_LOOKUP).find(k => item.label?.startsWith(k));
  return key ? CAT_COLORS[CAT_LOOKUP[key]] : "#6B7280";
}

function itemLabel(item) {
  if (item.type === "mb") return `PE${item.dn}`;
  return item.label?.split(" ").slice(0,2).join(" ") || "?";
}

// ─── HTML label (Leaflet kaarten, stap 4 5 7 8) ──────────────────────────────
export default function BoorLabel({ boringConfig, boorlengte, initialPos }) {
  const [pos,  setPos]  = useState(initialPos ?? { x: 16, y: 16 });
  const dragRef = useRef(null);

  if (!boringConfig?.boringD) return null;
  const { items = [], boringD } = boringConfig;

  function onMouseDown(e) {
    e.stopPropagation(); e.preventDefault();
    dragRef.current = { sx: e.clientX - pos.x, sy: e.clientY - pos.y };
    const onMove = ev => setPos({ x: ev.clientX - dragRef.current.sx, y: ev.clientY - dragRef.current.sy });
    const onUp   = ()  => { dragRef.current = null; window.removeEventListener("mousemove", onMove); window.removeEventListener("mouseup", onUp); };
    window.addEventListener("mousemove", onMove);
    window.addEventListener("mouseup",  onUp);
  }

  return (
    <div onMouseDown={onMouseDown} style={{
      position:"absolute", left:pos.x, top:pos.y, zIndex:1000,
      cursor:"grab", userSelect:"none", touchAction:"none",
      background:"white", border:"1.5px solid #FDBA74",
      borderRadius:10, padding:"8px 10px",
      boxShadow:"0 2px 12px rgba(0,0,0,0.13)", minWidth:148,
    }}>
      {/* Header */}
      <div style={{display:"flex",alignItems:"center",gap:6,marginBottom:6}}>
        <div style={{width:7,height:7,borderRadius:"50%",background:"#F97316",flexShrink:0}}/>
        <span style={{fontSize:10,fontWeight:700,color:"#EA580C",lineHeight:1}}>Boring configuratie</span>
        <span style={{marginLeft:"auto",fontSize:12,color:"#D1D5DB",lineHeight:1}}>⠿</span>
      </div>

      {/* Diameter + lengte */}
      <div style={{display:"flex",gap:10,marginBottom:6}}>
        <div>
          <div style={{fontSize:9,color:"#9CA3AF",marginBottom:1}}>Diameter</div>
          <div style={{fontSize:14,fontWeight:800,color:"#1F2937",lineHeight:1}}>Ø{boringD} <span style={{fontSize:10,fontWeight:500}}>mm</span></div>
        </div>
        {boorlengte && (
          <div style={{borderLeft:"1px solid #F3F4F6",paddingLeft:10}}>
            <div style={{fontSize:9,color:"#9CA3AF",marginBottom:1}}>Lengte</div>
            <div style={{fontSize:14,fontWeight:800,color:"#1F2937",lineHeight:1}}>{boorlengte} <span style={{fontSize:10,fontWeight:500}}>m</span></div>
          </div>
        )}
      </div>

      {/* Inhoud chips */}
      {items.length > 0 && (
        <div style={{display:"flex",flexWrap:"wrap",gap:3,paddingTop:4,borderTop:"1px solid #F3F4F6"}}>
          {items.map((item, idx) => (
            <div key={item.id ?? idx} style={{
              display:"flex",alignItems:"center",gap:3,
              background:"#F9FAFB",border:"1px solid #E5E7EB",
              borderRadius:20,padding:"2px 7px",
            }}>
              <div style={{width:6,height:6,borderRadius:"50%",background:itemColor(item,idx),flexShrink:0}}/>
              <span style={{fontSize:10,color:"#374151",whiteSpace:"nowrap"}}>{itemLabel(item)}</span>
              {item.type === "mb" && item.contents?.length > 0 && (
                <div style={{display:"flex",gap:2,marginLeft:1}}>
                  {item.contents.slice(0,4).map((c,ci) => {
                    const ck = Object.keys(CAT_LOOKUP).find(k => c.label?.startsWith(k));
                    return <div key={ci} style={{width:5,height:5,borderRadius:"50%",background:ck?CAT_COLORS[CAT_LOOKUP[ck]]:"#9CA3AF"}}/>;
                  })}
                </div>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

// ─── SVG label (Diepteligging profiel, stap 6) ───────────────────────────────
export function BoorLabelSVG({ boringConfig, boorlengte, x: ix = 20, y: iy = 20 }) {
  const [pos, setPos]   = useState({ x: ix, y: iy });
  const dragging = useRef(false);
  const origin   = useRef(null);

  if (!boringConfig?.boringD) return null;
  const { items = [], boringD } = boringConfig;
  const W = 148, lineH = 16;
  const rows = Math.ceil(items.length / 3);
  const H = 56 + (items.length > 0 ? 8 + rows * lineH : 0);

  function onMouseDown(e) {
    e.stopPropagation(); e.preventDefault();
    dragging.current = true;
    origin.current = { mx: e.clientX, my: e.clientY, px: pos.x, py: pos.y };
    const onMove = ev => {
      if (!dragging.current || !origin.current) return;
      setPos({ x: origin.current.px + ev.clientX - origin.current.mx, y: origin.current.py + ev.clientY - origin.current.my });
    };
    const onUp = () => { dragging.current = false; window.removeEventListener("mousemove", onMove); window.removeEventListener("mouseup", onUp); };
    window.addEventListener("mousemove", onMove);
    window.addEventListener("mouseup", onUp);
  }

  return (
    <g transform={`translate(${pos.x},${pos.y})`} onMouseDown={onMouseDown} style={{cursor:"grab",userSelect:"none"}}>
      {/* Schaduw */}
      <rect x={2} y={2} width={W} height={H} rx={7} fill="rgba(0,0,0,0.08)"/>
      {/* Achtergrond */}
      <rect x={0} y={0} width={W} height={H} rx={7} fill="white" stroke="#FDBA74" strokeWidth={1.5}/>
      {/* Header */}
      <circle cx={12} cy={14} r={4} fill="#F97316"/>
      <text x={22} y={18} fontSize={9} fontWeight="700" fill="#EA580C">Boring configuratie</text>
      <text x={W-8} y={18} fontSize={11} fill="#D1D5DB" textAnchor="end">⠿</text>
      {/* Separator */}
      <line x1={8} y1={24} x2={W-8} y2={24} stroke="#F3F4F6" strokeWidth={1}/>
      {/* Diameter */}
      <text x={12} y={36} fontSize={8} fill="#9CA3AF">Diameter</text>
      <text x={12} y={47} fontSize={13} fontWeight="800" fill="#1F2937">Ø{boringD} mm</text>
      {/* Lengte */}
      {boorlengte && <>
        <line x1={W/2} y1={26} x2={W/2} y2={50} stroke="#F3F4F6" strokeWidth={1}/>
        <text x={W/2+8} y={36} fontSize={8} fill="#9CA3AF">Lengte</text>
        <text x={W/2+8} y={47} fontSize={13} fontWeight="800" fill="#1F2937">{boorlengte} m</text>
      </>}
      {/* Items */}
      {items.length > 0 && <>
        <line x1={8} y1={56} x2={W-8} y2={56} stroke="#F3F4F6" strokeWidth={1}/>
        {items.map((item, idx) => {
          const col = idx % 3, row = Math.floor(idx / 3);
          const tx = 12 + col * 46, ty = 66 + row * lineH;
          return (
            <g key={item.id ?? idx} transform={`translate(${tx},${ty})`}>
              <circle cx={4} cy={-3} r={3.5} fill={itemColor(item,idx)}/>
              <text x={10} y={0} fontSize={8} fill="#374151">{itemLabel(item)}</text>
            </g>
          );
        })}
      </>}
    </g>
  );
}
