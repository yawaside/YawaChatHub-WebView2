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

### 4. «не собираются NSIS и desktop» — релиз был, а exe нет

**Симптом:** веб-сборка и релиз проходят, но шаги «Сборка desktop (C#)» и
NSIS-установщик не выполняются (или падают).

**Причина:** репозиторий был Next.js-версией **без директории `desktop/`**,
а `release.yml` продолжал ссылаться на `desktop/…`, `installer.nsi` и
`test -f dist/index.html` (это артефакты Vite-версии ТЗ).

**Исправление:**
1. Проверка `test -f dist/index.html` удалена — она ломала Next.js-сборку.
2. Шаги desktop (C# + NSIS) теперь выполняются **только если `desktop/` есть**
   (`if: hashFiles('desktop/**') != ''`), а сам шаг дополнительно проверяет
   наличие `dist/index.html`.
3. Директория `desktop/` **восстановлена** — полный C# WPF/WebView2-проект
   (окно, оверлей, SAPI5-озвучка, WidgetServer, хоткеи, трей, коннекторы)
   плюс NSIS-установщик и статичный OBS-виджет.
4. Релиз публикует `YawaChatHub.exe` + `YawaChatHub-Setup.exe`, только если
   desktop-сборка прошла (`has_desktop`), иначе — релиз без exe-артефактов.

## Desktop (C# · WPF · WebView2 · NSIS)

Каталог `desktop/` содержит нативное приложение по ТЗ:

```
desktop/
├── YawaChatHub.sln
├── installer.nsi                 # NSIS-установщик (русский, ярлыки, удаление)
├── build/icon.ico                # генерируется scripts/make-ico.mjs
├── widget/index.html             # статичный OBS-виджет (WebSocket)
└── YawaChatHub/
    ├── Program.cs                # запуск, TLS-обход, первый запуск
    ├── MainWindow.xaml(.cs)      # безрамочное окно + WebView2 (app://index.html#/app)
    ├── OverlayWindow.xaml(.cs)   # прозрачный оверлей (WS_EX_LAYERED/TRANSPARENT)
    └── Services/
        ├── BridgeHost.cs         # COM-объект `sp` для JS
        ├── SettingsService.cs    # settings.json + автосохранение
        ├── TtsService.cs         # SAPI5 + SSML ru-RU (очередь ≤12)
        ├── WidgetServer.cs       # HTTP + WebSocket для OBS
        ├── HotkeyManager.cs      # RegisterHotKey (глобально)
        ├── TrayManager.cs        # иконка в трее
        ├── ConnectorManager.cs   # Twitch/Kick через IRC (+ точка расширения)
        └── Models.cs
```

**Сборка (Windows):**

```bash
npm run build                    # 1. Vite → dist/index.html (single-file)
node scripts/make-ico.mjs        # 2. иконка
cd desktop
dotnet publish YawaChatHub -c Release -r win-x64 \
  --self-contained true /p:PublishSingleFile=true \
  /p:IncludeNativeLibrariesForSelfExtract=true -o publish/portable
makensis installer.nsi "/DVERSION=4.0.0" "/DPORTABLE=publish/portable/YawaChatHub.exe"
```

> ⚠️ Desktop-оболочка загружает **Vite single-file `dist/index.html`** через
> `SetVirtualHostNameToFolderMapping`. Next.js-сборка (`.next/`, серверный
> рендер) для этого не подходит — фронтенд для desktop собирается Vite
> (`vite build` → `dist/index.html`). В веб-варианте desktop-шаги просто
> пропускаются.

## Структура

```
src/
  app/                # / лендинг, /app приложение, /overlay, /widget, /api/*
  components/         # DesktopApp, оверлей, виджет-ряд, панели настроек, лендинг
  lib/                # types (settings.json §11), tts (фильтры §16), emotes,
                      # chat-sim (демо-коннекторы), store, bus (SSE), look, bridge (контракт SpBridge §4)
  db/                 # Drizzle-схема: таблица app_settings
desktop/              # C# WPF/WebView2 + NSIS (см. выше)
scripts/
  sync-version.ps1    # VERSION → version.ts + csproj (исправление CI)
  changelog.sh        # changelog из feat:/fix:/ui:/perf:
  make-ico.mjs        # генератор desktop/build/icon.ico
.github/workflows/
  release.yml         # автосборка → автотег → авторелиз (авто-детект desktop)
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
