import { useState, useMemo } from "react";

const PE_SIZES = [
  { dn: 32,  od: 32,  wall: 3.0,  id: 26.0  },
  { dn: 40,  od: 40,  wall: 3.7,  id: 32.6  },
  { dn: 50,  od: 50,  wall: 4.6,  id: 40.8  },
  { dn: 63,  od: 63,  wall: 5.8,  id: 51.4  },
  { dn: 75,  od: 75,  wall: 6.8,  id: 61.4  },
  { dn: 90,  od: 90,  wall: 8.2,  id: 73.6  },
  { dn: 110, od: 110, wall: 10.0, id: 90.0  },
  { dn: 125, od: 125, wall: 11.4, id: 102.2 },
  { dn: 160, od: 160, wall: 14.6, id: 130.8 },
  { dn: 200, od: 200, wall: 18.2, id: 163.6 },
  { dn: 250, od: 250, wall: 22.7, id: 204.6 },
];

const CATS = [
  { key: "ls", label: "Kabel LS", color: "#DC2626",
    items: [
      { label: "YMVK 4x10 mm2",  od: 19 },
      { label: "YMVK 4x16 mm2",  od: 22 },
      { label: "YMVK 4x25 mm2",  od: 24 },
      { label: "YMVK 4x35 mm2",  od: 26 },
      { label: "YMVK 4x50 mm2",  od: 29 },
      { label: "YMVK 4x95 mm2",  od: 35 },
      { label: "YMVK 4x150 mm2", od: 40 },
    ]},
  { key: "ms", label: "Kabel MS", color: "#7C3AED",
    items: [
      { label: "12 kV 1x95 mm2",  od: 40 },
      { label: "12 kV 1x150 mm2", od: 45 },
      { label: "12 kV 3x95 mm2",  od: 70 },
    ]},
  { key: "gf", label: "Glasvezel", color: "#D97706",
    items: [
      { label: "Microduct 10/8 mm",  od: 10 },
      { label: "Microduct 16/12 mm", od: 16 },
      { label: "GF kabel 12F",       od: 14 },
      { label: "GF kabel 24F",       od: 16 },
      { label: "GF kabel 96F",       od: 22 },
    ]},
  { key: "water", label: "Water PE", color: "#2563EB",
    items: [
      { label: "PE32 water", od: 32 },
      { label: "PE40 water", od: 40 },
      { label: "PE50 water", od: 50 },
      { label: "PE63 water", od: 63 },
      { label: "PE90 water", od: 90 },
    ]},
  { key: "gas", label: "Gas PE", color: "#F59E0B",
    items: [
      { label: "PE32 gas", od: 32 },
      { label: "PE40 gas", od: 40 },
      { label: "PE50 gas", od: 50 },
      { label: "PE63 gas", od: 63 },
    ]},
];

const MACHINES = [
  { id:"d10x15", brand:"Vermeer", model:"D10x15 S3", maxBoring:180, push:44.5, torque:1085, stangen:91,  engine:"Kubota D1105, 23 pk" },
  { id:"d20x22", brand:"Vermeer", model:"D20x22 S3", maxBoring:250, push:86.7, torque:2983, stangen:122, engine:"Deutz TD2.9, 74 pk" },
  { id:"d23x30", brand:"Vermeer", model:"D23x30 S3", maxBoring:300, push:102,  torque:4067, stangen:122, engine:"Deutz TCD2.9, 90 pk" },
  { id:"d36x50", brand:"Vermeer", model:"D36x50 S3", maxBoring:400, push:160,  torque:6779, stangen:152, engine:"Deutz TCD3.6, 130 pk" },
];

const TUBE_COLORS = ["#1D4ED8","#047857","#B45309","#6D28D9","#374151","#B91C1C"];
const FILL_FACTOR  = 0.40;
const BORING_FACTOR = 1.50;

let _uid = 1;
const uid = () => String(++_uid);

function boundR(radii) {
  if (!radii.length) return 0;
  if (radii.length === 1) return radii[0];
  const A = radii.reduce((s, r) => s + Math.PI * r * r, 0);
  return Math.sqrt(A / (Math.PI * 0.64));
}

function ringPos(n, ringR, cx, cy) {
  return Array.from({ length: n }, (_, i) => ({
    x: cx + ringR * Math.cos((2 * Math.PI * i) / n - Math.PI / 2),
    y: cy + ringR * Math.sin((2 * Math.PI * i) / n - Math.PI / 2),
  }));
}

function compute(items) {
  if (!items.length) return null;
  const proc = items.map((item, idx) => {
    if (item.type === "mb") {
      const pe = PE_SIZES.find(p => p.dn === item.dn) || PE_SIZES[6];
      const cArea  = item.contents.reduce((s, c) => s + Math.PI * (c.od / 2) ** 2, 0);
      const reqID  = item.contents.length ? 2 * Math.sqrt(cArea / (Math.PI * FILL_FACTOR)) : 0;
      const idArea = Math.PI * (pe.id / 2) ** 2;
      const fillPct = idArea > 0 ? Math.min((cArea / idArea) * 100, 100) : 0;
      return { ...item, pe, cArea, reqID, fillPct, fitsOK: pe.id >= reqID, effectiveOD: pe.od, color: TUBE_COLORS[idx % TUBE_COLORS.length] };
    }
    const cat = CATS.find(c => c.items.some(i => i.label === item.label));
    return { ...item, effectiveOD: item.od, color: cat?.color || "#6B7280" };
  });
  const bundleRmm = boundR(proc.map(p => p.effectiveOD / 2));
  const bundleD   = bundleRmm * 2;
  const rawBoring = bundleD * BORING_FACTOR;
  const boringD   = Math.max(Math.ceil(rawBoring / 25) * 25, 75);
  return { proc, bundleD, boringD };
}

function BoringViz({ res }) {
  const S = 280; const cx = S/2, cy = S/2 - 8;
  const maxPxR = S/2 - 32;
  const scale  = maxPxR / (res.boringD / 2);
  const bPx    = (res.boringD / 2) * scale;
  const topRadii  = res.proc.map(p => p.effectiveOD / 2);
  const topBR     = boundR(topRadii);
  const maxTopR   = topRadii.length ? Math.max(...topRadii) : 0;
  const topRingPx = res.proc.length === 1 ? 0 : Math.max((topBR - maxTopR) * scale, maxTopR * 0.6 * scale);
  const topPos    = res.proc.length === 1 ? [{ x: cx, y: cy }] : ringPos(res.proc.length, topRingPx, cx, cy);

  return (
    <svg width={S} height={S+8} viewBox={`0 0 ${S} ${S+8}`} style={{ display:"block", margin:"0 auto" }}>
      <defs>
        <marker id="arR" markerWidth="5" markerHeight="5" refX="4.5" refY="2.5" orient="auto"><polygon points="0,0 5,2.5 0,5" fill="#9CA3AF" /></marker>
        <marker id="arL" markerWidth="5" markerHeight="5" refX="0.5" refY="2.5" orient="auto-start-reverse"><polygon points="0,0 5,2.5 0,5" fill="#9CA3AF" /></marker>
      </defs>
      <circle cx={cx} cy={cy} r={bPx+22} fill="#C4A45A" />
      {Array.from({length:32},(_,i)=>{const a=(i*137.5*Math.PI)/180;const d=bPx+12+(i%5)*1.6;return <circle key={i} cx={cx+d*Math.cos(a)} cy={cy+d*Math.sin(a)} r={1.2} fill="#A0803A" opacity="0.65"/>;})}
      <circle cx={cx} cy={cy} r={bPx} fill="#C2D6DF" stroke="#7AAFC4" strokeWidth="1.5" />
      {res.proc.map((item,idx)=>{
        const pos=topPos[idx]; if(!pos)return null;
        const iPxR=Math.max((item.effectiveOD/2)*scale,4);
        if(item.type==="mb"){
          const wallPx=Math.max(item.pe.wall*scale,2);
          const innerPx=Math.max(iPxR-wallPx,2);
          const cRadii=item.contents.map(c=>c.od/2);
          const cBR=boundR(cRadii);
          const maxCR=cRadii.length?Math.max(...cRadii):0;
          const cRingPx=item.contents.length<=1?0:Math.max((cBR-maxCR)*scale,maxCR*0.55*scale);
          const cPos=item.contents.length===1?[{x:pos.x,y:pos.y}]:ringPos(item.contents.length,cRingPx,pos.x,pos.y);
          return(
            <g key={item.id}>
              <circle cx={pos.x} cy={pos.y} r={iPxR} fill={item.color}/>
              <circle cx={pos.x} cy={pos.y} r={innerPx} fill="#EBF4F8"/>
              {item.contents.map((c,ci)=>{const cp=cPos[ci];if(!cp)return null;const cat2=CATS.find(cc=>cc.items.some(i=>i.label===c.label));const cr=Math.max((c.od/2)*scale,2.5);return<circle key={c.id} cx={cp.x} cy={cp.y} r={cr} fill={cat2?.color||"#6B7280"}/>;  })}
            </g>
          );
        }
        const cat=CATS.find(c=>c.items.some(i=>i.label===item.label));
        return <circle key={item.id} cx={pos.x} cy={pos.y} r={iPxR} fill={cat?.color||"#6B7280"}/>;
      })}
      {(()=>{const y0=cy+bPx+10;return(<g><line x1={cx-bPx} y1={y0} x2={cx+bPx} y2={y0} stroke="#9CA3AF" strokeWidth="1" markerStart="url(#arL)" markerEnd="url(#arR)"/><text x={cx} y={y0+11} textAnchor="middle" fontSize="10" fill="#9CA3AF" fontFamily="system-ui">O{res.boringD} mm</text></g>);})()}
    </svg>
  );
}

function MachineCard({ machine, boringD, selected, onSelect }) {
  const compatible = machine.maxBoring >= boringD;
  const isSelected = selected === machine.id;
  return (
    <div onClick={()=>compatible&&onSelect(isSelected?null:machine.id)} style={{
      border:`1.5px solid ${isSelected?"#F97316":compatible?"var(--color-border-secondary)":"var(--color-border-tertiary)"}`,
      borderRadius:"var(--border-radius-md)", padding:"10px 12px",
      background:isSelected?"#FFF7ED":"var(--color-background-primary)",
      opacity:compatible?1:0.45, cursor:compatible?"pointer":"default",
      position:"relative", transition:"border-color 0.15s",
    }}>
      <div style={{position:"absolute",top:7,right:9,fontSize:10,borderRadius:4,padding:"1px 6px",
        color:isSelected?"#EA580C":compatible?"#16A34A":"#EF4444",
        background:isSelected?"#FFEDD5":compatible?"#DCFCE7":"#FEE2E2",
        fontWeight:isSelected?600:400,
      }}>
        {isSelected?"geselecteerd":compatible?"compatibel":`max O${machine.maxBoring} mm`}
      </div>
      <div style={{fontWeight:600,fontSize:13,color:"var(--color-text-primary)",marginBottom:2}}>{machine.brand} {machine.model}</div>
      <div style={{fontSize:11,color:"var(--color-text-secondary)",marginBottom:8}}>{machine.engine}</div>
      <div style={{display:"grid",gridTemplateColumns:"1fr 1fr",gap:"3px 16px"}}>
        {[["Max boring",`O${machine.maxBoring} mm`],["Duw/trek",`${machine.push} kN`],["Koppel",`${machine.torque.toLocaleString("nl")} Nm`],["Stangenrek",`${machine.stangen} m`]].map(([k,v])=>(
          <div key={k} style={{display:"flex",justifyContent:"space-between",alignItems:"baseline"}}>
            <span style={{fontSize:11,color:"var(--color-text-secondary)"}}>{k}</span>
            <span style={{fontSize:12,fontWeight:500,color:"var(--color-text-primary)"}}>{v}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

export default function BoringConfigurator() {
  const [items,   setItems]   = useState([]);
  const [panel,   setPanel]   = useState(null);
  const [peDN,    setPeDN]    = useState(110);
  const [catKey,  setCatKey]  = useState("ls");
  const [selItem, setSelItem] = useState(null);
  const [machine, setMachine] = useState(null);

  const res = useMemo(() => compute(items), [items]);
  const peData = PE_SIZES.find(p => p.dn === peDN);

  const openPanel = (mode, targetId=null) => { setPanel({mode,targetId}); setPeDN(110); setCatKey("ls"); setSelItem(null); };
  const closePanel = () => setPanel(null);
  const addMb = () => { setItems(prev=>[...prev,{id:uid(),type:"mb",dn:peDN,contents:[]}]); closePanel(); };
  const addContent = (targetId) => { if(!selItem)return; setItems(prev=>prev.map(it=>it.id===targetId?{...it,contents:[...it.contents,{id:uid(),...selItem}]}:it)); closePanel(); };
  const addDirect = () => { if(!selItem)return; setItems(prev=>[...prev,{id:uid(),type:"direct",...selItem}]); closePanel(); };
  const removeItem = id => setItems(prev=>prev.filter(it=>it.id!==id));
  const removeContent = (tid,cid) => setItems(prev=>prev.map(it=>it.id===tid?{...it,contents:it.contents.filter(c=>c.id!==cid)}:it));

  const warnings = res ? res.proc.filter(p=>p.type==="mb"&&!p.fitsOK) : [];

  const card = {
    background:"var(--color-background-primary)",
    border:"0.5px solid var(--color-border-secondary)",
    borderRadius:"var(--border-radius-lg)",
    padding:"16px", marginBottom:12,
  };

  const StepHead = ({num, label, done}) => (
    <div style={{display:"flex",alignItems:"center",gap:10,marginBottom:14}}>
      <div style={{
        width:24,height:24,borderRadius:"50%",flexShrink:0,
        background:done?"#F97316":"var(--color-background-secondary)",
        border:`1.5px solid ${done?"#F97316":"var(--color-border-secondary)"}`,
        display:"flex",alignItems:"center",justifyContent:"center",
        fontSize:12,fontWeight:600,color:done?"#FFF":"var(--color-text-secondary)",
      }}>
        {done ? <i className="ti ti-check" style={{fontSize:12}}/> : num}
      </div>
      <span style={{fontWeight:600,fontSize:14,color:"var(--color-text-primary)"}}>{label}</span>
    </div>
  );

  return (
    <div style={{maxWidth:480,margin:"0 auto",padding:"16px 12px",fontFamily:"system-ui,sans-serif"}}>

      {/* STAP 1 */}
      <div style={card}>
        <StepHead num={1} label="Wat gaat er doorheen?" done={items.length>0} />

        {items.map(item => {
          if(item.type==="mb"){
            const pr=res?.proc.find(p=>p.id===item.id);
            const color=pr?.color||"#6B7280";
            return(
              <div key={item.id} style={{border:`0.5px solid ${pr&&!pr.fitsOK?"#FCA5A5":"var(--color-border-tertiary)"}`,borderRadius:"var(--border-radius-md)",marginBottom:8,overflow:"hidden",background:pr&&!pr.fitsOK?"#FFF5F5":"transparent"}}>
                <div style={{display:"flex",alignItems:"center",gap:7,padding:"7px 10px",background:"var(--color-background-secondary)"}}>
                  <div style={{width:10,height:10,borderRadius:"50%",background:color,flexShrink:0}}/>
                  <span style={{fontWeight:500,fontSize:13,color:"var(--color-text-primary)"}}>PE {item.dn} mantelbuis</span>
                  {pr&&(<span style={{fontSize:11,color:pr.fitsOK?"#16A34A":"#EF4444"}}>{pr.fitsOK?`${Math.round(pr.fillPct)}% gevuld`:`te vol - min O${Math.ceil(pr.reqID)} mm`}</span>)}
                  <div style={{marginLeft:"auto",display:"flex",gap:4}}>
                    <button onClick={()=>openPanel("cable",item.id)} style={{fontSize:11,background:"#EFF6FF",color:"#2563EB",border:"0.5px solid #BFDBFE",borderRadius:"var(--border-radius-md)",padding:"2px 8px",cursor:"pointer"}}>
                      <i className="ti ti-plus" style={{fontSize:10}}/> kabel
                    </button>
                    <button onClick={()=>removeItem(item.id)} style={{fontSize:11,background:"transparent",color:"var(--color-text-secondary)",border:"0.5px solid var(--color-border-secondary)",borderRadius:"var(--border-radius-md)",padding:"2px 7px",cursor:"pointer"}}>
                      <i className="ti ti-trash" style={{fontSize:12}}/>
                    </button>
                  </div>
                </div>
                {item.contents.length>0&&(
                  <div style={{padding:"4px 10px 8px 24px"}}>
                    {item.contents.map(c=>{
                      const cat=CATS.find(cc=>cc.items.some(i=>i.label===c.label));
                      return(
                        <div key={c.id} style={{display:"flex",alignItems:"center",gap:6,padding:"2px 0"}}>
                          <div style={{width:6,height:6,borderRadius:"50%",background:cat?.color||"#6B7280",flexShrink:0}}/>
                          <span style={{fontSize:12,color:"var(--color-text-primary)"}}>{c.label}</span>
                          <span style={{fontSize:11,color:"var(--color-text-secondary)"}}>O{c.od} mm</span>
                          <button onClick={()=>removeContent(item.id,c.id)} style={{marginLeft:"auto",background:"none",border:"none",color:"var(--color-text-secondary)",cursor:"pointer",padding:0}}>
                            <i className="ti ti-x" style={{fontSize:12}}/>
                          </button>
                        </div>
                      );
                    })}
                  </div>
                )}
              </div>
            );
          }
          const cat=CATS.find(c=>c.items.some(i=>i.label===item.label));
          return(
            <div key={item.id} style={{display:"flex",alignItems:"center",gap:8,padding:"8px 10px",border:"0.5px solid var(--color-border-tertiary)",borderRadius:"var(--border-radius-md)",marginBottom:8}}>
              <div style={{width:10,height:10,borderRadius:"50%",background:cat?.color||"#6B7280",flexShrink:0}}/>
              <span style={{fontSize:13,color:"var(--color-text-primary)"}}>{item.label}</span>
              <span style={{fontSize:11,color:"var(--color-text-secondary)"}}>O{item.od} mm</span>
              <button onClick={()=>removeItem(item.id)} style={{marginLeft:"auto",background:"none",border:"none",color:"var(--color-text-secondary)",cursor:"pointer",padding:0}}>
                <i className="ti ti-trash" style={{fontSize:13}}/>
              </button>
            </div>
          );
        })}

        <div style={{display:"flex",gap:8,marginTop:items.length?4:0}}>
          {[{label:"+ Mantelbuis",mode:"mb"},{label:"+ Direct product",mode:"direct"}].map(b=>(
            <button key={b.mode} onClick={()=>openPanel(b.mode)} style={{flex:1,padding:"8px 0",background:"transparent",border:"0.5px dashed var(--color-border-secondary)",borderRadius:"var(--border-radius-md)",cursor:"pointer",fontSize:12,color:"var(--color-text-secondary)"}}>
              {b.label}
            </button>
          ))}
        </div>

        {panel&&(
          <div style={{marginTop:12,padding:12,background:"var(--color-background-secondary)",border:"0.5px solid #FDBA74",borderRadius:"var(--border-radius-md)"}}>
            <div style={{display:"flex",justifyContent:"space-between",alignItems:"center",marginBottom:10}}>
              <span style={{fontWeight:500,fontSize:13,color:"var(--color-text-primary)"}}>
                {panel.mode==="mb"?"Mantelbuis kiezen":panel.targetId?"Kabel / leiding toevoegen":"Direct product kiezen"}
              </span>
              <button onClick={closePanel} style={{background:"none",border:"none",cursor:"pointer",color:"var(--color-text-secondary)",fontSize:16}}>
                <i className="ti ti-x"/>
              </button>
            </div>

            {panel.mode==="mb"?(
              <>
                <div style={{fontSize:11,color:"var(--color-text-secondary)",marginBottom:7}}>Diameter PE SDR11:</div>
                <div style={{display:"flex",flexWrap:"wrap",gap:5,marginBottom:10}}>
                  {PE_SIZES.map(pe=>(
                    <button key={pe.dn} onClick={()=>setPeDN(pe.dn)} style={{padding:"3px 9px",borderRadius:"var(--border-radius-md)",border:`0.5px solid ${peDN===pe.dn?"#F97316":"var(--color-border-secondary)"}`,background:peDN===pe.dn?"#FFF7ED":"var(--color-background-primary)",color:peDN===pe.dn?"#EA580C":"var(--color-text-primary)",cursor:"pointer",fontSize:12,fontWeight:peDN===pe.dn?600:400}}>
                      O{pe.dn}
                    </button>
                  ))}
                </div>
                {peData&&(<div style={{fontSize:11,color:"var(--color-text-secondary)",marginBottom:10}}>ID {peData.id} mm · wand {peData.wall} mm</div>)}
                <button onClick={addMb} style={{width:"100%",padding:"8px 0",background:"#F97316",color:"#FFF",border:"none",borderRadius:"var(--border-radius-md)",cursor:"pointer",fontSize:13,fontWeight:500}}>Toevoegen</button>
              </>
            ):(
              <>
                <div style={{display:"flex",gap:4,flexWrap:"wrap",marginBottom:10}}>
                  {CATS.map(c=>(
                    <button key={c.key} onClick={()=>{setCatKey(c.key);setSelItem(null);}} style={{padding:"3px 9px",borderRadius:"var(--border-radius-md)",border:"none",background:catKey===c.key?c.color:"var(--color-background-primary)",color:catKey===c.key?"#FFF":"var(--color-text-secondary)",cursor:"pointer",fontSize:11,fontWeight:catKey===c.key?600:400}}>
                      {c.label}
                    </button>
                  ))}
                </div>
                <div style={{display:"flex",flexDirection:"column",gap:3,marginBottom:10}}>
                  {CATS.find(c=>c.key===catKey)?.items.map(it=>(
                    <button key={it.label} onClick={()=>setSelItem(it)} style={{display:"flex",justifyContent:"space-between",alignItems:"center",padding:"5px 10px",border:`0.5px solid ${selItem?.label===it.label?"#F97316":"var(--color-border-tertiary)"}`,background:selItem?.label===it.label?"#FFF7ED":"var(--color-background-primary)",borderRadius:"var(--border-radius-md)",cursor:"pointer",textAlign:"left"}}>
                      <span style={{fontSize:12,color:"var(--color-text-primary)"}}>{it.label}</span>
                      <span style={{fontSize:11,color:"var(--color-text-secondary)"}}>O{it.od} mm</span>
                    </button>
                  ))}
                </div>
                <button onClick={()=>panel.targetId?addContent(panel.targetId):addDirect()} disabled={!selItem} style={{width:"100%",padding:"8px 0",background:selItem?"#F97316":"var(--color-background-secondary)",color:selItem?"#FFF":"var(--color-text-secondary)",border:"none",borderRadius:"var(--border-radius-md)",cursor:selItem?"pointer":"default",fontSize:13,fontWeight:500}}>
                  Toevoegen
                </button>
              </>
            )}
          </div>
        )}
      </div>

      {/* STAP 2 */}
      <div style={{...card,opacity:res?1:0.4,pointerEvents:res?"auto":"none",transition:"opacity 0.2s"}}>
        <StepHead num={2} label="Berekening en dwarsdoorsnede" done={!!res} />

        {!res?(
          <div style={{textAlign:"center",fontSize:13,color:"var(--color-text-secondary)",padding:"20px 0"}}>Voeg materialen toe in stap 1</div>
        ):(
          <>
            <div style={{display:"grid",gridTemplateColumns:"1fr 1fr 1fr",gap:8,marginBottom:14}}>
              {[["Productbundel",`O${Math.round(res.bundleD)} mm`],["Vereiste boring",`O${res.boringD} mm`],["Norm","SIKB 1.5x"]].map(([k,v])=>(
                <div key={k} style={{background:"var(--color-background-secondary)",borderRadius:"var(--border-radius-md)",padding:"8px 10px",textAlign:"center"}}>
                  <div style={{fontSize:10,color:"var(--color-text-secondary)",marginBottom:3}}>{k}</div>
                  <div style={{fontSize:14,fontWeight:700,color:"var(--color-text-primary)"}}>{v}</div>
                </div>
              ))}
            </div>

            {warnings.length>0&&(
              <div style={{background:"#FFF5F5",border:"0.5px solid #FCA5A5",borderRadius:"var(--border-radius-md)",padding:"8px 10px",marginBottom:12}}>
                {warnings.map(w=>(
                  <div key={w.id} style={{fontSize:12,color:"#EF4444",marginBottom:2}}>
                    <i className="ti ti-alert-triangle" style={{fontSize:12}}/>{" "}
                    PE {w.dn} mantelbuis te klein - min O{Math.ceil(w.reqID)} mm nodig
                  </div>
                ))}
              </div>
            )}

            <BoringViz res={res}/>

            <div style={{display:"flex",flexWrap:"wrap",gap:"4px 12px",marginTop:10,padding:"8px 0 0",borderTop:"0.5px solid var(--color-border-tertiary)"}}>
              <div style={{display:"flex",alignItems:"center",gap:5}}>
                <div style={{width:12,height:12,borderRadius:2,background:"#C2D6DF",border:"1px solid #7AAFC4"}}/>
                <span style={{fontSize:10,color:"var(--color-text-secondary)"}}>Bentoniet</span>
              </div>
              {res.proc.map(item=>(
                <div key={item.id} style={{display:"flex",alignItems:"center",gap:5}}>
                  <div style={{width:12,height:12,borderRadius:"50%",background:item.color}}/>
                  <span style={{fontSize:10,color:"var(--color-text-secondary)"}}>{item.type==="mb"?`PE${item.dn} mantelbuis`:item.label}</span>
                </div>
              ))}
            </div>
          </>
        )}
      </div>

      {/* STAP 3 */}
      <div style={{...card,opacity:res?1:0.4,pointerEvents:res?"auto":"none",transition:"opacity 0.2s"}}>
        <StepHead num={3} label="Machine kiezen" done={!!machine} />
        {res&&(
          <div style={{fontSize:12,color:"var(--color-text-secondary)",marginBottom:12}}>
            Vereiste boring: <strong>O{res.boringD} mm</strong>
          </div>
        )}
        <div style={{display:"flex",flexDirection:"column",gap:8}}>
          {MACHINES.map(m=>(
            <MachineCard key={m.id} machine={m} boringD={res?.boringD??9999} selected={machine} onSelect={setMachine}/>
          ))}
        </div>
      </div>
    </div>
  );
}
