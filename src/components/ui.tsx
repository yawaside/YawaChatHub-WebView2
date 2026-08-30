"use client";

// ─── Базовые контролы настроек (ТЗ §9: Panel, Slider, Toggle, Select…) ─────
import { useState, type ReactNode } from "react";
import { ChevronDown, Plus, X, type LucideIcon } from "lucide-react";
import { cn, uid } from "@/lib/utils";

/* Панель-аккордеон. По ТЗ §14.1 все настроечные панели свёрнуты по умолчанию,
   кроме предпросмотров, «Ссылка для OBS» и «Горячие клавиши». */
export function Panel({
  title,
  icon: Icon,
  children,
  collapsible = true,
  defaultOpen = false,
  right,
  className,
}: {
  title: string;
  icon?: LucideIcon;
  children: ReactNode;
  collapsible?: boolean;
  defaultOpen?: boolean;
  right?: ReactNode;
  className?: string;
}) {
  const [open, setOpen] = useState(defaultOpen || !collapsible);
  const expanded = collapsible ? open : true;
  return (
    <section className={cn("panel overflow-hidden", className)}>
      <button
        type="button"
        onClick={() => collapsible && setOpen((o) => !o)}
        className={cn(
          "flex w-full items-center gap-3 px-4 py-3 text-left",
          collapsible ? "cursor-pointer" : "cursor-default",
        )}
      >
        {Icon && (
          <span
            className="grid size-8 flex-none place-items-center rounded-lg"
            style={{ background: "var(--panel-2)", color: "var(--accent)" }}
          >
            <Icon size={16} />
          </span>
        )}
        <span className="flex-1 text-[15px] font-semibold tracking-tight">{title}</span>
        {right}
        {collapsible && (
          <ChevronDown
            size={16}
            className="flex-none transition-transform duration-300"
            style={{ color: "var(--muted)", transform: expanded ? "rotate(180deg)" : "none" }}
          />
        )}
      </button>
      <div
        className="grid transition-[grid-template-rows] duration-300 ease-out"
        style={{ gridTemplateRows: expanded ? "1fr" : "0fr" }}
      >
        <div className="min-h-0 overflow-hidden">
          <div className="border-t px-4 pb-4 pt-3" style={{ borderColor: "var(--border)" }}>
            {children}
          </div>
        </div>
      </div>
    </section>
  );
}

export function Row({ label, desc, children, className }: { label: string; desc?: string; children: ReactNode; className?: string }) {
  return (
    <div className={cn("flex items-center justify-between gap-4 py-2", className)}>
      <div className="min-w-0">
        <div className="text-sm font-medium leading-tight">{label}</div>
        {desc && <div className="mt-0.5 text-xs leading-snug" style={{ color: "var(--muted)" }}>{desc}</div>}
      </div>
      <div className="flex flex-none items-center gap-2">{children}</div>
    </div>
  );
}

export function Toggle({ label, desc, value, onChange }: { label: string; desc?: string; value: boolean; onChange: (v: boolean) => void }) {
  return (
    <Row label={label} desc={desc}>
      <button type="button" role="switch" aria-checked={value} className="switch" data-on={value} onClick={() => onChange(!value)} aria-label={label} />
    </Row>
  );
}

export function Slider({
  label,
  value,
  min,
  max,
  step = 1,
  onChange,
  format,
  desc,
}: {
  label: string;
  value: number;
  min: number;
  max: number;
  step?: number;
  onChange: (v: number) => void;
  format?: (v: number) => string;
  desc?: string;
}) {
  const pct = ((value - min) / (max - min)) * 100;
  return (
    <div className="py-2">
      <div className="mb-1 flex items-center justify-between gap-3">
        <div>
          <span className="text-sm font-medium">{label}</span>
          {desc && <span className="ml-2 text-xs" style={{ color: "var(--muted)" }}>{desc}</span>}
        </div>
        <span className="rounded-md px-2 py-0.5 text-xs font-semibold tabular-nums" style={{ background: "var(--panel-2)", color: "var(--accent)" }}>
          {format ? format(value) : value}
        </span>
      </div>
      <input
        type="range"
        className="slider"
        min={min}
        max={max}
        step={step}
        value={value}
        style={{ "--val": `${pct}%` } as React.CSSProperties}
        onChange={(e) => onChange(Number(e.target.value))}
      />
    </div>
  );
}

export function Select({
  label,
  value,
  options,
  onChange,
  desc,
}: {
  label?: string;
  value: string;
  options: { value: string; label: string }[];
  onChange: (v: string) => void;
  desc?: string;
}) {
  const sel = (
    <select className="input" style={{ width: "auto", minWidth: 150 }} value={value} onChange={(e) => onChange(e.target.value)}>
      {options.map((o) => (
        <option key={o.value} value={o.value} style={{ background: "var(--bg-2)", color: "var(--text)" }}>
          {o.label}
        </option>
      ))}
    </select>
  );
  if (!label) return sel;
  return (
    <Row label={label} desc={desc}>
      {sel}
    </Row>
  );
}

export function Segmented<T extends string>({
  label,
  value,
  options,
  onChange,
  desc,
}: {
  label?: string;
  value: T;
  options: { value: T; label: string }[];
  onChange: (v: T) => void;
  desc?: string;
}) {
  const seg = (
    <div className="flex gap-1 rounded-xl p-1" style={{ background: "var(--panel-2)", border: "1px solid var(--border)" }}>
      {options.map((o) => (
        <button
          key={o.value}
          type="button"
          onClick={() => onChange(o.value)}
          className="rounded-lg px-3 py-1.5 text-xs font-semibold transition-all"
          style={
            value === o.value
              ? { background: "linear-gradient(135deg, var(--accent), color-mix(in srgb, var(--accent-2) 60%, var(--accent)))", color: "var(--accent-text)" }
              : { color: "var(--muted)" }
          }
        >
          {o.label}
        </button>
      ))}
    </div>
  );
  if (!label) return seg;
  return (
    <Row label={label} desc={desc}>
      {seg}
    </Row>
  );
}

export function ColorInput({ label, value, onChange, placeholder = "Авто" }: { label: string; value: string; onChange: (v: string) => void; placeholder?: string }) {
  return (
    <Row label={label}>
      {value && (
        <button type="button" onClick={() => onChange("")} className="text-xs font-medium" style={{ color: "var(--muted)" }} title="Сбросить">
          Сброс
        </button>
      )}
      <span className="relative grid size-8 place-items-center overflow-hidden rounded-lg border" style={{ borderColor: "var(--border-2)", background: value || "var(--panel-2)" }}>
        {!value && <span className="text-[9px] font-bold" style={{ color: "var(--muted)" }}>{placeholder.slice(0, 3).toUpperCase()}</span>}
        <input
          type="color"
          value={value || "#8b7bff"}
          onChange={(e) => onChange(e.target.value)}
          className="absolute inset-0 size-full cursor-pointer opacity-0"
          aria-label={label}
        />
      </span>
    </Row>
  );
}

export function NumBadge({ children, tone = "accent" }: { children: ReactNode; tone?: "accent" | "ok" | "muted" }) {
  const colors = {
    accent: { bg: "color-mix(in srgb, var(--accent) 16%, transparent)", fg: "var(--accent)" },
    ok: { bg: "color-mix(in srgb, var(--ok) 14%, transparent)", fg: "var(--ok)" },
    muted: { bg: "var(--panel-2)", fg: "var(--muted)" },
  }[tone];
  return (
    <span className="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[11px] font-bold tabular-nums" style={{ background: colors.bg, color: colors.fg }}>
      {children}
    </span>
  );
}

/** Редактор списка слов: банворды, маскировка, авторы (ТЗ §9, фильтры). */
export function WordListEditor({
  label,
  desc,
  words,
  onChange,
  placeholder,
}: {
  label: string;
  desc?: string;
  words: string[];
  onChange: (w: string[]) => void;
  placeholder?: string;
}) {
  const [draft, setDraft] = useState("");
  const add = () => {
    const w = draft.trim();
    if (!w) return;
    if (words.some((x) => x.toLowerCase() === w.toLowerCase())) return setDraft("");
    onChange([...words, w]);
    setDraft("");
  };
  return (
    <div className="py-2">
      <div className="mb-1.5">
        <div className="text-sm font-medium">{label}</div>
        {desc && <div className="text-xs" style={{ color: "var(--muted)" }}>{desc}</div>}
      </div>
      <div className="flex gap-2">
        <input
          className="input"
          placeholder={placeholder ?? "Добавить…"}
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={(e) => e.key === "Enter" && add()}
        />
        <button type="button" className="btn flex-none !px-3" onClick={add} aria-label="Добавить">
          <Plus size={15} />
        </button>
      </div>
      {words.length > 0 && (
        <div className="mt-2 flex flex-wrap gap-1.5">
          {words.map((w) => (
            <span key={w} className="inline-flex items-center gap-1 rounded-full px-2.5 py-1 text-xs font-medium" style={{ background: "var(--panel-2)", border: "1px solid var(--border)" }}>
              {w}
              <button type="button" onClick={() => onChange(words.filter((x) => x !== w))} className="opacity-60 transition-opacity hover:opacity-100" aria-label={`Удалить ${w}`}>
                <X size={12} />
              </button>
            </span>
          ))}
        </div>
      )}
    </div>
  );
}

export function IconBtn({ icon: Icon, onClick, title, active, danger }: { icon: LucideIcon; onClick?: () => void; title: string; active?: boolean; danger?: boolean }) {
  return (
    <button
      type="button"
      onClick={onClick}
      title={title}
      aria-label={title}
      className="grid size-8 place-items-center rounded-lg transition-all hover:scale-105"
      style={{
        background: active ? "color-mix(in srgb, var(--accent) 18%, transparent)" : "transparent",
        color: danger ? "var(--danger)" : active ? "var(--accent)" : "var(--muted)",
        border: `1px solid ${active ? "color-mix(in srgb, var(--accent) 40%, transparent)" : "transparent"}`,
      }}
    >
      <Icon size={16} />
    </button>
  );
}

export const rid = uid;
