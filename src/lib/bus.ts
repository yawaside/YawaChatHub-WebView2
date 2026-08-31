// ─── Серверная шина событий (SSE) ───────────────────────────────────────────
// Аналог WidgetServer.cs (HTTP + WebSocket для OBS, ТЗ §12): приложение
// публикует config/chat, клиенты (OBS-виджет, оверлей, app) слушают поток.
export type BusEvent = { type: string; [k: string]: unknown };

interface Client {
  id: string;
  kind: string; // "app" | "widget" | "overlay"
  send: (e: BusEvent) => void;
}

interface BusState {
  clients: Map<string, Client>;
}

const g = globalThis as unknown as { __yawaBus?: BusState };

function state(): BusState {
  if (!g.__yawaBus) g.__yawaBus = { clients: new Map() };
  return g.__yawaBus;
}

export function addClient(id: string, kind: string, send: (e: BusEvent) => void) {
  state().clients.set(id, { id, kind, send });
  notifyWidgetClients();
}

export function removeClient(id: string) {
  state().clients.delete(id);
  notifyWidgetClients();
}

export function publish(ev: BusEvent, kinds?: string[]) {
  for (const c of state().clients.values()) {
    if (kinds && !kinds.includes(c.kind)) continue;
    try {
      c.send(ev);
    } catch {
      /* клиент отвалится сам */
    }
  }
}

export function widgetClientCount(): number {
  let n = 0;
  for (const c of state().clients.values()) if (c.kind === "widget") n++;
  return n;
}

/** Транслирует число подключённых OBS-источников в приложение. */
export function notifyWidgetClients() {
  publish({ type: "clients", n: widgetClientCount() }, ["app"]);
}
