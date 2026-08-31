"use client";

// ─── Остальные вкладки: лента, горячие клавиши, интерфейс, о программе ──────
import { useEffect, useState } from "react";
import { Check, Info, Keyboard, LayoutList, MoonStar, RotateCcw, ScrollText } from "lucide-react";
import { useApp } from "@/lib/store";
import {
  DEFAULT_HOTKEYS, FX_LIST, HOTKEY_ACTIONS, THEMES,
  type ChatViewConfig, type HotkeyAction,
} from "@/lib/types";
import { comboFromEvent, cn } from "@/lib/utils";
import { Panel, Row, Segmented, Select, Slider, Toggle } from "../ui";

export function ChatViewPanel() {
  const { settings, patch } = useApp();
  const cv = settings.chatView;
  const set = (p: Partial<ChatViewConfig>) => patch({ chatView: p });
  return (
    <Panel title="Оформление ленты" icon={LayoutList}>
      <Segmented
        label="Стиль"
        value={cv.style}
        onChange={(v) => set({ style: v })}
        options={[
          { value: "classic", label: "Классика" },
          { value: "compact", label: "Компакт" },
          { value: "cards", label: "Карточки" },
        ]}
      />
      <Slider label="Размер текста" value={cv.fontSize} min={12} max={20} format={(v) => `${v} px`} onChange={(v) => set({ fontSize: v })} />
      <Slider label="Интервал строк" value={cv.rowGap} min={2} max={14} format={(v) => `${v} px`} onChange={(v) => set({ rowGap: v })} />
      <Slider label="Скругление" value={cv.radius} min={0} max={24} format={(v) => `${v} px`} onChange={(v) => set({ radius: v })} />
      <Toggle label="Иконка площадки" value={cv.showPlatform} onChange={(v) => set({ showPlatform: v })} />
      <Toggle label="Время сообщения" value={cv.showTime} onChange={(v) => set({ showTime: v })} />
      <Toggle label="Бейджи (мод, подписчик)" value={cv.showBadges} onChange={(v) => set({ showBadges: v })} />
      <Select label="Эффект появления" value={cv.messageEffect} onChange={(v) => set({ messageEffect: v as ChatViewConfig["messageEffect"] })} options={FX_LIST.map((f) => ({ value: f.id, label: f.name }))} />
      <Slider label="Скорость эффекта" value={cv.effectDuration} min={0.1} max={1.5} step={0.02} format={(v) => `${v.toFixed(2)} c`} onChange={(v) => set({ effectDuration: v })} />
    </Panel>
  );
}

export function HotkeysPanel() {
  const { settings, patch, toast } = useApp();
  const [recording, setRecording] = useState<HotkeyAction | null>(null);

  useEffect(() => {
    if (!recording) return;
    const onKey = (e: KeyboardEvent) => {
      e.preventDefault();
      e.stopPropagation();
      if (e.key === "Escape") return setRecording(null);
      const combo = comboFromEvent(e);
      if (!combo) return;
      const conflict = (Object.entries(settings.hotkeys) as [HotkeyAction, string][]).find(
        ([a, c]) => c === combo && a !== recording,
      );
      if (conflict) {
        toast(`Сочетание уже занято: «${HOTKEY_ACTIONS.find((x) => x.id === conflict[0])?.name}»`);
        setRecording(null);
        return;
      }
      patch({ hotkeys: { [recording]: combo } });
      toast(`Назначено: ${combo}`);
      setRecording(null);
    };
    window.addEventListener("keydown", onKey, true);
    return () => window.removeEventListener("keydown", onKey, true);
  }, [recording, settings.hotkeys, patch, toast]);

  return (
    <Panel
      title="Горячие клавиши"
      icon={Keyboard}
      collapsible={false}
      right={
        <button
          type="button"
          className="btn !px-3 !py-1.5 text-xs"
          onClick={(e) => {
            e.stopPropagation();
            patch({ hotkeys: { ...DEFAULT_HOTKEYS } });
            toast("Горячие клавиши сброшены на значения по умолчанию");
          }}
        >
          <RotateCcw size={13} /> Сбросить всё
        </button>
      }
    >
      <div className="mb-3 rounded-xl px-3 py-2 text-xs leading-snug" style={{ background: "var(--panel-2)", color: "var(--muted)" }}>
        В desktop-сборке работают глобально через WinAPI RegisterHotKey — даже со свёрнутым окном.
        В браузере — пока вкладка активна. Кликните по полю и нажмите новое сочетание, Esc — отмена.
      </div>
      <div className="flex flex-col">
        {HOTKEY_ACTIONS.map((a) => {
          const rec = recording === a.id;
          return (
            <Row key={a.id} label={a.name}>
              <button
                type="button"
                onClick={() => setRecording(rec ? null : a.id)}
                className={cn("kbd min-w-[150px] justify-center transition-all", rec && "animate-pulse")}
                style={rec ? { borderColor: "var(--accent)", color: "var(--accent)", boxShadow: "0 0 0 3px color-mix(in srgb, var(--accent) 20%, transparent)" } : undefined}
              >
                {rec ? "Нажмите сочетание…" : settings.hotkeys[a.id]}
              </button>
            </Row>
          );
        })}
      </div>
    </Panel>
  );
}

export function InterfacePanel() {
  const { settings, patch } = useApp();
  return (
    <div className="flex flex-col gap-3">
      <Panel title="Тема интерфейса" icon={MoonStar}>
        <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
          {THEMES.map((t) => {
            const active = settings.theme === t.id;
            return (
              <button
                key={t.id}
                type="button"
                onClick={() => patch({ theme: t.id })}
                className="rounded-xl p-3 text-left transition-transform hover:-translate-y-0.5"
                style={{
                  background: "var(--panel-2)",
                  border: `1px solid ${active ? "var(--accent)" : "var(--border)"}`,
                  boxShadow: active ? "0 0 0 1px var(--accent), 0 8px 24px var(--glow)" : "none",
                }}
              >
                <div className="mb-2 flex h-11 overflow-hidden rounded-lg" style={{ border: "1px solid rgba(128,128,128,.25)" }}>
                  <span className="h-full w-1/2" style={{ background: t.swatch[0] }} />
                  <span className="h-full w-1/2" style={{ background: t.swatch[1] }} />
                  <span className="h-full w-1/4" style={{ background: t.swatch[2] }} />
                </div>
                <div className="flex items-center justify-between text-sm font-semibold">
                  {t.name}
                  {active && <Check size={14} style={{ color: "var(--accent)" }} />}
                </div>
              </button>
            );
          })}
        </div>
      </Panel>

      <Panel title="Поведение окна" icon={ScrollText}>
        <Toggle label="Сворачивать в трей при закрытии" desc="По умолчанию выключено" value={settings.closeToTray} onChange={(v) => patch({ closeToTray: v })} />
        <Toggle label="Сворачивать в трей кнопкой «минус»" value={settings.minimizeToTray} onChange={(v) => patch({ minimizeToTray: v })} />
        <Toggle label="Запускать свёрнутым в трей" value={settings.startHidden} onChange={(v) => patch({ startHidden: v })} />
      </Panel>
    </div>
  );
}

export function AboutPanel() {
  return (
    <Panel title="О программе" icon={Info} collapsible={false}>
      <div className="flex items-start gap-4">
        <div
          className="grid size-14 flex-none place-items-center rounded-2xl"
          style={{ background: "linear-gradient(135deg, var(--accent), var(--accent-2))", boxShadow: "0 10px 30px var(--glow)" }}
        >
          <svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
            <path d="M8 9h8M8 12h5" />
          </svg>
        </div>
        <div>
          <div className="text-lg font-bold tracking-tight">YawaChatHub</div>
          <p className="mt-1 text-sm leading-relaxed" style={{ color: "var(--muted)" }}>
            Единая лента сообщений Twitch, YouTube Live, VK Video Live, Kick и TikTok Live
            с озвучкой, виджетом для OBS и игровым оверлеем.
          </p>
        </div>
      </div>
    </Panel>
  );
}
