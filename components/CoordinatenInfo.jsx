"use client";
import { useState } from "react";

// ─── CRS kennisbank ──────────────────────────────────────────────
const CRS_INFO = {
  "EPSG:28992": {
    kort:  "RD New",
    lang:  "Rijksdriehoekstelsel New",
    kleur: "#007A5A",
    bg:    "#E5F3EC",
    rand:  "#C3E6D5",
    eenheid: "meter",
    toelichting: "Standaard NL coördinatenstelsel. Gebruikt door alle PDOK-diensten.",
  },
  "EPSG:4326": {
    kort:  "WGS84",
    lang:  "World Geodetic System 1984",
    kleur: "#2563EB",
    bg:    "#EFF6FF",
    rand:  "#BFDBFE",
    eenheid: "graden",
    toelichting: "Wereldwijd GPS-stelsel. Wordt auto-geconverteerd naar RD New.",
  },
  "EPSG:3857": {
    kort:  "Web Mercator",
    lang:  "Pseudo-Mercator (Google / OSM)",
    kleur: "#7C3AED",
    bg:    "#F5F3FF",
    rand:  "#DDD6FE",
    eenheid: "meter",
    toelichting: "Web-tiling standaard. Niet voor meting; conversie naar RD New.",
  },
};

const APP_CRS = "EPSG:28992";

// ─── Onderleggers ────────────────────────────────────────────────
const ONDERLEGGERS = [
  {
    naam: "BRT Standaard",
    type: "WMTS",
    crs:  "EPSG:28992",
    url:  "service.pdok.nl/brt/…/standaard",
    opmerking: "Natiefbeschikbaar in RD New. Geen conversie nodig.",
    stappen: [3,4,5,6,7,8],
  },
  {
    naam: "BRT Grijs",
    type: "WMTS",
    crs:  "EPSG:28992",
    url:  "service.pdok.nl/brt/…/grijs",
    opmerking: "Natiefbeschikbaar in RD New. Geen conversie nodig.",
    stappen: [3,4,5,6,7,8],
  },
  {
    naam: "BRT Pastel",
    type: "WMTS",
    crs:  "EPSG:28992",
    url:  "service.pdok.nl/brt/…/pastel",
    opmerking: "Natiefbeschikbaar in RD New. Geen conversie nodig.",
    stappen: [3,4,5,6,7,8],
  },
  {
    naam: "Luchtfoto (HR)",
    type: "WMS",
    crs:  "EPSG:28992",
    url:  "service.pdok.nl/hwh/luchtfotorgb",
    opmerking: "PDOK WMS; app vraagt op in EPSG:28992 via bounding box.",
    stappen: [4,7,8],
  },
  {
    naam: "AHN4 Hoogtemodel",
    type: "WMS",
    crs:  "EPSG:28992",
    url:  "service.pdok.nl/rws/ahn/…/dtm_05m",
    opmerking: "PDOK WMS; hoogtepunten worden in RD New coördinaten teruggegeven.",
    stappen: [5,6,7],
  },
];

// ─── Overlays ────────────────────────────────────────────────────
const OVERLAYS = [
  {
    naam: "Kadastrale percelen",
    type: "WMS",
    crs:  "EPSG:28992",
    url:  "service.pdok.nl/kadaster/kadastralekaart",
    opmerking: "Kadaster WMS. Percelen in RD New, direct compatibel.",
    stappen: [3,4,5,6,7,8],
  },
  {
    naam: "BAG Panden",
    type: "WMS",
    crs:  "EPSG:28992",
    url:  "service.pdok.nl/lv/bag/…/pand",
    opmerking: "Basisregistratie Adressen & Gebouwen. Native EPSG:28992.",
    stappen: [3,8,9],
  },
  {
    naam: "BAG Adressen",
    type: "WMS",
    crs:  "EPSG:28992",
    url:  "service.pdok.nl/lv/bag/…/verblijfsobject",
    opmerking: "Adrescoördinaten in RD New.",
    stappen: [3,8],
  },
  {
    naam: "BGT Oppervlakten",
    type: "WMS",
    crs:  "EPSG:28992",
    url:  "service.pdok.nl/lv/bgt/…",
    opmerking: "Verhardingstype-lagen in EPSG:28992. Stap 5 BGT-analyse.",
    stappen: [5],
  },
  {
    naam: "GeoTOP / REGIS II",
    type: "WMS",
    crs:  "EPSG:28992",
    url:  "service.pdok.nl/tno/bro-brogeomodel",
    opmerking: "TNO bodemmodel. RD New native.",
    stappen: [6],
  },
  {
    naam: "BRO Grondwater",
    type: "WFS",
    crs:  "EPSG:28992",
    url:  "service.pdok.nl/bro/grondwatermonitoring",
    opmerking: "WFS-features in EPSG:28992. Peilbuizen als punten.",
    stappen: [6],
  },
  {
    naam: "Bodemkaart 1:50.000",
    type: "WMS",
    crs:  "EPSG:28992",
    url:  "service.pdok.nl/tno/bro-bodemkaart",
    opmerking: "TNO WMS. Native RD New.",
    stappen: [6],
  },
  {
    naam: "Gemeentegrenzen",
    type: "WMS",
    crs:  "EPSG:28992",
    url:  "service.pdok.nl/cbs/gebiedsindelingen",
    opmerking: "CBS statistisch gebied. EPSG:28992.",
    stappen: [4],
  },
];

// ─── Bestandsformaten ─────────────────────────────────────────────
const BESTANDSFORMATEN = [
  {
    formaat: "IMKL / KLIC ZIP",
    crs: "EPSG:28992",
    opmerking: "Wettelijk verplicht in RD New per IMKL-standaard. Direct inlaadbaar.",
    convert: false,
  },
  {
    formaat: "GML (GML 3.2)",
    crs: "EPSG:28992",
    opmerking: "Standaard CRS in NL GML-leveringen. Let op: soms EPSG:4326 bij export.",
    convert: false,
  },
  {
    formaat: "DXF (AutoCAD)",
    crs: "EPSG:28992",
    opmerking: "Verwacht RD New coördinaten (geen CRS-header in DXF). Verify met leverancier.",
    convert: false,
  },
  {
    formaat: "KML",
    crs: "EPSG:4326",
    opmerking: "KML standaard = WGS84. App converteert automatisch naar RD New bij inladen.",
    convert: true,
  },
  {
    formaat: "GeoJSON",
    crs: "EPSG:4326",
    opmerking: "RFC 7946 standaard = WGS84. App converteert automatisch naar RD New.",
    convert: true,
  },
  {
    formaat: "WKT / CSV met coördinaten",
    crs: "EPSG:28992",
    opmerking: "Verwacht RD New. Decimale meters (X=155000–280000, Y=300000–625000).",
    convert: false,
  },
];

// ─── Badge component ─────────────────────────────────────────────
function CrsBadge({ crs, compact = false }) {
  const info = CRS_INFO[crs] ?? { kort: crs, lang: crs, kleur: "#8FA6B2", bg: "#F5F7F9", rand: "#DEE6EA" };
  return (
    <span style={{
      display: "inline-flex", alignItems: "center", gap: 4,
      padding: compact ? "1px 7px" : "2px 8px",
      borderRadius: 20,
      background: info.bg, border: `1px solid ${info.rand}`,
      fontSize: compact ? 10 : 11, fontWeight: 600, color: info.kleur,
      whiteSpace: "nowrap",
    }}>
      {crs === APP_CRS
        ? <span style={{ fontSize: compact ? 9 : 10 }}>✓</span>
        : <span style={{ fontSize: compact ? 9 : 10 }}>⟳</span>
      }
      {info.kort}
    </span>
  );
}

function TypeBadge({ type }) {
  const kleuren = {
    WMTS: { bg: "#EFF6FF", kleur: "#1D4ED8", rand: "#BFDBFE" },
    WMS:  { bg: "#F5F3FF", kleur: "#7C3AED", rand: "#DDD6FE" },
    WFS:  { bg: "#ECFDF5", kleur: "#059669", rand: "#A7F3D0" },
  };
  const k = kleuren[type] ?? { bg: "#F5F7F9", kleur: "#8FA6B2", rand: "#DEE6EA" };
  return (
    <span style={{
      padding: "1px 6px", borderRadius: 4,
      background: k.bg, border: `1px solid ${k.rand}`,
      fontSize: 10, fontWeight: 700, color: k.kleur,
    }}>{type}</span>
  );
}

// ─── Sectie tabel ────────────────────────────────────────────────
function LagenTabel({ items, stap }) {
  const relevant = stap ? items.filter(i => !i.stappen || i.stappen.includes(stap)) : items;
  if (!relevant.length) return (
    <p style={{ fontSize: 12, color: "#8FA6B2", fontStyle: "italic", padding: "4px 0" }}>
      Geen lagen actief in deze stap.
    </p>
  );
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 4 }}>
      {relevant.map((item, i) => {
        const info = CRS_INFO[item.crs] ?? {};
        const match = item.crs === APP_CRS;
        return (
          <div key={i} style={{
            display: "grid",
            gridTemplateColumns: "1fr auto auto",
            alignItems: "center",
            gap: 8,
            padding: "7px 10px",
            background: "#fff",
            border: `1px solid ${match ? "#DEE6EA" : "#FDE68A"}`,
            borderRadius: 7,
            borderLeft: `3px solid ${match ? "#007A5A" : "#F59E0B"}`,
          }}>
            <div style={{ minWidth: 0 }}>
              <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
                <span style={{ fontSize: 12, fontWeight: 600, color: "#1B2B35" }}>{item.naam ?? item.formaat}</span>
                {item.type && <TypeBadge type={item.type}/>}
                {item.convert && (
                  <span style={{ fontSize: 10, color: "#D97706", background: "#FFFBEB",
                    border: "1px solid #FDE68A", borderRadius: 4, padding: "0 5px", fontWeight: 500 }}>
                    auto-convert
                  </span>
                )}
              </div>
              <div style={{ fontSize: 11, color: "#8FA6B2", marginTop: 2 }}>{item.opmerking}</div>
            </div>
            <CrsBadge crs={item.crs}/>
            <div style={{ textAlign: "right", fontSize: 11, color: match ? "#007A5A" : "#D97706", fontWeight: 600 }}>
              {match ? "✓ Match" : "⟳ Conversie"}
            </div>
          </div>
        );
      })}
    </div>
  );
}

// ─── Hoofd component ─────────────────────────────────────────────
export default function CoordinatenInfo({ stap }) {
  const [open,       setOpen]       = useState(false);
  const [actieveTab, setActieveTab] = useState("lagen"); // lagen | bestanden | crs

  const appCrs = CRS_INFO[APP_CRS];

  return (
    <div style={{
      marginTop: 10,
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
          width: "100%", padding: "8px 14px",
          background: open ? "#F5F7F9" : "#fff",
          border: "none", cursor: "pointer",
          borderBottom: open ? "1px solid #DEE6EA" : "none",
          textAlign: "left",
        }}
      >
        <svg width="13" height="13" viewBox="0 0 24 24" fill="none"
          stroke="#007A5A" strokeWidth="2" strokeLinecap="round" style={{ flexShrink: 0 }}>
          <circle cx="12" cy="12" r="10"/>
          <line x1="2" y1="12" x2="22" y2="12"/>
          <path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z"/>
        </svg>

        <span style={{ fontSize: 12, fontWeight: 600, color: "#1B2B35" }}>
          Coördinatenstelsels
        </span>

        {/* Compact CRS badge collapsed */}
        {!open && (
          <span style={{
            fontSize: 11, fontWeight: 500, color: "#007A5A",
            background: "#E5F3EC", border: "1px solid #C3E6D5",
            borderRadius: 20, padding: "1px 8px",
          }}>
            App: EPSG:28992 — RD New
          </span>
        )}

        <div style={{ flex: 1 }}/>
        <svg width="12" height="12" viewBox="0 0 24 24" fill="none"
          stroke="#8FA6B2" strokeWidth="2.5" strokeLinecap="round"
          style={{ transition: "transform .2s", transform: open ? "rotate(180deg)" : "none", flexShrink: 0 }}>
          <polyline points="6 9 12 15 18 9"/>
        </svg>
      </button>

      {/* ── Uitklapbaar paneel ── */}
      {open && (
        <div style={{ padding: "12px 14px" }}>

          {/* App CRS info */}
          <div style={{
            display: "flex", alignItems: "flex-start", gap: 10,
            padding: "9px 12px", marginBottom: 12,
            background: "#E5F3EC", border: "1px solid #C3E6D5", borderRadius: 8,
          }}>
            <div style={{
              flexShrink: 0, width: 32, height: 32, borderRadius: 8,
              background: "#007A5A", display: "flex", alignItems: "center",
              justifyContent: "center", fontSize: 16,
            }}>🗺️</div>
            <div>
              <div style={{ fontSize: 12, fontWeight: 700, color: "#1B2B35" }}>
                App coördinatenstelsel: <span style={{ color: "#007A5A" }}>EPSG:28992 — Rijksdriehoekstelsel (RD New)</span>
              </div>
              <div style={{ fontSize: 11, color: "#587080", marginTop: 2, lineHeight: 1.5 }}>
                Alle kaartlagen worden intern in RD New verwerkt (eenheid: meter).
                Proj4Leaflet converteert automatisch bij lagen in andere stelsels.
                Bereik: X 7.000–300.000 m · Y 289.000–629.000 m.
              </div>
            </div>
          </div>

          {/* Tabs */}
          <div style={{ display: "flex", gap: 4, marginBottom: 10 }}>
            {[
              { id: "lagen",     label: "Onderleggers & Overlays" },
              { id: "bestanden", label: "Bestandsformaten" },
              { id: "crs",       label: "CRS Legenda" },
            ].map(t => (
              <button key={t.id} onClick={() => setActieveTab(t.id)}
                style={{
                  padding: "4px 12px", borderRadius: 6, border: "none",
                  fontSize: 11.5, fontWeight: 600, cursor: "pointer",
                  background: actieveTab === t.id ? "#007A5A" : "#F5F7F9",
                  color: actieveTab === t.id ? "#fff" : "#587080",
                  transition: "all .15s",
                }}>
                {t.label}
              </button>
            ))}
          </div>

          {/* Tab: Lagen */}
          {actieveTab === "lagen" && (
            <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
              <div>
                <div style={{ fontSize: 11, fontWeight: 700, color: "#8FA6B2",
                  textTransform: "uppercase", letterSpacing: ".05em", marginBottom: 6 }}>
                  Achtergrond / Onderleggers
                </div>
                <LagenTabel items={ONDERLEGGERS} stap={stap}/>
              </div>
              <div>
                <div style={{ fontSize: 11, fontWeight: 700, color: "#8FA6B2",
                  textTransform: "uppercase", letterSpacing: ".05em", marginBottom: 6 }}>
                  Overlays
                </div>
                <LagenTabel items={OVERLAYS} stap={stap}/>
              </div>
            </div>
          )}

          {/* Tab: Bestanden */}
          {actieveTab === "bestanden" && (
            <div>
              <p style={{ fontSize: 11.5, color: "#587080", marginBottom: 8 }}>
                Coördinatenstelsels van inlaadbare bestandsformaten en hoe de app ze verwerkt.
              </p>
              <div style={{ display: "flex", flexDirection: "column", gap: 4 }}>
                {BESTANDSFORMATEN.map((b, i) => {
                  const match = b.crs === APP_CRS;
                  return (
                    <div key={i} style={{
                      display: "grid", gridTemplateColumns: "140px 1fr auto",
                      alignItems: "center", gap: 8,
                      padding: "7px 10px", background: "#fff",
                      border: `1px solid ${b.convert ? "#FDE68A" : "#DEE6EA"}`,
                      borderRadius: 7,
                      borderLeft: `3px solid ${b.convert ? "#F59E0B" : "#007A5A"}`,
                    }}>
                      <div>
                        <div style={{ fontSize: 12, fontWeight: 700, color: "#1B2B35" }}>{b.formaat}</div>
                      </div>
                      <div style={{ fontSize: 11, color: "#587080" }}>{b.opmerking}</div>
                      <div style={{ display: "flex", alignItems: "center", gap: 5 }}>
                        <CrsBadge crs={b.crs} compact/>
                        {b.convert && (
                          <span style={{ fontSize: 10, color: "#D97706", fontWeight: 600 }}>→ RD New</span>
                        )}
                      </div>
                    </div>
                  );
                })}
              </div>
            </div>
          )}

          {/* Tab: CRS Legenda */}
          {actieveTab === "crs" && (
            <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
              {Object.entries(CRS_INFO).map(([epsg, info]) => (
                <div key={epsg} style={{
                  padding: "10px 12px",
                  background: info.bg, border: `1px solid ${info.rand}`,
                  borderRadius: 8,
                }}>
                  <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 4 }}>
                    <span style={{ fontSize: 13, fontWeight: 700, color: info.kleur }}>{epsg}</span>
                    <span style={{ fontSize: 12, fontWeight: 600, color: "#1B2B35" }}>{info.lang}</span>
                    {epsg === APP_CRS && (
                      <span style={{ fontSize: 10, fontWeight: 600, color: "#007A5A",
                        background: "#fff", border: "1px solid #C3E6D5", borderRadius: 10,
                        padding: "0 7px" }}>
                        ✓ App-stelsel
                      </span>
                    )}
                  </div>
                  <div style={{ fontSize: 11.5, color: "#587080", lineHeight: 1.6 }}>
                    {info.toelichting}
                    {info.eenheid && <span style={{ marginLeft: 6, fontSize: 10,
                      color: info.kleur, fontWeight: 600 }}>Eenheid: {info.eenheid}</span>}
                  </div>
                </div>
              ))}

              {/* Legenda symbolen */}
              <div style={{ padding: "8px 10px", background: "#F5F7F9",
                border: "1px solid #DEE6EA", borderRadius: 8, fontSize: 11, color: "#587080" }}>
                <div style={{ fontWeight: 600, marginBottom: 4, color: "#1B2B35" }}>Legenda</div>
                <div style={{ display: "flex", gap: 16 }}>
                  <span><strong style={{ color: "#007A5A" }}>✓ Match</strong> — zelfde stelsel als app, geen conversie</span>
                  <span><strong style={{ color: "#D97706" }}>⟳ Conversie</strong> — Proj4 converteert automatisch</span>
                </div>
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
