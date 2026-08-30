# Changelog

## YawaChatHub v4.0.0

### Добавлено
- Восстановлен desktop-проект: C# WPF/WebView2 (окно, оверлей, SAPI5, WidgetServer, хоткеи, трей, коннекторы), NSIS-установщик и статичный OBS-виджет
- Единая лента сообщений Twitch, YouTube Live, VK Video Live, Kick, TikTok Live
- Озвучка стандартными голосами Windows (SAPI5), SSML с принудительным ru-RU
- OBS-виджет по статичной ссылке, оформление по соединению
- Игровой оверлей: drag, сквозные клики, фиксация позиции
- Глобальные горячие клавиши с переназначением

### Исправлено
- CI: ParserError на шаге «Синхронизировать версию» — экранирование кавычек вынесено в scripts/sync-version.ps1
- CI: `npm ci` падал с EUSAGE из-за отсутствия package-lock.json — установка зависимостей теперь устойчива (npm ci → иначе npm install)
- CI: сборка приведена к Next.js (проверка `.next` вместо `dist/index.html`), релизный архив из standalone-вывода
- CI: desktop-шаги (C#/NSIS) выполняются только при наличии desktop/ — сборка не падает без него
- CI: `next build` падал с «DATABASE_URL is required» на Collecting page data — подключение к БД стало ленивым (пул создаётся при первом запросе, а не при импорте модуля)
- CI: релиз выходил без exe — шаги desktop/NSIS ссылались на несуществующий `desktop/`; workflow теперь авто-детектит desktop (`hashFiles('desktop/**')`) и перед C#/NSIS дополнительно собирает Vite single-file `dist/index.html`
