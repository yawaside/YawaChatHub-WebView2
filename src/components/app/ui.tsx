import { useState } from "react";
import type { CSSProperties, ReactNode } from "react";
import { Check, ChevronRight } from "lucide-react";

/* ---------- тумблер ---------- */
export function Toggle({
  checked,
  onChange,
  disabled,
}: {
  checked: boolean;
  onChange: (v: boolean) => void;
  disabled?: boolean;
}) {
  return (
    <button
      type="button"
      disabled={disabled}
      onClick={() => onChange(!checked)}
      className="relative shrink-0 cursor-pointer rounded-full transition-all duration-200 disabled:opacity-40"
      style={{
        width: 34,
        height: 19,
        background: checked ? "var(--dw-accent)" : "var(--range-rest)",
        boxShadow: checked ? "0 0 10px color-mix(in srgb, var(--dw-accent) 45%, transparent)" : "none",
      }}
    >
      <span
        className="absolute top-[2.5px] rounded-full bg-white transition-all duration-200"
        style={{
          width: 14,
          height: 14,
          left: checked ? 17.5 : 2.5,
          boxShadow: "0 1px 3px rgba(0,0,0,0.4)",
        }}
      />
    </button>
  );
}

/* ---------- строка настройки с тумблером ---------- */
export function ToggleRow({
  label,
  hint,
  checked,
  onChange,
  children,
}: {
  label: string;
  hint?: string;
  checked: boolean;
  onChange: (v: boolean) => void;
  children?: ReactNode;
}) {
  return (
    <div>
      <div
        className="flex cursor-pointer items-center justify-between gap-3 py-1"
        onClick={() => onChange(!checked)}
      >
        <div className="min-w-0">
          <div className="truncate text-[12px]" style={{ color: "var(--dw-text)" }}>
            {label}
          </div>
          {hint && (
            <div className="truncate text-[10.5px]" style={{ color: "var(--dw-dim)" }}>
              {hint}
            </div>
          )}
        </div>
        <Toggle checked={checked} onChange={onChange} />
      </div>
      {children}
    </div>
  );
}

/* ---------- слайдер ---------- */
export function Slider({
  value,
  min,
  max,
  step = 0.01,
  onChange,
  format,
}: {
  value: number;
  min: number;
  max: number;
  step?: number;
  onChange: (v: number) => void;
  format?: (v: number) => string;
}) {
  const fill = ((value - min) / (max - min)) * 100;
  return (
    <div className="flex items-center gap-3">
      <input
        type="range"
        className="w-full"
        min={min}
        max={max}
        step={step}
        value={value}
        style={{ "--fill": `${fill}%` } as CSSProperties}
        onChange={(e) => onChange(Number(e.target.value))}
      />
      <span
        className="w-12 shrink-0 text-right text-[11.5px] tabular-nums"
        style={{ color: "var(--dw-dim)" }}
      >
        {format ? format(value) : String(value)}
      </span>
    </div>
  );
}

/* ---------- карточка панели ---------- */
export function Box({
  title,
  icon,
  children,
  actions,
  className = "",
}: {
  title?: string;
  icon?: ReactNode;
  children: ReactNode;
  actions?: ReactNode;
  className?: string;
}) {
  return (
    <section
      className={`rounded-xl border ${className}`}
      style={{ background: "var(--dw-panel)", borderColor: "var(--dw-line)" }}
    >
      {title && (
        <header
          className="flex items-center gap-2 border-b px-3 py-1.5"
          style={{ borderColor: "var(--dw-line)" }}
        >
          {icon}
          <h3 className="text-[12px] font-semibold tracking-wide" style={{ color: "var(--dw-text)" }}>
            {title}
          </h3>
          <div className="ml-auto flex items-center gap-1.5">{actions}</div>
        </header>
      )}
      <div className="p-3">{children}</div>
    </section>
  );
}

/* ---------- текстовое поле ---------- */
export function TextInput(props: React.InputHTMLAttributes<HTMLInputElement>) {
  return (
    <input
      {...props}
      type={props.type ?? "text"}
      className={`w-full rounded-lg border px-3 py-1.5 text-[12.5px] transition-colors outline-none ${props.className ?? ""}`}
      style={{
        background: "var(--dw-input)",
        borderColor: "transparent",
        color: "var(--dw-text)",
        ...props.style,
      }}
      onFocus={(e) => {
        e.currentTarget.style.borderColor = "var(--dw-accent)";
        props.onFocus?.(e);
      }}
      onBlur={(e) => {
        e.currentTarget.style.borderColor = "transparent";
        props.onBlur?.(e);
      }}
    />
  );
}

/* ---------- пилюля-кнопка ---------- */
export function Chip({
  active,
  onClick,
  children,
  title,
}: {
  active?: boolean;
  onClick?: () => void;
  children: ReactNode;
  title?: string;
}) {
  return (
    <button
      type="button"
      title={title}
      onClick={onClick}
      className="flex cursor-pointer items-center gap-1.5 rounded-full border px-2.5 py-1 text-[11.5px] font-medium whitespace-nowrap transition-all"
      style={{
        background: active ? "color-mix(in srgb, var(--dw-accent) 18%, transparent)" : "var(--dw-input)",
        borderColor: active ? "var(--dw-accent)" : "transparent",
        color: active ? "var(--dw-accent-2)" : "var(--dw-dim)",
      }}
    >
      {children}
    </button>
  );
}

/* ---------- маленькая кнопка-иконка ---------- */
export function IconBtn({
  icon,
  title,
  onClick,
  active,
  danger,
  size = 26,
}: {
  icon: ReactNode;
  title?: string;
  onClick?: (e: React.MouseEvent) => void;
  active?: boolean;
  danger?: boolean;
  size?: number;
}) {
  return (
    <button
      type="button"
      title={title}
      onClick={onClick}
      className="flex shrink-0 cursor-pointer items-center justify-center rounded-lg transition-colors hover:bg-[var(--dw-hover)]"
      style={{
        width: size,
        height: size,
        color: danger ? "#f87171" : active ? "var(--dw-accent-2)" : "var(--dw-dim)",
        background: active ? "color-mix(in srgb, var(--dw-accent) 15%, transparent)" : "transparent",
      }}
    >
      {icon}
    </button>
  );
}

/* ---------- чекбокс ---------- */
export function CheckRow({
  label,
  checked,
  onChange,
}: {
  label: string;
  checked: boolean;
  onChange: (v: boolean) => void;
}) {
  return (
    <label
      className="flex cursor-pointer items-center gap-2 py-1 text-[12px] select-none"
      style={{ color: "var(--dw-text)" }}
      onClick={() => onChange(!checked)}
    >
      <span
        className="flex h-[15px] w-[15px] items-center justify-center rounded border transition-colors"
        style={{
          background: checked ? "var(--dw-accent)" : "var(--dw-input)",
          borderColor: checked ? "var(--dw-accent)" : "var(--range-rest)",
        }}
      >
        {checked && <Check size={11} strokeWidth={3.5} color="#fff" />}
      </span>
      {label}
    </label>
  );
}

/* ---------- разделитель ---------- */
export function Divider() {
  return <div className="my-3 border-t" style={{ borderColor: "var(--dw-line)" }} />;
}

export const accentText: CSSProperties = { color: "var(--dw-accent-2)" };

/* ---------- сворачиваемый блок: прячет редкие настройки ---------- */
export function Collapsible({
  title,
  hint,
  children,
  defaultOpen = false,
}: {
  title: string;
  hint?: string;
  children: ReactNode;
  defaultOpen?: boolean;
}) {
  const [open, setOpen] = useState(defaultOpen);
  return (
    <div className="rounded-xl border" style={{ background: "var(--dw-panel)", borderColor: "var(--dw-line)" }}>
      <button
        onClick={() => setOpen(!open)}
        className="flex w-full cursor-pointer items-center gap-2 px-3 py-2 text-left"
      >
        <ChevronRight
          size={13}
          style={{
            color: "var(--dw-dim)",
            transform: open ? "rotate(90deg)" : "none",
            transition: "transform .15s ease",
          }}
        />
        <span className="text-[12px] font-semibold" style={{ color: "var(--dw-text)" }}>
          {title}
        </span>
        {hint && !open && (
          <span className="ml-auto truncate text-[10.5px]" style={{ color: "var(--dw-dim)" }}>
            {hint}
          </span>
        )}
      </button>
      {open && (
        <div className="border-t px-3 py-2.5" style={{ borderColor: "var(--dw-line)" }}>
          {children}
        </div>
      )}
    </div>
  );
}
