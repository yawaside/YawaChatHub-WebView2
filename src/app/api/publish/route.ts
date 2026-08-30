import { NextRequest, NextResponse } from "next/server";
import { publish } from "@/lib/bus";

export const dynamic = "force-dynamic";

/** Приложение шлёт новые сообщения чата в OBS-виджет и оверлей. */
export async function POST(req: NextRequest) {
  const body = await req.json().catch(() => null);
  if (!body || typeof body !== "object") {
    return NextResponse.json({ ok: false }, { status: 400 });
  }
  const { type, msg } = body as { type?: string; msg?: unknown };
  if (type === "chat" && msg) {
    publish({ type: "chat", msg }, ["widget", "overlay"]);
  }
  return NextResponse.json({ ok: true });
}
