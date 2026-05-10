# SC Native — состояние проекта

## Проект
WPF SoundCloud клиент на .NET 8. Папка: `D:\My soundcloud\SoundCloudClient\SoundCloudClient`

## Что сделано (все сессии)
- Кастомный UI без системного хрома (WindowStyle=None + WindowChrome) с закруглёнными углами (CornerRadius=12)
- Сайдбар с навигацией (Главная/Поиск/Лайки/Библиотека), сворачивается до иконок (50px) с анимацией
- Поиск треков через SoundCloud API v2
- Плеер снизу (NAudio/MediaFoundationReader) с прогрессом и громкостью
- Иконка громкости из Material Design Icons (PackIcon) — меняется по уровню
- Локальные лайки и плейлисты (library.json в %LocalAppData%/MusicBox/)
- Кнопка ⋯ у каждого трека — контекстное меню (лайки, плейлисты, удаление)
- Загрузка обложек через HttpClient (с User-Agent) — Artwork свойство в Track с INotifyPropertyChanged
- Фоновая картинка (background.jpg) + градиент поверх (непрозрачный, alpha=0xFF)
- Настройки: цветовая тема (6 пресетов) + фоновое изображение
- Секции-заголовки в списке (IsSectionHeader) — разделитель между облачными/локальными плейлистами
- Разделители в сайдбаре видны в обоих режимах (свёрнутом/развёрнутом)
- Track.Artwork, DurationFormatted, PropertyChanged помечены [JsonIgnore] — не ломают сериализацию
- Все диалоговые окна в едином стиле приложения (WindowChrome, кастомный titlebar с "S" + название, акцент #CC7B3A, фон #0D0D0D)
- Закруглённые углы у всех окон (WindowChrome CornerRadius=12 + Border CornerRadius=12)
- Закруглённые artwork треков, обложка плеера, аватарка (OpacityMask с VisualBrush для реального скругления Image)
- Сохранение последнего трека и громкости в settings.json (восстановление при запуске, Play запускает сохранённый трек)
- 10-полосный эквалайзер (32, 64, 125, 250, 500, 1k, 2k, 4k, 8k, 16k Hz) с диапазоном ±12 dB
- Пресеты эквалайзера: Flat, Bass, Treble, Vocal, Loud
- Всплывающая панель EQ (Popup) с вертикальными слайдерами над кнопкой EQ в плеере
- Настройки EQ сохраняются в settings.json
- Аудио-цепочка: MediaFoundationReader → Pcm16BitToSampleProvider → EqSampleProvider (BiQuadFilter) → SampleToWaveProvider → WaveOutEvent
- **Эквалайзер: инверсия значений слайдеров** — ползунок вверх = +dB, вниз = −dB. Реализовано через `_eqGains[i] = -(float)sliders[i].Value` в EqSlider_ValueChanged, `sliders[i].Value = -gains[i]` в ApplyEqPreset и LoadSettings. Track в шаблоне EqVerticalSlider имеет IsDirectionReversed="True" (захардкожено)
- **Тексты треков через LRCLIB API** (lrclib.net) — бесплатный, без API-ключа, отдаёт plainLyrics + syncedLyrics (LRC формат с таймстампами)
- **Встроенная панель текстов** (не отдельное окно) — кнопка ♫ в плеере открывает панель вместо списка треков, сайдбар и плеер остаются
- **Двухколоночный лейаут панели текстов**: левая часть (2/5) — карточка трека (обложка 220x220, название, артист, мини-контролы prev/play/next, мини-прогресс), правая часть (3/5) — текст
- **Караоке-режим**: если есть syncedLyrics — парсинг LRC, таймер 200мс отслеживает текущую строку по `_audioReader.CurrentTime`
- **Анимации в караоке**: плавная смена цвета (ColorAnimation 350мс), плавное изменение размера шрифта (DoubleAnimation 300мс), плавный автоскролл (cubic ease-out 500мс через Dispatcher)
- **Клик по строке текста = перемотка** — `_audioReader.CurrentTime` устанавливается на таймстамп строки, строка сразу подсвечивается
- **Очистка названий для поиска текстов** (CleanTitle/CleanArtist) — убирает (Original Mix), (Radio Edit), feat./ft./featuring, - Remix, [Deluxe] и т.д. 4 этапа поиска: точный → search по artist+title → search только по title → оригинальные грязные данные
- Кнопка "◂ Назад" возвращает к списку треков, навигация по сайдбару тоже закрывает панель текстов
- **Асинхронное восстановление навигации** — `RestoreNavContextAsync` ждёт загрузку секции перед `LoadPlaylistTracks`, `RestoreLastTrack` с `_suppressSelectionPlay`
- **Genre поле в Track** — `item["genre"]?.ToString() ?? item["label"]?.ToString() ?? ""`, парсинг в 3 местах SoundCloudService
- **AI-рекомендации через Groq API** — `RecommendationService` анализирует лайки, нормализует жанры (~50 маппингов), 3-уровневый фоллбэк: Groq → SoundCloud /me/recommendations → жанровый поиск
- **GroqService** — API клиент (llama-3.3-70b-versatile), системный промпт "music expert", шлёт до 30 лайков, парсит JSON ответ
- **Локальные лайки в разделе "Лайки"** — облачные лайки сверху, разделитель "ЛОКАЛЬНЫЕ", локальные ниже (дедупликация по HashSet)
- **Рекомендации на главной** — `LoadHomeAsync()` загружает лайки если нужно, получает рекомендации, показывает жанровые теги
- **Приложение переименовано в "SoundCloud Native" / "SC Native"** — UI, Discord, User-Agent. Путь настроек остался MusicBox
- **Discord Rich Presence — УДАЛЕНО** (попробовали, не зашло, убрали полностью)
  - Был создан DiscordRpcService.cs с разными подходами: timestamps, свой таймер 15 сек, текстовый прогресс
  - Проблема: зелёная полоска Discord работает криво при частых обновлениях, текстовый таймер ограничен лимитом Discord ~15 сек
  - Удалены: DiscordRpcService.cs, NuGet пакет DiscordRichPresence, все вызовы из MainWindow
  - Discord Application Client ID `1500177770519597139` — можно использовать потом если захотим вернуть

## Известные проблемы
- Пресеты EQ — стандартные, пользователь скажет какие нужны — переделать
- LyricsWindow.xaml/.cs — больше не используется (тексты теперь встроены в MainWindow), можно удалить
- Groq API ключ захардкожен в GroqService.cs — лучше вынести в настройки (уже есть поле в SettingsWindow)

## Ключевые файлы
- `MainWindow.xaml` + `MainWindow.xaml.cs` — основной UI, плеер, EQ, встроенная панель текстов с караоке, рекомендации
- `EqSampleProvider.cs` — 10-полосный эквалайзер (ISampleProvider + BiQuadFilter)
- `LyricsService.cs` — клиент LRCLIB API (поиск текстов, очистка названий, 4 этапа поиска)
- `LyricsWindow.xaml/.cs` — отдельное окно текстов (УСТАРЕЛО, не используется, тексты теперь встроены в MainWindow)
- `SoundCloudService.cs` — API логика (client_id парсинг, поиск, /me, лайки/плейлисты, SearchByGenreAsync, GetRecommendationsAsync)
- `RecommendationService.cs` — жанровый анализ + Groq AI + жанровый поиск (3-уровневый фоллбэк)
- `GroqService.cs` — Groq API клиент (llama-3.3-70b-versatile, JSON парсинг рекомендаций)
- `LocalLibrary.cs` — локальное хранение (library.json)
- `SettingsWindow.xaml/.cs` — настройки (тема + фон + Groq API ключ)
- `WebView2LoginWindow.xaml/.cs` — вход через WebView2
- `LoginWindow.xaml/.cs` — ручной ввод OAuth токена
- `CsvImportWindow.xaml/.cs` — импорт треков из CSV
- `PlaylistPickerDialog.xaml/.cs` — выбор плейлиста
- `CreatePlaylistDialog.xaml/.cs` — создание плейлиста

## Данные
- `%LocalAppData%/MusicBox/library.json` — лайки и плейлисты
- `%LocalAppData%/MusicBox/settings.json` — oauth_token, color1/2/3, volume, last_track, eq_gains[10], groq_api_key, top_genres
- `%LocalAppData%/MusicBox/background.jpg` — фоновая картинка

## Архитектура панели текстов (встроена в MainWindow)
- Кнопка ♫ в плеере → `LyricsButton_Click` → `ShowLyricsPanel()`
- `ShowLyricsPanel()`: скрывает ContentHeader + TracksScroll, показывает LyricsPanel, заполняет карточку трека, вызывает `LoadLyricsAsync()`
- `LoadLyricsAsync()`: через LyricsService ищет текст, если есть syncedLyrics — парсит LRC через `ParseLrc()`, строит UI через `BuildSyncedLyricsUI()`, запускает `_lyricsSyncTimer`
- `_lyricsSyncTimer` (200мс): читает `_audioReader.CurrentTime`, ищет текущую строку, вызывает `UpdateLyricsHighlightAnimated()` + `LyricsAutoScroll()`
- `UpdateLyricsHighlightAnimated()`: ColorAnimation + DoubleAnimation для каждой строки (активная = оранжевая #CC7B3A 16pt, рядом = полупрозрачная, далеко = тусклая)
- `LyricsAutoScroll()`: плавный скролл через Dispatcher с cubic ease-out
- Клик по TextBlock строки → `_audioReader.CurrentTime = _lyricsLines[lineIndex].Time`
- `HideLyricsPanel()`: останавливает таймер, скрывает LyricsPanel, показывает ContentHeader + TracksScroll

## Что делали этой сессией
1. Исправляли частоту обновления Discord RPC (500мс → 1с → 15с)
2. Убирали зелёный таймер Discord (timestamps), добавляли текстовый прогресс "1:23 / 3:45"
3. Добавляли SeekTo при перемотке, ResetTrackKey при паузе/resume
4. Искали референсы на GitHub (zxcloli666/SoundCloud-Desktop — Tauri/Rust, использует ActivityType::Listening + timestamps)
5. Переписывали DiscordRpcService с собственным System.Timers.Timer (15 сек) вместо ProgressTimer
6. В итоге удалили Discord RPC полностью — не понравилось как работает
   - Удалён DiscordRpcService.cs
   - Удалён NuGet DiscordRichPresence
   - Убраны все вызовы из MainWindow.xaml.cs
7. **Баг: панель текстов не обновлялась при автопереключении трека** — когда трек заканчивался и включался следующий, текст и карточка оставались от прошлого трека
   - Добавлена проверка в `PlayTrack()`: если `LyricsPanel.Visibility == Visible` → вызывается `ShowLyricsPanel()` для обновления
   - Обновление `LyricsArtwork.Source` при асинхронной загрузке обложки нового трека
8. **Баг: пролаг текста при переключении трека** — старый текст мигал перед появлением нового
   - В `ShowLyricsPanel()` добавлена мгновенная очистка: `_lyricsSyncTimer?.Stop()`, `LyricsSyncedPanel.Children.Clear()`, `LyricsPlainText.Text = ""`, показ `LyricsLoadingPanel`
   - Теперь при смене трека сразу показывается спиннер загрузки, старый текст не мигает
9. **Кастомный WPF-установщик** — отдельный проект `Installer/` в том же solution
   - Тёмная тема как у приложения (#0D0D0D фон, #CC7B3A акцент, закруглённые углы CornerRadius=12)
   - Кастомный titlebar с "S SC Native", кнопка закрытия с красным ховером
   - Пошаговый визард (4 шага) с индикатором (кружки + линии)
   - Шаг 1: Welcome — логотип, описание, что будет установлено
   - Шаг 2: Путь установки + опции (ярлык на рабочем столе, в Пуске, автозапуск)
   - Шаг 3: Прогресс-бар с процентами и статусом
   - Шаг 4: Готово + галочка «Запустить SC Native»
   - Кастомные стили: кнопки (AccentButton/GhostButton), прогресс-бар, чекбоксы, текстбокс
   - Приложение встроено как EmbeddedResource (app.zip) — ОДИН .exe на выходе
   - Установка: распаковка zip с прогрессом, ярлыки (.lnk через WScript.Shell COM), реестр (Uninstall), деинсталлятор (uninstall.bat)
   - Self-contained (.NET не нужен на целевой машине), PublishSingleFile
   - `build-release.bat` — двухэтапная сборка: app publish → zip → embed → installer publish
   - Результат: `ReleaseOutput/SCNativeSetup.exe` (~221 MB, один файл, всё внутри)

## На завтра — дизайн
- Продолжить работу над визуалом/дизайном приложения
