"use client";

/**
 * KlicAchtergrond
 * ───────────────
 * Laadt opgeslagen KLIC/DXF lagen als niet-interactieve achtergrond
 * op een bestaande Leaflet kaart (bijv. in MapTrace stap 4).
 *
 * Gebruik in MapTrace.jsx:
 *   import KlicAchtergrond from "./KlicAchtergrond";
 *   ...
 *   <KlicAchtergrond kaart={leafletMapRef.current} project={project} />
 *
 * Vereisten: project.bestanden_meta (JSON) + project.laag_instellingen (JSON)
 */

import { useEffect, useRef } from "react";

// ─── RD New → WGS84 (voor DXF/GML/KLIC coördinaten) ─────────────
function rdNaarWgs84(x, y) {
  if (Math.abs(x) <= 180 && Math.abs(y) <= 90) return [x, y];
  if (typeof window !== "undefined" && window.proj4) {
    try { const w = proj4("EPSG:28992","EPSG:4326",[x,y]); return [w[0],w[1]]; } catch {}
  }
  const dX=(x-155000)/100000, dY=(y-463000)/100000;
  const sumN=3235.65389*dY-32.58297*dX*dX-0.24750*dY*dY-0.84978*dX*dX*dY-0.06550*dY*dY*dY;
  const sumE=5260.52916*dX+105.94684*dX*dY+2.45656*dX*dY*dY-0.81885*dX*dX*dX;
  return [5.38720621+sumE/3600, 52.15517440+sumN/3600];
}

// ─── IMKL minimale parser ─────────────────────────────────────────
function parseImklMini(xmlTekst) {
  const doc = new DOMParser().parseFromString(xmlTekst, "text/xml");
  const netThema = {};
  doc.querySelectorAll("Utiliteitsnet").forEach(net => {
    const id = net.getAttributeNS?.("http://www.opengis.net/gml/3.2","id") || net.getAttribute("gml:id") || "";
    const href = net.querySelector("thema")?.getAttributeNS?.("http://www.w3.org/1999/xlink","href") || "";
    if (id) netThema[id] = href.split("/").pop() || "overig";
  });
  const themalagen = {};
  doc.querySelectorAll("UtilityLink").forEach(link => {
    const netHref = (link.querySelector("inNetwork")?.getAttributeNS?.("http://www.w3.org/1999/xlink","href") || "").replace(/^#/,"");
    const thema = netThema[netHref] || "overig";
    const posListEl = link.querySelector("posList");
    if (!posListEl) return;
    const nums = posListEl.textContent.trim().split(/\s+/).map(Number);
    const coords = [];
    for (let i = 0; i+1 < nums.length; i+=2) {
      const [lng, lat] = rdNaarWgs84(nums[i], nums[i+1]);
      coords.push([lat, lng]); // Leaflet: [lat, lng]
    }
    if (coords.length < 2) return;
    if (!themalagen[thema]) themalagen[thema] = [];
    themalagen[thema].push(coords);
  });
  return themalagen;
}

// ─── DXF minimale parser ──────────────────────────────────────────
function parseDxfMini(tekst) {
  const lijnen = [];
  const regels = tekst.split(/\r?\n/);
  let i = 0;
  while (i < regels.length) {
    if (regels[i].trim() !== "0") { i++; continue; }
    const type = regels[i+1]?.trim();
    let einde = i+2;
    while (einde < regels.length && regels[einde].trim() !== "0") einde++;
    const blok = regels.slice(i, einde);
    const getW = c => { for (let k=0;k<blok.length-1;k++) if(blok[k].trim()===String(c)) return parseFloat(blok[k+1].trim())||0; return 0; };
    if (type === "LINE") {
      const [lng1,lat1]=rdNaarWgs84(getW(10),getW(20));
      const [lng2,lat2]=rdNaarWgs84(getW(11),getW(21));
      lijnen.push([[lat1,lng1],[lat2,lng2]]);
    } else if (type === "LWPOLYLINE" || type === "POLYLINE") {
      const pts=[];
      for (let k=0;k<blok.length-3;k++) if(blok[k].trim()==="10"&&blok[k+2]?.trim()==="20"){const x=parseFloat(blok[k+1].trim()),y=parseFloat(blok[k+3].trim());if(!isNaN(x)&&!isNaN(y)){const[lng,lat]=rdNaarWgs84(x,y);pts.push([lat,lng]);}}
      if (pts.length>=2) lijnen.push(pts);
    }
    i = einde;
  }
  return lijnen;
}

// ─── CDN JSZip loader ─────────────────────────────────────────────
async function laadJSZip() {
  if (window.JSZip) return window.JSZip;
  await new Promise((ok,err)=>{const s=document.createElement("script");s.src="https://cdnjs.cloudflare.com/ajax/libs/jszip/3.10.1/jszip.min.js";s.onload=ok;s.onerror=err;document.head.appendChild(s);});
  return window.JSZip;
}

// ─── NEN-1775 standaardkleuren ────────────────────────────────────
const THEMA_KLEUR = {
  laagspanning:"#7B00AA",middenspanning:"#00CCFF",hoogspanning:"#FF4400",
  gasLageDruk:"#FFFF00",gasHogeDruk:"#FF0000",water:"#000080",
  datatransport:"#00CC00",rioolVrijverval:"#AA00CC",rioolOnderOverOfOnderdruk:"#AA00CC",
  warmte:"#FF6600",overig:"#888888",
};

// ════════════════════════════════════════════════════════════════════
export default function KlicAchtergrond({ kaart, project }) {
  const lagenRef = useRef([]);   // Leaflet layer refs voor cleanup

  useEffect(() => {
    if (!kaart || typeof window === "undefined") return;
    let actief = true;

    const bestanden = (() => { try { return JSON.parse(project?.bestanden_meta || "[]"); } catch { return []; } })();
    const opgeslagenInst = (() => { try { return JSON.parse(project?.laag_instellingen || "{}"); } catch { return {}; } })();
    const kaartBox = opgeslagenInst.__kaartBox ?? null;

    if (bestanden.length === 0) return;

    (async () => {
      // Verwijder eventuele eerder geladen achtergrondlagen
      lagenRef.current.forEach(l => { try { kaart.removeLayer(l); } catch {} });
      lagenRef.current = [];

      for (const bestand of bestanden) {
        if (!actief) break;
        if (!bestand.url) continue;
        const ext = bestand.naam.split(".").pop().toLowerCase();

        try {
          const res = await fetch(bestand.url);
          if (!res.ok) continue;

          if (ext === "zip") {
            // KLIC ZIP → parse IMKL
            const blob = await res.blob();
            const JSZip = await laadJSZip();
            const zip = await JSZip.loadAsync(blob);
            const xmlNaam = Object.keys(zip.files).find(n => n.includes("GI_gebiedsinformatie") && n.endsWith(".xml"));
            if (!xmlNaam || !actief) continue;
            const xmlTekst = await zip.files[xmlNaam].async("string");
            const themalagen = parseImklMini(xmlTekst);

            for (const [thema, lijnen] of Object.entries(themalagen)) {
              const lagId = `klic_${thema}`;
              const inst = opgeslagenInst[lagId];
              if (inst?.zichtbaar === false) continue;
              const kleur = inst?.kleur ?? THEMA_KLEUR[thema] ?? "#888888";
              const dikte = inst?.dikte ?? 2;
              const helderheid = inst?.helderheid ?? 0.7;

              lijnen.forEach(coords => {
                if (!actief) return;
                // Filterbox check
                if (kaartBox) {
                  const bboxOK = coords.some(([lat,lng]) =>
                    lat >= kaartBox.lat1 && lat <= kaartBox.lat2 &&
                    lng >= kaartBox.lng1 && lng <= kaartBox.lng2
                  );
                  if (!bboxOK) return;
                }
                const laag = window.L.polyline(coords, {
                  color: kleur, weight: dikte, opacity: helderheid * 0.8,
                  interactive: false,  // Niet klikbaar — alleen achtergrond
                });
                laag.addTo(kaart);
                lagenRef.current.push(laag);
              });
            }
          } else if (ext === "dxf") {
            // DXF bestand
            const tekst = await res.text();
            const lijnen = parseDxfMini(tekst);
            const inst = opgeslagenInst[bestand.id];
            if (inst?.zichtbaar === false) continue;
            const kleur = inst?.kleur ?? "#888888";
            const dikte = inst?.dikte ?? 2;
            const helderheid = inst?.helderheid ?? 0.7;
            lijnen.forEach(coords => {
              if (!actief) return;
              const laag = window.L.polyline(coords, { color:kleur, weight:dikte, opacity:helderheid*0.8, interactive:false });
              laag.addTo(kaart);
              lagenRef.current.push(laag);
            });
          }
        } catch (err) {
          console.warn("KlicAchtergrond:", bestand.naam, err.message);
        }
      }
    })();

    return () => {
      actief = false;
      lagenRef.current.forEach(l => { try { kaart.removeLayer(l); } catch {} });
      lagenRef.current = [];
    };
  }, [kaart, project?.bestanden_meta, project?.laag_instellingen]);

  return null; // Geen eigen DOM — alles gaat via Leaflet
}
