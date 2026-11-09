# Требования → код → тесты

Матрица всех требований задания (`docs/ORIGINAL_PROMPT.md`, §3). Статусы: ✅ реализовано · 🟡 частично · ⏭ перенесено в `docs/ROADMAP.md`.
Пути сокращены: `Core/` = `src/WorkTimeTracker.Core/`, `Data/` = `src/WorkTimeTracker.Data/`, `App/` = `src/WorkTimeTracker.App/`; тесты — `tests/WorkTimeTracker.*.Tests/`.

## F1. Быстрый текстовый ввод (P0)

| ID | Требование | Статус | Где реализовано | Тесты |
|---|---|---|---|---|
| F1.1 | Грамматика `[дата] начало разделитель конец [комментарий]`, формы времени, `сейчас`/`now` (P1), дата `dd.MM[.yyyy]`, прошлый год для будущей `dd.MM`, второй интервал, ночная смена, `24:00` только в конце, 0 < длительность ≤ 24 ч, запрет будущих дат | ✅ | `Core/Parsing/QuickEntryParser.cs`, `Core/Validation/EntryRules.cs`, `Core/Model/TimeRange.cs` | `Core.Tests/QuickEntryParserTests.cs`, `EntryRulesTests.cs` |
| F1.2 | Многострочная вставка, атомарное добавление, ошибки «Строка N: …» | ✅ | `QuickEntryParser.ParseMany`, `Core/Services/EntryService.AddFromTextAsync`, `Data/EntryRepository.AddAsync` (одна транзакция), `App/Views/JournalView` (многострочное поле, Enter/Shift+Enter) | `Core.Tests/QuickEntryParserBatchTests.cs`, `EntryServiceTests.AddFromText_Multiline_*`, `Data.Tests/EntryRepositoryTests.Add_IsAtomic`, `App.Tests/JournalViewModelTests.Add_Multiline_*`, `HeadlessSmokeTests.ShiftEnter_*` |
| F1.3 | Живой предпросмотр, ошибка красным с фрагментом, «Добавить»/Enter недоступны при ошибке | ✅ | `App/ViewModels/JournalViewModel.UpdatePreview`, `ParseError.DisplayText`, `JournalView.axaml` | `App.Tests/JournalViewModelTests.Preview_*`, `HeadlessSmokeTests.Window_Opens_TypeAndEnter_AddsRow`, `QuickEntryParserTests.Error_*` |
| F1.4 | Таблица эталонных примеров, ≥ 40 кейсов | ✅ | — | `QuickEntryParserTests.Valid` (38) + `Invalid` (35) + пустые (4) + отдельные проверки |

Фокус в поле при запуске — `JournalView.FocusQuickInput` (проверяется в `HeadlessSmokeTests`).

## F2. Ввод через форму (P0)

| ID | Требование | Статус | Где реализовано | Тесты |
|---|---|---|---|---|
| F2.1 | Дата (`CalendarDatePicker`, по умолчанию сегодня, будущие недоступны) | ✅ | `JournalView.axaml` (Expander «Ввести через форму», `DisplayDateEnd`), `JournalViewModel.SelectedDate/MaxDate` | `JournalViewModelTests.SelectedDate_*` |
| F2.2 | Начало/конец — `TimePicker` 24 ч, шаг 1 мин, кнопки «Сейчас» | ✅ | `JournalView.axaml`, `JournalViewModel.SetFormStartNow/SetFormEndNow` | `JournalViewModelTests.Form_NowButtons_UseClock` |
| F2.3 | Комментарий до 500 символов со счётчиком | ✅ | `MaxLength=500`, `JournalViewModel.FormCommentCounter` | `JournalViewModelTests.Form_NightShift_*` |
| F2.4 | Живой расчёт длительности, «+1 день»; 00:00–00:00 — целые сутки | ✅ | `JournalViewModel.UpdateFormPreview`, `FormHelpers.DurationWithNextDay` | `JournalViewModelTests.Form_NightShift_LiveDuration_AndAdd` |
| F2.5 | «Добавить», та же валидация (один доменный сервис) | ✅ | `EntryService.BuildDraft` → `EntryRules.CreateDraft`; `EntryService.AddAsync` | `EntryServiceTests.Form_UsesSameRules`, `JournalViewModelTests.Form_Errors_SameAsQuickInput` |

## F3. Статистика (P0, если не указано иное)

| ID | Требование | Статус | Где реализовано | Тесты |
|---|---|---|---|---|
| F3.1 | Периоды День/Неделя/Месяц/Год/Всё время/Произвольный, ◀/▶, «Сегодня», русская подпись | ✅ | `Core/Statistics/PeriodNavigator.cs`, `RuFormat.PeriodLabel`, `StatisticsViewModel`, `StatisticsView.axaml` | `PeriodNavigatorTests`, `RuFormatTests.PeriodLabel`, `StatisticsViewModelTests.Navigate_*`, `Kinds_*`, `CustomPeriod_*` |
| F3.2 | Итого, записей, рабочих дней, средние (рабочий/календарный день, запись), самый длинный/короткий день, среднее начало/окончание (`02:00 +1`) | ✅ | `StatisticsCalculator.Calculate`, `PeriodStatistics`, KPI в `StatisticsViewModel.BuildKpis` | `StatisticsCalculatorTests` (полная неделя, «до сегодня», ночные смены на границе, пустой период), `StatisticsViewModelTests.DefaultWeek_*` |
| F3.2 норма | Норма × рабочие дни (P1), переработка/недоработка | ✅ | `StatisticsCalculator` (дни нормы с первой записи — `docs/STATISTICS.md`), настройки в `SettingsViewModel` | `StatisticsCalculatorTests.Norm_*`, `CurrentWeek_*` |
| F3.2 сравнение | Δ ч и % с предыдущим периодом (P1) | ✅ | `StatisticsCalculator.Compare` (текущий период — за то же число дней), `StatisticsViewModel.ComparisonCaption` | `StatisticsCalculatorTests.FullWeek_ComparedWithPreviousWeek`, `CurrentMonth_*`, `Custom_*`, `AllTime_*` |
| F3.3 | Графики по дням (линия нормы, подсказка) и по месяцам | ✅ | `App/Controls/BarChart.cs`, `ChartControlBase.cs`, `StatisticsViewModel.BuildCharts` | `StatisticsViewModelTests.DefaultWeek_*`, `Kinds_*`, `ChartScale_*`, `HeadlessSmokeTests.AllPages_Render` |
| F3.3 дни недели | Среднее по дням недели (P1) | ✅ | `StatisticsCalculator.Weekdays`, `BarChart` | `StatisticsCalculatorTests.WeekdayAverages_*`, `StatisticsViewModelTests.Navigate_*` |
| F3.3 тепловая карта | Тепловая карта года (P2) | ✅ | `App/Controls/YearHeatmap.cs`, `HeatmapCell` | `StatisticsViewModelTests.Heatmap_Levels`, `Kinds_*` |
| F3.4 | Сводка «Сегодня · Неделя: … из 40 ч» на главном экране | ✅ | `JournalViewModel.LoadSummaryAsync` | `JournalViewModelTests.Add_FromQuickInput_*`, `SettingsChange_UpdatesSummaryNorm` |
| F3.5 | Экспорт CSV периода (P1): UTF-8 BOM, `;`, CRLF, экранирование | ✅ | `Core/Export/CsvExporter.cs`, `StatisticsViewModel.ExportCsvAsync` | `CsvExporterTests`, `StatisticsViewModelTests.ExportCsv_*` |
| F3.6 | Ночная смена относится к дате начала, правило задокументировано | ✅ | модель (ADR-0003), `docs/STATISTICS.md` | `QuickEntryParserTests.NightShift_*`, `StatisticsCalculatorTests.NightShift_OnWeekBoundary_*` |

## F4. Сохранение данных (P0)

| ID | Требование | Статус | Где реализовано | Тесты |
|---|---|---|---|---|
| F4.1 | Каждое изменение сразу в SQLite в транзакции, без «Сохранить» | ✅ | `Data/EntryRepository.cs`, `SettingsStore.cs`, `App/Services/SettingsService.cs` | `EntryRepositoryTests.Data_SurvivesReopen`, `SettingsAndAboutTests.Settings_ArePersisted` |
| F4.2 | `%LOCALAPPDATA%\WorkTimeTracker\worktime.db`, `--data-dir` | ✅ | `Data/DataPaths.cs`, `App/Services/CommandLineOptions.cs` | `DataPathsTests`, `CommandLineOptionsTests` |
| F4.3 | Портативный режим `portable.txt` (P1) | ✅ | `DataPaths.Resolve` | `DataPathsTests.PortableMarker_UsesDataNextToExe` |
| F4.4 | `schema_version`, миграции-скрипты, только добавляются | ✅ | `Data/Migrator.cs`, `Data/Migrations/*.sql` | `MigratorTests` (пустая → последняя, N → N+1, откат, БД новее программы) |
| F4.5 | Автобэкап раз в день (Backup API), 14 копий, кнопка «сейчас» (P1) | ✅ | `Data/BackupService.cs`, `DatabaseInitializer`, `SettingsViewModel.BackupNowAsync` | `BackupServiceTests`, `DatabaseInitializerTests.NextRun_*`, `SettingsAndAboutTests.Settings_BackupNow_*` |
| F4.6 | WAL, foreign_keys | ✅ | `Data/SqliteConnectionFactory.cs` | `MigratorTests.EmptyDatabase_MigratesToLatest` |
| F4.7 | БД не открывается → понятный диалог с путём и «Открыть папку», данные не удаляются | ✅ | `DatabaseInitializer` (`DatabaseOpenException`), `MainWindowViewModel.InitializeAsync`, `Views/MessageWindow` | `DatabaseInitializerTests.CorruptFile_*`, `TooNewDatabase_*`, `SettingsAndAboutTests.Main_CorruptDatabase_*` |
| F4.8 | Размер и положение окна (P2) | ✅ | `App/Services/WindowPlacementService.cs`, `SettingsStore` (`window`) | `SettingsStoreTests.SaveAndLoad_RoundTrip` (сохранение); восстановление — ручная проверка (`docs/DEVELOPMENT.md`, п. 12) |

## F5. Удаление (P0)

| ID | Требование | Статус | Где реализовано | Тесты |
|---|---|---|---|---|
| F5.1 | Иконка в строке, контекстное меню, `Del` | ✅ | `JournalView.axaml` (кнопка, `ContextMenu`, `KeyBinding Delete`), `JournalViewModel.DeleteEntry/DeleteSelected` | `HeadlessSmokeTests.Hotkeys_*`, `JournalViewModelTests.Delete_*` |
| F5.2 | Множественный выбор Ctrl/Shift+клик (P1) | ✅ | `ListBox SelectionMode="Multiple"`, `SelectedItems` ↔ `JournalViewModel.SelectedRows` | `JournalViewModelTests.Delete_Toast_Undo`, `HeadlessSmokeTests.Hotkeys_*` |
| F5.3 | Уведомление «Удалено записей: N · Отменить» на 8 с, `Ctrl+Z`, мягкое удаление, очистка через 30 дней | ✅ | `JournalViewModel` (тост, `UndoCommand`), `MainWindow` (Ctrl+Z), `EntryRepository.SoftDelete/Restore/PurgeDeleted`, `DatabaseInitializer` (ADR-0004) | `JournalViewModelTests.Toast_*`, `Undo_*`, `EntryRepositoryTests.SoftDelete_*`, `Purge_*` |

## F6. Комментарии и редактирование (P0)

| ID | Требование | Статус | Где реализовано | Тесты |
|---|---|---|---|---|
| F6.1 | Комментарий до 500 символов | ✅ | `EntryRules`, `CHECK` в схеме | `QuickEntryParserTests.CommentLongerThan500_*`, `EntryRepositoryTests.Add_IsAtomic` |
| F6.2 | Комментарий в строке с многоточием, полный — в подсказке | ✅ | `JournalView.axaml` (`TextTrimming`, `ToolTip.Tip`) | скриншоты `docs/images/journal-*.png` |
| F6.3 | Редактирование (двойной клик / карандаш), та же валидация, пересечения без самой записи | ✅ | `EntryEditorViewModel`, `Views/EditEntryWindow`, `EntryService.UpdateAsync` | `JournalViewModelTests.Edit_*`, `Editor_Validation`, `EntryServiceTests.Update_*`, `HeadlessSmokeTests.Dialogs_Render` |
| F6.4 | Поиск `Ctrl+F` без учёта регистра, включая кириллицу (P1) | ✅ | `EntryRepository.SearchAsync` (фильтр в C#, ru-RU), `JournalViewModel.SearchText`, `MainWindow` (Ctrl+F) | `EntryRepositoryTests.Search_IsCaseInsensitive_ForCyrillic` («Созвон» ↔ «созвон»), `JournalViewModelTests.Search_*`, `HeadlessSmokeTests.Hotkeys_*` |

## F7. Общие правила и UX

| ID | Требование | Статус | Где реализовано | Тесты |
|---|---|---|---|---|
| F7.1 | Запрет пересечений в абсолютных минутах, касание допустимо, понятная ошибка (P0) | ✅ | `Core/Validation/EntryValidator.cs`, `EntryService` (ADR-0007) | `EntryValidatorTests`, `EntryServiceTests.AddFromText_Overlap*`, `NightShift_*` |
| F7.2 | Журнал: ввод → форма → сводка → список по дням, 30 дней + «Показать ещё» | ✅ | `JournalView.axaml`, `JournalViewModel.LoadListAsync/ShowMore` | `JournalViewModelTests.Add_FromQuickInput_*`, `ShowMore_*`, `OlderYear_*` |
| F7.3 | Боковая панель `SplitView` (компактный режим) | ✅ | `MainWindow.axaml`, `MainWindowViewModel.Navigation` | `SettingsAndAboutTests.Main_Navigation` |
| F7.4 | Настройки: норма, рабочие дни, тема, папка данных, бэкап, экспорт всех данных | ✅ | `SettingsViewModel`, `SettingsView.axaml` | `SettingsAndAboutTests.Settings_*` |
| F7.5 | «О программе»: версия, репозиторий, путь к данным, лицензия | ✅ | `AboutViewModel`, `AboutView.axaml`, `AppInfo` | `SettingsAndAboutTests.About_*` |
| F7.6 | Горячие клавиши Enter, Ctrl+Z, Del, Ctrl+F, Ctrl+1…4, Esc | ✅ | `MainWindow.axaml(.cs)`, `JournalView.axaml(.cs)`, `EditEntryWindow`, `MessageWindow` | `HeadlessSmokeTests.*` |
| F7.7 | «8 ч 05 мин», KPI «8:05», русские даты независимо от ОС, 24 ч | ✅ | `DurationFormatter`, `RuFormat` (явные названия поверх ru-RU), `Program` (культура процесса) | `DurationFormatterTests`, `RuFormatTests` |
| F7.8 | Тема: системная по умолчанию, светлая и тёмная | ✅ | `ThemeService`, `Styles/Theme.axaml` | `SettingsAndAboutTests.ThemeService_Maps`, скриншоты светлой и тёмной темы |
| F7.9 | Иконка exe и окна | ✅ | `tools/IconGen`, `App/Assets/app.ico`, `ApplicationIcon`, `Window.Icon` | визуально (`Assets/icon-256.png`) |
| F7.10 | Глобальная обработка ошибок, логи `logs\app-YYYYMMDD.log` | ✅ | `App.axaml.cs` (`InstallErrorHandlers`), `AppLogging` | ручная проверка; логирование ошибок экспорта/бэкапа покрыто тестами ViewModel |
| F7.11 | `--version`, `--data-dir`, `--self-test` (код 0/1, `self-test.log`); `--data-dir` без пути — ошибка и код 1; self-test без `--data-dir` — во `%TEMP%` (см. DATA_MODEL.md) | ✅ | `Program.cs`, `CommandLineOptions`, `SelfTest` | `CommandLineOptionsTests`, `SelfTestTests`; CI запускает настоящий exe |

## Нефункциональные требования

| Требование | Статус | Как обеспечено / проверено |
|---|---|---|
| Холодный старт < 2 с, UI не блокируется | ✅ | БД в пуле потоков (`SqliteConnectionFactory.RunAsync`); self-test на Windows проходит все проверки за доли секунды (время каждого шага пишется в `self-test.log`); старт окна проверяется вручную |
| Офлайн, без телеметрии, без автообновлений, без прав администратора | ✅ | нет сетевого кода; `app.manifest` — `asInvoker`; `DOTNET_CLI_TELEMETRY_OPTOUT` только для SDK |
| `TreatWarningsAsErrors`, `Nullable`, compiled bindings, `x:DataType` | ✅ | `Directory.Build.props`, `AvaloniaUseCompiledBindingsByDefault`; сборка CI без предупреждений |
| Покрытие строк Core ≥ 90% | ✅ | `scripts/test.sh` + CI (порог); фактическое значение — в `docs/DEVELOPMENT_LOG.md` |
| `AutomationProperties.Name` у контролов | ✅ | все интерактивные элементы в `Views/*.axaml`; headless-тесты ищут по ним |
| Без `DataGrid`, без платных компонентов, только MIT/Apache/BSD | ✅ | `Directory.Packages.props`: Avalonia (MIT), CommunityToolkit.Mvvm (MIT), Dapper (Apache-2.0), Serilog (Apache-2.0), Microsoft.* (MIT), xUnit (Apache-2.0), coverlet (MIT), SkiaSharp (MIT) |

## Вне рамок v1.0.0

Установщик, подпись кода, облачная синхронизация, таймер «старт/стоп», проекты и теги, импорт CSV, автообновление, мультиязычность, версии для macOS/Linux — ⏭ `docs/ROADMAP.md`.
