import { clsx, type ClassValue } from "clsx";
import { twMerge } from "tailwind-merge";

export const cn = (...inputs: ClassValue[]) => twMerge(clsx(inputs));

export const uid = () =>
  typeof crypto !== "undefined" && "randomUUID" in crypto
    ? crypto.randomUUID()
    : Math.random().toString(36).slice(2) + Date.now().toString(36);

export const clamp = (v: number, min: number, max: number) =>
  Math.min(max, Math.max(min, v));

export const formatTime = (ts: number) => {
  const d = new Date(ts);
  const p = (n: number) => String(n).padStart(2, "0");
  return `${p(d.getHours())}:${p(d.getMinutes())}`;
};

/** Стабильный цвет ника из строки (hue-хэш). */
export const nameColor = (name: string): string => {
  let h = 0;
  for (let i = 0; i < name.length; i++) h = (h * 31 + name.charCodeAt(i)) | 0;
  const hue = ((h % 360) + 360) % 360;
  return `hsl(${hue} 85% 68%)`;
};

export const token = () =>
  "yawa_" + Math.random().toString(36).slice(2, 14) + Math.random().toString(36).slice(2, 6);

/** Глубокое слияние plain-объектов (массивы заменяются). */
export function deepMerge<T>(base: T, patch: unknown): T {
  if (patch === null || patch === undefined) return base;
  if (Array.isArray(patch)) return patch as T;
  if (typeof patch === "object" && typeof base === "object" && base !== null) {
    const out: Record<string, unknown> = { ...(base as Record<string, unknown>) };
    for (const [k, v] of Object.entries(patch as Record<string, unknown>)) {
      out[k] = k in out ? deepMerge(out[k], v) : v;
    }
    return out as T;
  }
  return patch as T;
}

/** Форматирует комбинацию горячих клавиш из KeyboardEvent. */
export function comboFromEvent(e: KeyboardEvent): string | null {
  const key = e.key;
  if (["Control", "Shift", "Alt", "Meta"].includes(key)) return null;
  const parts: string[] = [];
  if (e.ctrlKey) parts.push("Control");
  if (e.altKey) parts.push("Alt");
  if (e.shiftKey) parts.push("Shift");
  if (e.metaKey) parts.push("Meta");
  let k = key.length === 1 ? key.toUpperCase() : key;
  if (k === " ") k = "Space";
  parts.push(k);
  return parts.join("+");
}

export function comboMatches(e: KeyboardEvent, combo: string): boolean {
  const parts = combo.split("+");
  const key = parts[parts.length - 1];
  const need = (m: string) => parts.includes(m);
  const ek = e.key.length === 1 ? e.key.toUpperCase() : e.key === " " ? "Space" : e.key;
  return (
    ek === key &&
    e.ctrlKey === need("Control") &&
    e.altKey === need("Alt") &&
    e.shiftKey === need("Shift") &&
    e.metaKey === need("Meta")
  );
}
