import { useEffect, useState } from "react";
import {
  AlignLeft, Blend, Crosshair, Eye, Gamepad2, LayoutPanelTop, Lock, MousePointerClick,
  Move, Power, Timer, Unlock,
} from "lucide-react";
import type { OverlayConfig, OverlayCorner } from "../../lib/types";
import { isDesktop, openExternal, overlayBridge } from "../../lib/bridge";

import { OVERLAY_PRESETS } from "../../lib/presets";
import { PresetGrid } from "./Presets";
import { Box, CheckRow, Chip, EffectPicker, FontPicker, Slider, SpacingSlider, ToggleRow } from "./ui";

interface Props {
  cfg: OverlayConfig;
  patch: (c: OverlayConfig) => void;
  toast: (t: string) => void;
  hotkey?: string;
}

const CORNERS: { id: OverlayCorner; label: string }[] = [
  { id: "top-left", label: "↖ сверху слева" },
  { id: "top-right", label: "↗ сверху справа" },
  { id: "bottom-left", label: "↙ снизу слева" },
  { id: "bottom-right", label: "↘ снизу справа" },
];

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
      toast(v ? "Оверлей показан поверх игры" : "Оверлей скрыт");
    } else {
      openExternal(`${location.origin}${location.pathname}#/overlay`);
      toast("Оверлей открыт в новой вкладке (предпросмотр)");
    }
  };

  return (
    <div className="grid grid-cols-1 gap-3 overflow-y-auto xl:grid-cols-2">
      {/* ---------- состояние и активация ---------- */}
      <Box title="Оверлей поверх игры" icon={<Gamepad2 size={14} style={{ color: "var(--dw-dim)" }} />} className="xl:col-span-2">
        <div className="flex flex-wrap items-center gap-2">
          <button
            onClick={toggleOverlay}
            className="flex cursor-pointer items-center gap-2 rounded-xl px-4 py-2 text-[12.5px] font-bold text-white transition-all hover:brightness-110"
            style={{ background: visible ? "#ef4444" : "var(--dw-accent)" }}
          >
            <Power size={14} /> {visible ? "Скрыть оверлей" : "Показать оверлей"}
          </button>
          <span
            className="flex items-center gap-1.5 rounded-lg px-2.5 py-1.5 text-[11.5px]"
            style={{ background: "var(--dw-input)", color: "var(--dw-dim)" }}
          >
            Горячая клавиша:
            <b className="font-mono" style={{ color: "var(--dw-accent-2)" }}>
              {hotkey || "не назначена"}
            </b>
            <span className="opacity-70">— работает и поверх игры</span>
          </span>
          <ToggleRow
            label=""
            checked={cfg.enabled}
            onChange={(v) => {
              apply({ ...cfg, enabled: v });
              if (!v && isDesktop()) {
                overlayBridge.close();
                setVisible(false);
              }
            }}
          />
          <span className="-ml-1 text-[11.5px]" style={{ color: "var(--dw-dim)" }}>
            автозапуск вместе с приложением
          </span>
        </div>
      </Box>

      {/* ---------- готовые варианты оформления ---------- */}
      <Box title="Варианты оформления" icon={<Blend size={14} style={{ color: "var(--dw-dim)" }} />} className="xl:col-span-2">
        <PresetGrid presets={OVERLAY_PRESETS} cfg={cfg} onApply={(p) => apply({ ...cfg, ...p })} />
      </Box>

      {/* ---------- подложка и читаемость ---------- */}
      <Box title="Подложка и читаемость" icon={<Blend size={14} style={{ color: "var(--dw-dim)" }} />}>
        <div className="flex flex-col gap-3">
          <div>
            <label className="mb-1 flex items-center justify-between text-[11px]" style={{ color: "var(--dw-dim)" }}>
              <span>Прозрачность подложки</span>
              <span>{cfg.backdropOpacity === 0 ? "полностью прозрачная" : "текст не затрагивается"}</span>
            </label>
            <Slider
              value={cfg.backdropOpacity}
              min={0}
              max={1}
              step={0.01}
              onChange={(v) => apply({ ...cfg, backdropOpacity: v })}
              format={(v) => `${Math.round(v * 100)}%`}
            />
          </div>
          <div>
            <label className="mb-1 block text-[11px]" style={{ color: "var(--dw-dim)" }}>
              Прозрачность текста (отдельно)
            </label>
            <Slider
              value={cfg.textOpacity}
              min={0.3}
              max={1}
              step={0.01}
              onChange={(v) => apply({ ...cfg, textOpacity: v })}
              format={(v) => `${Math.round(v * 100)}%`}
            />
          </div>
          <div>
            <label className="mb-1 block text-[11px]" style={{ color: "var(--dw-dim)" }}>
              Скругление подложки
            </label>
            <Slider value={cfg.radius} min={0} max={20} step={1} onChange={(v) => apply({ ...cfg, radius: v })} format={(v) => `${v}px`} />
          </div>
          <CheckRow label="Тень текста (читаемость на светлой игре)" checked={cfg.shadowText} onChange={(v) => apply({ ...cfg, shadowText: v })} />
          <CheckRow label="Цветная полоса площадки слева" checked={cfg.accentBar} onChange={(v) => apply({ ...cfg, accentBar: v })} />
          {/* предпросмотр — в правой колонке настроек, здесь не дублируем */}
        </div>
      </Box>

      {/* ---------- эффект появления ---------- */}
      <Box title="Эффект появления сообщений" icon={<Blend size={14} style={{ color: "var(--dw-dim)" }} />}>
        <EffectPicker effect={cfg.effect} speed={cfg.effectSpeed} onChange={(v) => apply({ ...cfg, ...v })} />
      </Box>

      {/* ---------- размещение ---------- */}
      <Box title="Размещение на экране" icon={<Move size={14} style={{ color: "var(--dw-dim)" }} />}>
        <div className="flex flex-col gap-3">
          <ToggleRow
            label="Свободное размещение"
            hint="перетаскивайте оверлей мышью в любую точку экрана"
            checked={cfg.freePosition}
            onChange={(v) => {
              apply({ ...cfg, freePosition: v, corner: v ? "free" : "top-left", clickThrough: v ? false : cfg.clickThrough, locked: false });
              toast(v ? "Тяните оверлей мышью, затем «Зафиксировать»" : "Возврат к привязке по углу");
            }}
          />

          {cfg.freePosition ? (
            <>
              <div className="flex flex-wrap gap-1.5">
                <button
                  onClick={() => {
                    apply({ ...cfg, locked: !cfg.locked, clickThrough: !cfg.locked ? true : false });
                    toast(cfg.locked ? "Оверлей разблокирован — можно двигать" : "Позиция зафиксирована, клик-сквозь включён");
                  }}
                  className="flex flex-1 cursor-pointer items-center justify-center gap-1.5 rounded-lg py-2 text-[12px] font-semibold text-white"
                  style={{ background: cfg.locked ? "var(--dw-accent)" : "#f59e0b" }}
                >
                  {cfg.locked ? <><Lock size={13} /> Зафиксировано</> : <><Unlock size={13} /> Зафиксировать позицию</>}
                </button>
                <button
                  onClick={() => {
                    overlayBridge.center();
                    toast("Оверлей возвращён в центр экрана");
                  }}
                  className="cursor-pointer rounded-lg px-3 py-2 text-[12px]"
                  style={{ background: "var(--dw-input)", color: "var(--dw-dim)" }}
                >
                  <Crosshair size={13} className="mr-1 inline" /> В центр
                </button>
              </div>
              <p className="text-[10.5px] leading-snug" style={{ color: "var(--dw-dim)" }}>
                В режиме перемещения клик-сквозь временно отключается — иначе окно нельзя было бы схватить мышью.
                После «Зафиксировать» оверлей снова пропускает клики в игру.
              </p>
            </>
          ) : (
            <>
              <div className="grid grid-cols-2 gap-1.5">
                {CORNERS.map((c) => (
                  <Chip key={c.id} active={cfg.corner === c.id} onClick={() => apply({ ...cfg, corner: c.id })}>
                    {c.label}
                  </Chip>
                ))}
              </div>
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="mb-1 block text-[11px]" style={{ color: "var(--dw-dim)" }}>Отступ по X</label>
                  <Slider value={cfg.offsetX} min={0} max={400} step={2} onChange={(v) => apply({ ...cfg, offsetX: v })} format={(v) => `${v}`} />
                </div>
                <div>
                  <label className="mb-1 block text-[11px]" style={{ color: "var(--dw-dim)" }}>Отступ по Y</label>
                  <Slider value={cfg.offsetY} min={0} max={400} step={2} onChange={(v) => apply({ ...cfg, offsetY: v })} format={(v) => `${v}`} />
                </div>
              </div>
            </>
          )}

          <div>
            <label className="mb-1 block text-[11px]" style={{ color: "var(--dw-dim)" }}>Ширина панели</label>
            <Slider value={cfg.width} min={240} max={900} step={10} onChange={(v) => apply({ ...cfg, width: v })} format={(v) => `${v}px`} />
          </div>
          <div className="flex items-center gap-1.5">
            <span className="text-[11px]" style={{ color: "var(--dw-dim)" }}>Новые сообщения:</span>
            <Chip active={cfg.growth === "down"} onClick={() => apply({ ...cfg, growth: "down" })}>вниз ↓</Chip>
            <Chip active={cfg.growth === "up"} onClick={() => apply({ ...cfg, growth: "up" })}>вверх ↑</Chip>
          </div>
          <ToggleRow
            label="Клик-сквозь"
            hint="окно не перехватывает мышь — игра не теряет фокус"
            checked={cfg.clickThrough}
            onChange={(v) => apply({ ...cfg, clickThrough: v })}
          />
        </div>
      </Box>

      {/* ---------- панель площадок ---------- */}
      <Box title="Панель площадок в оверлее" icon={<LayoutPanelTop size={14} style={{ color: "var(--dw-dim)" }} />}>
        <div className="flex flex-col gap-1">
          <ToggleRow label="Показывать панель" hint="донаты и статусы подключённых площадок" checked={cfg.showStats} onChange={(v) => apply({ ...cfg, showStats: v })} />

          <div className="flex items-center gap-1.5 py-1.5">
            <span className="text-[11px]" style={{ color: "var(--dw-dim)" }}>Расположение:</span>
            <Chip active={cfg.statsPos === "bottom"} onClick={() => apply({ ...cfg, statsPos: "bottom" })}>снизу ↓</Chip>
            <Chip active={cfg.statsPos === "top"} onClick={() => apply({ ...cfg, statsPos: "top" })}>сверху ↑</Chip>
          </div>

          <ToggleRow
            label="Сумма донатов первым элементом"
            hint="оригинальная иконка DonationAlerts, сумма за сессию"
            checked={cfg.showDonations}
            onChange={(v) => apply({ ...cfg, showDonations: v })}
          />
          <ToggleRow
            label="Скрывать неподключённые площадки"
            hint="в панели остаются только реально подключённые каналы"
            checked={cfg.hideDisconnected}
            onChange={(v) => apply({ ...cfg, hideDisconnected: v })}
          />
          <ToggleRow label="Счётчик зрителей у площадок" checked={cfg.showViewers} onChange={(v) => apply({ ...cfg, showViewers: v })} />
          <ToggleRow label="Компактный вид панели" hint="без числа донатов, только сумма" checked={cfg.statsCompact} onChange={(v) => apply({ ...cfg, statsCompact: v })} />
          <p className="pt-1 text-[10.5px] leading-snug" style={{ color: "var(--dw-dim)" }}>
            У каждой подключённой площадки показываются иконка и индикатор эфира: зелёная точка — в эфире,
            приглушённая — офлайн. Данные обновляются из коннекторов в реальном времени.
          </p>
        </div>
      </Box>

      {/* ---------- содержимое строки ---------- */}
      <Box title="Содержимое ленты оверлея" icon={<AlignLeft size={14} style={{ color: "var(--dw-dim)" }} />}>
        <div className="flex flex-col gap-3">
          <div>
            <label className="mb-1 block text-[11px]" style={{ color: "var(--dw-dim)" }}>Размер текста</label>
            <Slider value={cfg.fontSize} min={10} max={30} step={1} onChange={(v) => apply({ ...cfg, fontSize: v })} format={(v) => `${v}px`} />
          </div>
          <SpacingSlider value={cfg.spacing} onChange={(v) => apply({ ...cfg, spacing: v })} />
          <FontPicker value={cfg.fontFamily} onChange={(id) => apply({ ...cfg, fontFamily: id })} />
          <div>
            <label className="mb-1 block text-[11px]" style={{ color: "var(--dw-dim)" }}>Сообщений видно</label>
            <Slider value={cfg.maxMessages} min={1} max={20} step={1} onChange={(v) => apply({ ...cfg, maxMessages: v })} format={(v) => `${v}`} />
          </div>
          <div className="border-t pt-2" style={{ borderColor: "var(--dw-line)" }}>
            <CheckRow label="Иконка площадки" checked={cfg.showPlatform} onChange={(v) => apply({ ...cfg, showPlatform: v })} />
            <CheckRow label="Время сообщения" checked={cfg.showTime} onChange={(v) => apply({ ...cfg, showTime: v })} />
            <CheckRow label="Значки (мод, саб, vip)" checked={cfg.showBadges} onChange={(v) => apply({ ...cfg, showBadges: v })} />
            <CheckRow label="Стикеры и эмоуты" checked={cfg.showEmotes} onChange={(v) => apply({ ...cfg, showEmotes: v })} />
          </div>
          <ToggleRow
            label="Только события"
            hint="донаты, сабы, рейды — без обычного чата"
            checked={cfg.eventsOnly}
            onChange={(v) => apply({ ...cfg, eventsOnly: v })}
          />
        </div>
      </Box>

      {/* ---------- авто-скрытие ---------- */}
      <Box title="Исчезновение сообщений" icon={<Timer size={14} style={{ color: "var(--dw-dim)" }} />}>
        <ToggleRow
          label="Скрывать старые сообщения"
          hint="строка исчезает через заданное время"
          checked={cfg.autoFade}
          onChange={(v) => apply({ ...cfg, autoFade: v })}
        />
        {cfg.autoFade && (
          <div className="pt-2">
            <label className="mb-1 block text-[11px]" style={{ color: "var(--dw-dim)" }}>Время жизни строки</label>
            <Slider value={cfg.fadeAfterSec} min={5} max={180} step={5} onChange={(v) => apply({ ...cfg, fadeAfterSec: v })} format={(v) => `${v} сек`} />
          </div>
        )}
        <div className="mt-3 flex items-start gap-2 rounded-lg p-2.5" style={{ background: "var(--dw-input)" }}>
          <MousePointerClick size={13} className="mt-0.5 shrink-0" style={{ color: "var(--dw-dim)" }} />
          <p className="text-[10.5px] leading-snug" style={{ color: "var(--dw-dim)" }}>
            В WebView2-сборке оверлей — отдельное окно поверх всех окон. Все настройки применяются
            мгновенно, без перезапуска. Показ/скрытие — кнопкой выше или горячей клавишей.
          </p>
        </div>
      </Box>

      <Box title="Как это работает" icon={<Eye size={14} style={{ color: "var(--dw-dim)" }} />} className="xl:col-span-2">
        <ul className="flex flex-col gap-1.5 text-[11.5px] leading-relaxed" style={{ color: "var(--dw-dim)" }}>
          <li>• <b style={{ color: "var(--dw-text)" }}>Прозрачность подложки</b> меняет только фон плашки — текст, ники и иконки остаются полностью непрозрачными.</li>
          <li>• Подложка постоянная: при значении <b style={{ color: "var(--dw-text)" }}>0%</b> она полностью прозрачна, а строки читаются за счёт тени текста.</li>
          <li>• Панель площадок берёт данные из активных коннекторов: сумма донатов, иконка, эфир и зрители.</li>
          <li>• Хоткей показа/скрытия настраивается в разделе «Хоткеи» и регистрируется системно (работает в полноэкранной игре).</li>
        </ul>
      </Box>
    </div>
  );
}
