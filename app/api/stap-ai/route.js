// app/api/stap-ai/route.js — Groq LLM met stap-context en kennisbank
// Vereist: GROQ_API_KEY in Vercel environment variables
// Gratis model: llama-3.1-8b-instant | Beter model: llama-3.3-70b-versatile

const GROQ_MODEL = "llama-3.3-70b-versatile"; // verander naar "llama-3.1-8b-instant" voor snelheid

// ─── Stap-specifieke system prompts ──────────────────────────────────────────
const STAP_PROMPTS = {
  1: `Je helpt bij boring-configuratie voor HDD. Geef advies over:
- Vereiste boordiameter: SIKB-norm = bundel × 1.5, vulgraad mantelbuis max 40%
- Machineselectie op basis van bodemtype, diameter en stangenrek
- PE SDR-klassen, wanddiktes, treksterkte
- Mantelbuis dimensionering voor kabeltrekking`,

  2: `Je helpt bij het laden van KLIC en leidingontwerpen. Geef advies over:
- Bestandsformaten: GML, DXF, GeoJSON, KML, ZIP
- NEN-1775 kleuren: LS=paars, MS=cyaan, Gas=geel, Water=donkerblauw, Data=groen
- Kwaliteitscontrole KLIC-meldingen
- Ontbrekende dieptegegevens signaleren`,

  3: `Je helpt bij het interpreteren van KLIC-lagen en conflicten. Geef advies over:
- Vrijwaringszones per leidingtype (CROW 500)
- Risicoklassering: Rood/Oranje/Groen
- Minimale kruisingshoeken en afstanden
- NEN-1775 standaard interpretatie`,

  4: `Je helpt bij het plannen van het boortracé. Geef advies over:
- Minimale inslaghoek: 8-12° in zand, max 20°
- Minimale afstand bebouwing: 3× boordiameter, min 1.5m
- Stuurcapaciteit machine vs. boogstraal
- Obstakelomleiding: watergangen, wegen, bomen`,

  5: `Je helpt bij BGT-oppervlakteanalyse. Geef advies over:
- Risiconiveaus: gesloten verharding=hoog, groen=laag, water=hoog
- Vergunningsvereisten per oppervlaktetype
- CROW 500 eisen per situatie
- Asbest en verontreinigingsrisico's`,

  6: `Je helpt bij diepteligging en dwarsprofiel. Geef advies over:
- AHN4 hoogtedata en NAP-waarden
- Minimale dekking boven KLIC: gas≥1.0m, water≥1.0m, LS≥0.6m
- Max segment-hoek in zand: ~20°, klei: ~15°
- Grondwaterstand en NAP-berekeningen`,

  7: `Je helpt bij machineplaatsing en logistiek. Geef advies over:
- Benodigde ruimte: boormachine + werkzone minimum 6×3m
- Bentoniet verbruik: 200-400 liter per boormeter (zand)
- Minimum afstand bentonietput tot woning: 10m
- BLVC-maatregelen en verkeersregelaars`,

  8: `Je helpt bij 3D-ontwerp interpretatie. Geef advies over:
- Ruimtelijke conflicten identificeren in 3D
- Minimale verticale afstanden
- CesiumJS export en data-levering
- Controle op kruisingsdetails`,

  9: `Je helpt bij afronding en rapportage. Geef advies over:
- Volledigheid checklist prescan
- CROW 500, SIKB, NEN-1775 normencheck
- Rapportage-eisen opdrachtgever
- Export en bestandslevering`,
};

// ─── Project context samenstellen ────────────────────────────────────────────
function projectContext(project, boringConfig) {
  if (!project) return "";
  const bc = boringConfig ?? (() => { try { return JSON.parse(project.boring_config || "null"); } catch { return null; } })();
  const trace = (() => { try { const g = project.boortrace_geojson; const gj = typeof g === "string" ? JSON.parse(g) : g; return gj?.coordinates ?? []; } catch { return []; } })();
  const traceLengte = (() => {
    if (trace.length < 2) return null;
    let d = 0;
    for (let i = 1; i < trace.length; i++) {
      const [ln1,la1]=trace[i-1],[ln2,la2]=trace[i],R=6371000,f=Math.PI/180;
      const a=Math.sin((la2-la1)*f/2)**2+Math.cos(la1*f)*Math.cos(la2*f)*Math.sin((ln2-ln1)*f/2)**2;
      d += R*2*Math.atan2(Math.sqrt(a),Math.sqrt(1-a));
    }
    return Math.round(d);
  })();

  return `
## Projectdata
- Project: ${project.naam ?? "—"} | Opdrachtgever: ${project.opdrachtgever ?? "—"}
- Locatie: ${project.locatie ?? "—"} | Bodemtype: ${project.bodemtype ?? "—"}
- Boorlengte invoer: ${project.boorlengte_m ? `${project.boorlengte_m} m` : "—"} | Boorlengte tracé: ${traceLengte ? `${traceLengte} m` : "—"}
${bc ? `
## Boring configuratie
- Vereiste boring: ${bc.boringD ? `Ø${bc.boringD} mm` : "—"} | Machine: ${bc.machine ?? "—"}
- Items: ${bc.items?.map(i => i.type==="mb" ? `PE${i.dn}${i.contents?.length?` (${i.contents.map(c=>c.label).join(", ")})`:""}`  : i.label).join(" | ") ?? "—"}` : ""}`;
}

// ─── Kennisbank context ────────────────────────────────────────────────────────
function kennisbankContext(kennisbank) {
  if (!kennisbank?.length) return "";
  const MAX_CHARS = 6000; // niet te veel tokens verbruiken
  let tekst = "\n## Kennisbank (upload documenten)\n";
  let used = 0;
  for (const doc of kennisbank) {
    const fragment = doc.tekst?.slice(0, MAX_CHARS - used) ?? "";
    if (!fragment) break;
    tekst += `\n### ${doc.naam}\n${fragment}\n`;
    used += fragment.length;
    if (used >= MAX_CHARS) { tekst += "\n*(Meer documenten beschikbaar — gebruik specifieke vragen)*\n"; break; }
  }
  return tekst;
}

// ─── Route handler ────────────────────────────────────────────────────────────
export async function POST(request) {
  try {
    const { berichten, stap, project, boringConfig, kennisbank } = await request.json();
    const stapPrompt = STAP_PROMPTS[stap] ?? STAP_PROMPTS[1];
    const stapNamen = ["","Boring configuratie","Ontwerp inladen","Ontwerp bekijken","Boorlijn tekenen","Oppervlakteanalyse","Diepteligging","Machine locatie","3D ontwerp","Eindontwerp"];

    const systemPrompt = `Je bent een gespecialiseerde HDD-prescan assistent voor PrescanAI (Horizontaal Gestuurd Boren).
Je werkt nu in **Stap ${stap}: ${stapNamen[stap] ?? ""}**.

${stapPrompt}

## Gedragsregels
- Antwoord ALTIJD in het Nederlands
- Wees concreet: gebruik getallen, normen (CROW 500, SIKB, NEN-1775, BRL SIKB 7000)
- Doe berekeningen als het relevant is
- Houd antwoorden beknopt (max 3-4 alinea's) tenzij uitleg nodig is
- Zeg eerlijk als je het niet weet
${projectContext(project, boringConfig)}
${kennisbankContext(kennisbank)}`;

    const apiKey = process.env.GROQ_API_KEY;
    if (!apiKey) {
      return Response.json({ error: "GROQ_API_KEY niet ingesteld. Ga naar Vercel → Settings → Environment Variables." }, { status: 500 });
    }

    const groqMessages = [
      { role: "system", content: systemPrompt },
      ...berichten,
    ];

    const response = await fetch("https://api.groq.com/openai/v1/chat/completions", {
      method: "POST",
      headers: {
        "Authorization": `Bearer ${apiKey}`,
        "Content-Type": "application/json",
      },
      body: JSON.stringify({
        model: GROQ_MODEL,
        messages: groqMessages,
        max_tokens: 1024,
        temperature: 0.7,
        stream: true,
      }),
    });

    if (!response.ok) {
      const err = await response.json().catch(() => ({}));
      // Rate limit handling
      if (response.status === 429) {
        const retryAfter = response.headers.get("retry-after") ?? "60";
        return Response.json({
          error: `Rate limit bereikt. Wacht ${retryAfter} seconden en probeer opnieuw. (Groq gratis: 30 req/min)`,
          rateLimited: true,
          retryAfter: parseInt(retryAfter),
        }, { status: 429 });
      }
      return Response.json({ error: err.error?.message ?? "Groq API fout" }, { status: 500 });
    }

    // Stream OpenAI-formaat door naar client
    return new Response(response.body, {
      headers: {
        "Content-Type": "text/event-stream",
        "Cache-Control": "no-cache",
        "Connection": "keep-alive",
      },
    });

  } catch (err) {
    console.error("Stap-AI fout:", err);
    return Response.json({ error: "Serverfout: " + err.message }, { status: 500 });
  }
}
