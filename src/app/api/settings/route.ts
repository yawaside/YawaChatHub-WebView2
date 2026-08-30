import { NextRequest, NextResponse } from "next/server";
import { getSettings, patchSettings, lookOf } from "@/lib/server-settings";

export const dynamic = "force-dynamic";

export async function GET() {
  const s = await getSettings();
  return NextResponse.json({ settings: s, look: lookOf(s) });
}

export async function PATCH(req: NextRequest) {
  const patch = await req.json().catch(() => ({}));
  const s = await patchSettings(patch as Record<string, unknown>);
  return NextResponse.json({ settings: s, look: lookOf(s) });
}
