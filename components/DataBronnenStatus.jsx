"use client";
import { useState, useEffect, useCallback, useRef } from "react";

// ─── Databron definities ────────────────────────────────────────
const ALLE_BRONNEN = [
  {
    id: "brt",
    naam: "PDOK BRT",
    sub: "Achtergrondkaarten",
    url: "https://service.pdok.nl/brt/achtergrondkaart/wmts/v2_0?SERVICE=WMTS&REQUEST=GetCapabilities",
    stappen: [3, 4, 7, 8],
  },
  {
    id: "ahn",
    naam: "AHN4",
    sub: "Maaiveldprofiel & hoogtes",
    url: "https://service.pdok.nl/rws/ahn/wms/v1_0?SERVICE=WMS&REQUEST=GetCapabilities",
    stappen: [4, 5, 6, 7],
  },
  {
    id: "bgt",
    naam: "BGT",
    sub: "Verharding & oppervlakten",
    url: "https://service.pdok.nl/lv/bgt/wms/v1_0?SERVICE=WMS&REQUEST=GetCapabilities",
    stappen: [5],
  },
  {
    id: "bag",
    naam: "BAG",
    sub: "3D-gebouwen & adressen",
    url: "https://service.pdok.nl/lv/bag/wms/v2_0?SERVICE=WMS&REQUEST=GetCapabilities",
    stappen: [3, 8, 9],
  },
  {
    id: "geotop",
    naam: "GeoTOP / REGIS",
    sub: "Bodemopbouw & geologie",
    url: "https://service.pdok.nl/tno/bro-brogeomodel/wms/v4_0?SERVICE=WMS&REQUEST=GetCapabilities",
    stappen: [6],
  },
  {
    id: "bro",
    naam: "BRO Grondwater",
    sub: "Peilbuizen & grondwaterstand",
    url: "https://service.pdok.nl/bro/grondwatermonitoring/wfs/v1_0?SERVICE=WFS&REQUEST=GetCapabilities",
    stappen: [6],
  },
  {
    id: "kadaster",
    naam: "Kadaster / KLIC",
    sub: "Kabels, leidingen & percelen",
    url: "https://service.pdok.nl/kadaster/kadastralekaart/wms/v5_0?SERVICE=WMS&REQUEST=GetCapabilities",
    stappen: [3, 4, 7, 8],
  },
];

// ─── Verbindingscheck ────────────────────────────────────────────
async function checkVerbinding(url, timeoutMs = 8000) {
  try {
    const ctrl = new AbortController();
    const timer = setTimeout(() => ctrl.abort(), timeoutMs);
    const resp = await fetch(url, {
      signal: ctrl.signal,
      cache: "no-store",
    });
    clearTimeout(timer);
    // WMS/WFS GetCapabilities returns 200 for XML, even for valid services
    return resp.status < 500 ? "ok" : "error";
  } catch (e) {
    if (e.name === "AbortError") return "timeout";
    // Fallback: CORS error still means server is up
    if (e.message?.toLowerCase().includes("cors") ||
        e.message?.toLowerCase().includes("failed to fetch") ||
        e.message?.toLowerCase().includes("network")) {
      // Try with no-cors to see if server is reachable
      try {
        await fetch(url, { mode: "no-cors", cache: "no-store" });
        return "ok"; // opaque response = server responded
      } catch {
        return "error";
      }
    }
    return "error";
  }
}

// ─── Status icoon ────────────────────────────────────────────────
function StatusIcon({ status, size = 14 }) {
  if (status === "checking") return (
    <div style={{ width: size, height: size, borderRadius: "50%",
      border: "2px solid #DEE6EA", borderTopColor: "#007A5A",
      animation: "spin 0.7s linear infinite", flexShrink: 0 }}/>
  );
  if (status === "ok") return (
    <svg width={size} height={size} viewBox="0 0 16 16" fill="none" style={{ flexShrink: 0 }}>
      <circle cx="8" cy="8" r="8" fill="#16A34A"/>
      <path d="M4.5 8l2.5 2.5 4.5-5" stroke="white" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round"/>
    </svg>
  );
  if (status === "timeout") return (
    <svg width={size} height={size} viewBox="0 0 16 16" fill="none" style={{ flexShrink: 0 }}>
      <circle cx="8" cy="8" r="8" fill="#F59E0B"/>
      <path d="M8 5v3.5l2 2" stroke="white" strokeWidth="1.6" strokeLinecap="round"/>
    </svg>
  );
  if (status === "error") return (
    <svg width={size} height={size} viewBox="0 0 16 16" fill="none" style={{ flexShrink: 0 }}>
      <circle cx="8" cy="8" r="8" fill="#DC2626"/>
      <path d="M5.5 5.5l5 5M10.5 5.5l-5 5" stroke="white" strokeWidth="1.6" strokeLinecap="round"/>
    </svg>
  );
  // idle
  return (
    <div style={{ width: size, height: size, borderRadius: "50%",
      background: "#EEF2F4", border: "1.5px solid #DEE6EA", flexShrink: 0 }}/>
  );
}

// ─── Hoofd component ─────────────────────────────────────────────
export default function DataBronnenStatus({ stap }) {
  const bronnen = ALLE_BRONNEN.filter(b => b.stappen.includes(stap));
  const [open,      setOpen]      = useState(false);
  const [statussen, setStatussen] = useState(() =>
    Object.fromEntries(bronnen.map(b => [b.id, "idle"]))
  );
  const [lastCheck, setLastCheck] = useState(null);
  const checkingRef = useRef(false);

  const controleer = useCallback(async () => {
    if (checkingRef.current) return;
    checkingRef.current = true;
    // Alles op 'checking'
    setStatussen(prev => Object.fromEntries(Object.keys(prev).map(k => [k, "checking"])));
    // Check parallel
    const results = await Promise.all(
      bronnen.map(async b => {
        const status = await checkVerbinding(b.url);
        setStatussen(prev => ({ ...prev, [b.id]: status }));
        return { id: b.id, status };
      })
    );
    setLastCheck(new Date().toLocaleTimeString("nl-NL", { hour: "2-digit", minute: "2-digit" }));
    checkingRef.current = false;
  }, [bronnen]);

  // Auto-check bij openen
  useEffect(() => {
    if (open && Object.values(statussen).every(s => s === "idle")) {
      controleer();
    }
  }, [open]);

  if (bronnen.length === 0) return null;

  // Summary berekenen
  const aantalOk  = Object.values(statussen).filter(s => s === "ok").length;
  const aantalErr = Object.values(statussen).filter(s => s === "error").length;
  const aantalBez = Object.values(statussen).filter(s => s === "checking").length;
  const totaal    = bronnen.length;
  const isChecking = aantalBez > 0;

  const summaryKleur =
    isChecking        ? "#587080" :
    aantalErr > 0     ? "#DC2626" :
    aantalOk === totaal && Object.values(statussen).every(s => s !== "idle") ? "#16A34A" :
    "#8FA6B2";

  return (
    <>
      <style>{`@keyframes spin { to { transform: rotate(360deg); } }`}</style>
      <div style={{
        marginTop: 16,
        border: "1px solid #DEE6EA",
        borderRadius: 10,
        overflow: "hidden",
        background: "#fff",
        fontFamily: "Inter, DM Sans, sans-serif",
      }}>
        {/* ── Header ── */}
        <button
          onClick={() => setOpen(v => !v)}
          style={{
            display: "flex", alignItems: "center", gap: 8,
            width: "100%", padding: "9px 14px",
            background: open ? "#F5F7F9" : "#fff",
            border: "none", cursor: "pointer",
            borderBottom: open ? "1px solid #DEE6EA" : "none",
          }}
        >
          {/* Verbinding icoon */}
          <svg width="13" height="13" viewBox="0 0 24 24" fill="none"
            stroke={summaryKleur} strokeWidth="2" strokeLinecap="round" style={{ flexShrink: 0 }}>
            <path d="M5 12.55a11 11 0 0 1 14.08 0M1.42 9a16 16 0 0 1 21.16 0M8.53 16.11a6 6 0 0 1 6.95 0M12 20h.01"/>
          </svg>

          <span style={{ fontSize: 12, fontWeight: 600, color: "#1B2B35" }}>
            Databronnen verbinding
          </span>

          {/* Dot indicators (collapsed) */}
          {!open && (
            <div style={{ display: "flex", gap: 3, alignItems: "center", marginLeft: 2 }}>
              {bronnen.map(b => {
                const s = statussen[b.id];
                const col = s === "ok" ? "#16A34A" : s === "error" ? "#DC2626" :
                  s === "timeout" ? "#F59E0B" : s === "checking" ? "#007A5A" : "#DEE6EA";
                return (
                  <div key={b.id} title={`${b.naam}: ${s}`} style={{
                    width: 7, height: 7, borderRadius: "50%", background: col,
                    animation: s === "checking" ? "spin 1s linear infinite" : "none",
                  }}/>
                );
              })}
            </div>
          )}

          {/* Summary tekst */}
          {!open && Object.values(statussen).some(s => s !== "idle") && (
            <span style={{ fontSize: 11, color: summaryKleur, fontWeight: 500, marginLeft: 2 }}>
              {isChecking ? "Controleren…" :
               `${aantalOk}/${totaal} bereikbaar`}
            </span>
          )}

          <div style={{ flex: 1 }}/>

          {/* Tijdstip */}
          {lastCheck && !open && (
            <span style={{ fontSize: 10, color: "#8FA6B2" }}>{lastCheck}</span>
          )}

          {/* Chevron */}
          <svg width="12" height="12" viewBox="0 0 24 24" fill="none"
            stroke="#8FA6B2" strokeWidth="2.5" strokeLinecap="round"
            style={{ transition: "transform .2s", transform: open ? "rotate(180deg)" : "none", flexShrink: 0 }}>
            <polyline points="6 9 12 15 18 9"/>
          </svg>
        </button>

        {/* ── Uitklapbaar paneel ── */}
        {open && (
          <div style={{ padding: "10px 14px 12px" }}>
            {/* Controle knop + tijdstip */}
            <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 10 }}>
              <span style={{ fontSize: 11, color: "#8FA6B2" }}>
                {lastCheck ? `Laatste check: ${lastCheck}` : "Nog niet gecontroleerd"}
              </span>
              <button
                onClick={controleer}
                disabled={isChecking}
                style={{
                  display: "flex", alignItems: "center", gap: 5,
                  padding: "4px 12px", fontSize: 11, fontWeight: 600,
                  borderRadius: 6, border: "1px solid #DEE6EA",
                  background: isChecking ? "#F5F7F9" : "#fff",
                  color: isChecking ? "#8FA6B2" : "#007A5A",
                  cursor: isChecking ? "default" : "pointer",
                  transition: "all .15s",
                }}
              >
                <svg width="11" height="11" viewBox="0 0 24 24" fill="none"
                  stroke="currentColor" strokeWidth="2.5" strokeLinecap="round"
                  style={{ animation: isChecking ? "spin 0.7s linear infinite" : "none" }}>
                  <path d="M21.5 2v6h-6M2.5 22v-6h6M2 11.5a10 10 0 0 1 18.8-4.3M22 12.5a10 10 0 0 1-18.8 4.2"/>
                </svg>
                {isChecking ? "Controleren…" : "Controleer opnieuw"}
              </button>
            </div>

            {/* Bron kaartjes */}
            <div style={{
              display: "grid",
              gridTemplateColumns: `repeat(${Math.min(bronnen.length, 3)}, 1fr)`,
              gap: 8,
            }}>
              {bronnen.map(b => {
                const s = statussen[b.id];
                const borderKleur = s === "ok" ? "#D1FAE5" : s === "error" ? "#FEE2E2" :
                  s === "timeout" ? "#FEF3C7" : "#DEE6EA";
                const bgKleur = s === "ok" ? "#F0FDF4" : s === "error" ? "#FFF5F5" :
                  s === "timeout" ? "#FFFBEB" : "#F5F7F9";

                return (
                  <div key={b.id} style={{
                    border: `1px solid ${borderKleur}`,
                    borderRadius: 8,
                    padding: "8px 10px",
                    background: bgKleur,
                    display: "flex",
                    alignItems: "flex-start",
                    gap: 8,
                    transition: "all .2s",
                  }}>
                    <StatusIcon status={s} size={15}/>
                    <div style={{ minWidth: 0 }}>
                      <div style={{ fontSize: 12, fontWeight: 600, color: "#1B2B35", lineHeight: 1.3 }}>
                        {b.naam}
                      </div>
                      <div style={{ fontSize: 11, color: "#8FA6B2", marginTop: 1 }}>
                        {b.sub}
                      </div>
                      <div style={{
                        fontSize: 10, marginTop: 3, fontWeight: 500,
                        color: s === "ok" ? "#16A34A" : s === "error" ? "#DC2626" :
                          s === "timeout" ? "#D97706" : s === "checking" ? "#007A5A" : "#B0C4CE",
                      }}>
                        {s === "ok"       ? "✓ Bereikbaar" :
                         s === "error"    ? "✗ Niet bereikbaar" :
                         s === "timeout"  ? "⏱ Time-out" :
                         s === "checking" ? "Controleren…" : "—"}
                      </div>
                    </div>
                  </div>
                );
              })}
            </div>

            {/* Fout-melding als er errors zijn */}
            {aantalErr > 0 && !isChecking && (
              <div style={{
                marginTop: 8, padding: "7px 10px",
                background: "#FFF5F5", border: "1px solid #FEE2E2", borderRadius: 7,
                fontSize: 11, color: "#991B1B",
              }}>
                ⚠️ {aantalErr} bron{aantalErr > 1 ? "nen" : ""} niet bereikbaar.
                Controleer je internetverbinding of probeer het later opnieuw.
              </div>
            )}
          </div>
        )}
      </div>
    </>
  );
}
