"use client";

// ─── Вкладка «Оверлей» (ТЗ §9, §13) ─────────────────────────────────────────
import { ExternalLink, Eye, Frame, Gamepad2, GripHorizontal, Lock, MousePointerClick, Unlock } from "lucide-react";
import { useApp } from "@/lib/store";
import { lookVars } from "@/lib/look";
import { WidgetMsgRow } from "../widget-row";
import { FX_LIST, type OverlayConfig } from "@/lib/types";
import { ColorInput, Panel, Row, Segmented, Select, Slider, Toggle } from "../ui";

export function OverlayPanel() {
  const { settings, patch, messages, toast } = useApp();
  const o = settings.overlay;
  const setO = (p: Partial<OverlayConfig>) => patch({ overlay: p });

  const openWindow = () => {
    window.open("/overlay", "yawa-overlay", "width=420,height=640,popup=yes");
    toast("Окно оверлея открыто. В desktop — поверх игры");
  };

  const shown = messages.filter((m) => !m.sys).slice(-o.maxMessages);

  return (
    <div className="grid items-start gap-3 xl:grid-cols-[1fr_400px]">
      <div className="flex min-w-0 flex-col gap-3">
        <Panel title="Игровой оверлей" icon={Gamepad2}>
          <Toggle
            label="Оверлей включён"
            desc="Прозрачное окно поверх игры, AlwaysOnTop"
            value={o.enabled}
            onChange={(v) => setO({ enabled: v })}
          />
          <Row label="Окно оверлея" desc="Адрес #/overlay, позиция запоминается">
            <button type="button" className="btn btn-accent !py-2 text-xs" onClick={openWindow}>
              <ExternalLink size={14} /> Открыть окно
            </button>
          </Row>
          <Toggle
            label="Сквозные клики"
            desc="Клики проходят в игру (WS_EX_TRANSPARENT в desktop)"
            value={o.clickThrough}
            onChange={(v) => setO({ clickThrough: v })}
          />
          <Toggle
            label="Закрепить позицию"
            desc="Запрет перемещения окна мышью"
            value={o.locked}
            onChange={(v) => setO({ locked: v })}
          />
        </Panel>

        <Panel title="Лента оверлея" icon={Frame}>
          <Segmented
            label="Режим"
            value={o.mode}
            onChange={(v) => setO({ mode: v })}
            options={[
              { value: "compact", label: "Компактный" },
              { value: "cozy", label: "Уютный" },
            ]}
          />
          <Slider label="Максимум сообщений" value={o.maxMessages} min={1} max={12} onChange={(v) => setO({ maxMessages: v })} />
          <Slider label="Размер текста" value={o.fontSize} min={10} max={20} format={(v) => `${v} px`} onChange={(v) => setO({ fontSize: v })} />
          <Select label="Эффект появления" value={o.effect} onChange={(v) => setO({ effect: v as OverlayConfig["effect"] })} options={FX_LIST.map((f) => ({ value: f.id, label: f.name }))} />
          <Slider label="Скорость эффекта" value={o.effectDuration} min={0.1} max={1.5} step={0.02} format={(v) => `${v.toFixed(2)} c`} onChange={(v) => setO({ effectDuration: v })} />
          <Toggle label="Рамка окна" desc="Если выключена — только сообщения, без подложки шапки" value={o.showBorder} onChange={(v) => setO({ showBorder: v })} />
          <Toggle label="Иконка площадки" value={o.showPlatform} onChange={(v) => setO({ showPlatform: v })} />
          <Toggle label="Время сообщения" value={o.showTime} onChange={(v) => setO({ showTime: v })} />
        </Panel>

        <Panel title="Подложка" icon={GripHorizontal}>
          <Slider label="Прозрачность подложки" value={o.bgOpacity} min={0} max={100} format={(v) => `${v}%`} onChange={(v) => setO({ bgOpacity: v })} />
          <Slider label="Скругление" value={o.radius} min={0} max={28} format={(v) => `${v} px`} onChange={(v) => setO({ radius: v })} />
          <ColorInput label="Цвет текста" value={o.textColor} onChange={(v) => setO({ textColor: v })} />
          <ColorInput label="Цвет ников" value={o.nameColor} onChange={(v) => setO({ nameColor: v })} />
          <ColorInput label="Цвет подложки" value={o.bgColor} onChange={(v) => setO({ bgColor: v })} />
          <div className="py-2">
            <div className="mb-1 text-sm font-medium">Фоновая картинка</div>
            <input className="input" placeholder="https://… (URL изображения)" value={o.bgImage} onChange={(e) => setO({ bgImage: e.target.value })} />
          </div>
        </Panel>
      </div>

      <div className="xl:sticky xl:top-3">
        {/* Предпросмотр — всегда раскрыт */}
        <Panel title="Предпросмотр поверх игры" icon={Eye} collapsible={false}>
          <div
            className="relative overflow-hidden rounded-xl"
            style={{
              minHeight: 340,
              background:
                "radial-gradient(500px 220px at 70% 0%, rgba(255,190,90,.35), transparent 60%), linear-gradient(180deg, #263a5e 0%, #3f5e46 55%, #2c4429 100%)",
            }}
          >
            {/* имитация игровой сцены */}
            <div className="absolute inset-x-0 bottom-0 h-16" style={{ background: "linear-gradient(180deg, transparent, rgba(0,0,0,.35))" }} />
            <div className="absolute left-6 top-8 size-16 rounded-full opacity-80" style={{ background: "radial-gradient(circle, #ffe9b0, #ffb84d)" }} />
            <div className="absolute bottom-10 left-10 h-16 w-44 rounded-t-full opacity-60" style={{ background: "#1d3320" }} />
            <div className="absolute bottom-6 right-24 h-20 w-56 rounded-t-full opacity-50" style={{ background: "#182b18" }} />

            {/* макет окна оверлея */}
            <div className="absolute right-3 top-3 w-[78%]" style={lookVars({ theme: "minimal-dark", fontSize: o.fontSize, bgOpacity: o.bgOpacity, radius: o.radius, shadow: true, border: o.showBorder, textColor: o.textColor, nameColor: o.nameColor, bgColor: o.bgColor, bgImage: o.bgImage }, o.effectDuration)}>
              {o.showBorder && (
                <div
                  className="mb-1.5 flex items-center gap-1.5 px-3 py-1.5 text-[11px] font-semibold"
                  style={{
                    borderRadius: "var(--w-radius)",
                    background: "var(--w-bg)",
                    border: "var(--w-border-w) solid var(--w-border)",
                    color: "var(--w-text)",
                  }}
                >
                  <span className="size-1.5 rounded-full live-dot" style={{ background: "var(--ok)" }} />
                  YawaChatHub
                  <span className="ml-auto flex items-center gap-1 opacity-60">
                    {o.clickThrough ? <MousePointerClick size={11} /> : o.locked ? <Lock size={11} /> : <Unlock size={11} />}
                    {o.clickThrough ? "сквозные клики" : o.locked ? "закреплён" : "тяните за шапку"}
                  </span>
                </div>
              )}
              <div className="flex flex-col gap-1.5">
                {shown.length === 0 && (
                  <div className="px-3 py-2 text-[11px]" style={{ borderRadius: "var(--w-radius)", background: "var(--w-bg)", border: "var(--w-border-w) solid var(--w-border)", color: "var(--w-text)" }}>
                    Ожидание сообщений…
                  </div>
                )}
                {shown.map((m) => (
                  <WidgetMsgRow key={m.id} msg={m} fx={o.effect} opts={{ style: o.mode === "compact" ? "compact" : o.style, showPlatform: o.showPlatform, showTime: o.showTime }} />
                ))}
              </div>
            </div>
          </div>
          <div className="mt-2 text-xs leading-snug" style={{ color: "var(--muted)" }}>
            Окно всегда непрозрачное (opacity = 1) — прозрачной является только подложка, текст остаётся чётким.
          </div>
        </Panel>
      </div>
    </div>
  );
}
