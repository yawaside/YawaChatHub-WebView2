/**
 * Мост между веб-рендерером и нативной оболочкой.
 *
 * В оригинальном репозитории это был Electron IPC. В WebView2-сборке роль
 * main-процесса выполняет C#-хост (см. папку webview2/): он регистрирует
 * host-объект `window.chrome.webview.hostObjects.yawa` и обрабатывает
 * асинхронные сообщения через `chrome.webview.postMessage`.
 *
 * В обычном браузере моста нет — включается демо-режим (localStorage +
 * speechSynthesis), точно как «сайт» в оригинале.
 */

export interface WidgetInfo {
  url: string;
  port: number;
  running: boolean;
}

/** подписка на канал: то, что хост подключает реальными коннекторами */
export interface ChannelSub {
  platform: "twitch" | "youtube" | "vkplay" | "kick" | "tiktok" | "donationalerts";
  username: string;
  token?: string;
  currency?: string;
}

interface PendingCall {
  resolve: (v: unknown) => void;
  reject: (e: Error) => void;
}

declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage: (msg: unknown) => void;
        addEventListener: (type: "message", cb: (e: { data: unknown }) => void) => void;
        hostObjects?: { yawa?: Record<string, unknown> };
      };
    };
  }
}

const wv = typeof window !== "undefined" ? window.chrome?.webview : undefined;

export function isDesktop(): boolean {
  return !!wv;
}

/* ---------------- асинхронный JSON-RPC поверх postMessage ---------------- */

let seq = 1;
const pending = new Map<number, PendingCall>();
const hostEventHandlers = new Set<(type: string, payload: unknown) => void>();

if (wv) {
  wv.addEventListener("message", (e) => {
    const d = e.data as {
      id?: number;
      ok?: boolean;
      result?: unknown;
      error?: string;
      event?: string;
      payload?: unknown;
    } | null;
    if (!d || typeof d !== "object") return;
    if (d.event) {
      hostEventHandlers.forEach((h) => h(d.event!, d.payload));
      return;
    }
    if (typeof d.id === "number" && pending.has(d.id)) {
      const p = pending.get(d.id)!;
      pending.delete(d.id);
      if (d.ok) p.resolve(d.result);
      else p.reject(new Error(d.error ?? "host error"));
    }
  });
}

function call<T = unknown>(method: string, ...args: unknown[]): Promise<T> {
  if (!wv) return Promise.reject(new Error("no bridge"));
  const id = seq++;
  wv.postMessage({ id, method, args });
  return new Promise<T>((resolve, reject) => {
    pending.set(id, { resolve: resolve as (v: unknown) => void, reject });
    window.setTimeout(() => {
      if (pending.has(id)) {
        pending.delete(id);
        reject(new Error("bridge timeout"));
      }
    }, 5000);
  });
}

/** подписка на события хоста (глобальные горячие клавиши, трей и т.п.) */
export function onHostEvent(cb: (type: string, payload: unknown) => void): () => void {
  hostEventHandlers.add(cb);
  return () => hostEventHandlers.delete(cb);
}

/* ---------------- настройки ---------------- */

const LS_PREFIX = "yawa:";

export async function settingGet<T>(key: string, fallback: T): Promise<T> {
  if (wv) {
    try {
      const v = await call<T | null>("settings.get", key);
      if (v !== null && v !== undefined) return v;
    } catch {
      /* fallthrough */
    }
  }
  try {
    const raw = localStorage.getItem(LS_PREFIX + key);
    return raw ? { ...fallback, ...JSON.parse(raw) } : fallback;
  } catch {
    return fallback;
  }
}

export async function settingSet(key: string, value: unknown): Promise<void> {
  try {
    localStorage.setItem(LS_PREFIX + key, JSON.stringify(value));
  } catch {
    /* noop */
  }
  if (wv) {
    try {
      await call("settings.set", key, value);
    } catch {
      /* noop */
    }
  }
}

/* ---------------- окно без рамки ---------------- */

export const windowCtl = {
  minimize() {
    if (wv) void call("window.minimize");
  },
  toggleMaximize() {
    if (wv) void call("window.toggleMaximize");
  },
  close(closeToTray: boolean) {
    if (wv) void call("window.close", closeToTray);
  },
  async isMaximized(): Promise<boolean> {
    if (!wv) return false;
    try {
      return await call<boolean>("window.isMaximized");
    } catch {
      return false;
    }
  },
};

/* ---------------- TTS (SAPI на хосте / speechSynthesis в браузере) */

export interface HostVoice {
  id: string;
  title: string;
  gender?: string;
}

export const ttsBridge = {
  speak(text: string, voice: string, rate: number, volume: number, engine: "edge" | "sapi" = "edge") {
    if (wv) return call("tts.speak", text, voice, Math.round(rate * 10 - 10), Math.round(volume * 100), engine);
    return Promise.reject(new Error("no bridge"));
  },
  /** русские голоса хоста: нейросетевые Edge + системные SAPI */
  async voices(): Promise<{ edge: HostVoice[]; sapi: HostVoice[] }> {
    if (!wv) return { edge: [], sapi: [] };
    try {
      return await call<{ edge: HostVoice[]; sapi: HostVoice[] }>("tts.voices");
    } catch {
      return { edge: [], sapi: [] };
    }
  },
  pause() {
    if (wv) void call("tts.pause");
  },
  resume() {
    if (wv) void call("tts.resume");
  },
  clear() {
    if (wv) void call("tts.clear");
  },
};

/* ---------------- оверлей / виджет / хоткеи ---------------- */

export const overlayBridge = {
  open() {
    if (wv) void call("overlay.open");
  },
  close() {
    if (wv) void call("overlay.close");
  },
  /** переключить видимость окна оверлея; возвращает новое состояние */
  async toggle(): Promise<boolean> {
    if (!wv) return false;
    try {
      return await call<boolean>("overlay.toggle");
    } catch {
      return false;
    }
  },
  async isVisible(): Promise<boolean> {
    if (!wv) return false;
    try {
      return await call<boolean>("overlay.isVisible");
    } catch {
      return false;
    }
  },
  set(cfg: unknown) {
    if (wv) void call("overlay.set", cfg);
  },
  /** статистика площадок для панели оверлея */
  stats(stats: unknown) {
    if (wv) void call("overlay.stats", stats);
  },
  /** начать перетаскивание окна оверлея мышью (свободное размещение) */
  beginDrag() {
    if (wv) void call("overlay.beginDrag");
  },
  /** вернуть оверлей в центр экрана */
  center() {
    if (wv) void call("overlay.center");
  },
};

export async function widgetInfo(): Promise<WidgetInfo> {
  if (wv) {
    try {
      return await call<WidgetInfo>("widget.info");
    } catch {
      /* fallthrough */
    }
  }
  return { url: `${location.origin}${location.pathname}#/widget`, port: 0, running: false };
}

export function applyHotkeys(map: Record<string, string>) {
  if (wv) void call("hotkeys.apply", map);
}

/** копирование в буфер: через хост надёжнее, в браузере — clipboard API */
export async function copyText(text: string): Promise<boolean> {
  if (wv) {
    try {
      await call("clipboard.set", text);
      return true;
    } catch {
      /* fallthrough */
    }
  }
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    return false;
  }
}

/* ---------------- каналы: реальные коннекторы хоста ----------------
 * В ответ хост шлёт события:
 *  "channel.status" → {platform, username, status, viewers}
 *  "chat.message"   → ChatMsg
 */
export const channelsApi = {
  connect(sub: ChannelSub) {
    if (wv) void call("channels.connect", sub);
  },
  disconnect(sub: Pick<ChannelSub, "platform" | "username">) {
    if (wv) void call("channels.disconnect", sub);
  },
  reconnect(sub: ChannelSub) {
    if (wv) void call("channels.reconnect", sub);
  },
};

/** открыть ссылку в браузере по умолчанию (авторизация чат-бота и пр.) */
export function openExternal(url: string) {
  if (wv) void call("shell.openExternal", url);
  else window.open(url, "_blank", "noopener");
}
