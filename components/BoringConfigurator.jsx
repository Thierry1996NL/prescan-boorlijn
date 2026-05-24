"use client";
import { useState, useMemo, useEffect, useRef, useCallback } from "react";
import { updateProject } from "@/lib/supabase-queries";
import BoringSVG, { PE_SIZES, CATS, TUBE_COLORS, FILL_FACTOR, BORING_FACTOR, gravityPack, computeBoring } from "@/components/BoringSVG";

const S   = 280;
const CX  = S / 2;
const CY  = S / 2 - 8;
const BPX = S / 2 - 32;

const MACHINES = [
  {id:"d10x15",brand:"Vermeer",model:"D10x15 S3",maxBoring:180,push:44.5, torque:1085,stangen:91, engine:"Kubota D1105, 23 pk"},
  {id:"d20x22",brand:"Vermeer",model:"D20x22 S3",maxBoring:250,push:86.7, torque:2983,stangen:122,engine:"Deutz TD2.9, 74 pk"},
  {id:"d23x30",brand:"Vermeer",model:"D23x30 S3",maxBoring:300,push:102,  torque:4067,stangen:122,engine:"Deutz TCD2.9, 90 pk"},
  {id:"d36x50",brand:"Vermeer",model:"D36x50 S3",maxBoring:400,push:160,  torque:6779,stangen:152,engine:"Deutz TCD3.6, 130 pk"},
];

let _uid = 1;
const uid = () => String(++_uid);

function clamp(x, y, maxR) {
  const d = Math.sqrt(x * x + y * y);
  return d <= maxR ? { x, y } : { x: (x / d) * maxR, y: (y / d) * maxR };
}

// ─── BORING CANVAS (drag + uitlijnen) ────────────────────────────────────────
function BoringCanvas({ res, customPos, setCustomPos, selected, setSelected }) {
  const svgRef  = useRef(null);
  const dragRef = useRef(null);
  const { proc } = res;
  const scale = BPX / (res.boringD / 2);

  const gravityPos = useMemo(() => {
    const its = proc.map(p => ({ r: (p.effectiveOD / 2) * scale }));
    return gravityPack(its, BPX);
  }, [proc, scale]);

  function getPos(id, idx) { return customPos[id] ?? gravityPos[idx] ?? { x: 0, y: 0 }; }

  function svgCoords(e) {
    const rect = svgRef.current.getBoundingClientRect();
    const cx = e.touches ? e.touches[0].clientX : e.clientX;
    const cy = e.touches ? e.touches[0].clientY : e.clientY;
    return { x: cx - rect.left - CX, y: cy - rect.top - CY };
  }

  function onItemDown(e, id, r, idx) {
    e.preventDefault(); e.stopPropagation();
    const cur = getPos(id, idx);
    const m   = svgCoords(e);
    dragRef.current = { id, r, mx0: m.x, my0: m.y, px0: cur.x, py0: cur.y };
    if (!e.shiftKey) setSelected(new Set([id]));
    else setSelected(prev => { const s = new Set(prev); s.has(id) ? s.delete(id) : s.add(id); return s; });
  }

  const onMove = useCallback(e => {
    if (!dragRef.current) return;
    const { id, r, mx0, my0, px0, py0 } = dragRef.current;
    const m = svgCoords(e);
    setCustomPos(prev => ({ ...prev, [id]: clamp(px0 + m.x - mx0, py0 + m.y - my0, BPX - r) }));
  }, []);

  const onUp = useCallback(() => { dragRef.current = null; }, []);

  function align(type) {
    if (!selected.size) return;
    const ids  = [...selected];
    const idxs = ids.map(id => proc.findIndex(p => p.id === id));
    const poses = ids.map((id, i) => getPos(id, idxs[i]));
    const rs    = ids.map((_, i) => (proc[idxs[i]].effectiveOD / 2) * scale);
    setCustomPos(prev => {
      const n = { ...prev };
      if (type === "left")    { const v = Math.min(...poses.map(p => p.x)); ids.forEach((id,i) => { n[id] = clamp(v, poses[i].y, BPX - rs[i]); }); }
      if (type === "right")   { const v = Math.max(...poses.map(p => p.x)); ids.forEach((id,i) => { n[id] = clamp(v, poses[i].y, BPX - rs[i]); }); }
      if (type === "centerH") { ids.forEach((id,i) => { n[id] = clamp(0, poses[i].y, BPX - rs[i]); }); }
      if (type === "top")     { const v = Math.min(...poses.map(p => p.y)); ids.forEach((id,i) => { n[id] = clamp(poses[i].x, v, BPX - rs[i]); }); }
      if (type === "bottom")  { const v = Math.max(...poses.map(p => p.y)); ids.forEach((id,i) => { n[id] = clamp(poses[i].x, v, BPX - rs[i]); }); }
      if (type === "centerV") { ids.forEach((id,i) => { n[id] = clamp(poses[i].x, 0, BPX - rs[i]); }); }
      if (type === "distH" && ids.length > 1) {
        const s = ids.map((id,i) => ({id,x:poses[i].x,y:poses[i].y,r:rs[i]})).sort((a,b)=>a.x-b.x);
        const step = (s.at(-1).x - s[0].x) / (s.length - 1);
        s.forEach((it,i) => { n[it.id] = clamp(s[0].x + i*step, it.y, BPX - it.r); });
      }
      if (type === "distV" && ids.length > 1) {
        const s = ids.map((id,i) => ({id,x:poses[i].x,y:poses[i].y,r:rs[i]})).sort((a,b)=>a.y-b.y);
        const step = (s.at(-1).y - s[0].y) / (s.length - 1);
        s.forEach((it,i) => { n[it.id] = clamp(it.x, s[0].y + i*step, BPX - it.r); });
      }
      return n;
    });
  }

  const TB = (label, title, fn, dis) => (
    <button key={title} onClick={fn} title={title} disabled={dis}
            className={`px-2 py-1 text-xs rounded border ${dis?"border-gray-100 text-gray-300 cursor-default":"border-gray-200 text-gray-600 hover:border-orange-300 hover:text-orange-600 cursor-pointer"}`}>
      {label}
    </button>
  );

  const nSel = selected.size;
  return (
    <div>
      <div className="flex flex-wrap items-center gap-1.5 mb-3 p-2 bg-gray-50 rounded-lg border border-gray-200">
        <span className="text-xs text-gray-400 mr-1">Uitlijnen:</span>
        {TB("⇤","Links",()=>align("left"),nSel<1)}
        {TB("↔","Midden H",()=>align("centerH"),nSel<1)}
        {TB("⇥","Rechts",()=>align("right"),nSel<1)}
        <div className="w-px h-4 bg-gray-200 mx-0.5"/>
        {TB("⇡","Boven",()=>align("top"),nSel<1)}
        {TB("↕","Midden V",()=>align("centerV"),nSel<1)}
        {TB("⇣","Onder",()=>align("bottom"),nSel<1)}
        <div className="w-px h-4 bg-gray-200 mx-0.5"/>
        {TB("⇹H","Verdelen H",()=>align("distH"),nSel<2)}
        {TB("⇹V","Verdelen V",()=>align("distV"),nSel<2)}
        <div className="w-px h-4 bg-gray-200 mx-0.5"/>
        <button onClick={() => { setCustomPos({}); setSelected(new Set()); }}
                className="px-2 py-1 text-xs rounded border border-gray-200 text-gray-500 hover:border-orange-300 hover:text-orange-600">
          ↓ Zwaartekracht
        </button>
        {nSel > 0 && <span className="text-xs text-orange-500 ml-auto">{nSel} geselecteerd</span>}
      </div>
      <p className="text-xs text-gray-400 text-center mb-2">Klik om te selecteren · Shift+klik voor meerdere · Slepen om te verplaatsen</p>
      <svg ref={svgRef} width={S} height={S+8} viewBox={`0 0 ${S} ${S+8}`}
           style={{display:"block",margin:"0 auto",cursor:"default",touchAction:"none"}}
           onMouseMove={onMove} onMouseUp={onUp} onMouseLeave={onUp}
           onTouchMove={onMove} onTouchEnd={onUp}
           onClick={() => setSelected(new Set())}>
        <defs>
          <marker id="bcR" markerWidth="5" markerHeight="5" refX="4.5" refY="2.5" orient="auto"><polygon points="0,0 5,2.5 0,5" fill="#9CA3AF"/></marker>
          <marker id="bcL" markerWidth="5" markerHeight="5" refX="0.5" refY="2.5" orient="auto-start-reverse"><polygon points="0,0 5,2.5 0,5" fill="#9CA3AF"/></marker>
          <filter id="bcSel"><feDropShadow dx="0" dy="0" stdDeviation="3" floodColor="#F97316" floodOpacity="0.8"/></filter>
        </defs>
        <circle cx={CX} cy={CY} r={BPX+22} fill="#C4A45A"/>
        {Array.from({length:32},(_,i)=>{const a=(i*137.5*Math.PI)/180,d=BPX+12+(i%5)*1.6;return <circle key={i} cx={CX+d*Math.cos(a)} cy={CY+d*Math.sin(a)} r={1.2} fill="#A0803A" opacity="0.65"/>;  })}
        <circle cx={CX} cy={CY} r={BPX} fill="#C2D6DF" stroke="#7AAFC4" strokeWidth="1.5"/>
        <line x1={CX} y1={CY-BPX+8} x2={CX} y2={CY+BPX-8} stroke="#7AAFC4" strokeWidth="0.5" strokeDasharray="3,4" opacity="0.4"/>
        <line x1={CX-BPX+8} y1={CY} x2={CX+BPX-8} y2={CY} stroke="#7AAFC4" strokeWidth="0.5" strokeDasharray="3,4" opacity="0.4"/>

        {proc.map((item, idx) => {
          const pos   = getPos(item.id, idx);
          const px    = CX + pos.x, py = CY + pos.y;
          const iPxR  = Math.max((item.effectiveOD / 2) * scale, 4);
          const isSel = selected.has(item.id);
          if (item.type === "mb") {
            const wallPx  = Math.max(item.pe.wall * scale, 2);
            const innerPx = Math.max(iPxR - wallPx, 2);
            const cPos    = gravityPack(item.contents.map(c => ({ r: Math.max((c.od/2)*scale, 2.5) })), innerPx);
            return (
              <g key={item.id} style={{cursor:"grab"}}
                 onMouseDown={e=>onItemDown(e,item.id,iPxR,idx)}
                 onTouchStart={e=>onItemDown(e,item.id,iPxR,idx)}>
                <circle cx={px} cy={py} r={iPxR} fill={item.color} filter={isSel?"url(#bcSel)":undefined}/>
                <circle cx={px} cy={py} r={innerPx} fill="#EBF4F8"/>
                {item.contents.map((c,ci)=>{const cp=cPos[ci];if(!cp)return null;const cat2=CATS.find(cc=>cc.items.some(i=>i.label===c.label));return <circle key={c.id} cx={px+cp.x} cy={py+cp.y} r={Math.max((c.od/2)*scale,2.5)} fill={cat2?.color||"#6B7280"}/>;  })}
                {isSel && <circle cx={px} cy={py} r={iPxR} fill="none" stroke="#F97316" strokeWidth="2" strokeDasharray="4,3"/>}
              </g>
            );
          }
          const cat = CATS.find(c => c.items.some(i => i.label === item.label));
          return (
            <g key={item.id} style={{cursor:"grab"}}
               onMouseDown={e=>onItemDown(e,item.id,iPxR,idx)}
               onTouchStart={e=>onItemDown(e,item.id,iPxR,idx)}>
              <circle cx={px} cy={py} r={iPxR} fill={cat?.color||"#6B7280"} filter={isSel?"url(#bcSel)":undefined}/>
              {isSel && <circle cx={px} cy={py} r={iPxR} fill="none" stroke="#F97316" strokeWidth="2" strokeDasharray="4,3"/>}
            </g>
          );
        })}

        {(()=>{const y0=CY+BPX+10;return(<g><line x1={CX-BPX} y1={y0} x2={CX+BPX} y2={y0} stroke="#9CA3AF" strokeWidth="1" markerStart="url(#bcL)" markerEnd="url(#bcR)"/><text x={CX} y={y0+11} textAnchor="middle" fontSize="10" fill="#9CA3AF" fontFamily="system-ui">Ø{res.boringD} mm</text></g>);})()}
      </svg>
    </div>
  );
}

// ─── MACHINE CARD ─────────────────────────────────────────────────────────────
function MachineCard({ machine, boringD, selected, onSelect }) {
  const ok = machine.maxBoring >= boringD, isSel = selected === machine.id;
  return (
    <div onClick={()=>ok&&onSelect(isSel?null:machine.id)} className={`relative rounded-lg p-3 border transition-all ${isSel?"border-orange-400 bg-orange-50":ok?"border-gray-200 bg-white hover:border-gray-300 cursor-pointer":"border-gray-100 bg-gray-50 opacity-40 cursor-default"}`}>
      <div className={`absolute top-2 right-2 text-xs px-1.5 py-0.5 rounded font-medium ${isSel?"bg-orange-100 text-orange-700":ok?"bg-green-100 text-green-700":"bg-red-100 text-red-600"}`}>
        {isSel?"✓ geselecteerd":ok?"compatibel":`max Ø${machine.maxBoring} mm`}
      </div>
      <div className="text-sm font-semibold text-gray-800 pr-24">{machine.brand} {machine.model}</div>
      <div className="text-xs text-gray-400 mb-2">{machine.engine}</div>
      <div className="grid grid-cols-2 gap-x-4 gap-y-0.5">
        {[["Max boring",`Ø${machine.maxBoring} mm`],["Duw/trek",`${machine.push} kN`],["Koppel",`${machine.torque.toLocaleString("nl")} Nm`],["Stangenrek",`${machine.stangen} m`]].map(([k,v])=>(
          <div key={k} className="flex justify-between"><span className="text-xs text-gray-400">{k}</span><span className="text-xs font-medium text-gray-700">{v}</span></div>
        ))}
      </div>
    </div>
  );
}

// ─── HOOFD COMPONENT ──────────────────────────────────────────────────────────
export default function BoringConfigurator({ projectId, initialConfig, onConfigChange }) {
  const [items,     setItems]     = useState([]);
  const [panel,     setPanel]     = useState(null);
  const [peDN,      setPeDN]      = useState(110);
  const [catKey,    setCatKey]    = useState("ls");
  const [selItem,   setSelItem]   = useState(null);
  const [machine,   setMachine]   = useState(null);
  const [customPos, setCustomPos] = useState({});
  const [selected,  setSelected]  = useState(new Set());
  const [saving,    setSaving]    = useState(false);
  const [saved,     setSaved]     = useState(false);

  useEffect(() => {
    if (!initialConfig) return;
    try {
      const p = typeof initialConfig === "string" ? JSON.parse(initialConfig) : initialConfig;
      if (p.items)     setItems(p.items);
      if (p.machine)   setMachine(p.machine);
      if (p.customPos) setCustomPos(p.customPos);
    } catch {}
  }, [initialConfig]);

  const res      = useMemo(() => computeBoring(items), [items]);
  const peData   = PE_SIZES.find(p => p.dn === peDN);
  const warnings = res ? res.proc.filter(p => p.type === "mb" && !p.fitsOK) : [];

  // Stuur config omhoog naar page.js zodat andere stappen het kunnen gebruiken
  useEffect(() => {
    if (onConfigChange) {
      onConfigChange({ items, machine, customPos, boringD: res?.boringD ?? null });
    }
  }, [items, machine, customPos, res?.boringD]);

  const openPanel  = (mode, tid=null) => { setPanel({mode,tid}); setPeDN(110); setCatKey("ls"); setSelItem(null); };
  const closePanel = () => setPanel(null);
  const addMb      = () => { setItems(p=>[...p,{id:uid(),type:"mb",dn:peDN,contents:[]}]); closePanel(); };
  const addContent = tid => { if(!selItem)return; setItems(p=>p.map(it=>it.id===tid?{...it,contents:[...it.contents,{id:uid(),...selItem}]}:it)); closePanel(); };
  const addDirect  = () => { if(!selItem)return; setItems(p=>[...p,{id:uid(),type:"direct",...selItem}]); closePanel(); };
  const removeItem = id => { setItems(p=>p.filter(it=>it.id!==id)); setCustomPos(p=>{const n={...p};delete n[id];return n;}); };
  const removeContent = (tid,cid) => setItems(p=>p.map(it=>it.id===tid?{...it,contents:it.contents.filter(c=>c.id!==cid)}:it));

  async function handleSave() {
    if (!projectId) return;
    setSaving(true); setSaved(false);
    try {
      await updateProject(projectId, {
        boring_config: JSON.stringify({ items, machine, customPos, boringD: res?.boringD ?? null }),
      });
      setSaved(true); setTimeout(() => setSaved(false), 3000);
    } catch (e) { console.error(e); alert("Opslaan mislukt. Probeer opnieuw."); }
    finally { setSaving(false); }
  }

  const StepHead = ({ num, label, done }) => (
    <div className="flex items-center gap-2.5 mb-4">
      <div className={`w-6 h-6 rounded-full flex items-center justify-center text-xs font-semibold flex-shrink-0 ${done?"bg-orange-500 text-white":"bg-gray-100 text-gray-400 border border-gray-200"}`}>{done?"✓":num}</div>
      <span className="text-sm font-semibold text-gray-800">{label}</span>
    </div>
  );

  return (
    <div className="mt-4 space-y-3">

      {/* STAP 1 */}
      <div className="bg-white border border-gray-200 rounded-xl p-4">
        <StepHead num={1} label="Wat gaat er doorheen?" done={items.length > 0}/>

        {items.map(item => {
          if (item.type === "mb") {
            const pr = res?.proc.find(p=>p.id===item.id);
            return (
              <div key={item.id} className={`rounded-lg mb-2 overflow-hidden border ${pr&&!pr.fitsOK?"border-red-200 bg-red-50":"border-gray-100"}`}>
                <div className="flex items-center gap-2 px-3 py-2 bg-gray-50">
                  <div className="w-2.5 h-2.5 rounded-full flex-shrink-0" style={{background:pr?.color||"#6B7280"}}/>
                  <span className="text-xs font-medium text-gray-800">PE {item.dn} mantelbuis</span>
                  {pr && <span className={`text-xs ${pr.fitsOK?"text-green-600":"text-red-500"}`}>{pr.fitsOK?`${Math.round(pr.fillPct)}% gevuld`:`te vol — min Ø${Math.ceil(pr.reqID)} mm`}</span>}
                  <div className="ml-auto flex gap-1.5">
                    <button onClick={()=>openPanel("cable",item.id)} className="text-xs px-2 py-1 bg-blue-50 text-blue-600 border border-blue-200 rounded">+ kabel</button>
                    <button onClick={()=>removeItem(item.id)} className="text-xs px-2 py-1 border border-gray-200 rounded text-gray-400 hover:text-red-500">✕</button>
                  </div>
                </div>
                {item.contents.length > 0 && (
                  <div className="px-3 py-1.5 pl-6 space-y-0.5">
                    {item.contents.map(c=>{
                      const cat=CATS.find(cc=>cc.items.some(i=>i.label===c.label));
                      return (<div key={c.id} className="flex items-center gap-2">
                        <div className="w-1.5 h-1.5 rounded-full flex-shrink-0" style={{background:cat?.color||"#6B7280"}}/>
                        <span className="text-xs text-gray-700">{c.label}</span>
                        <span className="text-xs text-gray-400">Ø{c.od} mm</span>
                        <button onClick={()=>removeContent(item.id,c.id)} className="ml-auto text-gray-300 hover:text-red-400 text-xs">✕</button>
                      </div>);
                    })}
                  </div>
                )}
              </div>
            );
          }
          const cat=CATS.find(c=>c.items.some(i=>i.label===item.label));
          return (
            <div key={item.id} className="flex items-center gap-2 px-3 py-2 border border-gray-100 rounded-lg mb-2">
              <div className="w-2.5 h-2.5 rounded-full flex-shrink-0" style={{background:cat?.color||"#6B7280"}}/>
              <span className="text-xs text-gray-800">{item.label}</span>
              <span className="text-xs text-gray-400">Ø{item.od} mm</span>
              <button onClick={()=>removeItem(item.id)} className="ml-auto text-gray-300 hover:text-red-400 text-xs">✕</button>
            </div>
          );
        })}

        <div className="flex gap-2 mt-1">
          {[{label:"+ Mantelbuis",mode:"mb"},{label:"+ Direct product",mode:"direct"}].map(b=>(
            <button key={b.mode} onClick={()=>openPanel(b.mode)} className="flex-1 py-2 text-xs text-gray-400 border border-dashed border-gray-200 rounded-lg hover:border-orange-300 hover:text-orange-500 transition-colors">{b.label}</button>
          ))}
        </div>

        {panel && (
          <div className="mt-3 p-3 bg-gray-50 border border-orange-200 rounded-lg">
            <div className="flex justify-between items-center mb-3">
              <span className="text-xs font-semibold text-gray-700">{panel.mode==="mb"?"Mantelbuis kiezen":panel.tid?"Kabel / leiding toevoegen":"Direct product kiezen"}</span>
              <button onClick={closePanel} className="text-gray-400 hover:text-gray-600">✕</button>
            </div>
            {panel.mode === "mb" ? (
              <>
                <p className="text-xs text-gray-400 mb-2">Diameter PE SDR11:</p>
                <div className="flex flex-wrap gap-1.5 mb-3">
                  {PE_SIZES.map(pe=>(
                    <button key={pe.dn} onClick={()=>setPeDN(pe.dn)} className={`text-xs px-2.5 py-1 rounded border transition-colors ${peDN===pe.dn?"border-orange-400 bg-orange-50 text-orange-600 font-semibold":"border-gray-200 text-gray-600"}`}>Ø{pe.dn}</button>
                  ))}
                </div>
                {peData && <p className="text-xs text-gray-400 mb-3">ID {peData.id} mm · wand {peData.wall} mm</p>}
                <button onClick={addMb} className="w-full py-2 bg-orange-500 text-white text-sm font-medium rounded-lg hover:bg-orange-600">Toevoegen</button>
              </>
            ) : (
              <>
                <div className="flex gap-1.5 flex-wrap mb-3">
                  {CATS.map(c=>(
                    <button key={c.key} onClick={()=>{setCatKey(c.key);setSelItem(null);}} className={`text-xs px-2.5 py-1 rounded border transition-colors ${catKey===c.key?"text-white border-transparent":"border-gray-200 text-gray-500"}`} style={catKey===c.key?{background:c.color}:{}}>{c.label}</button>
                  ))}
                </div>
                <div className="space-y-1 mb-3">
                  {CATS.find(c=>c.key===catKey)?.items.map(it=>(
                    <button key={it.label} onClick={()=>setSelItem(it)} className={`w-full flex justify-between px-2.5 py-1.5 rounded border text-left ${selItem?.label===it.label?"border-orange-400 bg-orange-50":"border-gray-200 bg-white"}`}>
                      <span className="text-xs text-gray-800">{it.label}</span>
                      <span className="text-xs text-gray-400">Ø{it.od} mm</span>
                    </button>
                  ))}
                </div>
                <button onClick={()=>panel.tid?addContent(panel.tid):addDirect()} disabled={!selItem} className={`w-full py-2 text-sm font-medium rounded-lg ${selItem?"bg-orange-500 text-white hover:bg-orange-600":"bg-gray-100 text-gray-400 cursor-default"}`}>Toevoegen</button>
              </>
            )}
          </div>
        )}
      </div>

      {/* STAP 2 */}
      <div className={`bg-white border border-gray-200 rounded-xl p-4 transition-opacity ${res?"opacity-100":"opacity-40 pointer-events-none"}`}>
        <StepHead num={2} label="Berekening en dwarsdoorsnede" done={!!res}/>
        {!res ? (
          <p className="text-xs text-gray-400 text-center py-8">Voeg materialen toe in stap 1</p>
        ) : (
          <>
            <div className="grid grid-cols-3 gap-2 mb-4">
              {[["Productbundel",`Ø${Math.round(res.bundleD)} mm`],["Vereiste boring",`Ø${res.boringD} mm`],["Norm","SIKB 1.5×"]].map(([k,v])=>(
                <div key={k} className="bg-gray-50 rounded-lg px-3 py-2 text-center">
                  <div className="text-xs text-gray-400 mb-0.5">{k}</div>
                  <div className="text-sm font-bold text-gray-800">{v}</div>
                </div>
              ))}
            </div>
            {warnings.length > 0 && (
              <div className="bg-red-50 border border-red-200 rounded-lg px-3 py-2 mb-3">
                {warnings.map(w=><p key={w.id} className="text-xs text-red-600">⚠ PE {w.dn} te klein — min Ø{Math.ceil(w.reqID)} mm nodig</p>)}
              </div>
            )}
            <BoringCanvas res={res} customPos={customPos} setCustomPos={setCustomPos} selected={selected} setSelected={setSelected}/>
            <div className="flex flex-wrap gap-x-3 gap-y-1 mt-3 pt-3 border-t border-gray-100">
              <div className="flex items-center gap-1.5">
                <div className="w-3 h-3 rounded border" style={{background:"#C2D6DF",borderColor:"#7AAFC4"}}/>
                <span className="text-xs text-gray-400">Bentoniet</span>
              </div>
              {res.proc.map(item=>(
                <div key={item.id} className="flex items-center gap-1.5">
                  <div className="w-3 h-3 rounded-full" style={{background:item.color}}/>
                  <span className="text-xs text-gray-400">{item.type==="mb"?`PE${item.dn} mantelbuis`:item.label}</span>
                </div>
              ))}
            </div>
          </>
        )}
      </div>

      {/* STAP 3 */}
      <div className={`bg-white border border-gray-200 rounded-xl p-4 transition-opacity ${res?"opacity-100":"opacity-40 pointer-events-none"}`}>
        <StepHead num={3} label="Machine kiezen" done={!!machine}/>
        {res && <p className="text-xs text-gray-500 mb-3">Vereiste boring: <strong>Ø{res.boringD} mm</strong></p>}
        <div className="space-y-2">
          {MACHINES.map(m=>(
            <MachineCard key={m.id} machine={m} boringD={res?.boringD??9999} selected={machine} onSelect={setMachine}/>
          ))}
        </div>
      </div>

      {/* OPSLAAN */}
      <div className="flex items-center justify-between pt-1 pb-2">
        <span className={`text-xs transition-opacity duration-500 ${saved?"text-green-600 opacity-100":"opacity-0"}`}>✓ Opgeslagen</span>
        <button onClick={handleSave} disabled={saving||!items.length}
                className={`px-5 py-2.5 text-sm font-medium rounded-lg transition-colors ${items.length?saving?"bg-orange-300 text-white cursor-wait":"bg-orange-500 text-white hover:bg-orange-600":"bg-gray-100 text-gray-400 cursor-default"}`}>
          {saving?"Opslaan…":"Configuratie opslaan"}
        </button>
      </div>
    </div>
  );
}
