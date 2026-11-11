# Требования → код → тесты

Матрица всех требований задания (`docs/ORIGINAL_PROMPT.md`, §3). Статусы: ✅ реализовано · 🟡 частично · ⏭ перенесено в `docs/ROADMAP.md`.
Пути сокращены: `Core/` = `src/WorkTimeTracker.Core/`, `Data/` = `src/WorkTimeTracker.Data/`, `App/` = `src/WorkTimeTracker.App/`; тесты — `tests/WorkTimeTracker.*.Tests/`.

## F1. Быстрый текстовый ввод (P0)

| ID | Требование | Статус | Где реализовано | Тесты |
|---|---|---|---|---|
| F1.1 | Грамматика `[дата] начало разделитель конец [комментарий]`, формы времени, `сейчас`/`now` (P1), дата `dd.MM[.yyyy]`, прошлый год для будущей `dd.MM`, второй интервал, ночная смена, `24:00` только в конце, 0 < длительность ≤ 24 ч, запрет будущих дат | ✅ | `Core/Parsing/QuickEntryParser.cs`, `Core/Validation/EntryRules.cs`, `Core/Model/TimeRange.cs` | `Core.Tests/QuickEntryParserTests.cs`, `EntryRulesTests.cs` |
| F1.2 | Многострочная вставка, атомарное добавление, ошибки «Строка N: …» | ✅ | `QuickEntryParser.ParseMany`, `Core/Services/EntryService.AddFromTextAsync`, `Data/EntryRepository.AddAsync` (одна транзакция), `App/Views/JournalView` (многострочное поле, Enter/Shift+Enter) | `Core.Tests/QuickEntryParserBatchTests.cs`, `EntryServiceTests.AddFromText_Multiline_*`, `Data.Tests/EntryRepositoryTests.Add_IsAtomic`, `App.Tests/JournalViewModelTests.Add_Multiline_*`, `HeadlessSmokeTests.ShiftEnter_*` |
| F1.3 | Живой предпросмотр, ошибка красным с фрагментом, «Добавить»/Enter недоступны при ошибке | ✅ | `App/ViewModels/EntryInputViewModel.UpdatePreview`, `ParseError.DisplayText`, `EntryInputView.axaml` | `App.Tests/JournalViewModelTests.Preview_*`, `HeadlessSmokeTests.Window_Opens_TypeAndEnter_AddsRow`, `QuickEntryParserTests.Error_*` |
| F1.4 | Таблица эталонных примеров, ≥ 40 кейсов | ✅ | — | `QuickEntryParserTests.Valid` (38) + `Invalid` (35) + пустые (4) + отдельные проверки |

Фокус в поле при запуске — `EntryInputView.FocusQuickInput` (проверяется в `HeadlessSmokeTests`).

## F2. Ввод через форму (P0)

| ID | Требование | Статус | Где реализовано | Тесты |
|---|---|---|---|---|
| F2.1 | Дата (`CalendarDatePicker`, по умолчанию сегодня, будущие недоступны) | ✅ | `EntryInputView.axaml` (Expander «Ввести через форму», `DisplayDateEnd`), `EntryInputViewModel.SelectedDate/MaxDate` | `JournalViewModelTests.SelectedDate_*` |
| F2.2 | Начало/конец — `TimePicker` 24 ч, шаг 1 мин, кнопки «Сейчас» | ✅ | `EntryInputView.axaml`, `EntryInputViewModel.SetFormStartNow/SetFormEndNow` | `JournalViewModelTests.Form_NowButtons_UseClock` |
| F2.3 | Комментарий до 500 символов со счётчиком | ✅ | `MaxLength=500`, `EntryInputViewModel.FormCommentCounter` | `JournalViewModelTests.Form_NightShift_*` |
| F2.4 | Живой расчёт длительности, «+1 день»; 00:00–00:00 — целые сутки | ✅ | `EntryInputViewModel.UpdateFormPreview`, `FormHelpers.DurationWithNextDay` | `JournalViewModelTests.Form_NightShift_LiveDuration_AndAdd` |
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
| F5.3 | Уведомление «Удалено записей: N · Отменить» на 8 с, `Ctrl+Z`, мягкое удаление, очистка через 30 дней | ✅ | `UndoToastViewModel` (уведомление, `UndoCommand`), `Views/UndoToastView`, `JournalViewModel.DeleteAsync`, `MainWindow` (Ctrl+Z), `EntryRepository.SoftDelete/Restore/PurgeDeleted`, `DatabaseInitializer` (ADR-0004) | `JournalViewModelTests.Toast_*`, `Undo_*`, `EntryRepositoryTests.SoftDelete_*`, `Purge_*` |

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
| F7.6 | Горячие клавиши Enter, Ctrl+Z, Del, Ctrl+F, Ctrl+1…5 (с v1.1.0, F8.1), Esc | ✅ | `MainWindow.axaml(.cs)`, `JournalView.axaml(.cs)`, `EditEntryWindow`, `MessageWindow` | `HeadlessSmokeTests.*` |
| F7.7 | «8 ч 05 мин», KPI «8:05», русские даты независимо от ОС, 24 ч | ✅ | `DurationFormatter`, `RuFormat` (явные названия поверх ru-RU), `Program` (культура процесса) | `DurationFormatterTests`, `RuFormatTests` |
| F7.8 | Тема: системная по умолчанию, светлая и тёмная | ✅ | `ThemeService`, `Styles/Theme.axaml` | `SettingsAndAboutTests.ThemeService_Maps`, скриншоты светлой и тёмной темы |
| F7.9 | Иконка exe и окна | ✅ | `tools/IconGen`, `App/Assets/app.ico`, `ApplicationIcon`, `Window.Icon` | визуально (`Assets/icon-256.png`) |
| F7.10 | Глобальная обработка ошибок, логи `logs\app-YYYYMMDD.log` | ✅ | `App.axaml.cs` (`InstallErrorHandlers`), `AppLogging` | ручная проверка; логирование ошибок экспорта/бэкапа покрыто тестами ViewModel |
| F7.11 | `--version`, `--data-dir`, `--self-test` (код 0/1, `self-test.log`); `--data-dir` без пути — ошибка и код 1; self-test без `--data-dir` — во `%TEMP%` (см. DATA_MODEL.md) | ✅ | `Program.cs`, `CommandLineOptions`, `SelfTest` | `CommandLineOptionsTests`, `SelfTestTests`; CI запускает настоящий exe |

## F8. Блокнот (v1.1.0)

Задание — [`prompts/2026-11-10-notepad.md`](prompts/2026-11-10-notepad.md), решения — [ADR-0009](adr/0009-notepad.md), данные — [`DATA_MODEL.md`](DATA_MODEL.md#блокнот-f8-миграция-0003_notepadsql).

| ID | Требование | Статус | Где реализовано | Тесты |
|---|---|---|---|---|
| F8.1 | Вкладка «Блокнот» — вторая; `Ctrl+1…5` | ✅ | `MainWindowViewModel.Navigation`, `MainWindow.axaml` (`KeyBinding`, шаблон страницы), `Views/NotepadView` | `SettingsAndAboutTests.Main_Navigation`, `HeadlessSmokeTests.Hotkeys_*`, `NotepadSmokeTests.CtrlTwo_*` |
| F8.2 | Тот же контрол быстрого ввода и та же форма | ✅ | `EntryInputViewModel` + `Views/EntryInputView` (общие с «Журналом»), `NotepadService.AddFromTextAsync/AddAsync` → `EntryService` | `NotepadViewModelTests.S1_*`, `Form_AddsToNotepadAndJournal`, `NightShift_*`, `NotepadSmokeTests.CtrlTwo_*` |
| F8.3 | Пересечения — со всеми записями базы | ✅ | `EntryService.CheckOverlapsAsync` (общая проверка) | **S7:** `NotepadViewModelTests.S7_*`, `NotepadServiceTests.S7_*` |
| F8.4 | Запись и привязка — одна транзакция | ✅ | `NotepadRepository.AddEntriesAsync` (`BEGIN IMMEDIATE`), `EntryRepository.InsertEntries`, делегат `EntryInserter` | `NotepadRepositoryTests.Add_IsAtomic_*`, `Add_FailingLink_RollsBackEntry` |
| F8.5 | Записи из «Журнала» в блокнот не попадают | ✅ | привязки создаёт только `NotepadRepository.AddEntriesAsync` | **S10:** `NotepadViewModelTests.S10_*`, `NotepadServiceTests.S10_*`, `NotepadRepositoryTests.JournalEntries_*` |
| F8.6 | Плашка «В блокноте: 7 ч 30 мин · 2 записи · ведётся с …», обновляется сразу, в т. ч. из «Журнала» | ✅ | `NotepadFormat.Summary`, `NotepadViewModel.SummaryText/LoadAsync`, подписка на `AppEvents.EntriesChanged` | **S1, S4, S6:** `NotepadViewModelTests`; `NotepadFormatTests.Summary_*` |
| F8.7 | Название листа на плашке, до 100 символов, «Без названия» | ✅ | `NotepadViewModel.Title`, `NotepadService.SetTitleAsync/NormalizeTitle`, `NotepadView` (`TitleBox`), `CHECK` в схеме | `NotepadViewModelTests.Title_*` (в т. ч. название + немедленная очистка/добавление), `NotepadServiceTests.NormalizeTitle_*`, `NotepadSmokeTests.Title_*` (Enter, уход со вкладки, закрытие окна) |
| F8.8 | Порядок по дате и началу, дата в каждой строке | ✅ | `NotepadRepository.LoadEntries` (`ORDER BY`), `EntryRow.DateText` | `NotepadViewModelTests.NightShift_CountsWhole_OrderByDateAndStart`, `S1_*` |
| F8.9 | Блокнот, название и архив переживают перезапуск | ✅ | всё в SQLite (`notepad_sheets`, `notepad_items`) | **S3:** `NotepadViewModelTests.S3_*`; `NotepadRepositoryTests.Data_SurvivesReopen` |
| F8.10 | Правка меняет саму запись (тот же диалог) | ✅ | `NotepadViewModel.EditEntryAsync` → `EntryEditorViewModel` → `EntryService.UpdateAsync` | **S4:** `NotepadViewModelTests.S4_*`, `Edit_Cancelled_*` |
| F8.11 | ✕ / `Del` / меню — убрать только из блокнота; множественный выбор; отмена | ✅ | `NotepadViewModel.RemoveEntry/RemoveSelected`, `NotepadRepository.RemoveItemsAsync/RestoreItemsAsync`, `NotepadView` (кнопка, `ContextMenu`, `KeyBinding Delete`) | **S5:** `NotepadViewModelTests.S5_*`, `Remove_MultipleSelected_*`, `NotepadSmokeTests.Delete_*` |
| F8.12 | Удаление в «Журнале» — отовсюду; отмена возвращает; подсказка дополнена | ✅ | видимость через `entries.deleted_at` в `NotepadRepository.LoadEntries`; `JournalView.axaml` (подсказка) | **S6:** `NotepadViewModelTests.S6_*`; `NotepadRepositoryTests.SoftDeletedEntry_*`; `NotepadSmokeTests.JournalDeleteTooltip_MentionsNotepad` |
| F8.13 | Полного удаления записи во вкладке нет | ✅ | у `NotepadViewModel` нет команды удаления записи; `INotepadRepository` не изменяет `entries` | `NotepadViewModelTests.S5_*` (дамп `entries` не меняется) |
| F8.14 | «Очистить всё» → архив; без подтверждения, с отменой; неактивна при пустом блокноте | ✅ | `NotepadViewModel.ClearAllAsync` (`CanClear`), `NotepadService.ClearAsync/UndoClearAsync`, `NotepadRepository.ClearCurrentAsync/UndoClearAsync` | **S2, S8:** `NotepadViewModelTests.S2_*`, `S8_*`, `Clear_*`; `NotepadRepositoryTests.UndoClear_*` |
| F8.15 | Архив: «Текущий \| Архив (N)», карточки, раскрытие записей | ✅ | `NotepadViewModel.SelectedTabIndex/ArchiveSheets`, `ArchiveSheetViewModel`, `NotepadFormat.SheetTitle/DateRange/Cleared` | `NotepadViewModelTests.Archive_NewestFirst_Tabs_EmptyStates`, `S2_*`; `NotepadFormatTests`; `NotepadSmokeTests.Title_Clear_Archive_*` |
| F8.16 | Архив «живой»; «записи удалены» | ✅ | записи листа — соединение с `entries` при чтении; `ArchiveSheetViewModel.AllEntriesDeleted` | `NotepadViewModelTests.Archive_IsLive_EditedAndAllDeleted`, `S6_*`; `NotepadRepositoryTests.Archive_IsLive_*` |
| F8.17 | «Удалить из архива» — только лист; отмена | ✅ | `NotepadViewModel.DeleteSheetAsync`, `NotepadRepository.DeleteSheetAsync/RestoreSheetAsync` | **S9:** `NotepadViewModelTests.S9_*`, `NotepadServiceTests.S9_*` |
| F8.18 | Очистка мусора при запуске (30 дней) | ✅ | `NotepadRepository.PurgeAsync`, `DatabaseInitializer` | **S12:** `NotepadViewModelTests.S12_*`; `NotepadRepositoryTests.Purge_*`, `Startup_PurgesNotepadGarbage` |
| F8.19 | Пустые состояния блокнота и архива | ✅ | `NotepadViewModel.EmptyCurrentText/EmptyArchiveText`, `NotepadView.axaml` | `NotepadViewModelTests.Archive_NewestFirst_Tabs_EmptyStates`, `NotepadSmokeTests` |
| F8.20 | `Ctrl+Z` — последнее действие активной вкладки | ✅ | `UndoToastViewModel` (по экземпляру у «Журнала» и «Блокнота»), `MainWindowViewModel.ActiveUndo`, `MainWindow.axaml.cs` (ADR-0009) | `NotepadViewModelTests.ActiveUndo_FollowsCurrentPage`, `Undo_UnavailableAfterNextNotepadChange`, `NotepadSmokeTests.Delete_*` |

### Сценарии приёмки

Каждый сценарий — тест на настоящих ViewModel и БД (`tests/WorkTimeTracker.App.Tests/NotepadViewModelTests.cs`); часть продублирована на уровне сервисов (`NotepadServiceTests`).

| ID | Сценарий | Тест |
|---|---|---|
| S1 | День по кускам → «8 ч 00 мин · 2 записи», журнал, статистика 8 ч | `S1_DayInPieces_SumJournalAndStatistics` |
| S2 | Заказ на 3 дня, 23 ч 15 мин → «Очистить всё» → архив; статистика та же | `S2_MultiDayOrder_Clear_ArchiveCard_StatisticsUnchanged` |
| S3 | Перезапуск на той же БД | `S3_Restart_KeepsSheetTitleEntriesAndArchive` |
| S4 | Правка 09:00–19:00 → 09:00–18:00 | `S4_EditInNotepad_ChangesEntryEverywhere` |
| S5 | ✕ и «Отменить» | `S5_Remove_KeepsEntryInJournalAndStatistics_UndoReturns` |
| S6 | Удаление в «Журнале» записей текущего и архивного листа, отмена | `S6_DeleteInJournal_RemovesFromNotepadAndArchive_UndoRestores` |
| S7 | Пересечение с записью журнала | `S7_OverlapWithJournalEntry_NothingAddedAnywhere` |
| S8 | «Очистить всё» → «Отменить» | `S8_AccidentalClear_UndoRestoresSheetWithTitle` |
| S9 | «Удалить из архива» → «Отменить» | `S9_DeleteFromArchive_KeepsEntries_UndoReturnsSheet` |
| S10 | Запись добавлена через «Журнал» | `S10_EntryAddedInJournal_DoesNotAppearInNotepad` |
| S11 | Многострочная вставка с невалидной строкой | `S11_MultilinePaste_WithInvalidLine_AddsNothing` |
| S12 | Очистка мусора через 30 дней | `S12_StartupAfter30Days_PurgesNotepadGarbage_NotEntries` |

«Статистика не изменилась» проверяется двумя способами: отпечаток всех показателей `StatisticsViewModel` за месяц и побайтный дамп таблицы `entries` (включая `updated_at`) до и после действия. То же — в `--self-test` на настоящем exe (`SelfTest.RunNotepadChecks`).

Миграция БД 1.0.0 с данными — `NotepadMigrationTests.DatabaseV100_WithData_MigratesAndKeepsEverything`.

## Нефункциональные требования

| Требование | Статус | Как обеспечено / проверено |
|---|---|---|
| Холодный старт < 2 с, UI не блокируется | ✅ | БД в пуле потоков (`SqliteConnectionFactory.RunAsync`); self-test на Windows проходит все проверки за доли секунды (время каждого шага пишется в `self-test.log`); старт окна проверяется вручную |
| Офлайн, без телеметрии, без автообновлений, без прав администратора | ✅ | нет сетевого кода; `app.manifest` — `asInvoker`; `DOTNET_CLI_TELEMETRY_OPTOUT` только для SDK |
| `TreatWarningsAsErrors`, `Nullable`, compiled bindings, `x:DataType` | ✅ | `Directory.Build.props`, `AvaloniaUseCompiledBindingsByDefault`; сборка CI без предупреждений |
| Покрытие строк Core ≥ 90% | ✅ | `scripts/test.sh` + CI (порог); фактическое значение — в `docs/DEVELOPMENT_LOG.md` |
| `AutomationProperties.Name` у контролов | ✅ | все интерактивные элементы в `Views/*.axaml`; headless-тесты ищут по ним |
| Без `DataGrid`, без платных компонентов, только MIT/Apache/BSD | ✅ | `Directory.Packages.props`: Avalonia (MIT), CommunityToolkit.Mvvm (MIT), Dapper (Apache-2.0), Serilog (Apache-2.0), Microsoft.* (MIT), xUnit (Apache-2.0), coverlet (MIT), SkiaSharp (MIT) |

## Вне рамок v1.0.0 и v1.1.0

Установщик, подпись кода, облачная синхронизация, таймер «старт/стоп», проекты и теги, импорт CSV, автообновление, мультиязычность, версии для macOS/Linux — ⏭ `docs/ROADMAP.md`.
