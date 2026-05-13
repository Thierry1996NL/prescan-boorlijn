// ============================================================
// Prescan Boorlijn AI — Supabase Query Functies
// lib/supabase-queries.js
// ============================================================

import { createClient } from "@supabase/supabase-js";

const supabase = createClient(
  process.env.NEXT_PUBLIC_SUPABASE_URL,
  process.env.NEXT_PUBLIC_SUPABASE_ANON_KEY
);

export { supabase };


// ============================================================
// PROJECTEN
// ============================================================

/** Haal alle projecten op van de ingelogde gebruiker */
export async function getProjecten() {
  const { data, error } = await supabase
    .from("projecten")
    .select("*")
    .order("aangemaakt_op", { ascending: false });

  if (error) throw error;
  return data;
}

/** Haal één project op met alle gerelateerde data voor de AI-context */
export async function getProjectMetContext(projectId) {
  const { data: project, error: projectError } = await supabase
    .from("projecten")
    .select("*")
    .eq("id", projectId)
    .single();

  if (projectError) throw projectError;

  // Haal de laatste KLIC-melding op
  const { data: klic } = await supabase
    .from("klic_meldingen")
    .select("*")
    .eq("project_id", projectId)
    .eq("verwerkt", true)
    .order("aangemaakt_op", { ascending: false })
    .limit(1)
    .single();

  // Haal alle kruisingen op
  const { data: kruisingen } = await supabase
    .from("kruisingen")
    .select("*")
    .eq("project_id", projectId)
    .order("kruising_positie_m", { ascending: true });

  return {
    ...project,
    klic_samenvatting: klic?.samenvatting ?? null,
    kruisingen: kruisingen ?? [],
  };
}

/** Maak een nieuw project aan */
export async function maakProject(projectData) {
  const { data: { user } } = await supabase.auth.getUser();

  const { data, error } = await supabase
    .from("projecten")
    .insert({
      ...projectData,
      aangemaakt_door: user.id,
    })
    .select()
    .single();

  if (error) throw error;
  return data;
}

/** Werk een project bij */
export async function updateProject(projectId, updates) {
  const { data, error } = await supabase
    .from("projecten")
    .update(updates)
    .eq("id", projectId)
    .select()
    .single();

  if (error) throw error;
  return data;
}


// ============================================================
// CHAT BERICHTEN
// ============================================================

/** Haal de chatgeschiedenis op voor een project (voor multi-turn geheugen) */
export async function getChatGeschiedenis(projectId, limiet = 20) {
  const { data, error } = await supabase
    .from("chat_berichten")
    .select("rol, inhoud")
    .eq("project_id", projectId)
    .order("aangemaakt_op", { ascending: true })
    .limit(limiet);

  if (error) throw error;

  // Formatteer naar het formaat dat de Claude API verwacht
  return data.map((b) => ({
    role: b.rol,
    content: b.inhoud,
  }));
}

/** Sla een chatbericht op */
export async function slaBerichtOp(projectId, rol, inhoud) {
  const { data: { user } } = await supabase.auth.getUser();

  const { error } = await supabase
    .from("chat_berichten")
    .insert({
      project_id: projectId,
      gebruiker_id: user.id,
      rol,
      inhoud,
    });

  if (error) throw error;
}

/** Verwijder de chatgeschiedenis van een project */
export async function verwijderChatGeschiedenis(projectId) {
  const { error } = await supabase
    .from("chat_berichten")
    .delete()
    .eq("project_id", projectId);

  if (error) throw error;
}


// ============================================================
// KLIC MELDINGEN
// ============================================================

/** Upload een KLIC-bestand naar Supabase Storage */
export async function uploadKlicBestand(projectId, bestand) {
  const pad = `${projectId}/${Date.now()}-${bestand.name}`;

  const { error: uploadError } = await supabase.storage
    .from("klic-bestanden")
    .upload(pad, bestand);

  if (uploadError) throw uploadError;

  const { data, error } = await supabase
    .from("klic_meldingen")
    .insert({
      project_id: projectId,
      bestandsnaam: bestand.name,
      opgeslagen_pad: pad,
      verwerkt: false,
    })
    .select()
    .single();

  if (error) throw error;
  return data;
}

/** Sla de AI-samenvatting van een KLIC-melding op */
export async function updateKlicSamenvatting(klicId, samenvatting) {
  const { error } = await supabase
    .from("klic_meldingen")
    .update({ samenvatting, verwerkt: true })
    .eq("id", klicId);

  if (error) throw error;
}


// ============================================================
// KRUISINGEN
// ============================================================

/** Sla gedetecteerde kruisingen op (vervangt bestaande voor dit project) */
export async function slaKruisingenOp(projectId, klicId, kruisingen) {
  // Verwijder bestaande kruisingen voor dit project
  await supabase
    .from("kruisingen")
    .delete()
    .eq("project_id", projectId);

  if (kruisingen.length === 0) return;

  const { error } = await supabase
    .from("kruisingen")
    .insert(
      kruisingen.map((k) => ({
        project_id: projectId,
        klic_melding_id: klicId,
        ...k,
      }))
    );

  if (error) throw error;
}


// ============================================================
// DAGRAPPORTEN
// ============================================================

/** Sla een dagrapport op */
export async function slaRapportOp(projectId, rapportData) {
  const { data: { user } } = await supabase.auth.getUser();

  const { data, error } = await supabase
    .from("dagrapporten")
    .insert({
      project_id: projectId,
      ingevoerd_door: user.id,
      ...rapportData,
    })
    .select()
    .single();

  if (error) throw error;
  return data;
}

/** Haal alle dagrapporten op voor een project */
export async function getDagrapporten(projectId) {
  const { data, error } = await supabase
    .from("dagrapporten")
    .select("*")
    .eq("project_id", projectId)
    .order("datum", { ascending: false });

  if (error) throw error;
  return data;
}


// ============================================================
// AUTHENTICATIE
// ============================================================

export async function login(email, wachtwoord) {
  const { data, error } = await supabase.auth.signInWithPassword({
    email,
    password: wachtwoord,
  });
  if (error) throw error;
  return data;
}

export async function logout() {
  const { error } = await supabase.auth.signOut();
  if (error) throw error;
}

export async function getHuidigeGebruiker() {
  const { data: { user } } = await supabase.auth.getUser();
  if (!user) return null;

  const { data: profiel } = await supabase
    .from("profielen")
    .select("*")
    .eq("id", user.id)
    .single();

  return profiel;
}
