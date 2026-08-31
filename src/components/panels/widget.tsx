"use client";

// ─── Вкладка «Виджет OBS» (ТЗ §9, §12) ──────────────────────────────────────
import { useEffect, useState } from "react";
import { Brush, Check, Copy, ExternalLink, Eye, FlaskConical, Layers, Link2, MonitorPlay, Palette } from "lucide-react";
import { useApp } from "@/lib/store";
import { desktopWidgetInfo, desktopWidgetTest } from "@/lib/desktop-bridge";
import { lookVars, widgetLike } from "@/lib/look";
import { WidgetMsgRow } from "../widget-row";
import { makeMsg } from "@/lib/chat-sim";
import { FX_LIST, WIDGET_PRESETS, type WidgetConfig } from "@/lib/types";
import { ColorInput, NumBadge, Panel, Row, Segmented, Select, Slider, Toggle } from "../ui";

export function WidgetPanel() {
  const { settings, patch, messages, publishMsg, widgetClients, toast, demo, desktop } = useApp();
  const w = settings.widget;
  const [info, setInfo] = useState<{ url: string; port: number } | null>(null);

  useEffect(() => {
    if (demo) {
      setInfo({ url: `${location.origin}/widget?token=yawa_demo`, port: 47823 });
      return;
    }
    if (desktop) {
      const native = desktopWidgetInfo();
      if (native) setInfo({ url: native.url, port: native.port });
      return;
    }
    fetch("/api/widget-info", { cache: "no-store" })
      .then((r) => r.json())
      .then((d) => setInfo({ url: d.url, port: d.port }))
      .catch(() => {});
  }, [demo, desktop, settings.token]);

  const setW = (p: Partial<WidgetConfig>) => patch({ widget: p });

  const copy = async () => {
    if (!info) return;
    try {
      await navigator.clipboard.writeText(info.url);
      toast("Ссылка для OBS скопирована");
    } catch {
      toast("Не удалось скопировать — выделите вручную");
    }
  };

  const sendTest = () => {
    const platforms = ["twitch", "youtube", "kick", "vk", "tiktok"] as const;
    const pl = platforms[Math.floor(Math.random() * platforms.length)];
    const message = makeMsg(pl, "obs-test", "widget_test", "Тест виджета OBS: смайлы и текст на месте Kappa");
    // Это явная пользовательская проверка виджета. В native-режиме тест уходит
    // только в OBS WidgetServer, а не добавляется в ленту настоящего чата.
    if (desktop) desktopWidgetTest(message);
    else publishMsg(message);
    toast("Тестовое сообщение отправлено в виджет");
  };

  const shown = messages.filter((m) => !m.sys).slice(-w.maxMessages);
  const ordered = w.dir === "down" ? [...shown].reverse() : shown;

  return (
    <div className="grid items-start gap-3 xl:grid-cols-[1fr_400px]">
      <div className="flex min-w-0 flex-col gap-3">
        {/* Всегда раскрыта (ТЗ §14.1) */}
        <Panel title="Ссылка для OBS" icon={Link2} collapsible={false}>
          <Row label="Статичный URL" desc="Оформление приходит по соединению — ссылка не меняется при смене стиля">
            <NumBadge tone={widgetClients > 0 ? "ok" : "muted"}>
              <MonitorPlay size={11} /> {widgetClients} источн.
            </NumBadge>
          </Row>
          <div className="mt-1 flex gap-2">
            <input className="input font-mono !text-xs" readOnly value={info?.url ?? "Загрузка…"} onFocus={(e) => e.target.select()} />
            <button type="button" className="btn btn-accent flex-none !px-3" onClick={copy} title="Скопировать">
              <Copy size={15} />
            </button>
            <a className="btn flex-none !px-3" href={info?.url} target="_blank" rel="noreferrer" title="Открыть">
              <ExternalLink size={15} />
            </a>
          </div>
          <div className="mt-3 flex flex-wrap items-center gap-3">
            <button type="button" className="btn !py-2 text-xs" onClick={sendTest}>
              <FlaskConical size={14} style={{ color: "var(--accent-2)" }} /> Тестовое сообщение
            </button>
            <span className="text-xs" style={{ color: "var(--muted)" }}>
              В OBS: «Источник → Браузер», вставьте ссылку, ширина 500, высота 600.
            </span>
          </div>
        </Panel>

        <Panel title="Пресеты оформления" icon={Palette}>
          <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
            {WIDGET_PRESETS.map((p) => {
              const active = w.theme === p.id;
              return (
                <button
                  key={p.id}
                  type="button"
                  onClick={() => { setW(p.patch); toast(`Пресет «${p.name}» применён`); }}
                  className="group relative overflow-hidden rounded-xl p-3 text-left transition-transform hover:-translate-y-0.5"
                  style={{
                    background: "var(--panel-2)",
                    border: `1px solid ${active ? "var(--accent)" : "var(--border)"}`,
                    boxShadow: active ? "0 0 0 1px var(--accent), 0 8px 24px var(--glow)" : "none",
                  }}
                >
                  <div className="mb-2 flex h-10 flex-col justify-center gap-1 rounded-lg px-2" style={{ background: p.swatch.bg, border: "1px solid rgba(128,128,128,.2)" }}>
                    <span className="block h-1.5 w-3/5 rounded-full" style={{ background: p.swatch.accent }} />
                    <span className="block h-1.5 w-4/5 rounded-full opacity-70" style={{ background: p.swatch.text }} />
                  </div>
                  <div className="flex items-center justify-between text-xs font-semibold">
                    {p.name}
                    {active && <Check size={13} style={{ color: "var(--accent)" }} />}
                  </div>
                </button>
              );
            })}
          </div>
        </Panel>

        <Panel title="Содержимое" icon={Layers}>
          <Segmented
            label="Стиль сообщений"
            value={w.style}
            onChange={(v) => setW({ style: v })}
            options={[
              { value: "clean", label: "Чистый" },
              { value: "compact", label: "Компакт" },
              { value: "bubbles", label: "Пузыри" },
            ]}
          />
          <Segmented
            label="Направление"
            desc="Куда движутся новые сообщения"
            value={w.dir}
            onChange={(v) => setW({ dir: v })}
            options={[
              { value: "up", label: "Снизу вверх" },
              { value: "down", label: "Сверху вниз" },
            ]}
          />
          <Slider label="Максимум сообщений" value={w.maxMessages} min={1} max={20} onChange={(v) => setW({ maxMessages: v })} />
          <Slider label="Время показа" desc="После — сообщение исчезает" value={w.duration} min={2} max={30} format={(v) => `${v} c`} onChange={(v) => setW({ duration: v })} />
          <Toggle label="Иконка площадки" value={w.showPlatform} onChange={(v) => setW({ showPlatform: v })} />
          <Toggle label="Время сообщения" value={w.showTime} onChange={(v) => setW({ showTime: v })} />
        </Panel>

        <Panel title="Оформление" icon={Brush}>
          <Select
            label="Тема"
            value={w.theme}
            onChange={(v) => setW({ theme: v })}
            options={WIDGET_PRESETS.map((p) => ({ value: p.id, label: p.name }))}
          />
          <Slider label="Размер текста" value={w.fontSize} min={11} max={28} format={(v) => `${v} px`} onChange={(v) => setW({ fontSize: v })} />
          <Slider label="Прозрачность подложки" value={w.bgOpacity} min={0} max={100} format={(v) => `${v}%`} onChange={(v) => setW({ bgOpacity: v })} />
          <Slider label="Скругление" value={w.radius} min={0} max={28} format={(v) => `${v} px`} onChange={(v) => setW({ radius: v })} />
          <Select label="Эффект появления" value={w.effect} onChange={(v) => setW({ effect: v as WidgetConfig["effect"] })} options={FX_LIST.map((f) => ({ value: f.id, label: f.name }))} />
          <Slider label="Скорость эффекта" value={w.effectDuration} min={0.1} max={1.5} step={0.02} format={(v) => `${v.toFixed(2)} c`} onChange={(v) => setW({ effectDuration: v })} />
          <Toggle label="Рамка сообщений" value={w.border} onChange={(v) => setW({ border: v })} />
          <Toggle label="Тень" value={w.shadow} onChange={(v) => setW({ shadow: v })} />
          <div className="my-1 h-px" style={{ background: "var(--border)" }} />
          <ColorInput label="Цвет текста" value={w.textColor} onChange={(v) => setW({ textColor: v })} />
          <ColorInput label="Цвет ников" value={w.nameColor} onChange={(v) => setW({ nameColor: v })} />
          <ColorInput label="Цвет подложки" value={w.bgColor} onChange={(v) => setW({ bgColor: v })} />
          <div className="py-2">
            <div className="mb-1 text-sm font-medium">Фоновая картинка</div>
            <input className="input" placeholder="https://… (URL изображения)" value={w.bgImage} onChange={(e) => setW({ bgImage: e.target.value })} />
          </div>
        </Panel>
      </div>

      {/* Предпросмотр — всегда раскрыт (ТЗ §14.1) */}
      <div className="xl:sticky xl:top-3">
        <Panel title="Предпросмотр · живой" icon={Eye} collapsible={false}>
          <div
            className="relative overflow-hidden rounded-xl p-3 checker"
            style={{
              minHeight: 340,
              background:
                "repeating-conic-gradient(rgba(128,128,128,.12) 0% 25%, transparent 0% 50%) 0 0 / 22px 22px, linear-gradient(160deg, #1a2030, #0d1017)",
            }}
          >
            <div className="flex flex-col justify-end gap-2" style={{ minHeight: 318, ...lookVars(widgetLike(w), w.effectDuration) }}>
              {ordered.length === 0 && (
                <div className="grid flex-1 place-items-center py-16 text-xs" style={{ color: "rgba(255,255,255,.4)" }}>
                  Ожидание сообщений из ленты…
                </div>
              )}
              {ordered.map((m) => (
                <WidgetMsgRow key={m.id} msg={m} fx={w.effect} opts={{ style: w.style, showPlatform: w.showPlatform, showTime: w.showTime }} />
              ))}
            </div>
          </div>
          <div className="mt-2 text-xs leading-snug" style={{ color: "var(--muted)" }}>
            Предпросмотр полностью совпадает с картинкой в OBS — тот же рендер и те же настройки.
          </div>
        </Panel>
      </div>
    </div>
  );
}
