"use client";
import { useState, useEffect, useCallback, useRef } from "react";

// ─── Per stap relevante bronnen ──────────────────────────────────────────────
const BRONNEN = [
  // Achtergrondkaarten
  { id:"pdok_brt",       label:"PDOK BRT Achtergrond", cat:"Achtergrond",
    url:"https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0/grijs/EPSG:3857/6/33/21.png",
    stappen:[2,3,4,5,6,7,8,9,10] },
  { id:"pdok_luchtfoto", label:"PDOK Luchtfoto WMS",   cat:"Achtergrond",
    url:"https://service.pdok.nl/hwh/luchtfotorgb/wms/v1_0?SERVICE=WMS&REQUEST=GetCapabilities",
    stappen:[3,4,7,8] },

  // Overlays
  { id:"pdok_kadaster",  label:"Kadaster Percelen",    cat:"Overlay",
    url:"https://service.pdok.nl/kadaster/kadastralekaart/wms/v5_0?SERVICE=WMS&REQUEST=GetCapabilities",
    stappen:[3,4,5,6,7,8] },
  { id:"pdok_bag",       label:"PDOK BAG (Panden)",    cat:"Overlay",
    url:"https://service.pdok.nl/lv/bag/wms/v2_0?SERVICE=WMS&REQUEST=GetCapabilities",
    stappen:[3,4,9] },
  { id:"pdok_bgt",       label:"PDOK BGT Oppervlak",   cat:"Overlay",
    url:"https://service.pdok.nl/lv/bgt/wms/v1_0?SERVICE=WMS&REQUEST=GetCapabilities",
    stappen:[3,4,5] },

  // Data
  { id:"pdok_ahn4",      label:"AHN4 Hoogtemodel",     cat:"Data",
    url:"https://service.pdok.nl/rws/ahn/wms/v1_0?SERVICE=WMS&REQUEST=GetCapabilities",
    stappen:[5,6,7] },
  { id:"pdok_bodemkaart",label:"Bodemkaart 1:50.000",  cat:"Data",
    url:"https://service.pdok.nl/tno/bro-bodemkaart/wms/v1_0?SERVICE=WMS&REQUEST=GetCapabilities",
    stappen:[6] },
  { id:"pdok_regis",     label:"REGIS II Hydrogeologie",cat:"Data",
    url:"https://service.pdok.nl/tno/regis-ii/wms/v1_0?SERVICE=WMS&REQUEST=GetCapabilities",
    stappen:[6] },
  { id:"bro_grondwater", label:"BRO Grondwater",        cat:"Data",
    url:"https://publiek.broservices.nl/sr/cpt/v2/objects?pageSize=1",
    stappen:[6] },
  { id:"cesium_ion",     label:"CesiumJS Ion / PDOK 3D",cat:"Data",
    url:"https://ion.cesium.com",
    stappen:[9] },
  { id:"pdok_buisleidingen", label:"PDOK Buisleidingen",cat:"Data",
    url:"https://service.pdok.nl/rvo/buisleidingen/wms/v1_0?SERVICE=WMS&REQUEST=GetCapabilities",
    stappen:[3,4,5] },
];

const CAT_KLEUREN = {
  Achtergrond: { bg:"#EFF6FF", border:"#BFDBFE", text:"#1D4ED8" },
  Overlay:     { bg:"#F0FDF4", border:"#BBF7D0", text:"#15803D" },
  Data:        { bg:"#FFF7ED", border:"#FED7AA", text:"#C2410C" },
};

async function checkBron(url, timeout = 6000) {
  const ctrl = new AbortController();
  const t    = setTimeout(() => ctrl.abort(), timeout);
  try {
    await fetch(url, { method:"GET", mode:"no-cors", signal:ctrl.signal });
    clearTimeout(t);
    return "ok";
  } catch(e) {
    clearTimeout(t);
    return e.name === "AbortError" ? "timeout" : "error";
  }
}

export default function DataStatusPanel({ stap = 1 }) {
  const [open,      setOpen]      = useState(false);
  const [statuses,  setStatuses]  = useState({}); // id → "idle"|"checking"|"ok"|"timeout"|"error"
  const [checkTime, setCheckTime] = useState(null);
  const abortRef = useRef(false);

  const relevanteBronnen = BRONNEN.filter(b => b.stappen.includes(stap));

  const check = useCallback(async () => {
    if (!relevanteBronnen.length) return;
    abortRef.current = false;

    // Zet alle op "checking"
    setStatuses(Object.fromEntries(relevanteBronnen.map(b => [b.id, "checking"])));

    // Check parallel
    await Promise.all(relevanteBronnen.map(async b => {
      if (abortRef.current) return;
      const status = await checkBron(b.url);
      if (!abortRef.current) {
        setStatuses(prev => ({ ...prev, [b.id]: status }));
      }
    }));

    if (!abortRef.current) {
      setCheckTime(new Date().toLocaleTimeString("nl-NL", { hour:"2-digit", minute:"2-digit", second:"2-digit" }));
    }
  }, [stap]);

  // Check on mount and on stap change
  useEffect(() => {
    setStatuses({});
    setCheckTime(null);
    abortRef.current = false;
    if (open) check();
    return () => { abortRef.current = true; };
  }, [stap]);

  // Check when opening
  useEffect(() => {
    if (open && Object.keys(statuses).length === 0) check();
  }, [open]);

  if (!relevanteBronnen.length) return null;

  // Counts
  const ok      = relevanteBronnen.filter(b => statuses[b.id] === "ok").length;
  const err     = relevanteBronnen.filter(b => ["error","timeout"].includes(statuses[b.id])).length;
  const totaal  = relevanteBronnen.length;
  const allDone = Object.values(statuses).every(s => ["ok","error","timeout"].includes(s));

  // Groepeer per categorie
  const perCat = {};
  for (const b of relevanteBronnen) {
    if (!perCat[b.cat]) perCat[b.cat] = [];
    perCat[b.cat].push(b);
  }

  function StatusIcoon({ status }) {
    if (status === "checking") return (
      <div className="w-4 h-4 border-2 border-[#E5F3EC] border-t-[#007A5A] rounded-full animate-spin flex-shrink-0"/>
    );
    if (status === "ok") return (
      <div className="w-4 h-4 rounded-full bg-[#16A34A] flex items-center justify-center flex-shrink-0">
        <svg width="9" height="9" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="3.5" strokeLinecap="round"><polyline points="20 6 9 17 4 12"/></svg>
      </div>
    );
    if (status === "timeout") return (
      <div className="w-4 h-4 rounded-full bg-[#F59E0B] flex items-center justify-center flex-shrink-0">
        <svg width="9" height="9" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="3" strokeLinecap="round"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
      </div>
    );
    if (status === "error") return (
      <div className="w-4 h-4 rounded-full bg-[#DC2626] flex items-center justify-center flex-shrink-0">
        <svg width="9" height="9" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="3.5" strokeLinecap="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
      </div>
    );
    return <div className="w-4 h-4 rounded-full bg-[#E4EAED] flex-shrink-0"/>;
  }

  function SummaryDot() {
    if (!allDone || Object.keys(statuses).length === 0) return (
      <div className="w-2 h-2 rounded-full bg-[#F59E0B] animate-pulse"/>
    );
    if (err === 0) return <div className="w-2 h-2 rounded-full bg-[#16A34A]"/>;
    if (err === totaal) return <div className="w-2 h-2 rounded-full bg-[#DC2626]"/>;
    return <div className="w-2 h-2 rounded-full bg-[#F59E0B]"/>;
  }

  return (
    <div className="mt-4 border border-[#DEE6EA] rounded-xl overflow-hidden bg-white">
      {/* ── Header / Toggle ── */}
      <button
        onClick={() => setOpen(v => !v)}
        className="w-full flex items-center gap-3 px-4 py-2.5 hover:bg-[#F5F7F9] transition-colors"
      >
        <SummaryDot />
        <span className="text-xs font-semibold text-[#587080]">Databronnen status</span>
        {allDone && Object.keys(statuses).length > 0 && (
          <span className={`text-[10.5px] font-medium px-2 py-0.5 rounded-full ${
            err === 0
              ? "bg-[#DCFCE7] text-[#15803D]"
              : err === totaal
              ? "bg-[#FEE2E2] text-[#DC2626]"
              : "bg-[#FEF3C7] text-[#B45309]"
          }`}>
            {ok}/{totaal} bereikbaar
          </span>
        )}
        {Object.keys(statuses).length > 0 && !allDone && (
          <span className="text-[10.5px] text-[#8FA6B2]">Controleren…</span>
        )}
        {checkTime && (
          <span className="ml-auto text-[10.5px] text-[#B0C4CE]">gecontroleerd {checkTime}</span>
        )}
        <div className="flex items-center gap-2 ml-auto">
          {allDone && (
            <button
              onClick={e => { e.stopPropagation(); check(); }}
              className="text-[10.5px] text-[#007A5A] hover:text-[#00915F] font-medium flex items-center gap-1 px-2 py-0.5 rounded-lg hover:bg-[#E5F3EC] transition-colors"
            >
              <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round"><path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8"/><path d="M3 3v5h5"/></svg>
              Vernieuwen
            </button>
          )}
          <svg
            width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="#8FA6B2" strokeWidth="2.5" strokeLinecap="round"
            style={{ transform: open ? "rotate(180deg)" : "none", transition: "transform .2s" }}
          >
            <polyline points="6 9 12 15 18 9"/>
          </svg>
        </div>
      </button>

      {/* ── Expanded content ── */}
      {open && (
        <div className="border-t border-[#DEE6EA] p-3">
          <div className="flex flex-col gap-3">
            {Object.entries(perCat).map(([cat, bronnen]) => {
              const k = CAT_KLEUREN[cat] || CAT_KLEUREN.Data;
              return (
                <div key={cat}>
                  {/* Categorie label */}
                  <div className="flex items-center gap-2 mb-1.5">
                    <span className="text-[10px] font-semibold uppercase tracking-wide"
                      style={{ color: k.text }}>
                      {cat}
                    </span>
                    <div className="flex-1 h-px" style={{ background: k.border }}/>
                  </div>
                  {/* Source cards */}
                  <div className="grid gap-1.5" style={{ gridTemplateColumns: "repeat(auto-fill, minmax(200px, 1fr))" }}>
                    {bronnen.map(b => {
                      const st = statuses[b.id] || "idle";
                      return (
                        <div key={b.id}
                          className="flex items-center gap-2.5 px-3 py-2 rounded-lg border transition-colors"
                          style={{
                            background: st === "ok" ? "#F0FDF4"
                              : st === "error" ? "#FEF2F2"
                              : st === "timeout" ? "#FFFBEB"
                              : "#F5F7F9",
                            borderColor: st === "ok" ? "#BBF7D0"
                              : st === "error" ? "#FECACA"
                              : st === "timeout" ? "#FDE68A"
                              : "#DEE6EA",
                          }}
                        >
                          <StatusIcoon status={st} />
                          <div className="flex-1 min-w-0">
                            <div className="text-xs font-medium text-[#1B2B35] truncate">{b.label}</div>
                            <div className={`text-[10px] font-medium ${
                              st === "ok" ? "text-[#15803D]"
                              : st === "error" ? "text-[#DC2626]"
                              : st === "timeout" ? "text-[#B45309]"
                              : st === "checking" ? "text-[#007A5A]"
                              : "text-[#8FA6B2]"
                            }`}>
                              {st === "ok" ? "Verbonden"
                               : st === "error" ? "Niet bereikbaar"
                               : st === "timeout" ? "Time-out"
                               : st === "checking" ? "Controleren…"
                               : "—"}
                            </div>
                          </div>
                          <span className="text-[9px] font-semibold px-1.5 py-0.5 rounded-md flex-shrink-0"
                            style={{ background: k.bg, color: k.text, border: `1px solid ${k.border}` }}>
                            {cat}
                          </span>
                        </div>
                      );
                    })}
                  </div>
                </div>
              );
            })}
          </div>
          <p className="text-[10.5px] text-[#B0C4CE] mt-3">
            Verbindingen worden getest via een proefverzoek naar elke databron. Geen verbinding = raadpleeg netwerkinstellingen of contacteer PDOK.
          </p>
        </div>
      )}
    </div>
  );
}
