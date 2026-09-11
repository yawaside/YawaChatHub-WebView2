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
