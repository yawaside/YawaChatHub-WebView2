// ─── Единый расчёт CSS-переменных оформления виджета/оверлея ───────────────
// Используется и в живом предпросмотре, и в OBS-виджете, и в оверлее —
// этим гарантируется «100% предпросмотр ↔ рабочий виджет» (критерий №9).
import type { CSSProperties } from "react";
import type { WidgetConfig } from "./types";

interface ThemeBase {
  bg: string;
  text: string;
  name: string;
  border: string;
  shadow: string;
}

const THEME_BASE: Record<string, ThemeBase> = {
  "minimal-dark": { bg: "#0e1118", text: "#f2f4f8", name: "#a99dff", border: "rgba(255,255,255,.12)", shadow: "0 10px 28px rgba(2,6,18,.5)" },
  "minimal-light": { bg: "#ffffff", text: "#171a21", name: "#5b54e6", border: "rgba(10,15,30,.12)", shadow: "0 10px 26px rgba(40,55,100,.22)" },
  neon: { bg: "#080a12", text: "#e8fbff", name: "#38e8ff", border: "rgba(56,232,255,.4)", shadow: "0 0 24px rgba(56,232,255,.25)" },
  glass: { bg: "#ffffff", text: "#ffffff", name: "#c9bcff", border: "rgba(255,255,255,.28)", shadow: "0 10px 30px rgba(2,6,18,.35)" },
  cyber: { bg: "#10071c", text: "#ffe95e", name: "#ff2ea6", border: "rgba(255,46,166,.45)", shadow: "0 0 26px rgba(255,46,166,.3)" },
  pastel: { bg: "#faf0ff", text: "#4a3f55", name: "#a855f7", border: "rgba(168,85,247,.25)", shadow: "0 10px 24px rgba(120,60,180,.2)" },
  console: { bg: "#000000", text: "#9dff9d", name: "#4ade80", border: "rgba(74,222,128,.45)", shadow: "none" },
  streamer: { bg: "#14102a", text: "#ffffff", name: "#ff8a3d", border: "rgba(255,138,61,.35)", shadow: "0 12px 30px rgba(2,6,18,.5)" },
};

export interface LookInput {
  theme: string;
  fontSize: number;
  bgOpacity: number;
  radius: number;
  shadow: boolean;
  border: boolean;
  textColor: string;
  nameColor: string;
  bgColor: string;
  bgImage: string;
}

export function lookVars(cfg: LookInput, dur: number): CSSProperties {
  const base = THEME_BASE[cfg.theme] ?? THEME_BASE["minimal-dark"];
  const text = cfg.textColor || base.text;
  const name = cfg.nameColor || base.name;
  const bgSolid = cfg.bgColor || base.bg;
  const alpha = Math.round((cfg.bgOpacity / 100) * 1000) / 1000;
  const vars: Record<string, string | number> = {
    "--w-text": text,
    "--w-name": name,
    "--w-bg": cfg.bgImage ? "transparent" : `color-mix(in srgb, ${bgSolid} ${Math.round(alpha * 100)}%, transparent)`,
    "--w-border": base.border,
    "--w-radius": `${cfg.radius}px`,
    "--w-font": `${cfg.fontSize}px`,
    "--w-shadow": cfg.shadow ? base.shadow : "none",
    "--w-border-w": cfg.border ? "1px" : "0px",
    "--dur": `${dur}s`,
  };
  if (cfg.bgImage) {
    vars["--w-bg-image"] = cfg.bgImage.startsWith("http") ? `url(${cfg.bgImage})` : cfg.bgImage;
  }
  return vars as CSSProperties;
}

export const widgetLike = (w: WidgetConfig): LookInput => ({
  theme: w.theme,
  fontSize: w.fontSize,
  bgOpacity: w.bgOpacity,
  radius: w.radius,
  shadow: w.shadow,
  border: w.border,
  textColor: w.textColor,
  nameColor: w.nameColor,
  bgColor: w.bgColor,
  bgImage: w.bgImage,
});
