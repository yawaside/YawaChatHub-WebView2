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

### 4. Portable EXE запускался с пустым окном

**Причины:**

1. Релиз содержал только `YawaChatHub.exe`, но `dist/index.html` оставался
   внешним Content-файлом и не попадал к пользователю.
2. WebView2 открывал `app://index.html`, хотя virtual-host mapping обслуживает
   HTTP/HTTPS URL.
3. Безрамочный React-тайтлбар не отправлял команды в WPF, поэтому крестик лишь
   показывал уведомление и приложение приходилось завершать диспетчером задач.
4. Сетевые службы запускались до показа главного окна и могли задержать UI.

**Исправление:**

- Vite single-file HTML и OBS-виджет встроены в assembly как
  `EmbeddedResource`, поэтому релиз действительно состоит из одного EXE.
- При запуске `WebAssets` извлекает ресурсы в
  `%LocalAppData%\YawaChatHub\Web` и WebView2 открывает
  `https://yawachat.invalid/index.html#/app`.
- Добавлены нативные команды окна через `chrome.webview.postMessage`:
  закрытие, сворачивание, разворачивание и drag.
- До успешной навигации отображается нативный экран загрузки; при ошибке он
  показывает диагноз, кнопки «Повторить» и «Закрыть приложение».
- Фоновые службы стартуют после окна и не блокируют UI.
- NSIS полностью исключён: workflow публикует только portable
  `YawaChatHub.exe`.
- Portable EXE использует отдельный режим `desktop`: `ChatSimulator` в нём
  выключен, `settings.json` и SAPI5 идут через C# IPC-мост, а в ленту
  допускаются только `sp:chat` от нативных коннекторов. Интерактивная
  симуляция существует лишь внутри демо-рамки лендинга.

## Desktop (C# · WPF · WebView2 · portable)

```
desktop/
├── YawaChatHub.sln
├── build/icon.ico                # генерируется scripts/make-ico.mjs
├── widget/index.html             # встраивается в portable EXE
└── YawaChatHub/
    ├── Program.cs                # окно первым, службы после запуска UI
    ├── MainWindow.xaml(.cs)      # WebView2 + тёмный экран диагностики, без белой полосы
    ├── OverlayWindow.xaml(.cs)   # прозрачный оверлей
    └── Services/
        ├── WebAssets.cs          # EmbeddedResource → LocalAppData
        ├── BridgeHost.cs         # COM-объект `sp` для JS
        ├── SettingsService.cs    # settings.json + автосохранение
        ├── TtsService.cs         # SAPI5 + SSML ru-RU
        ├── WidgetServer.cs       # HTTP + WebSocket для OBS
        ├── HotkeyManager.cs      # RegisterHotKey
        ├── TrayManager.cs        # иконка в трее
        ├── ConnectorManager.cs   # коннекторы площадок
        └── Models.cs
```

**Сборка (Windows):**

```bash
npm install
npm run build                    # Next.js веб-версия
npx vite build                   # dist/index.html для WebView2
node scripts/make-ico.mjs        # desktop/build/icon.ico
cd desktop
dotnet publish YawaChatHub -c Release -r win-x64 \
  --self-contained true /p:PublishSingleFile=true \
  /p:IncludeNativeLibrariesForSelfExtract=true -o publish/portable
```

Итоговый релиз содержит только `YawaChatHub.exe`; `dist/index.html` уже
встроен внутрь файла и извлекается автоматически.

## Структура

```
src/
  app/                # / лендинг, /app приложение, /overlay, /widget, /api/*
  components/         # DesktopApp, панели настроек, лендинг
  lib/                # types, tts, emotes, store, look, native bridge
  db/                 # Drizzle-схема
desktop/              # C# WPF/WebView2 portable
scripts/
  sync-version.ps1    # VERSION → version.ts + csproj
  changelog.sh        # changelog из feat:/fix:/ui:/perf:
  make-ico.mjs        # генератор desktop/build/icon.ico
.github/workflows/
  release.yml         # Next.js + Vite + portable EXE + релиз
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
