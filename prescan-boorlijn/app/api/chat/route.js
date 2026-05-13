// ============================================================
// Prescan Boorlijn AI — Claude API Route
// app/api/chat/route.js  (Next.js App Router)
// ============================================================

import { getProjectMetContext, getChatGeschiedenis } from "@/lib/supabase-queries";

function buildSystemPrompt(project) {
  const base = `
Je bent een gespecialiseerde AI-assistent voor gestuurde boringen (HDD – Horizontal Directional Drilling), ontwikkeld voor aannemers, onderaannemers en opdrachtgevers in de Nederlandse infrasector.

## Jouw rol
Je helpt gebruikers bij het uitvoeren van prescans voor boortracés. Je analyseert KLIC-data, beoordeelt kruisingen met kabels en leidingen, toetst aan geldende normen en genereert concrete adviezen en rapporten.

## Wat je doet

### 1. KLIC-analyse
- Je verwerkt aangeleverde KLIC-data en identificeert alle kabels en leidingen in de omgeving van het boortracé.
- Je benoemt leidingtype, netbeheerder, diepteligging (indien bekend) en afstand tot de boorlijn.
- Je signaleert ontbrekende dieptegegevens en geeft aan waar dit een risico vormt.

### 2. Kruisingencheck & risicoklassering
- Je beoordeelt elke kruising op basis van vrijwaringszones uit CROW 500 en het aangeleverde PvE.
- Risicoklassering: Rood = directe overschrijding, Oranje = maatregel vereist, Groen = veilig.
- Voor elke rode en oranje kruising geef je een concrete aanbeveling.

### 3. Tracéadvies
- Bij kritieke kruisingen bereken je een alternatief tracé en vergelijk je beide opties.
- De eindverantwoordelijkheid ligt altijd bij de uitvoerder.

### 4. Technisch booradvies
- Boorspoeling per grondsoort, minimale boogstralen, trekkrachten, volumeberekeningen.
- Risico's bij boringen onder watergangen, wegen, spoor en bebouwing.

### 5. Dagrapport & documentatie
- Genereer professionele dagrapporten geschikt voor oplevering aan opdrachtgevers.

## Normen
- CROW 500, NEN 3650/3651, WIBON, PvE aannemer (zie projectcontext)

## Gedragsregels
- Wees precies: maten, afstanden en normen met bronvermelding.
- Meld ontbrekende data en het effect op de betrouwbaarheid.
- Gebruik vakjargon passend bij ervaren grondwerkers.
- Antwoord in het Nederlands.
- Voeg altijd een disclaimer toe bij prescans: de analyse is ondersteunend; de eindverantwoordelijkheid ligt bij de uitvoerder.
- Toon: professioneel, direct en praktisch.
`.trim();

  if (!project) return base;

  let ctx = `\n\n---\n## Actief project\n`;
  ctx += `- **Naam:** ${project.naam ?? "onbekend"}\n`;
  ctx += `- **Opdrachtgever:** ${project.opdrachtgever ?? "onbekend"}\n`;
  ctx += `- **Locatie:** ${project.locatie ?? "onbekend"}\n`;
  ctx += `- **Boorlengte:** ${project.boorlengte_m ? project.boorlengte_m + " m" : "onbekend"}\n`;
  ctx += `- **Buisdiameter:** ${project.diameter_mm ? project.diameter_mm + " mm" : "onbekend"}\n`;
  ctx += `- **Buismateriaal:** ${project.materiaal ?? "onbekend"}\n`;
  ctx += `- **Bodemtype:** ${project.bodemtype ?? "onbekend"}\n`;
  ctx += `- **Bijzonderheden:** ${project.bijzonderheden ?? "geen"}\n`;

  if (project.pve) {
    ctx += `\n### PvE — vrijwaringszones\n${project.pve}\n`;
  }

  if (project.klic_samenvatting) {
    ctx += `\n### KLIC-samenvatting\n${project.klic_samenvatting}\n`;
  }

  if (project.kruisingen?.length > 0) {
    ctx += `\n### Gedetecteerde kruisingen\n`;
    project.kruisingen.forEach((k, i) => {
      ctx += `${i + 1}. **${k.leidingtype}** (${k.netbeheerder}) — afstand: ${k.afstand_cm} cm — diepte: ${k.diepte_m ?? "onbekend"} m — positie: ${k.kruising_positie_m ?? "?"} m — risico: ${k.risico}\n`;
    });
  }

  return base + ctx;
}

export async function POST(request) {
  try {
    const { bericht, projectId } = await request.json();

    if (!bericht) {
      return Response.json({ error: "Geen bericht meegegeven" }, { status: 400 });
    }

    // Haal projectcontext en chatgeschiedenis parallel op
    const [project, geschiedenis] = await Promise.all([
      projectId ? getProjectMetContext(projectId) : Promise.resolve(null),
      projectId ? getChatGeschiedenis(projectId, 20) : Promise.resolve([]),
    ]);

    const systemPrompt = buildSystemPrompt(project);

    const response = await fetch("https://api.anthropic.com/v1/messages", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "anthropic-version": "2023-06-01",
      },
      body: JSON.stringify({
        model: "claude-sonnet-4-20250514",
        max_tokens: 1500,
        system: systemPrompt,
        messages: [
          ...geschiedenis,
          { role: "user", content: bericht },
        ],
      }),
    });

    const data = await response.json();

    if (!response.ok) {
      console.error("Claude API fout:", data);
      return Response.json({ error: "AI-fout, probeer opnieuw" }, { status: 500 });
    }

    const antwoord = data.content
      .filter((b) => b.type === "text")
      .map((b) => b.text)
      .join("\n");

    return Response.json({ antwoord });
  } catch (err) {
    console.error("Route fout:", err);
    return Response.json({ error: "Serverfout" }, { status: 500 });
  }
}
