import { redirect } from "next/navigation";
import { maakSupabaseServer } from "@/lib/supabase-server";

export default async function Home() {
  const supabase = maakSupabaseServer();
  const { data: { user } } = await supabase.auth.getUser();

  if (!user) redirect("/login");
  redirect("/projecten");
}
