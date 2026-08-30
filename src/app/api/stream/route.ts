import { NextRequest } from "next/server";
import { addClient, removeClient } from "@/lib/bus";
import { getSettings, lookOf } from "@/lib/server-settings";
import { uid } from "@/lib/utils";

export const dynamic = "force-dynamic";

export async function GET(req: NextRequest) {
  const kind = req.nextUrl.searchParams.get("kind") ?? "widget";
  const wantToken = req.nextUrl.searchParams.get("token") ?? "";
  const s = await getSettings();
  if (kind !== "app" && s.token && wantToken !== s.token) {
    return new Response("unauthorized", { status: 401 });
  }

  const id = uid();
  const encoder = new TextEncoder();
  const stream = new ReadableStream<Uint8Array>({
    start(controller) {
      const sendEvent = (ev: { type: string; [k: string]: unknown }) => {
        try {
          controller.enqueue(encoder.encode(`data: ${JSON.stringify(ev)}\n\n`));
        } catch {}
      };
      sendEvent({ type: "hello", kind });
      // при подключении нового клиента — сразу текущий config (ТЗ §12.2)
      if (kind === "widget") sendEvent({ type: "widget:config", cfg: s.widget, look: lookOf(s) });
      if (kind === "overlay") sendEvent({ type: "overlay:config", cfg: s.overlay });
      addClient(id, kind, sendEvent);
      const hb = setInterval(() => {
        try {
          controller.enqueue(encoder.encode(`: hb ${Date.now()}\n\n`));
        } catch {}
      }, 15000);
      req.signal.addEventListener("abort", () => {
        clearInterval(hb);
        removeClient(id);
        try {
          controller.close();
        } catch {}
      });
    },
  });

  return new Response(stream, {
    headers: {
      "Content-Type": "text/event-stream; charset=utf-8",
      "Cache-Control": "no-cache, no-transform",
      Connection: "keep-alive",
      "X-Accel-Buffering": "no",
    },
  });
}
