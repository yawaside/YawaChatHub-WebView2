import { useEffect, useState } from "react";
import { Crosshair, Lock, Power, Unlock } from "lucide-react";
import type { OverlayConfig } from "../../lib/types";
import { isDesktop, openExternal, overlayBridge } from "../../lib/bridge";
import { OVERLAY_STYLES } from "../../lib/presets";
import { StyleGallery } from "./Presets";
import { CheckRow, Chip, Collapsible, Slider, ToggleRow } from "./ui";

interface Props {
  cfg: OverlayConfig;
  patch: (c: OverlayConfig) => void;
  toast: (t: string) => void;
  hotkey?: string;
}



export default function OverlayPanel({ cfg, patch, toast, hotkey }: Props) {
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    if (isDesktop()) void overlayBridge.isVisible().then(setVisible);
  }, []);

  const apply = (c: OverlayConfig) => {
    patch(c);
    overlayBridge.set(c);
  };

  const toggleOverlay = async () => {
    if (isDesktop()) {
      const v = await overlayBridge.toggle();
      setVisible(v);
      toast(v ? "Оверлей показан" : "Оверлей скрыт");
    } else {
      openExternal(`${location.origin}${location.pathname}#/overlay`);
      toast("Оверлей открыт в новой вкладке");
    }
  };

  return (
    <div className="flex flex-col gap-2">
      {/* показать/скрыть — главное действие */}
      <div
        className="flex flex-wrap items-center gap-2 rounded-xl border px-3 py-2"
        style={{ background: "var(--dw-panel)", borderColor: "var(--dw-line)" }}
      >
        <button
          onClick={toggleOverlay}
          className="flex cursor-pointer items-center gap-2 rounded-lg px-3 py-1.5 text-[12px] font-bold text-white transition-all hover:brightness-110"
          style={{ background: visible ? "#ef4444" : "var(--dw-accent)" }}
        >
          <Power size={13} /> {visible ? "Скрыть" : "Показать"}
        </button>
        <span className="text-[11px]" style={{ color: "var(--dw-dim)" }}>
          {hotkey ? (
            <>
              Быстрый вызов:{" "}
              <b className="font-mono" style={{ color: "var(--dw-accent-2)" }}>
                {hotkey}
              </b>{" "}
              — изменить в разделе «Хоткеи»
            </>
          ) : (
            "Назначьте клавишу в разделе «Хоткеи»"
          )}
        </span>
      </div>

      <StyleGallery styles={OVERLAY_STYLES} cfg={cfg} onApply={(s) => apply({ ...cfg, ...s })} />

      <Collapsible title="Подстроить размер" hint={`${cfg.fontSize}px · ${cfg.maxMessages} строк`}>
        <div className="flex flex-col gap-2">
          <div>
            <label className="mb-1 block text-[11px]" style={{ color: "var(--dw-dim)" }}>Размер текста</label>
            <Slider value={cfg.fontSize} min={10} max={30} step={1} onChange={(v) => apply({ ...cfg, fontSize: v })} format={(v) => `${v}px`} />
          </div>
          <div>
            <label className="mb-1 block text-[11px]" style={{ color: "var(--dw-dim)" }}>Сообщений видно</label>
            <Slider value={cfg.maxMessages} min={1} max={20} step={1} onChange={(v) => apply({ ...cfg, maxMessages: v })} format={(v) => `${v}`} />
          </div>
          <div>
            <label className="mb-1 block text-[11px]" style={{ color: "var(--dw-dim)" }}>Интервал между сообщениями</label>
            <Slider value={cfg.spacing} min={0} max={16} step={1} onChange={(v) => apply({ ...cfg, spacing: v })} format={(v) => `${v}px`} />
          </div>
          <div>
            <label className="mb-1 block text-[11px]" style={{ color: "var(--dw-dim)" }}>
              Прозрачность подложки — текст не затрагивается
            </label>
            <Slider value={cfg.backdropOpacity} min={0} max={1} step={0.01} onChange={(v) => apply({ ...cfg, backdropOpacity: v })} format={(v) => `${Math.round(v * 100)}%`} />
          </div>
        </div>
      </Collapsible>

      <Collapsible title="Окно оверлея" hint={cfg.locked ? "зафиксировано" : "можно двигать"}>
        <div className="flex flex-col gap-2">
          {/* Оверлей всегда перетаскивается мышью — отдельные настройки
              позиции (углы и отступы) убраны как лишние. */}
          <div className="flex gap-1.5">
            <button
              onClick={() => {
                apply({ ...cfg, locked: !cfg.locked, clickThrough: !cfg.locked });
                toast(cfg.locked ? "Оверлей можно двигать мышью" : "Позиция зафиксирована");
              }}
              className="flex flex-1 cursor-pointer items-center justify-center gap-1.5 rounded-lg py-1.5 text-[11.5px] font-semibold text-white"
              style={{ background: cfg.locked ? "var(--dw-accent)" : "#f59e0b" }}
            >
              {cfg.locked ? <><Lock size={12} /> Зафиксировано</> : <><Unlock size={12} /> Зафиксировать</>}
            </button>
            <button
              onClick={() => { overlayBridge.center(); toast("Оверлей в центре экрана"); }}
              className="cursor-pointer rounded-lg px-3 py-1.5 text-[11.5px]"
              style={{ background: "var(--dw-input)", color: "var(--dw-dim)" }}
            >
              <Crosshair size={12} className="mr-1 inline" /> В центр
            </button>
          </div>
          <p className="text-[10.5px] leading-snug" style={{ color: "var(--dw-dim)" }}>
            Тяните оверлей мышью в любое место экрана. После фиксации окно перестаёт
            перехватывать мышь и не мешает игре.
          </p>

          <div>
            <label className="mb-1 block text-[11px]" style={{ color: "var(--dw-dim)" }}>Ширина панели</label>
            <Slider value={cfg.width} min={240} max={900} step={10} onChange={(v) => apply({ ...cfg, width: v })} format={(v) => `${v}px`} />
          </div>
          <div className="flex items-center gap-1.5">
            <span className="text-[11px]" style={{ color: "var(--dw-dim)" }}>Новые сообщения:</span>
            <Chip active={cfg.growth === "down"} onClick={() => apply({ ...cfg, growth: "down" })}>вниз ↓</Chip>
            <Chip active={cfg.growth === "up"} onClick={() => apply({ ...cfg, growth: "up" })}>вверх ↑</Chip>
          </div>
        </div>
      </Collapsible>

      <Collapsible title="Панель площадок" hint={cfg.showStats ? (cfg.statsPos === "bottom" ? "снизу" : "сверху") : "скрыта"}>
        <div className="flex flex-col gap-1">
          <ToggleRow label="Показывать панель" checked={cfg.showStats} onChange={(v) => apply({ ...cfg, showStats: v })} />
          <div className="flex items-center gap-1.5 py-1">
            <span className="text-[11px]" style={{ color: "var(--dw-dim)" }}>Расположение:</span>
            <Chip active={cfg.statsPos === "bottom"} onClick={() => apply({ ...cfg, statsPos: "bottom" })}>снизу ↓</Chip>
            <Chip active={cfg.statsPos === "top"} onClick={() => apply({ ...cfg, statsPos: "top" })}>сверху ↑</Chip>
          </div>
          <CheckRow label="Сумма донатов первым элементом" checked={cfg.showDonations} onChange={(v) => apply({ ...cfg, showDonations: v })} />
          <CheckRow label="Скрывать неподключённые площадки" checked={cfg.hideDisconnected} onChange={(v) => apply({ ...cfg, hideDisconnected: v })} />
          <CheckRow label="Счётчик зрителей" checked={cfg.showViewers} onChange={(v) => apply({ ...cfg, showViewers: v })} />
          <CheckRow label="Компактный вид" checked={cfg.statsCompact} onChange={(v) => apply({ ...cfg, statsCompact: v })} />
        </div>
      </Collapsible>

      <Collapsible title="Содержимое и поведение" hint={cfg.eventsOnly ? "только события" : "весь чат"}>
        <div className="flex flex-col gap-1">
          <CheckRow label="Иконка площадки" checked={cfg.showPlatform} onChange={(v) => apply({ ...cfg, showPlatform: v })} />
          <CheckRow label="Время сообщения" checked={cfg.showTime} onChange={(v) => apply({ ...cfg, showTime: v })} />
          <CheckRow label="Значки (мод, саб, vip)" checked={cfg.showBadges} onChange={(v) => apply({ ...cfg, showBadges: v })} />
          <CheckRow label="Стикеры и эмоуты" checked={cfg.showEmotes} onChange={(v) => apply({ ...cfg, showEmotes: v })} />
          <ToggleRow label="Только события" hint="донаты, сабы, рейды" checked={cfg.eventsOnly} onChange={(v) => apply({ ...cfg, eventsOnly: v })} />
          <ToggleRow label="Скрывать старые сообщения" checked={cfg.autoFade} onChange={(v) => apply({ ...cfg, autoFade: v })} />
          {cfg.autoFade && (
            <div className="pt-1">
              <label className="mb-1 block text-[11px]" style={{ color: "var(--dw-dim)" }}>Время жизни строки</label>
              <Slider value={cfg.fadeAfterSec} min={5} max={180} step={5} onChange={(v) => apply({ ...cfg, fadeAfterSec: v })} format={(v) => `${v} сек`} />
            </div>
          )}
        </div>
      </Collapsible>
    </div>
  );
}
