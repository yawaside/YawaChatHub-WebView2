"use client";

// ─── Вкладка «Озвучка» (ТЗ §9, §16) ─────────────────────────────────────────
import { useEffect, useMemo, useState } from "react";
import { Filter, ListChecks, Pause, Play, SkipForward, Square, Volume2, AudioLines, Quote } from "lucide-react";
import { useApp } from "@/lib/store";
import { buildPhrase, listVoices } from "@/lib/tts";
import { makeMsg } from "@/lib/chat-sim";
import { Panel, Row, Slider, Toggle, Select, WordListEditor, NumBadge, IconBtn } from "../ui";
import type { TtsFilters } from "@/lib/types";

const BAN_PRESETS: { name: string; words: string[] }[] = [
  { name: "Грубость и токсик", words: ["дурак", "идиот", "тупой", "дебил", "лох", "нуб", "рак"] },
  { name: "Реклама и спам", words: ["подпишись", "заработок", "крипта", "ставки", "промокод", "казино", "инвестиции"] },
  { name: "Политика", words: ["политика", "выборы", "правительство", "парламент"] },
];

export function VoicePanel() {
  const { settings, patch, ttsQueue, ttsPaused, ttsPauseToggle, ttsSkip, ttsClear, speakTest, toast } = useApp();
  const tts = settings.tts;
  const f = tts.filters;
  const [voices, setVoices] = useState<{ name: string; voiceURI: string; lang: string }[]>([]);

  useEffect(() => {
    const load = () => setVoices(listVoices());
    load();
    if ("speechSynthesis" in window) {
      speechSynthesis.addEventListener("voiceschanged", load);
      return () => speechSynthesis.removeEventListener("voiceschanged", load);
    }
  }, []);

  const setT = (p: Record<string, unknown>) => patch({ tts: p });
  const setF = (p: Partial<TtsFilters>) => patch({ tts: { filters: p } });

  const sampleMsg = useMemo(() => makeMsg("twitch", "demo", "ivan_play", "Привет! Отличный стрим, продолжай!"), []);
  const phrasePreview = buildPhrase(sampleMsg, sampleMsg.text, tts);

  const applyPreset = (words: string[], name: string) => {
    const merged = [...f.banWords];
    for (const w of words) if (!merged.some((x) => x.toLowerCase() === w.toLowerCase())) merged.push(w);
    setF({ banWords: merged });
    toast(`Пресет «${name}» добавлен в банворды`);
  };

  return (
    <div className="flex flex-col gap-3">
      <Panel title="Озвучка сообщений" icon={Volume2}>
        <Toggle label="Озвучивать новые сообщения" desc="Стандартные голоса Windows (SAPI5), принудительно ru-RU" value={tts.enabled} onChange={(v) => setT({ enabled: v })} />
        <div className="my-1 h-px" style={{ background: "var(--border)" }} />
        <Row label="Очередь" desc="FIFO, максимум 12 — старое выбрасывается">
          <NumBadge tone={ttsQueue > 8 ? "accent" : "muted"}>{ttsQueue} в очереди</NumBadge>
          <IconBtn icon={ttsPaused ? Play : Pause} title={ttsPaused ? "Продолжить" : "Пауза"} onClick={ttsPauseToggle} active={ttsPaused} />
          <IconBtn icon={SkipForward} title="Пропустить текущее" onClick={ttsSkip} />
          <IconBtn icon={Square} title="Очистить очередь" onClick={ttsClear} danger />
        </Row>
        <Slider label="Скорость речи" value={tts.rate} min={0.5} max={2} step={0.05} format={(v) => `×${v.toFixed(2)}`} onChange={(v) => setT({ rate: v })} />
        <Slider label="Громкость" value={tts.volume} min={0} max={1} step={0.05} format={(v) => `${Math.round(v * 100)}%`} onChange={(v) => setT({ volume: v })} />
        <Select
          label="Голос"
          desc={voices.length === 0 ? "В браузере список голосов может быть пуст" : `Доступно голосов: ${voices.length}`}
          value={tts.voiceURI}
          onChange={(v) => setT({ voiceURI: v })}
          options={[
            { value: "", label: "Системный по умолчанию" },
            ...voices.map((v) => ({ value: v.voiceURI, label: `${v.name} (${v.lang})` })),
          ]}
        />
        <Toggle
          label="Озвучка через OBS"
          desc="Звук отдаётся в Browser Source — громкостью управляет OBS, локально не проигрывается"
          value={tts.obsTts}
          onChange={(v) => setT({ obsTts: v })}
        />
      </Panel>

      <Panel title="Шаблон фразы" icon={Quote}>
        <Toggle label="Называть автора" value={tts.template.author} onChange={(v) => setT({ template: { author: v } })} />
        <Toggle label="Называть площадку" value={tts.template.platform} onChange={(v) => setT({ template: { platform: v } })} />
        <Toggle label="Читать текст сообщения" value={tts.template.text} onChange={(v) => setT({ template: { text: v } })} />
        <div className="mt-2 rounded-xl p-3" style={{ background: "var(--panel-2)", border: "1px dashed var(--border-2)" }}>
          <div className="mb-2 text-xs font-semibold uppercase tracking-wider" style={{ color: "var(--muted)" }}>Предпросмотр шаблона</div>
          <div className="text-sm italic leading-snug">«{phrasePreview}»</div>
          <button type="button" className="btn btn-accent mt-3 !py-2 text-xs" onClick={() => speakTest()}>
            <AudioLines size={14} /> Прослушать
          </button>
        </div>
      </Panel>

      {/* Особое правило ТЗ §14.1: сам блок не сворачивается,
          сворачивается только подблок «Пресеты банвордов». */}
      <Panel title="Фильтры озвучки" icon={Filter} collapsible={false}>
        <div className="grid gap-x-6 sm:grid-cols-2">
          <Toggle label="Ссылки → «ссылка»" value={f.links} onChange={(v) => setF({ links: v })} />
          <Toggle label="Игнорировать команды (! /)" value={f.commands} onChange={(v) => setF({ commands: v })} />
          <Toggle label="Игнорировать эмодзи и смайлы" value={f.emoji} onChange={(v) => setF({ emoji: v })} />
          <Toggle label="Анти-повтор (45 с)" value={f.dedupe} onChange={(v) => setF({ dedupe: v })} />
          <Toggle label="Сжимать повторы (аааа→аа)" value={f.squashRepeats} onChange={(v) => setF({ squashRepeats: v })} />
          <Toggle label="Убирать мусорные символы" value={f.stripSymbols} onChange={(v) => setF({ stripSymbols: v })} />
        </div>
        <div className="my-1 h-px" style={{ background: "var(--border)" }} />
        <div className="grid gap-x-6 sm:grid-cols-2">
          <Slider label="Макс. длина" value={f.maxLen} min={40} max={600} step={10} format={(v) => `${v} симв.`} onChange={(v) => setF({ maxLen: v })} />
          <Slider label="Мин. длина" value={f.minLen} min={1} max={10} format={(v) => `${v} симв.`} onChange={(v) => setF({ minLen: v })} />
          <Slider label="Лимит в минуту" value={f.perMin} min={1} max={60} format={(v) => `${v} фраз`} onChange={(v) => setF({ perMin: v })} />
          <Slider label="Макс. CAPS" value={f.maxCapsRatio} min={10} max={100} format={(v) => `${v}%`} onChange={(v) => setF({ maxCapsRatio: v })} />
        </div>
        <div className="my-1 h-px" style={{ background: "var(--border)" }} />
        <WordListEditor label="Банворды" desc="Фразы с этими словами не озвучиваются" words={f.banWords} onChange={(w) => setF({ banWords: w })} />
        <WordListEditor label="Маскировка слов" desc="Заменяются на «пип»" words={f.maskWords} onChange={(w) => setF({ maskWords: w })} />
        <WordListEditor label="Игнорировать авторов" words={f.banAuthors} onChange={(w) => setF({ banAuthors: w })} placeholder="Ник автора…" />
        <WordListEditor label="Белый список авторов" desc="Если не пуст — озвучиваются только они" words={f.allowAuthors} onChange={(w) => setF({ allowAuthors: w })} placeholder="Ник автора…" />

        <Panel title="Пресеты банвордов" icon={ListChecks} className="mt-3">
          <div className="flex flex-col gap-2">
            {BAN_PRESETS.map((p) => (
              <div key={p.name} className="flex items-center justify-between gap-3 rounded-xl px-3 py-2" style={{ background: "var(--panel-2)" }}>
                <div>
                  <div className="text-sm font-medium">{p.name}</div>
                  <div className="text-xs" style={{ color: "var(--muted)" }}>{p.words.join(", ")}</div>
                </div>
                <button type="button" className="btn flex-none !px-3 !py-1.5 text-xs" onClick={() => applyPreset(p.words, p.name)}>
                  Добавить
                </button>
              </div>
            ))}
          </div>
        </Panel>
      </Panel>
    </div>
  );
}
