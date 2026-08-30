// ─── IPC-мост SpBridge (ТЗ §4) ──────────────────────────────────────────────
// В desktop-сборке C# публикует COM-объект `sp` через
// CoreWebView2.AddHostObjectToScript("sp", bridgeHost), и JS обращается как
// window.sp.* — ответы методов чтения всегда JSON-строки.
// В браузере ниже тот же контракт эмулируется mock-мостом.
import type { Channel, ChatMsg, OverlayConfig, PlatformId } from "./types";

export interface WidgetInfo {
  port: number;
  token: string;
  url: string;
}

export interface SpBridge {
  mode: "app" | "overlay";
  platform: "win32";

  // Каналы
  getChannels(): Promise<Channel[]>;
  addChannel(platform: PlatformId, channelId: string): void;
  removeChannel(platform: PlatformId, channelId: string): void;
  onChannels(cb: (list: Channel[]) => void): void;
  onChat(cb: (msg: ChatMsg) => void): void;
  diagnoseNet(): void;

  // Виджет OBS
  widgetUrl(): Promise<string>;
  widgetInfo(): Promise<WidgetInfo>;
  widgetTest(msg: ChatMsg): void;
  widgetConfig(payload: unknown): void;
  onWidgetClients(cb: (n: number) => void): void;

  // Настройки
  settings: {
    get(): Promise<Record<string, unknown>>;
    patch(p: Record<string, unknown>): void;
    onChange(cb: (s: Record<string, unknown>) => void): void;
  };

  // Горячие клавиши
  onHotkey(cb: (action: string) => void): void;
  hotkeys: { apply(map: Record<string, string>): void };

  // Окно
  window: {
    minimize(): void;
    toggleMaximize(): void;
    hideToTray(): void;
    close(): void;
    isMaximized(): Promise<boolean>;
    onMaximize(cb: (v: boolean) => void): void;
  };

  // Озвучка (стандартные голоса Windows SAPI5)
  tts: {
    speak(p: { id: string; text: string; rate: number; volume: number; voice?: string }): void;
    skip(): void;
    stopAll(): void;
    voices(): Promise<string[]>;
    onEnd(cb: (id: string) => void): void;
  };

  // Оверлей
  overlay: {
    get(): Promise<OverlayConfig>;
    set(cfg: Partial<OverlayConfig>): void;
    onChange(cb: (o: OverlayConfig) => void): void;
  };

  app: { quit(): void };
}

declare global {
  interface Window {
    sp?: SpBridge;
  }
}

/** true, когда фронтенд работает внутри WebView2 desktop-оболочки. */
export const isDesktop = () => typeof window !== "undefined" && !!window.sp;
