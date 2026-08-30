"use client";

/** Команды нативному окну WebView2 через официальный web-message канал. */
export type NativeWindowAction =
  | "minimize"
  | "maximize"
  | "close"
  | "hide"
  | "quit"
  | "drag";

type WebViewChannel = {
  postMessage(message: unknown): void;
};

function channel(): WebViewChannel | undefined {
  const w = window as unknown as {
    chrome?: { webview?: WebViewChannel };
    __YAWA_DESKTOP__?: boolean;
  };
  return w.chrome?.webview;
}

export function isNativeDesktop(): boolean {
  return typeof window !== "undefined" && !!channel();
}

/** Возвращает true, если команда была отправлена desktop-оболочке. */
export function nativeWindow(action: NativeWindowAction): boolean {
  if (typeof window === "undefined") return false;
  const webview = channel();
  if (!webview) return false;
  try {
    webview.postMessage({ type: "window", action });
    return true;
  } catch {
    return false;
  }
}
