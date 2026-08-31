"use client";

// ─── Адаптер реального C# WebView2-моста ────────────────────────────────────
// В portable EXE C# публикует `sp` как COM host object. Здесь нет моков: все
// настройки, каналы и SAPI5-команды идут непосредственно в native-слой.
import type { ChatMsg, Channel, OverlayConfig, PlatformId, Settings } from "./types";

type NativeSp = {
  settingsGet?: () => string;
  settingsPatch?: (payload: string) => void;
  getChannels?: () => string;
  addChannel?: (platform: string, channel: string) => void;
  removeChannel?: (platform: string, channel: string) => void;
  diagnoseNet?: () => void;
  widgetUrl?: () => string;
  widgetInfo?: () => string;
  widgetTest?: (message: string) => void;
  widgetConfig?: (config: string) => void;
  minimize?: () => void;
  toggleMaximize?: () => void;
  hideToTray?: () => void;
  close?: () => void;
  isMaximized?: () => boolean;
  ttsSpeak?: (payload: string) => void;
  ttsSkip?: () => void;
  ttsStopAll?: () => void;
  ttsVoices?: () => string;
  overlayGet?: () => string;
  overlaySet?: (payload: string) => void;
  appQuit?: () => void;
  dragMove?: () => void;
};

function rawBridge(): NativeSp | undefined {
  // bridge.ts объявляет public-контракт window.sp. В native-сборке C# host
  // object имеет плоские COM-методы (settingsGet, ttsSpeak и т. п.), поэтому
  // здесь намеренно используем локальный runtime-cast.
  return (window as unknown as { sp?: NativeSp }).sp;
}

export const isDesktopBridge = () =>
  typeof window !== "undefined" && typeof rawBridge()?.settingsGet === "function";

function bridge(): NativeSp | null {
  return isDesktopBridge() ? rawBridge() ?? null : null;
}

function parse<T>(raw: string | undefined, fallback: T): T {
  if (!raw) return fallback;
  try {
    return JSON.parse(raw) as T;
  } catch {
    return fallback;
  }
}

/** Дожидается host object: это нужно только в первые миллисекунды WebView2. */
export async function waitForDesktopBridge(timeout = 3000): Promise<boolean> {
  const until = Date.now() + timeout;
  while (Date.now() < until) {
    if (isDesktopBridge()) return true;
    await new Promise((resolve) => setTimeout(resolve, 40));
  }
  return isDesktopBridge();
}

export function desktopSettings(): Settings | null {
  const b = bridge();
  if (!b?.settingsGet) return null;
  return parse<Settings | null>(b.settingsGet(), null);
}

export function desktopPatch(patch: Record<string, unknown>) {
  bridge()?.settingsPatch?.(JSON.stringify(patch));
}

export function desktopChannels(): Channel[] {
  return parse(bridge()?.getChannels?.(), [] as Channel[]);
}

export function desktopAddChannel(platform: PlatformId, channelId: string) {
  bridge()?.addChannel?.(platform, channelId);
}

export function desktopRemoveChannel(platform: PlatformId, channelId: string) {
  bridge()?.removeChannel?.(platform, channelId);
}

export function desktopTtsSpeak(payload: { id: string; text: string; rate: number; volume: number; voice?: string }) {
  bridge()?.ttsSpeak?.(JSON.stringify(payload));
}

export const desktopTtsSkip = () => bridge()?.ttsSkip?.();
export const desktopTtsStopAll = () => bridge()?.ttsStopAll?.();

export function desktopTtsVoices(): string[] {
  return parse(bridge()?.ttsVoices?.(), [] as string[]);
}

export function desktopOverlaySet(config: Partial<OverlayConfig>) {
  bridge()?.overlaySet?.(JSON.stringify(config));
}

export function desktopOverlayGet(): OverlayConfig | null {
  return parse<OverlayConfig | null>(bridge()?.overlayGet?.(), null);
}

export function desktopWidgetInfo(): { port: number; token: string; url: string } | null {
  return parse(bridge()?.widgetInfo?.(), null);
}

export function desktopWidgetTest(msg: ChatMsg) {
  bridge()?.widgetTest?.(JSON.stringify(msg));
}

/** События приходят от BridgeHost.Emit* через CustomEvent. */
export function onDesktopEvent<T>(name: string, listener: (detail: T) => void): () => void {
  const handle = (event: Event) => listener((event as CustomEvent<T>).detail);
  window.addEventListener(name, handle as EventListener);
  return () => window.removeEventListener(name, handle as EventListener);
}
