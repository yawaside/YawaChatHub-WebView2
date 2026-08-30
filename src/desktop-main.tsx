import React, { useEffect } from "react";
import { createRoot } from "react-dom/client";
import { MousePointerClick, Pin, X } from "lucide-react";
import "./app/globals.css";
import DesktopApp from "./components/desktop-app";
import { AppProvider, useApp } from "./lib/store";
import { lookVars } from "./lib/look";
import { WidgetMsgRow } from "./components/widget-row";

function DesktopOverlay() {
  const { settings, messages } = useApp();
  const cfg = settings.overlay;
  const shown = messages.filter((m) => !m.sys).slice(-cfg.maxMessages);

  useEffect(() => {
    document.documentElement.classList.add("overlay-mode");
    return () => document.documentElement.classList.remove("overlay-mode");
  }, []);

  return (
    <main className="h-screen w-screen overflow-hidden p-2" style={{ background: "transparent" }}>
      <div
        className="flex h-full flex-col justify-end"
        style={lookVars(
          {
            theme: "minimal-dark",
            fontSize: cfg.fontSize,
            bgOpacity: cfg.bgOpacity,
            radius: cfg.radius,
            shadow: true,
            border: cfg.showBorder,
            textColor: cfg.textColor,
            nameColor: cfg.nameColor,
            bgColor: cfg.bgColor,
            bgImage: cfg.bgImage,
          },
          cfg.effectDuration,
        )}
      >
        {cfg.showBorder && (
          <div
            className="app-drag mb-1.5 flex select-none items-center gap-2 px-3 py-1.5"
            style={{
              borderRadius: "var(--w-radius)",
              background: "var(--w-bg)",
              border: "var(--w-border-w) solid var(--w-border)",
              color: "var(--w-text)",
              fontSize: Math.max(10, cfg.fontSize - 1),
              pointerEvents: cfg.clickThrough ? "none" : "auto",
            }}
          >
            <span className="size-1.5 flex-none rounded-full live-dot" style={{ background: "#34d399" }} />
            <span className="font-bold">YawaChatHub</span>
            <span className="ml-2 opacity-55">{cfg.locked ? "закреплён" : cfg.clickThrough ? "сквозные клики" : "тяните за шапку"}</span>
            <span className="app-no-drag ml-auto flex items-center gap-1">
              {cfg.clickThrough && <MousePointerClick size={11} className="opacity-60" />}
              {cfg.locked && <Pin size={11} className="opacity-60" />}
              <button type="button" className="opacity-50 transition-opacity hover:opacity-100" onClick={() => window.close()} aria-label="Скрыть">
                <X size={12} />
              </button>
            </span>
          </div>
        )}
        <div className="app-no-drag flex flex-col justify-end gap-1.5" style={{ pointerEvents: cfg.clickThrough ? "none" : "auto" }}>
          {shown.map((m) => (
            <WidgetMsgRow
              key={m.id}
              msg={m}
              fx={cfg.effect}
              opts={{
                style: cfg.mode === "compact" ? "compact" : cfg.style,
                showPlatform: cfg.showPlatform,
                showTime: cfg.showTime,
              }}
            />
          ))}
        </div>
      </div>
    </main>
  );
}

function Root() {
  const hash = window.location.hash.toLowerCase();
  if (hash.includes("overlay")) {
    return (
      <AppProvider demo>
        <DesktopOverlay />
      </AppProvider>
    );
  }
  return <DesktopApp demo />;
}

createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <Root />
  </React.StrictMode>,
);
