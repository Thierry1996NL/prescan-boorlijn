// app/api/stap-ai/route.js — Groq LLM met stap-context, kennisbank en automatische analyses
// Vereist: GROQ_API_KEY in Vercel environment variables

const GROQ_MODEL = "llama-3.3-70b-versatile";

// ─── Chat prompts (voor vragen) ───────────────────────────────────────────────
const STAP_PROMPTS = {
  1: `Je helpt bij boring-configuratie voor HDD. Geef advies over:
- Vereiste boordiameter: SIKB-norm = bundel × 1.5, vulgraad mantelbuis max 40%
- Machineselectie op basis van bodemtype, diameter en stangenrek
- PE SDR-klassen, wanddiktes, treksterkte
- Mantelbuis dimensionering voor kabeltrekking`,
  2: `Je helpt bij het laden van KLIC en leidingontwerpen. Geef advies over:
- Bestandsformaten: GML, DXF, GeoJSON, KML, ZIP
- NEN-1775 kleuren: LS=paars, MS=cyaan, Gas=geel, Water=donkerblauw, Data=groen
- Kwaliteitscontrole KLIC-meldingen, ontbrekende dieptegegevens`,
  3: `Je helpt bij interpreteren van KLIC-lagen en conflicten. Vrijwaringszones (CROW 500), risicoklassering Rood/Oranje/Groen, kruisingshoeken.`,
  4: `Je helpt bij plannen van het boortracé. Inslaghoeken 8-20°, afstand bebouwing, stuurcapaciteit machine.`,
  5: `Je helpt bij BGT-oppervlakteanalyse. Risiconiveaus, vergunningsvereisten, CROW 500.`,
  6: `Je helpt bij interpretatie van ondergrondgegevens (DINO Loket / BRO). Geef advies over BRO DGM/GeoTOP (laagopbouw), REGIS II (hydraulische eigenschappen, spoelingsverlies), geomorfologie (stuwwallen, veen, dekzand), bodemkaart, grondwaterstanden (opbarstrisico), AHN hoogtemodel.`,
  7: `Je helpt bij diepteligging en dwarsprofiel. AHN4 data, NAP-waarden, minimale dekking boven KLIC, segmenthoeken.`,
  8: `Je helpt bij machineplaatsing en logistiek. Ruimtebehoefte, bentoniet verbruik, BLVC-maatregelen.`,
  9: `Je helpt bij 3D-ontwerp interpretatie. Ruimtelijke conflicten, minimale afstanden, export.`,
 10: `Je helpt bij afronding, rapportage en export. Volledigheidscheck prescan, CROW 500/SIKB/NEN-1775, exportformaten DXF/PDF/rapport.`,
};

// ─── Analyse prompts (voor automatische stap-analyse) ────────────────────────
const ANALYSE_PROMPTS = {
  1: (ctx) => `Analyseer de boring-configuratie hieronder en geef een beknopte technische beoordeling.
Gebruik dit format per punt: ✅ Goed | ⚠️ Aandachtspunt | ❌ Probleem

Beoordeel:
1. **Boordiameter** — Is Ø${ctx.boringD ?? "onbekend"}mm correct voor de productbundel? (SIKB: bundel × 1.5)
2. **Vulgraad** — Is de vulgraad van de mantelbuizen ≤ 40%?
3. **Machine** — Is ${ctx.machine ?? "geen machine geselecteerd"} geschikt voor Ø${ctx.boringD ?? "?"}mm en bodemtype ${ctx.bodemtype ?? "onbekend"}?
4. **Inhoud** — ${ctx.items ?? "geen items"} — zijn dit gangbare combinaties?
5. **Aandachtspunten** — Benoem max 2 risico's of verbeterpunten.

Wees kort en direct. Max 5 regels per punt.`,

  2: (ctx) => `Analyseer welke KLIC-typen beschikbaar zijn voor dit project en geef een compleetheidscheck.

Beschikbare KLIC-data: ${ctx.klicBeschikbaar ?? "geen"}
Project locatie: ${ctx.locatie ?? "onbekend"}

Beoordeel:
1. **Volledigheid** — Zijn alle relevante leidingtypen aanwezig? (LS, MS, Gas, Water, Tele, Riool)
2. **Ontbrekende data** — Wat ontbreekt mogelijk nog?
3. **Risico** — Wat is het risico van ontbrekende leidingdata voor dit tracé?

Gebruik: ✅ aanwezig | ⚠️ aandachtspunt | ❌ ontbreekt/risico. Max 5 regels per punt.`,

  3: (ctx) => `Analyseer de KLIC-kruisingen voor dit boortracé.

Kruisingen gevonden: ${ctx.klicKruisingen ?? "onbekend"}
Tracélengte: ${ctx.traceLengte ?? "onbekend"}
Bodemtype: ${ctx.bodemtype ?? "onbekend"}

Beoordeel per aanwezig leidingtype:
1. **Risicoklassering** — Rood/Oranje/Groen per kruising (CROW 500 vrijwaringszones)
2. **Kritieke kruisingen** — Welke verdienen extra aandacht?
3. **Aanbeveling** — Concrete actiepunten voor risicovolle kruisingen.

Gebruik ✅ ⚠️ ❌. Beknopt.`,

  4: (ctx) => `Analyseer het boortracé en beoordeel de haalbaarheid.

Tracélengte: ${ctx.traceLengte ?? "onbekend"}
Richting: ${ctx.richting ?? "onbekend"}
Machine: ${ctx.machine ?? "onbekend"}
Boordiameter: Ø${ctx.boringD ?? "?"}mm
Bodemtype: ${ctx.bodemtype ?? "onbekend"}

Beoordeel:
1. **Haalbaarheid** — Is dit tracé haalbaar voor de geselecteerde machine?
2. **Stangenrek** — Is de tracélengte binnen het bereik van de machine?
3. **Richting** — Zijn er aandachtspunten bij de gekozen richting?
4. **Risico's** — Maximaal 2 tracé-gerelateerde risico's.

✅ ⚠️ ❌. Beknopt per punt.`,

  5: (ctx) => `Analyseer de BGT-oppervlakteanalyse voor dit boortracé.

Oppervlaktypen langs tracé: ${ctx.bgtSamenvatting ?? "niet beschikbaar"}
Tracélengte: ${ctx.traceLengte ?? "onbekend"}

Beoordeel:
1. **Risiconiveau** — Wat is het overall risico van het tracé (CROW 500)?
2. **Vergunningen** — Welke vergunningen zijn waarschijnlijk nodig?
3. **Schaderisico** — Herstelkosten en schaderisico per oppervlaktetype.
4. **Aanbeveling** — Kan het tracé worden geoptimaliseerd?

✅ ⚠️ ❌. Max 4 regels per punt.`,

  6: (ctx) => `Analyseer de beschikbare ondergrondgegevens (DINO Loket / BRO) voor dit boortracé.

Tracélengte: ${ctx.traceLengte ?? "onbekend"}
Bodemtype: ${ctx.bodemtype ?? "onbekend"}
Locatie: ${ctx.locatie ?? "onbekend"}

Beoordeel:
1. **Geologische opbouw** — Welke risico's zijn te verwachten op basis van het bodemtype en de locatie (veen, klei, zand)?
2. **Grondwater** — Wat is het verwachte opbarstrisico en welke invloed heeft dit op de boring?
3. **Geomorfologie** — Zijn er bijzondere landvormen (stuwwallen, duinen, veengebieden) langs het tracé?
4. **Aanbeveling** — Welke BRO-datasets zijn het meest kritisch voor dit project?

✅ ⚠️ ❌. Max 4 regels per punt.`,

  7: (ctx) => `Analyseer de diepteligging en het dwarsprofiel.

Max diepte: ${ctx.maxDiepte ?? "onbekend"}
Maaiveld: ${ctx.napMin ?? "?"} tot ${ctx.napMax ?? "?"} m NAP
Dieptepunten: ${ctx.aantalDieptePunten ?? 0}
KLIC kruisingen: ${ctx.klicKruisingen ?? 0}
Bodemtype: ${ctx.bodemtype ?? "onbekend"}

Beoordeel:
1. **Diepte** — Is ${ctx.maxDiepte ?? "?"}m diepte acceptabel voor dit bodemtype?
2. **Segmenthoeken** — Zijn de hoeken haalbaar voor de machine?
3. **KLIC dekking** — Is er voldoende vrije ruimte boven KLIC-leidingen?
4. **NAP-waarden** — Zijn er grondwater- of stabiliteitsproblemen te verwachten?

✅ ⚠️ ❌. Max 4 regels per punt.`,

  8: (ctx) => `Analyseer de machine- en bentonietlocatie.

Machine: ${ctx.machine ?? "onbekend"}
Machine afmeting: ${ctx.machineAfm ?? "onbekend"}
Bentoniet put: ${ctx.bentonietAfm ?? "onbekend"}
Tracélengte: ${ctx.traceLengte ?? "onbekend"}
Bodemtype: ${ctx.bodemtype ?? "onbekend"}

Beoordeel:
1. **Ruimtebehoefte** — Is de opgegeven ruimte voldoende voor veilig werken?
2. **Bentoniet** — Schat het bentonietverbruik (200-400 L/m in zand) voor dit tracé.
3. **Logistiek** — Aandachtspunten voor aanvoer en afvoer.
4. **Veiligheid** — BLVC of verkeersmaatregelen nodig?

✅ ⚠️ ❌. Beknopt.`,

  9: (ctx) => `Analyseer het 3D-ontwerp en geef een ruimtelijke beoordeling.

Boordiameter: Ø${ctx.boringD ?? "?"}mm
Tracélengte: ${ctx.traceLengte ?? "onbekend"}
Max diepte: ${ctx.maxDiepte ?? "onbekend"}
KLIC kruisingen: ${ctx.klicKruisingen ?? 0}

Beoordeel:
1. **Ruimtelijke conflicten** — Zijn er 3D-conflicten te verwachten op basis van de data?
2. **Verticale vrije ruimte** — Voldoende dekking boven kabels en leidingen?
3. **Horizontale vrije ruimte** — Laterale afstanden tot obstakels?
4. **Aandachtspunten** — Top 2 ruimtelijke risico's.

✅ ⚠️ ❌. Beknopt.`,

 10: (ctx) => `Doe een volledigheidscheck voor deze HDD-prescan.

Project: ${ctx.projectNaam ?? "onbekend"}
Tracélengte: ${ctx.traceLengte ?? "onbekend"}
Boring: Ø${ctx.boringD ?? "?"}mm | Machine: ${ctx.machine ?? "?"}
KLIC data: ${ctx.klicBeschikbaar ?? "?"}
Dieptedata: ${ctx.heeftDiepte ?? "nee"}
BGT analyse: ${ctx.heeftBGT ?? "nee"}
Machine locatie: ${ctx.heeftMachine ?? "nee"}

Beoordeel elke stap op volledigheid:
1. Projectinformatie & Boring ✅/⚠️/❌
2. KLIC data geladen ✅/⚠️/❌
3. Boorlijn getekend ✅/⚠️/❌
4. Oppervlakteanalyse ✅/⚠️/❌
5. Ondergrondanalyse ✅/⚠️/❌
6. Dwarsprofiel ✅/⚠️/❌
7. Machine locatie ✅/⚠️/❌
8. **Overall oordeel** — Is deze prescan klaar voor aanbesteding?

Geef een duidelijk eindconclusie.`,
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
    const { berichten, stap, project, boringConfig, kennisbank, analyseer, analyseContext, extraInstructie } = await request.json();
    const stapNamen = ["","Boring configuratie","Ontwerp inladen","Ontwerp bekijken","Boorlijn tekenen","Oppervlakteanalyse","Ondergrondanalyse","Dwarsprofiel","Machine locatie","3D ontwerp","Eindrapport & Export"];

    const apiKey = process.env.GROQ_API_KEY;
    if (!apiKey) {
      return Response.json({ error: "GROQ_API_KEY niet ingesteld. Ga naar Vercel → Settings → Environment Variables." }, { status: 500 });
    }

    let systemPrompt, messages;

    if (analyseer) {
      const analyseFn = ANALYSE_PROMPTS[stap] ?? ANALYSE_PROMPTS[1];
      const analyseBericht = analyseFn(analyseContext ?? {});
      systemPrompt = `Je bent een gespecialiseerde HDD-prescan expert voor PrescanAI.
Je doet een technische analyse van stap ${stap}: ${stapNamen[stap] ?? ""}.
Antwoord ALTIJD in het Nederlands. Gebruik ✅ ⚠️ ❌ voor beoordelingen.
Wees concreet en beknopt. Gebruik normen: CROW 500, SIKB, NEN-1775.
${projectContext(project, boringConfig)}
${kennisbankContext(kennisbank ?? [])}${extraInstructie ? `\n\n## Eigen instructies van gebruiker\n${extraInstructie}` : ""}`;
      messages = [{ role: "user", content: analyseBericht }];
    } else {
      const stapPrompt = STAP_PROMPTS[stap] ?? STAP_PROMPTS[1];
      systemPrompt = `Je bent een gespecialiseerde HDD-prescan assistent voor PrescanAI.
Je werkt nu in **Stap ${stap}: ${stapNamen[stap] ?? ""}**.
${stapPrompt}
Antwoord ALTIJD in het Nederlands. Wees concreet, gebruik normen (CROW 500, SIKB, NEN-1775).
${projectContext(project, boringConfig)}
${kennisbankContext(kennisbank ?? [])}${extraInstructie ? `\n\n## Eigen instructies van gebruiker\n${extraInstructie}` : ""}`;
      messages = berichten ?? [];
    }

    const response = await fetch("https://api.groq.com/openai/v1/chat/completions", {
      method: "POST",
      headers: { "Authorization": `Bearer ${apiKey}`, "Content-Type": "application/json" },
      body: JSON.stringify({
        model: GROQ_MODEL,
        messages: [{ role: "system", content: systemPrompt }, ...messages],
        max_tokens: analyseer ? 800 : 1024,
        temperature: analyseer ? 0.3 : 0.7,
        stream: true,
      }),
    });

    if (!response.ok) {
      const err = await response.json().catch(() => ({}));
      if (response.status === 429) {
        const retryAfter = response.headers.get("retry-after") ?? "60";
        return Response.json({ error: `Rate limit. Wacht ${retryAfter}s.`, rateLimited: true, retryAfter: parseInt(retryAfter) }, { status: 429 });
      }
      return Response.json({ error: err.error?.message ?? "Groq API fout" }, { status: 500 });
    }

    return new Response(response.body, {
      headers: { "Content-Type": "text/event-stream", "Cache-Control": "no-cache", "Connection": "keep-alive" },
    });

  } catch (err) {
    console.error("Stap-AI fout:", err);
    return Response.json({ error: "Serverfout: " + err.message }, { status: 500 });
  }
}
