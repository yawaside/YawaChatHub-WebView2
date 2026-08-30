# YawaChatHub

Единая лента чатов **Twitch, YouTube Live, VK Video Live, Kick и TikTok Live** для стримеров под Windows — с озвучкой стандартными голосами Windows (SAPI5), виджетом для OBS и игровым оверлеем поверх игры.

## Режимы (один код)

| Режим | Адрес | Назначение |
|---|---|---|
| Сайт-витрина | `/` | Лендинг с живым демо |
| Приложение | `/app` (`#/app` в desktop) | Полноценное окно приложения |
| Оверлей | `/overlay` (`#/overlay` в desktop) | Прозрачное окно поверх игры |
| OBS-виджет | `/widget?token=…` | Статичная ссылка для Browser Source |

Веб-сборка (Next.js + PostgreSQL): настройки хранятся на сервере,
config/chat доезжают до OBS-виджета и оверлея по потоку (SSE) — аналог
`WidgetServer.cs` из desktop-версии. В браузере работает полное демо:
мок-коннекторы эмулируют живой чат пяти площадок, озвучка идёт через
Web Speech API с теми же фильтрами (в desktop — SAPI5 + SSML ru-RU).

## Исправление CI

### 1. ParserError в «Синхронизировать версию»

**Симптом:**

```
ParserError: ...ps1:3
(Get-Content src/version.ts) -replace '"[\d.]+"', "\"$v\"" | Set-Content ...
Unexpected token '$v\""' in expression or statement.
```

**Причина:** обратный слэш **не является** escape-символом в PowerShell.
Строка `"\"$v\""` не парсится — pwsh падает ещё до выполнения шага.

**Исправление:** вся подмена версии вынесена в `scripts/sync-version.ps1`,
где кавычки собираются нативно (`'"' + $Version + '"'` или `` "`"$Version`"" ``).
Workflow вызывает скрипт одной строкой без опасной интерполяции:

```yaml
- name: Синхронизировать версию
  shell: pwsh
  run: |
    scripts/sync-version.ps1 -Version "${{ steps.ver.outputs.version }}"
```

Если когда-либо понадобится inline-вариант в `pwsh`, корректные формы:

```powershell
(Get-Content src/version.ts) -replace '"[\d.]+"', ('"' + $v + '"') | Set-Content src/version.ts
(Get-Content src/version.ts) -replace '"[\d.]+"', "`"$v`""             | Set-Content src/version.ts
```

### 2. `npm ci` → EUSAGE (нет package-lock.json)

**Симптом:**

```
npm error code EUSAGE
npm error The `npm ci` command can only install with an existing package-lock.json
```

**Причина:** в репозиторий не закоммичен `package-lock.json`, а `npm ci`
требует его наличие и без него не запускается.

**Исправление:** установка зависимостей в workflow стала устойчивой:

```bash
if [ -f package-lock.json ]; then
  npm ci --no-audit --no-fund
else
  npm install --no-audit --no-fund   # создаст package-lock.json
fi
```

**Рекомендация:** закоммитьте сгенерированный `package-lock.json` —
тогда сборки будут детерминированными (npm ci), и кэш в CI заработает.

### 3. `next build` падал: «Error: DATABASE_URL is required»

**Симптом:**

```
Collecting page data ...
Error: DATABASE_URL is required
> Build error occurred
Error: Failed to collect page data for /api/health (или /api/settings)
```

**Причина:** `src/db/index.ts` бросал исключение **при импорте модуля**, если
`DATABASE_URL` не задан. На этапе «Collecting page data» Next.js импортирует
все API-маршруты — а в CI на GitHub нет ни базы, ни переменной окружения.

**Исправление:** подключение к БД стало ленивым (Proxy): пул `pg` создаётся
только при первом реальном запросе, при импорте ничего не бросается.
Сборка проходит без базы; `DATABASE_URL` нужен лишь на работающем сервере.

### 4. Сборка Next.js вместо Vite

Проект собирается Next.js, поэтому проверка `test -f dist/index.html`
(из Vite-версии ТЗ) заменена на `test -d .next`. Для релиза Next.js собирается
с `output: "standalone"` и упаковывается в `YawaChatHub-web-<версия>.tar.gz`.
Шаги desktop (C#/NSIS) выполняются только при наличии `desktop/` в репозитории.

## Структура

```
src/
  app/                # / лендинг, /app приложение, /overlay, /widget, /api/*
  components/         # DesktopApp, оверлей, виджет-ряд, панели настроек, лендинг
  lib/                # types (settings.json §11), tts (фильтры §16), emotes,
                      # chat-sim (демо-коннекторы), store, bus (SSE), look, bridge (контракт SpBridge §4)
  db/                 # Drizzle-схема: таблица app_settings
scripts/
  sync-version.ps1    # VERSION → version.ts + csproj (исправление CI)
  changelog.sh        # changelog из feat:/fix:/ui:/perf:
.github/workflows/
  release.yml         # автосборка → автотег → авторелиз (исправлен)
  ci.yml              # проверка PR
VERSION               # единственный источник версии
```

## Локальная разработка

```bash
npm install
npx drizzle-kit push    # применить схему БД
npm run dev             # http://localhost:3000
```

Проверки перед пушем: `npx tsc --noEmit` и `npm run build`.

## Принципы (из ТЗ)

- Любое изменение настройки → немедленный `settings.patch()`, кнопок «Сохранить» нет.
- Все настроечные панели свёрнуты по умолчанию, кроме предпросмотров, «Ссылка для OBS» и «Горячие клавиши».
- Оверлей: `opacity` окна всегда `1`, прозрачна только подложка CSS, текст непрозрачный.
- Виджет OBS: URL статичен, оформление только через соединение; предпросмотр ↔ рабочий виджет — один рендер.
- Changelog: только `feat:`, `fix:`, `ui:`, `perf:`.
- Без телеметрии и нейросетевых голосов — только системные SAPI5.

## Лицензия

MIT
