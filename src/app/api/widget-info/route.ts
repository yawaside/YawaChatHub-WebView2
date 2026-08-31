import { NextRequest, NextResponse } from "next/server";
import { getSettings } from "@/lib/server-settings";
import { widgetClientCount } from "@/lib/bus";

export const dynamic = "force-dynamic";

/** Информация для панели «Ссылка для OBS» (ТЗ: URL статичен). */
export async function GET(req: NextRequest) {
  const s = await getSettings();
  const host = req.headers.get("x-forwarded-host") ?? req.headers.get("host") ?? "127.0.0.1:3000";
  const proto = req.headers.get("x-forwarded-proto") ?? "http";
  const url = `${proto}://${host}/widget?token=${s.token}`;
  return NextResponse.json({
    port: s.port,
    token: s.token,
    url,
    clients: widgetClientCount(),
  });
}
