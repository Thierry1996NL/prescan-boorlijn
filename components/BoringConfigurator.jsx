"use client";
import { useState, useMemo } from "react";

// ─── DATA ─────────────────────────────────────────────────────────────────────

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
  { id:"d10x15", brand:"Vermeer", model:"D10x15 S3", maxBoring:180, push:44.5,  torque:1085, stangen:91,  engine:"Kubota D1105, 23 pk" },
  { id:"d20x22", brand:"Vermeer", model:"D20x22 S3", maxBoring:250, push:86.7,  torque:2983, stangen:122, engine:"Deutz TD2.9, 74 pk" },
  { id:"d23x30", brand:"Vermeer", model:"D23x30 S3", maxBoring:300, push:102,   torque:4067, stangen:122, engine:"Deutz TCD2.9, 90 pk" },
  { id:"d36x50", brand:"Vermeer", model:"D36x50 S3", maxBoring:400, push:160,   torque:6779, stangen:152, engine:"Deutz TCD3.6, 130 pk" },
];

const TUBE_COLORS = ["#1D4ED8","#047857","#B45309","#6D28D9","#374151","#B91C1C"];
const FILL_FACTOR   = 0.40;
const BORING_FACTOR = 1.50;

let _uid = 1;
const uid = () => String(++_uid);

// ─── MATH ─────────────────────────────────────────────────────────────────────

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
  const boringD   = Math.max(Math.ceil(bundleD * BORING_FACTOR / 25) * 25, 75);
  return { proc, bundleD, boringD };
}

// ─── SVG DWARSDOORSNEDE ───────────────────────────────────────────────────────

function BoringViz({ res }) {
  const S = 280, cx = S / 2, cy = S / 2 - 8;
  const scale = (S / 2 - 32) / (res.boringD / 2);
  const bPx   = (res.boringD / 2) * scale;
  const topRadii  = res.proc.map(p => p.effectiveOD / 2);
  const topBR     = boundR(topRadii);
  const maxTopR   = topRadii.length ? Math.max(...topRadii) : 0;
  const topRingPx = res.proc.length === 1 ? 0 : Math.max((topBR - maxTopR) * scale, maxTopR * 0.6 * scale);
  const topPos    = res.proc.length === 1 ? [{ x: cx, y: cy }] : ringPos(res.proc.length, topRingPx, cx, cy);

  return (
    <svg width={S} height={S + 8} viewBox={`0 0 ${S} ${S + 8}`} style={{ display: "block", margin: "0 auto" }}>
      <defs>
        <marker id="bcArR" markerWidth="5" markerHeight="5" refX="4.5" refY="2.5" orient="auto"><polygon points="0,0 5,2.5 0,5" fill="#9CA3AF" /></marker>
        <marker id="bcArL" markerWidth="5" markerHeight="5" refX="0.5" refY="2.5" orient="auto-start-reverse"><polygon points="0,0 5,2.5 0,5" fill="#9CA3AF" /></marker>
      </defs>
      <circle cx={cx} cy={cy} r={bPx + 22} fill="#C4A45A" />
      {Array.from({ length: 32 }, (_, i) => {
        const a = (i * 137.5 * Math.PI) / 180;
        const d = bPx + 12 + (i % 5) * 1.6;
        return <circle key={i} cx={cx + d * Math.cos(a)} cy={cy + d * Math.sin(a)} r={1.2} fill="#A0803A" opacity="0.65" />;
      })}
      <circle cx={cx} cy={cy} r={bPx} fill="#C2D6DF" stroke="#7AAFC4" strokeWidth="1.5" />
      {res.proc.map((item, idx) => {
        const pos  = topPos[idx]; if (!pos) return null;
        const iPxR = Math.max((item.effectiveOD / 2) * scale, 4);
        if (item.type === "mb") {
          const wallPx  = Math.max(item.pe.wall * scale, 2);
          const innerPx = Math.max(iPxR - wallPx, 2);
          const cRadii  = item.contents.map(c => c.od / 2);
          const cBR     = boundR(cRadii);
          const maxCR   = cRadii.length ? Math.max(...cRadii) : 0;
          const cRingPx = item.contents.length <= 1 ? 0 : Math.max((cBR - maxCR) * scale, maxCR * 0.55 * scale);
          const cPos    = item.contents.length === 1 ? [{ x: pos.x, y: pos.y }] : ringPos(item.contents.length, cRingPx, pos.x, pos.y);
          return (
            <g key={item.id}>
              <circle cx={pos.x} cy={pos.y} r={iPxR} fill={item.color} />
              <circle cx={pos.x} cy={pos.y} r={innerPx} fill="#EBF4F8" />
              {item.contents.map((c, ci) => {
                const cp = cPos[ci]; if (!cp) return null;
                const cat2 = CATS.find(cc => cc.items.some(i => i.label === c.label));
                return <circle key={c.id} cx={cp.x} cy={cp.y} r={Math.max((c.od / 2) * scale, 2.5)} fill={cat2?.color || "#6B7280"} />;
              })}
            </g>
          );
        }
        const cat = CATS.find(c => c.items.some(i => i.label === item.label));
        return <circle key={item.id} cx={pos.x} cy={pos.y} r={iPxR} fill={cat?.color || "#6B7280"} />;
      })}
      {(() => {
        const y0 = cy + bPx + 10;
        return (
          <g>
            <line x1={cx - bPx} y1={y0} x2={cx + bPx} y2={y0} stroke="#9CA3AF" strokeWidth="1" markerStart="url(#bcArL)" markerEnd="url(#bcArR)" />
            <text x={cx} y={y0 + 11} textAnchor="middle" fontSize="10" fill="#9CA3AF" fontFamily="system-ui">Ø{res.boringD} mm</text>
          </g>
        );
      })()}
    </svg>
  );
}

// ─── MACHINE CARD ─────────────────────────────────────────────────────────────

function MachineCard({ machine, boringD, selected, onSelect }) {
  const compatible = machine.maxBoring >= boringD;
  const isSelected = selected === machine.id;
  return (
    <div
      onClick={() => compatible && onSelect(isSelected ? null : machine.id)}
      className={`relative rounded-lg p-3 border transition-all ${
        isSelected ? "border-orange-400 bg-orange-50" :
        compatible ? "border-gray-200 bg-white hover:border-gray-300 cursor-pointer" :
        "border-gray-100 bg-gray-50 opacity-40 cursor-default"
      }`}
    >
      <div className={`absolute top-2 right-2 text-xs px-1.5 py-0.5 rounded font-medium ${
        isSelected ? "bg-orange-100 text-orange-700" :
        compatible ? "bg-green-100 text-green-700" :
        "bg-red-100 text-red-600"
      }`}>
        {isSelected ? "✓ geselecteerd" : compatible ? "compatibel" : `max Ø${machine.maxBoring} mm`}
      </div>
      <div className="text-sm font-semibold text-gray-800 pr-24">{machine.brand} {machine.model}</div>
      <div className="text-xs text-gray-400 mb-2">{machine.engine}</div>
      <div className="grid grid-cols-2 gap-x-4 gap-y-0.5">
        {[["Max boring", `Ø${machine.maxBoring} mm`], ["Duw/trek", `${machine.push} kN`], ["Koppel", `${machine.torque.toLocaleString("nl")} Nm`], ["Stangenrek", `${machine.stangen} m`]].map(([k, v]) => (
          <div key={k} className="flex justify-between">
            <span className="text-xs text-gray-400">{k}</span>
            <span className="text-xs font-medium text-gray-700">{v}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

// ─── HOOFD COMPONENT ──────────────────────────────────────────────────────────

export default function BoringConfigurator() {
  const [items,   setItems]   = useState([]);
  const [panel,   setPanel]   = useState(null);
  const [peDN,    setPeDN]    = useState(110);
  const [catKey,  setCatKey]  = useState("ls");
  const [selItem, setSelItem] = useState(null);
  const [machine, setMachine] = useState(null);

  const res    = useMemo(() => compute(items), [items]);
  const peData = PE_SIZES.find(p => p.dn === peDN);

  const openPanel  = (mode, targetId = null) => { setPanel({ mode, targetId }); setPeDN(110); setCatKey("ls"); setSelItem(null); };
  const closePanel = () => setPanel(null);
  const addMb      = () => { setItems(p => [...p, { id: uid(), type: "mb", dn: peDN, contents: [] }]); closePanel(); };
  const addContent = (tid) => { if (!selItem) return; setItems(p => p.map(it => it.id === tid ? { ...it, contents: [...it.contents, { id: uid(), ...selItem }] } : it)); closePanel(); };
  const addDirect  = () => { if (!selItem) return; setItems(p => [...p, { id: uid(), type: "direct", ...selItem }]); closePanel(); };
  const removeItem = id => setItems(p => p.filter(it => it.id !== id));
  const removeContent = (tid, cid) => setItems(p => p.map(it => it.id === tid ? { ...it, contents: it.contents.filter(c => c.id !== cid) } : it));

  const warnings = res ? res.proc.filter(p => p.type === "mb" && !p.fitsOK) : [];

  const StepHead = ({ num, label, done }) => (
    <div className="flex items-center gap-2.5 mb-4">
      <div className={`w-6 h-6 rounded-full flex items-center justify-center text-xs font-semibold flex-shrink-0 ${done ? "bg-orange-500 text-white" : "bg-gray-100 text-gray-400 border border-gray-200"}`}>
        {done ? "✓" : num}
      </div>
      <span className="text-sm font-semibold text-gray-800">{label}</span>
    </div>
  );

  return (
    <div className="mt-4 space-y-3">

      {/* ══ STAP 1 — MATERIALEN ══ */}
      <div className="bg-white border border-gray-200 rounded-xl p-4">
        <StepHead num={1} label="Wat gaat er doorheen?" done={items.length > 0} />

        {/* Lijst items */}
        {items.map(item => {
          if (item.type === "mb") {
            const pr = res?.proc.find(p => p.id === item.id);
            const color = pr?.color || "#6B7280";
            return (
              <div key={item.id} className={`rounded-lg mb-2 overflow-hidden border ${pr && !pr.fitsOK ? "border-red-200 bg-red-50" : "border-gray-100"}`}>
                <div className="flex items-center gap-2 px-3 py-2 bg-gray-50">
                  <div className="w-2.5 h-2.5 rounded-full flex-shrink-0" style={{ background: color }} />
                  <span className="text-xs font-medium text-gray-800">PE {item.dn} mantelbuis</span>
                  {pr && <span className={`text-xs ${pr.fitsOK ? "text-green-600" : "text-red-500"}`}>{pr.fitsOK ? `${Math.round(pr.fillPct)}% gevuld` : `te vol — min Ø${Math.ceil(pr.reqID)} mm`}</span>}
                  <div className="ml-auto flex gap-1.5">
                    <button onClick={() => openPanel("cable", item.id)} className="text-xs px-2 py-1 bg-blue-50 text-blue-600 border border-blue-200 rounded">+ kabel</button>
                    <button onClick={() => removeItem(item.id)} className="text-xs px-2 py-1 border border-gray-200 rounded text-gray-400 hover:text-red-500">✕</button>
                  </div>
                </div>
                {item.contents.length > 0 && (
                  <div className="px-3 py-1.5 pl-6 space-y-0.5">
                    {item.contents.map(c => {
                      const cat = CATS.find(cc => cc.items.some(i => i.label === c.label));
                      return (
                        <div key={c.id} className="flex items-center gap-2">
                          <div className="w-1.5 h-1.5 rounded-full flex-shrink-0" style={{ background: cat?.color || "#6B7280" }} />
                          <span className="text-xs text-gray-700">{c.label}</span>
                          <span className="text-xs text-gray-400">Ø{c.od} mm</span>
                          <button onClick={() => removeContent(item.id, c.id)} className="ml-auto text-gray-300 hover:text-red-400 text-xs">✕</button>
                        </div>
                      );
                    })}
                  </div>
                )}
              </div>
            );
          }
          const cat = CATS.find(c => c.items.some(i => i.label === item.label));
          return (
            <div key={item.id} className="flex items-center gap-2 px-3 py-2 border border-gray-100 rounded-lg mb-2">
              <div className="w-2.5 h-2.5 rounded-full flex-shrink-0" style={{ background: cat?.color || "#6B7280" }} />
              <span className="text-xs text-gray-800">{item.label}</span>
              <span className="text-xs text-gray-400">Ø{item.od} mm</span>
              <button onClick={() => removeItem(item.id)} className="ml-auto text-gray-300 hover:text-red-400 text-xs">✕</button>
            </div>
          );
        })}

        {/* Toevoegen knoppen */}
        <div className="flex gap-2 mt-1">
          {[{ label: "+ Mantelbuis", mode: "mb" }, { label: "+ Direct product", mode: "direct" }].map(b => (
            <button key={b.mode} onClick={() => openPanel(b.mode)} className="flex-1 py-2 text-xs text-gray-400 border border-dashed border-gray-200 rounded-lg hover:border-orange-300 hover:text-orange-500 transition-colors">
              {b.label}
            </button>
          ))}
        </div>

        {/* Add-panel */}
        {panel && (
          <div className="mt-3 p-3 bg-gray-50 border border-orange-200 rounded-lg">
            <div className="flex justify-between items-center mb-3">
              <span className="text-xs font-semibold text-gray-700">
                {panel.mode === "mb" ? "Mantelbuis kiezen" : panel.targetId ? "Kabel / leiding toevoegen" : "Direct product kiezen"}
              </span>
              <button onClick={closePanel} className="text-gray-400 hover:text-gray-600 text-base leading-none">✕</button>
            </div>

            {panel.mode === "mb" ? (
              <>
                <p className="text-xs text-gray-400 mb-2">Diameter PE SDR11:</p>
                <div className="flex flex-wrap gap-1.5 mb-3">
                  {PE_SIZES.map(pe => (
                    <button key={pe.dn} onClick={() => setPeDN(pe.dn)} className={`text-xs px-2.5 py-1 rounded border transition-colors ${peDN === pe.dn ? "border-orange-400 bg-orange-50 text-orange-600 font-semibold" : "border-gray-200 text-gray-600 hover:border-gray-300"}`}>
                      Ø{pe.dn}
                    </button>
                  ))}
                </div>
                {peData && <p className="text-xs text-gray-400 mb-3">ID {peData.id} mm · wand {peData.wall} mm</p>}
                <button onClick={addMb} className="w-full py-2 bg-orange-500 text-white text-sm font-medium rounded-lg hover:bg-orange-600 transition-colors">Toevoegen</button>
              </>
            ) : (
              <>
                <div className="flex gap-1.5 flex-wrap mb-3">
                  {CATS.map(c => (
                    <button key={c.key} onClick={() => { setCatKey(c.key); setSelItem(null); }} className={`text-xs px-2.5 py-1 rounded border transition-colors ${catKey === c.key ? "text-white border-transparent" : "border-gray-200 text-gray-500 hover:border-gray-300"}`}
                      style={catKey === c.key ? { background: c.color } : {}}>
                      {c.label}
                    </button>
                  ))}
                </div>
                <div className="space-y-1 mb-3">
                  {CATS.find(c => c.key === catKey)?.items.map(it => (
                    <button key={it.label} onClick={() => setSelItem(it)} className={`w-full flex justify-between px-2.5 py-1.5 rounded border text-left transition-colors ${selItem?.label === it.label ? "border-orange-400 bg-orange-50" : "border-gray-200 bg-white hover:border-gray-300"}`}>
                      <span className="text-xs text-gray-800">{it.label}</span>
                      <span className="text-xs text-gray-400">Ø{it.od} mm</span>
                    </button>
                  ))}
                </div>
                <button onClick={() => panel.targetId ? addContent(panel.targetId) : addDirect()} disabled={!selItem} className={`w-full py-2 text-sm font-medium rounded-lg transition-colors ${selItem ? "bg-orange-500 text-white hover:bg-orange-600" : "bg-gray-100 text-gray-400 cursor-default"}`}>
                  Toevoegen
                </button>
              </>
            )}
          </div>
        )}
      </div>

      {/* ══ STAP 2 — BEREKENING & DOORSNEDE ══ */}
      <div className={`bg-white border border-gray-200 rounded-xl p-4 transition-opacity ${res ? "opacity-100" : "opacity-40 pointer-events-none"}`}>
        <StepHead num={2} label="Berekening en dwarsdoorsnede" done={!!res} />

        {!res ? (
          <p className="text-xs text-gray-400 text-center py-8">Voeg materialen toe in stap 1</p>
        ) : (
          <>
            {/* Cijfers */}
            <div className="grid grid-cols-3 gap-2 mb-4">
              {[["Productbundel", `Ø${Math.round(res.bundleD)} mm`], ["Vereiste boring", `Ø${res.boringD} mm`], ["Norm", "SIKB 1.5×"]].map(([k, v]) => (
                <div key={k} className="bg-gray-50 rounded-lg px-3 py-2 text-center">
                  <div className="text-xs text-gray-400 mb-0.5">{k}</div>
                  <div className="text-sm font-bold text-gray-800">{v}</div>
                </div>
              ))}
            </div>

            {/* Waarschuwingen */}
            {warnings.length > 0 && (
              <div className="bg-red-50 border border-red-200 rounded-lg px-3 py-2 mb-3">
                {warnings.map(w => (
                  <p key={w.id} className="text-xs text-red-600">⚠ PE {w.dn} te klein — min Ø{Math.ceil(w.reqID)} mm nodig</p>
                ))}
              </div>
            )}

            <BoringViz res={res} />

            {/* Legenda */}
            <div className="flex flex-wrap gap-x-3 gap-y-1 mt-3 pt-3 border-t border-gray-100">
              <div className="flex items-center gap-1.5">
                <div className="w-3 h-3 rounded border" style={{ background: "#C2D6DF", borderColor: "#7AAFC4" }} />
                <span className="text-xs text-gray-400">Bentoniet</span>
              </div>
              {res.proc.map(item => (
                <div key={item.id} className="flex items-center gap-1.5">
                  <div className="w-3 h-3 rounded-full" style={{ background: item.color }} />
                  <span className="text-xs text-gray-400">{item.type === "mb" ? `PE${item.dn} mantelbuis` : item.label}</span>
                </div>
              ))}
            </div>
          </>
        )}
      </div>

      {/* ══ STAP 3 — MACHINE ══ */}
      <div className={`bg-white border border-gray-200 rounded-xl p-4 transition-opacity ${res ? "opacity-100" : "opacity-40 pointer-events-none"}`}>
        <StepHead num={3} label="Machine kiezen" done={!!machine} />
        {res && <p className="text-xs text-gray-500 mb-3">Vereiste boring: <strong>Ø{res.boringD} mm</strong> — compatibele machines zijn groen</p>}
        <div className="space-y-2">
          {MACHINES.map(m => (
            <MachineCard key={m.id} machine={m} boringD={res?.boringD ?? 9999} selected={machine} onSelect={setMachine} />
          ))}
        </div>
      </div>
    </div>
  );
}
