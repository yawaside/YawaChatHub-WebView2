import { LayoutList, Palette } from "lucide-react";
import type { ChatViewConfig, FeedDensity } from "../../lib/types";
import { FEED_PRESETS } from "../../lib/presets";
import { PresetGrid } from "./Presets";
import { Box, CheckRow, FontPicker, Slider, SpacingSlider, ToggleRow } from "./ui";

const DENSITIES: { id: FeedDensity; name: string; desc: string }[] = [
  { id: "cozy", name: "Комфортный", desc: "крупные карточки, воздух между строками" },
  { id: "standard", name: "Стандартный", desc: "баланс плотности и читаемости" },
  { id: "compact", name: "Компактный", desc: "плотные строки для быстрого чата" },
  { id: "tight", name: "Без отступов", desc: "текст прижат к краям без полей" },
];

interface Props {
  view: ChatViewConfig;
  patch: (c: ChatViewConfig) => void;
}

export default function ChatViewPanel({ view, patch }: Props) {
  return (
    <div className="flex flex-col gap-3">
      <Box title="Варианты оформления" icon={<Palette size={14} style={{ color: "var(--dw-dim)" }} />}>
        <PresetGrid presets={FEED_PRESETS} cfg={view} onApply={(p) => patch({ ...view, ...p })} />
      </Box>

      <div className="grid grid-cols-1 gap-3 xl:grid-cols-2">
      <Box title="Плотность ленты" icon={<LayoutList size={14} style={{ color: "var(--dw-dim)" }} />}>
        <div className="grid grid-cols-2 gap-2">
          {DENSITIES.map((d) => {
            const on = view.density === d.id;
            return (
              <button
                key={d.id}
                onClick={() => patch({ ...view, density: d.id })}
                className="cursor-pointer rounded-xl border p-3 text-left transition-all"
                style={{
                  background: on ? "color-mix(in srgb, var(--dw-accent) 12%, transparent)" : "var(--dw-input)",
                  borderColor: on ? "var(--dw-accent)" : "transparent",
                }}
              >
                <div className="pb-2">
                  <div
                    className="rounded"
                    style={{
                      background: "var(--dw-panel2)",
                      height: { cozy: 34, standard: 26, compact: 18, tight: 12 }[d.id],
                      marginBottom: { cozy: 8, standard: 5, compact: 3, tight: 1 }[d.id] * 2,
                    }}
                  />
                  <div className="rounded" style={{ background: "var(--dw-panel2)", height: { cozy: 34, standard: 26, compact: 18, tight: 12 }[d.id] }} />
                </div>
                <p className="text-[12px] font-semibold" style={{ color: on ? "var(--dw-accent-2)" : "var(--dw-text)" }}>
                  {d.name}
                </p>
                <p className="text-[10.5px] leading-snug" style={{ color: "var(--dw-dim)" }}>
                  {d.desc}
                </p>
              </button>
            );
          })}
        </div>
      </Box>

      <Box title="Оформление">
        <div className="flex flex-col gap-1">
          <div className="pb-2">
            <label className="mb-1 block text-[11px]" style={{ color: "var(--dw-dim)" }}>
              Размер текста
            </label>
            <Slider value={view.fontSize} min={10} max={22} step={1} onChange={(v) => patch({ ...view, fontSize: v })} format={(v) => `${v}px`} />
          </div>
          <div className="pb-2">
            <SpacingSlider value={view.spacing} onChange={(v) => patch({ ...view, spacing: v })} />
          </div>
          <div className="pb-2">
            <FontPicker value={view.fontFamily} onChange={(id) => patch({ ...view, fontFamily: id })} />
          </div>
          <ToggleRow label="Подложка под сообщениями" checked={view.backdrop} onChange={(v) => patch({ ...view, backdrop: v })} />
          <ToggleRow label="Анимация появления" checked={view.animations} onChange={(v) => patch({ ...view, animations: v })} />
          <ToggleRow label="Цветные никнеймы" checked={view.accentNick} onChange={(v) => patch({ ...view, accentNick: v })} />
          <div className="border-t pt-2" style={{ borderColor: "var(--dw-line)" }}>
            <p className="pb-1 text-[11px]" style={{ color: "var(--dw-dim)" }}>
              Элементы строки
            </p>
            <CheckRow label="Время сообщения" checked={view.showTime} onChange={(v) => patch({ ...view, showTime: v })} />
            <CheckRow label="Значки (мод, саб, vip)" checked={view.showBadges} onChange={(v) => patch({ ...view, showBadges: v })} />
            <CheckRow label="Иконка площадки" checked={view.showPlatform} onChange={(v) => patch({ ...view, showPlatform: v })} />
          </div>
        </div>
      </Box>
      </div>
    </div>
  );
}
