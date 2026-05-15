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

export async function getProjecten() {
  const { data, error } = await supabase
    .from("projecten")
    .select("*")
    .order("aangemaakt_op", { ascending: false });
  if (error) throw error;
  return data;
}

export async function getProjectMetContext(projectId) {
  const { data: project, error: projectError } = await supabase
    .from("projecten")
    .select("*")
    .eq("id", projectId)
    .single();
  if (projectError) throw projectError;

  const { data: klic } = await supabase
    .from("klic_meldingen")
    .select("*")
    .eq("project_id", projectId)
    .eq("verwerkt", true)
    .order("aangemaakt_op", { ascending: false })
    .limit(1)
    .single();

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

export async function maakProject(projectData) {
  const { data: { user } } = await supabase.auth.getUser();
  const { data, error } = await supabase
    .from("projecten")
    .insert({ ...projectData, aangemaakt_door: user.id })
    .select()
    .single();
  if (error) throw error;
  return data;
}

/** Update project — zonder .select().single() na de update om RLS-leesfout te voorkomen */
export async function updateProject(projectId, updates) {
  const { error } = await supabase
    .from("projecten")
    .update(updates)
    .eq("id", projectId);
  if (error) throw error;
  // Haal de bijgewerkte rij apart op
  const { data, error: selectError } = await supabase
    .from("projecten")
    .select("*")
    .eq("id", projectId)
    .single();
  if (selectError) {
    // SELECT mislukt maar UPDATE is gelukt — geef lege return terug
    console.warn("updateProject: SELECT na update mislukt, update zelf is geslaagd", selectError.message);
    return { id: projectId, ...updates };
  }
  return data;
}

// ============================================================
// CHAT BERICHTEN
// ============================================================

export async function getChatGeschiedenis(projectId, limiet = 20) {
  const { data, error } = await supabase
    .from("chat_berichten")
    .select("rol, inhoud")
    .eq("project_id", projectId)
    .order("aangemaakt_op", { ascending: true })
    .limit(limiet);
  if (error) throw error;
  return data.map(b => ({ role: b.rol, content: b.inhoud }));
}

export async function slaBerichtOp(projectId, rol, inhoud) {
  const { data: { user } } = await supabase.auth.getUser();
  const { error } = await supabase
    .from("chat_berichten")
    .insert({ project_id: projectId, rol, inhoud, aangemaakt_door: user.id });
  if (error) throw error;
}

// ============================================================
// AUTH
// ============================================================

export async function logout() {
  const { error } = await supabase.auth.signOut();
  if (error) throw error;
}
