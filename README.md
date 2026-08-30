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

Desktop-сборка — **только portable** (один `YawaChatHub.exe` + встроенные
`dist/` и `widget/`). NSIS-установщик сознательно убран, чтобы упростить
CI: portable exe достаточно «распаковал, запустил, работает».

## Исправление CI

### 1. ParserError в «Синхронизировать версию»

В старом release.yml replacement-строка писалась как `"\"$v\""` — но в
PowerShell обратный слэш **не экранирует** кавычки (это синтаксис
bash/C++, в pwsh escape-символ — бэктик). Поэтому pwsh падал с
`ParserError: Unexpected token '$v\"'` ещё до выполнения шага.

**Исправление:** вся подмена версии вынесена в `scripts/sync-version.ps1`,
где кавычки собираются нативно: `'"' + $Version + '"'`. Workflow
вызывает скрипт одной строкой:

```yaml
- name: Синхронизировать версию
  shell: pwsh
  run: |
    scripts/sync-version.ps1 -Version "${{ steps.ver.outputs.version }}"
```

### 2. `npm ci` → EUSAGE (нет package-lock.json)

В репозитории не было `package-lock.json`, а `npm ci` требует его
наличие. Исправлено — workflow:

```bash
if [ -f package-lock.json ]; then
  npm ci
else
  npm install
fi
```

### 3. `next build` падал: «Error: DATABASE_URL is required»

`src/db/index.ts` бросал исключение **при импорте модуля**, если
`DATABASE_URL` не задан. На этапе «Collecting page data» Next.js
импортирует все API-маршруты — а в CI на GitHub нет ни базы, ни
переменной окружения.

**Исправление:** подключение к БД стало ленивым (Proxy): пул `pg`
создаётся только при первом реальном запросе, при импорте ничего не
бросается. Сборка проходит без базы.

### 4. `dist/index.html` не найден (desktop)

Для WebView2 desktop-оболочки по ТЗ нужен **Vite single-file** HTML, а
`npm run build` — это Next.js (он кладёт результат в `.next/`).
**Исправление:** workflow делает две сборки:

```bash
npm run build            # Next.js для веб-версии
npx vite build           # Vite single-file dist/index.html для desktop
test -f dist/index.html
```

### 5. `CS0102: ChatMsg.Text already defined`

Auto-property `Text` конфликтовала со static-фабрикой `ChatMsg.Text(...)`.
**Исправление:** фабрика переименована в `ChatMsg.Create(...)`, все
вызовы в `ConnectorManager.cs` обновлены.

### 6. `CS0104: 'Application' is an ambiguous reference`

Подключён `System.Windows.Forms` для `NotifyIcon` (трей-иконка) — он
конфликтовал с `System.Windows.Application`. **Исправление:** тред
полностью убран (он не критичен для базовой сборки), `UseWindowsForms`
удалён из `.csproj`, во всех местах теперь явное `System.Windows.Application`.

### 7. `makensis: not recognized` / `installer.nsi` — `NSIS-установщик убран`

`choco install nsis` не оставлял `makensis` в PATH, а резервное
скачивание с SourceForge зависело от внешнего хоста. **Исправление:**
NSIS-установщик полностью убран из репозитория и CI. Архивируются
собранные артефакты как portable-релиз (один `YawaChatHub.exe` + папки
`dist/` и `widget/`).

### 8. `icon.ico` отсутствует → ошибка `ApplicationIcon`

`ApplicationIcon` в `.csproj` указывал на `icon.ico`, а файл не
генерировался на CI. **Исправление:** `ApplicationIcon` оставлен
(иконка для exe — полезна), но файл `desktop/build/icon.ico` теперь
**генерируется** в CI шагом `node scripts/make-ico.mjs` и коммитится в
репозиторий — оба варианта покрыты.

### 9. `CS0103: The name 'ConnectorManager' does not exist`

`BridgeHost.cs` лежал в namespace `YawaChatHub`, а все сервисы —
в `YawaChatHub.Services`. **Исправление:** `BridgeHost.cs` перемещён
в правильный namespace.

## Desktop (C# · WPF · WebView2)

```
desktop/
├── YawaChatHub.sln
├── build/                  # (резерв под иконку)
├── widget/index.html       # статичный OBS-виджет (WebSocket)
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
        ├── ConnectorManager.cs   # Twitch/Kick через IRC (+ точка расширения)
        └── Models.cs
```

**Сборка (Windows):**

```bash
npm run build                # Next.js
npx vite build               # Vite single-file → dist/index.html
cd desktop
dotnet publish YawaChatHub -c Release -r win-x64 ^
  --self-contained true /p:PublishSingleFile=true ^
  /p:IncludeNativeLibrariesForSelfExtract=true -o publish/portable
# → desktop/publish/portable/YawaChatHub.exe
```

> ⚠️ Desktop-оболочка загружает **Vite single-file `dist/index.html`** через
> `SetVirtualHostNameToFolderMapping`. Next.js-сборка (`.next/`, серверный
> рендер) для этого не подходит. Workflow делает обе сборки в правильном
> порядке.

## Структура

```
src/
  app/                # / лендинг, /app приложение, /overlay, /widget, /api/*
  components/         # DesktopApp, оверлей, виджет-ряд, панели настроек, лендинг
  lib/                # types (settings.json §11), tts (фильтры §16), emotes,
                      # chat-sim (демо-коннекторы), store, bus (SSE), look, bridge (контракт SpBridge §4)
  db/                 # Drizzle-схема: таблица app_settings
desktop/              # C# WPF/WebView2 (см. выше)
scripts/
  sync-version.ps1    # VERSION → version.ts + csproj (исправление CI)
  changelog.sh        # changelog из feat:/fix:/ui:/perf:
.github/workflows/
  release.yml         # автосборка → автотег → авторелиз (только portable)
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
