// BoringSVG.jsx
// Gedeelde boring-visualisatie + berekeningen voor alle stappen
// Geëxporteerd: default BoringSVG, computeBoring, CATS, TUBE_COLORS

export const PE_SIZES = [
  { dn:32,  od:32,  wall:3.0,  id:26.0  }, { dn:40,  od:40,  wall:3.7,  id:32.6  },
  { dn:50,  od:50,  wall:4.6,  id:40.8  }, { dn:63,  od:63,  wall:5.8,  id:51.4  },
  { dn:75,  od:75,  wall:6.8,  id:61.4  }, { dn:90,  od:90,  wall:8.2,  id:73.6  },
  { dn:110, od:110, wall:10.0, id:90.0  }, { dn:125, od:125, wall:11.4, id:102.2 },
  { dn:160, od:160, wall:14.6, id:130.8 }, { dn:200, od:200, wall:18.2, id:163.6 },
  { dn:250, od:250, wall:22.7, id:204.6 },
];

export const TUBE_COLORS  = ["#1D4ED8","#047857","#B45309","#6D28D9","#374151","#B91C1C"];
export const FILL_FACTOR  = 0.40;
export const BORING_FACTOR = 1.50;

export const CATS = [
  { key:"ls",    label:"Kabel LS",  color:"#DC2626",
    items:[{label:"YMVK 4x10 mm2",od:19},{label:"YMVK 4x16 mm2",od:22},{label:"YMVK 4x25 mm2",od:24},
           {label:"YMVK 4x35 mm2",od:26},{label:"YMVK 4x50 mm2",od:29},{label:"YMVK 4x95 mm2",od:35},{label:"YMVK 4x150 mm2",od:40}]},
  { key:"ms",    label:"Kabel MS",  color:"#7C3AED",
    items:[{label:"12 kV 1x95 mm2",od:40},{label:"12 kV 1x150 mm2",od:45},{label:"12 kV 3x95 mm2",od:70}]},
  { key:"gf",    label:"Glasvezel", color:"#D97706",
    items:[{label:"Microduct 10/8 mm",od:10},{label:"Microduct 16/12 mm",od:16},
           {label:"GF kabel 12F",od:14},{label:"GF kabel 24F",od:16},{label:"GF kabel 96F",od:22}]},
  { key:"water", label:"Water PE",  color:"#2563EB",
    items:[{label:"PE32 water",od:32},{label:"PE40 water",od:40},{label:"PE50 water",od:50},
           {label:"PE63 water",od:63},{label:"PE90 water",od:90}]},
  { key:"gas",   label:"Gas PE",    color:"#F59E0B",
    items:[{label:"PE32 gas",od:32},{label:"PE40 gas",od:40},{label:"PE50 gas",od:50},{label:"PE63 gas",od:63}]},
];

// ─── GRAVITY PACK (geëxporteerd) ──────────────────────────────────────────────
export function gravityPack(items, containerR) {
  if (!items.length) return [];
  if (items.length === 1) return [{ x: 0, y: containerR - items[0].r }];
  const sorted = items.map((it, i) => ({ ...it, orig: i })).sort((a, b) => b.r - a.r);
  const placed = [];
  for (const item of sorted) {
    const maxR = containerR - item.r;
    if (maxR <= 0) { placed.push({ ...item, x: 0, y: 0 }); continue; }
    let bestX = 0, bestY = -maxR;
    for (let si = -120; si <= 120; si++) {
      const x = (si / 120) * maxR;
      if (x * x > maxR * maxR + 0.01) continue;
      let y = Math.sqrt(Math.max(0, maxR * maxR - x * x));
      for (const p of placed) {
        const dx = x - p.x, minD = item.r + p.r;
        if (Math.abs(dx) < minD) y = Math.min(y, p.y - Math.sqrt(Math.max(0, minD * minD - dx * dx)));
      }
      if (x * x + y * y <= maxR * maxR + 0.5 && y > bestY) { bestY = y; bestX = x; }
    }
    placed.push({ ...item, x: bestX, y: bestY });
  }
  const result = new Array(items.length);
  for (const p of placed) result[p.orig] = { x: p.x, y: p.y };
  return result;
}

// ─── BEREKENING (geëxporteerd voor gebruik in stap 9 + BoringBanner) ──────────
export function computeBoring(items) {
  if (!items || !items.length) return null;
  const proc = items.map((item, idx) => {
    if (item.type === "mb") {
      const pe = PE_SIZES.find(p => p.dn === item.dn) || PE_SIZES[6];
      const cArea  = item.contents.reduce((s, c) => s + Math.PI * (c.od / 2) ** 2, 0);
      const reqID  = item.contents.length ? 2 * Math.sqrt(cArea / (Math.PI * FILL_FACTOR)) : 0;
      const idArea = Math.PI * (pe.id / 2) ** 2;
      const fillPct = idArea > 0 ? Math.min((cArea / idArea) * 100, 100) : 0;
      return { ...item, pe, reqID, fillPct, fitsOK: pe.id >= reqID,
               effectiveOD: pe.od, color: TUBE_COLORS[idx % TUBE_COLORS.length] };
    }
    const cat = CATS.find(c => c.items.some(i => i.label === item.label));
    return { ...item, effectiveOD: item.od, color: cat?.color || "#6B7280" };
  });
  const totalArea = proc.reduce((s, p) => s + Math.PI * (p.effectiveOD / 2) ** 2, 0);
  const bundleD   = 2 * Math.sqrt(totalArea / (Math.PI * 0.64));
  const boringD   = Math.max(Math.ceil(bundleD * BORING_FACTOR / 25) * 25, 75);
  return { proc, bundleD, boringD };
}

// ─── BORING SVG COMPONENT ─────────────────────────────────────────────────────
// Props:
//   res        — uitvoer van computeBoring()
//   customPos  — {[id]: {x,y}} aangepaste posities (px t.o.v. middelpunt op schaal size)
//   size       — SVG breedte/hoogte (default 280)
//   showLabel  — toon Ø-maatvoering onderaan (default true)

export default function BoringSVG({ res, customPos = {}, size = 280, showLabel = true }) {
  if (!res) return null;
  const S   = size;
  const CX  = S / 2;
  const CY  = S / 2 - (S > 200 ? 8 : 4);
  const BPX = S / 2 - (S > 200 ? 32 : 16);
  const scale = BPX / (res.boringD / 2);

  // Gravity posities als fallback
  const gItems = res.proc.map(p => ({ r: (p.effectiveOD / 2) * scale }));
  const gPos   = gravityPack(gItems, BPX);

  // Schaal customPos naar huidige size
  // customPos is opgeslagen in px bij size=280; herbereken als fractie
  const REF_BPX = 280 / 2 - 32; // 108 px — referentie waarmee geslagen is
  function getPos(id, idx) {
    if (customPos[id]) {
      const frac = customPos[id];
      return { x: frac.x * (BPX / REF_BPX), y: frac.y * (BPX / REF_BPX) };
    }
    return gPos[idx] ?? { x: 0, y: 0 };
  }

  const markerId = `ar_${size}`; // unieke marker-id per size

  return (
    <svg width={S} height={showLabel ? S + 8 : S} viewBox={`0 0 ${S} ${showLabel ? S + 8 : S}`}
         style={{ display: "block" }}>
      <defs>
        <marker id={`${markerId}R`} markerWidth="5" markerHeight="5" refX="4.5" refY="2.5" orient="auto">
          <polygon points="0,0 5,2.5 0,5" fill="#9CA3AF"/>
        </marker>
        <marker id={`${markerId}L`} markerWidth="5" markerHeight="5" refX="0.5" refY="2.5" orient="auto-start-reverse">
          <polygon points="0,0 5,2.5 0,5" fill="#9CA3AF"/>
        </marker>
      </defs>

      {/* Zand */}
      <circle cx={CX} cy={CY} r={BPX + S * 0.08} fill="#C4A45A"/>
      {Array.from({length: size > 160 ? 28 : 14}, (_, i) => {
        const a = (i * 137.5 * Math.PI) / 180;
        const d = BPX + S * 0.045 + (i % 5) * 1.4;
        return <circle key={i} cx={CX + d * Math.cos(a)} cy={CY + d * Math.sin(a)} r={1.1} fill="#A0803A" opacity="0.6"/>;
      })}

      {/* Bentoniet */}
      <circle cx={CX} cy={CY} r={BPX} fill="#C2D6DF" stroke="#7AAFC4" strokeWidth="1.5"/>

      {/* Centerlijn hints */}
      {size > 160 && <>
        <line x1={CX} y1={CY-BPX+6} x2={CX} y2={CY+BPX-6} stroke="#7AAFC4" strokeWidth="0.5" strokeDasharray="3,4" opacity="0.35"/>
        <line x1={CX-BPX+6} y1={CY} x2={CX+BPX-6} y2={CY} stroke="#7AAFC4" strokeWidth="0.5" strokeDasharray="3,4" opacity="0.35"/>
      </>}

      {/* Items */}
      {res.proc.map((item, idx) => {
        const pos  = getPos(item.id, idx);
        const px   = CX + pos.x, py = CY + pos.y;
        const iPxR = Math.max((item.effectiveOD / 2) * scale, 3);

        if (item.type === "mb") {
          const wallPx  = Math.max(item.pe.wall * scale, 1.5);
          const innerPx = Math.max(iPxR - wallPx, 1.5);
          const cItems  = item.contents.map(c => ({ r: Math.max((c.od / 2) * scale, 2) }));
          const cPos    = gravityPack(cItems, innerPx);
          return (
            <g key={item.id}>
              <circle cx={px} cy={py} r={iPxR} fill={item.color}/>
              <circle cx={px} cy={py} r={innerPx} fill="#EBF4F8"/>
              {item.contents.map((c, ci) => {
                const cp  = cPos[ci]; if (!cp) return null;
                const cat = CATS.find(cc => cc.items.some(i => i.label === c.label));
                return <circle key={c.id} cx={px + cp.x} cy={py + cp.y}
                               r={Math.max((c.od / 2) * scale, 2)} fill={cat?.color || "#6B7280"}/>;
              })}
            </g>
          );
        }
        const cat = CATS.find(c => c.items.some(i => i.label === item.label));
        return <circle key={item.id} cx={px} cy={py} r={iPxR} fill={cat?.color || "#6B7280"}/>;
      })}

      {/* Maatvoering */}
      {showLabel && (() => {
        const y0 = CY + BPX + (S > 200 ? 10 : 6);
        const fs = S > 200 ? 10 : 8;
        return (
          <g>
            <line x1={CX - BPX} y1={y0} x2={CX + BPX} y2={y0}
                  stroke="#9CA3AF" strokeWidth="1"
                  markerStart={`url(#${markerId}L)`} markerEnd={`url(#${markerId}R)`}/>
            <text x={CX} y={y0 + fs + 2} textAnchor="middle" fontSize={fs}
                  fill="#9CA3AF" fontFamily="system-ui">Ø{res.boringD} mm</text>
          </g>
        );
      })()}
    </svg>
  );
}
