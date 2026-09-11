import type { CSSProperties } from "react";

/**
 * Эффекты появления сообщений. Один набор на все три поверхности:
 * лента приложения, игровой оверлей и OBS-виджет. Скорость задаётся
 * отдельно и передаётся в CSS переменной --fx-speed.
 */
export type EffectId = "none" | "fade" | "slide-left" | "slide-up" | "scale" | "blur" | "bounce";

export interface EffectMeta {
  id: EffectId;
  name: string;
  desc: string;
}

export const EFFECTS: EffectMeta[] = [
  { id: "none", name: "Без эффекта", desc: "мгновенное появление" },
  { id: "fade", name: "Проявление", desc: "плавная прозрачность" },
  { id: "slide-left", name: "Выезд сбоку", desc: "сдвиг слева направо" },
  { id: "slide-up", name: "Выезд снизу", desc: "сдвиг снизу вверх" },
  { id: "scale", name: "Увеличение", desc: "лёгкий зум" },
  { id: "blur", name: "Из размытия", desc: "фокусировка текста" },
  { id: "bounce", name: "Отскок", desc: "пружинистое появление" },
];

/** CSS-класс анимации; none — без анимации вовсе */
export function effectClass(effect: EffectId | undefined): string {
  const id = effect ?? "fade";
  return id === "none" ? "" : `fx fx-${id}`;
}

/** Длительность эффекта: 1 = обычная (280 мс), меньше = быстрее */
export function effectVars(speed: number | undefined): CSSProperties {
  const s = Math.min(3, Math.max(0.2, speed ?? 1));
  return { ["--fx-speed" as string]: `${Math.round(280 * s)}ms` };
}

export function effectName(id: EffectId | undefined): string {
  return EFFECTS.find((e) => e.id === (id ?? "fade"))?.name ?? "Проявление";
}

/**
 * Обводка текста заданной толщины. 0 — обводки нет, дальше плотность растёт.
 * Реализована тенями по кругу: работает в ленте, оверлее и OBS-виджете.
 */
export function textOutline(width: number | undefined, fallbackShadow = false): string {
  const w = Math.max(0, Math.min(6, Math.round(width ?? (fallbackShadow ? 2 : 0))));
  if (w === 0) return "none";

  // Минимум теней для плотной обводки: 8 направлений достаточно, а рендер
  // дешевле, чем при 12 — важно для оверлея с десятками сообщений.
  const shadows: string[] = [];
  const steps = 8;
  for (let i = 0; i < steps; i++) {
    const angle = (Math.PI * 2 * i) / steps;
    const x = +(Math.cos(angle) * w).toFixed(1);
    const y = +(Math.sin(angle) * w).toFixed(1);
    shadows.push(`${x}px ${y}px 0 rgba(0,0,0,0.9)`);
  }
  shadows.push(`0 1px 2px rgba(0,0,0,0.7)`);
  return shadows.join(", ");
}
