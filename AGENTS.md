# AGENTS.md — Navis Elevation (плагин для Navisworks)

## Communication
- Отвечай пользователю на русском языке.
- Если требование, задача или ожидание пользователя неясны, неоднозначны или могут быть истолкованы по-разному — обязательно задай уточняющий вопрос через инструмент `question` прежде чем действовать. Лучше уточнить один раз, чем сделать не то.
- Пользователь новичок в программировании (знаком только с базовым Python) и в вайб-кодинге. Объясняй каждое действие простым языком, без лишнего сленга. Рассказывай, зачем делается шаг и что он даёт.

## Project identity
- C#-плагин для Autodesk Navisworks Manage 2021 (единственная установленная версия).
- Назначение: показывать отметку (высоту) элемента модели при наведении курсора в 3D-виде.
- Целевой фреймворк: `net48` — Navisworks 2021 работает как хост на .NET Framework 4.8. Не переводи на `net8.0` без явной необходимости.

## Architecture
- Входной плагин Navisworks — класс с атрибутом `[Plugin]`. Логика делится на два слоя:
  - `src\NavisElevation.Core\` (`netstandard2.0`) — логика без зависимостей: форматирование отметки, настройки, лог. **Не должна** ссылаться на Navisworks API.
  - `src\NavisElevation.Navis\` (`net48`) — только то, что требует Navisworks: перехват мыши, кнопка, оверлей.
- Основной механизм захвата элемента — `ToolPlugin`, который переопределяет `MouseMove` и вызывает `View.PickItemFromPoint`. Он включается через `Document.Tool.SetCustomToolPlugin`.
- **Исходники `Core` компилируются прямо в сборку плагина** (`<Compile Include>`), а не подключаются как `ProjectReference`. Причина: Navisworks загружает сборку плагина сам и не гарантирует разрешение второй сборки рядом, а такая ошибка молча ничего не сообщает. Побочное следствие: нельзя ссылаться на `NavisElevation.Core` как на сборку из проекта плагина — типы продублируются.

## Build & references
- Ссылка на API Navisworks — только `<Reference>` + `HintPath` на DLL в папке установки. NuGet-пакета `Navisworks.Api` **не существует**, не пытайся его искать.
- `<Private>false</Private>` на ссылках Navisworks обязателен: Navisworks подгружает сборки сам, копия рядом с плагином ломает загрузку.
- Путь к установке — свойство `NavisworksInstallDir` в `Directory.Build.props`, переопределяется через `-p:NavisworksInstallDir=...`.
- DLL Navisworks не коммитить в репозиторий (проверено `.gitignore`).
- Targeting pack .NET Framework 4.8 в системе **не установлен**; reference assemblies берутся из NuGet-пакета `Microsoft.NETFramework.ReferenceAssemblies`, который подключается в `Directory.Build.targets`.
- Манифеста `.addin` here не бывает — это механизм Revit. Navisworks сам читает папку `Plugins` в папке установки и бандлы в `%APPDATA%\Autodesk\ApplicationPlugins`.

## Developer workflow
- Build: пользователь собирает и разворачивает плагин самостоятельно через `build-and-deploy.bat` в корне репозитория. Агент НЕ должен запускать этот BAT-файл и не должен копировать файлы в `%APPDATA%\Autodesk\ApplicationPlugins\` без явного запроса.
- Скрипт обязан начинаться с `cd /d "%~dp0"`: cmd разрешает относительные пути относительно текущей папки терминала, а не папки скрипта. Без этой строки запуск из любой другой папки падает с «Solution not found».
- Для проверки компиляции агент может использовать `dotnet build build\NavisElevation.sln -c Debug`.
- Отладка: Attach to process к `Roamer.exe` — это процесс Navisworks Manage 2021.
- Нет автоматических юнит-тестов без установленного Navisworks; предпочитай интеграционные проверки через загрузку плагина в Navisworks.
- **Агент не может запустить Navisworks.** Потолок проверки — «собирается» и «атрибуты
  присутствуют в собранной DLL», что проверяется рефлексией. Всё дальше — только глазами
  пользователя. Поэтому не ходи по ленте вслепую: добавляй запись в `DiagnosticLog`
  (`Core/DiagnosticLog.cs`, `%LOCALAPPDATA%\NavisElevation\plugin.log`), чтобы следующий
  запуск давал факты. Два провала подряд на ленте показали, почему: обе ошибки были
  «молчащими» — Navisworks грузил плагин и просто ничего не показывал.

## Planning & documentation
- Любой план реализации новой фичи или серьёзного изменения должен быть сохранён в папку `docs/` в виде Markdown-файла.
- Если план написан, но пользователь решил не реализовывать его сейчас — файл помещается в `docs/future/`.
- Если план находится в процессе реализации — файл помещается в `docs/in-progress/`.
  **План остаётся в `docs/in-progress/` до тех пор, пока соответствующие коммиты не будут запушены на GitHub.**
- Если план реализуется — файл помещается в `docs/realized/` (создать папку при необходимости).
  **Перемещение в `docs/realized/` происходит только после успешного пуша коммитов фичи на GitHub.**
- В плане обязательно добавлять контрольный список ручной проверки для пользователя, так как агент не может сам запустить Navisworks.

## Version control & commits
- Коммиты должны быть смысловыми и атомарными: одна законченная фича, исправление или refactoring — один коммит.
- Не копить долго множество мелких изменений в одной рабочей копии; фиксировать прогресс регулярно.
- Сообщение коммита должно кратко описывать, что изменилось и зачем.
- **Любые git-операции — только по явному запросу.** Не делать `git commit`, `git push`, `git reset`, `git revert`, `git stash`, `git rebase` и любые другие мутации истории без прямого разрешения пользователя. Сначала явно спросить, потом действовать. Даже если изменения выглядят готовыми к коммиту, не коммитить без подтверждения.
- **Пуш только по явному запросу:** не делать `git push` без прямого разрешения пользователя. Даже если коммиты уже собраны, перед пушом нужно получить явное подтверждение.

## Release
- **При выпуске нового релиза обновляй `README.md`.** Он отображается на главной странице GitHub и несёт основную информацию о плагине: описание, возможности, установку, инструкции. Перед релизом сверяй README с фактическим функционалом и предлагай пользователю обновить устаревшие разделы.
- Если релиз добавляет новые модули или меняет сценарии работы — сначала внести их в README (и в план документации), потом выпускать релиз.
- Номер версии хранится в `Version.txt` в корне и оттуда попадает в сборку через `Directory.Build.props`.

## Navisworks API: как искать

Для Revit в соседнем проекте есть MCP-сервер `revit-api-docs`. **Здесь его нет и он не нужен:**
для Navisworks источник истины — рефлексия по установленной сборке.

| Задача | Инструмент |
|---|---|
| Существует ли член/тип в API, его точная сигнатура, namespace, перегрузки | Рефлексия по `C:\Program Files\Autodesk\Navisworks Manage 2021\Autodesk.Navisworks.Api.dll` |
| Примеры от сообщества, best practices, известные проблемы | Exa (`tools.exa.web_search_exa`) |
| Официальная документация .NET / NuGet | Context7 |

### Рецепт рефлексии

```powershell
$api = [System.Reflection.Assembly]::LoadFrom("C:\Program Files\Autodesk\Navisworks Manage 2021\Autodesk.Navisworks.Api.dll")
$api.GetType('Autodesk.Navisworks.Api.ModelItem').GetMembers('Public,Instance,DeclaredOnly') |
  ForEach-Object { $_.ToString() } | Sort-Object -Unique
```

Полезно знать: `GetTypes()` возвращает и internal-типы — это позволяет проверить,
что типа действительно нет вовсе, а не только в публичном API. Так было найдено, что
`BasePoint` в API Navisworks отсутствует полностью.

Подсказки по навигации:
- `ModelItem` лежит в `Autodesk.Navisworks.Api`, **а не** в `.Models` (в 2018 и ранних
  версиях документация показывает другое namespace — проверяй рефлексией, не памятью).
- У `PickItemResult` два нужных поля: `ModelItem` и `Point`.
- Для «перехвата мыши» у `ToolPlugin` есть `MouseMove`, `MouseDown`, `MouseUp`,
  `MouseLeave`, `KeyDown`, `OverlayRender`.
- **`SetCustomToolPlugin` принимает только экземпляр, созданный самим Navisworks, а не
  `new`.** Правильный вызов — через реестр плагинов:
  ```csharp
  PluginRecord record = Application.Plugins.FindPlugin(ToolId + "." + DeveloperId);
  document.Tool.SetCustomToolPlugin((ToolPlugin)record.LoadPlugin());
  ```
  База `Plugin` хранит ссылку на `PluginRecord`; у объекта, созданного вручную, она
  `null`, и Navisworks падает с `NullReferenceException` **внутри себя**, без всякой
  диагностики. Следствие: у `ToolPlugin` обязан быть **конструктор без параметров**,
  потому что создаёт его Navisworks через `Activator.CreateInstance`. Состояние,
  которое раньше передавалось аргументами, приходится отдавать иначе — например
  статическим полем, заполняемым непосредственно перед включением инструмента.
- **`ToolPlugin.CustomToolPluginId` возвращает `"<id>.<id разработчика>"`, а не голый
  `id`.** Сравнение с одним только `PluginIds.ToolId` не сойдётся никогда.
- **Своя вкладка ленты требует `CommandHandlerPlugin`, а не `AddInPlugin`.** Только
  `CommandHandlerPlugin` несёт `[RibbonLayout]` и `[RibbonTab]`; наследование в C#
  одинарное, поэтому кнопка во встроенной Add-Ins и кнопка на своей вкладке живут в
  двух разных классах. Разметка — `Autodesk.Windows.*` из `AdWindows.dll` плюс
  `Autodesk.Navisworks.Gui.Roamer.AIRLook.NWRibbonButton` из `navisworks.gui.roamer.dll`,
  обе подключаются с `Private=false`. Образец настоящей вкладки лежит в самой поставке:
  `ru-RU\GlueRibbon.xaml` — с него взяты имена пространств имён и набор атрибутов.
  Разметку кладём **файлом в языковую подпапку рядом с плагином** (`ru-RU` для русского
  интерфейса, `en-US` как запасной), вместе с файлом локализации `*.name`, на который
  указывает `[Strings]`. Это не ресурс внутри сборки: в сборке Navisworks разметку не
  ищет. Описано в блоге Autodesk про ленту Navisworks: «the xaml and name file must
  reside in the sub-folder of the plug-in dll. The sub-folder name needs to be a Language
  Code». Если файл не найден, вкладка молча не появляется — без ошибки и без записи в
  лог. Образец: у встроенных плагинов Navisworks разметка лежит в `ru-RU\RenderTab.xaml`
  и `ru-RU\GlueRibbon.xaml`.

Проверка собранного плагина без запуска Navisworks: атрибуты видны в самой сборке,
их можно прочитать рефлексией по `bin\Debug\Navis\NavisElevation.dll` — так проверяется,
что кнопка ленты действительно зарегистрирована.

## API gotchas
- `Document.Tool` — это `DocumentParts.DocumentTool`, а не одноимённый enum `Tool`.
  Включение своего курсора: `Document.Tool.SetCustomToolPlugin(toolPlugin)`.
  Возврат к обычному инструменту: `Document.Tool.Set(Tool.CommonOrbit)`. Значение
  `Tool.CustomToolPlugin` означает, что активен наш инструмент.
- **Свою панель нельзя встроить в 3D-вид.** У `View` нет ни одного метода для показа
  панели, а `ViewControl` — обычный `System.Windows.Forms.Control` без способа его
  подвесить. Для UI внутри вида рисуй в оверлее: `ToolPlugin.OverlayRender` →
  `Graphics.Text2D(font, text, Point2D.Origin, x, y)` между `BeginWindowContext()` и
  `EndWindowContext()`, где x/y — пиксели окна. Своя панель сбоку — только
  `DockPanePlugin`.
- После изменения оверлея обязательно зови `View.RequestDelayedRedraw(ViewRedrawRequests.OverlayRender)` —
  в enum есть `None`, `Render`, `OverlayRender`, `All`, и по умолчанию будет `None`.
- **Координаты в `Graphics.Text2D` отсчитываются от нижнего левого угла окна, `y` растёт
  вверх.** Верхний край — это `view.Height`, а не ноль. Подпись, нарисованная на
  `margin + i * lineHeight`, уезжает в нижний углу, а строки идут снизу вверх; тень,
  смещённая на `+1`, ложится поверх текста, а не под ним. Источники: официальный пример
  Autodesk в блоге про `RenderPlugin` и скриншот из Navisworks.
- **WPF-окно из плагина поверх Navisworks работает**, потому что GUI самой программы
  WPF-based: `navisworks.gui.roamer.dll` ссылается на PresentationFramework,
  PresentationCore и WindowsBase. Родительское окно задаётся через `WindowInteropHelper`,
  потому что у WPF свойство `Window.Owner` принимает объект `Window`, а не `HWND`.
- `PluginAttribute` принимает только **два** аргумента: id и developerId. Имя, подсказки
  и иконка задаются именованными свойствами. `DisplayName` есть у `PluginAttribute`,
  `RibbonTabAttribute` и `CommandAttribute`, но **нет** у `RibbonLayoutAttribute`
  и `ToolTipsAttribute` — подсказки задавай через `CommandAttribute.ToolTip` /
  `ExtendedToolTip`.
- **Плагин ставится бандлом в `%APPDATA%\Autodesk\ApplicationPlugins\`, а не просто
  файлом в папку `Plugins`.** Автозагрузка в 2021 идёт только из
  `C:\Program Files\Autodesk\Navisworks Manage 2021\Plugins\<имя>\<имя>.dll`, а запись
  туда требует прав администратора, которых у пользователя нет. Документированный
  способ установки без прав — бандл:
  `%APPDATA%\Autodesk\ApplicationPlugins\<имя>.bundle\PackageContents.xml` плюс
  `Contents\2021\<имя>.dll`. Три условия, каждое нарушается **молча**: наличие
  `PackageContents.xml`, атрибут `AppType="ManagedPlugin"` в `ComponentEntry` и
  `SeriesMin`/`SeriesMax` = `Nw18` (Nw17=2020, Nw18=2021, Nw19=2022). `ModuleName`
  задаёт точный относительный путь к DLL, поэтому имя папки в `Contents` любое.
  Описание бандла лежит в `deploy\PackageContents.xml`.
- **Папка `%APPDATA%\Autodesk\Navisworks Manage 2021\Plugins` в 2021 не сканируется.**
  Проверено опытом, а не предположением: DLL была разложена туда, Navisworks запущен
  позже — и `%LOCALAPPDATA%\NavisElevation\plugin.log` не появился, то есть сборка не
  загрузилась. В бинарниках Navisworks есть строка `Autodesk\ApplicationPlugins`, а
  строки с этой пользовательской папкой нет.
- **Не делай вывод о корректности выкладки по тому, что папка и DLL в ней есть.**
  Скрипт развёртывания мог создать их сам. Проверять надо по логу плагина: единственный
  признак, что Navisworks дошёл до сборки, — файл `%LOCALAPPDATA%\NavisElevation\plugin.log`.
- **Вкладка Add-Ins появляется только если хотя бы один плагин загрузился.** Она не
  «сломана» и не скрыта настройкой: если плагины не загрузились, её просто не создают.
  Отсутствие вкладки = отсутствие загруженного плагина, а не проблема ленты.
- **`AddInPlugin` без атрибута локации загружается молча и без кнопки.** Нужен
  `[AddInPlugin(AddInLocation.AddIn)]`: значение `AddInLocation` по умолчанию равно
  `None`, и плагин просто не привязывается ни к одному месту в интерфейсе — ни ошибки,
  ни записи в лог. Значения: `None, AddIn, Import, Export, Help,
  CurrentSelectionContextMenu, CurrentSelection2DContextMenu`.
- **Своя вкладка на ленте требует XAML-файла.** `RibbonLayoutAttribute` принимает путь
  к XAML-ресурсу (его параметр так и называется — `xaml`), а не строку-идентификатор.
  По документации Autodesk вкладка появляется только вместе с валидным `RibbonLayout`;
  если сослаться на несуществующий ресурс, вкладка **молча не появляется**, без ошибки.
  Разметка: `RibbonControl` → `RibbonTab` → `RibbonPanel` → `RibbonPanelSource` →
  `local:NWRibbonButton`, плюс ссылки на сборки `Roamer` (GUI Navisworks) и `AdWindows`.
  `Id` вкладки и `Id` кнопки в XAML обязаны совпадать с `[RibbonTab]` и `[Command]`.
- **Разметка ленты лежит файлом в языковой подпапке рядом с плагином, а не ресурсом
  в сборке.** То есть `Contents\2021\ru-RU\RibbonDefinition.xaml` плюс
  `Contents\2021\ru-RU\<имя>.name`, на который указывает `[Strings]`. `ru-RU` — под язык
  интерфейса Navisworks, `en-US` — запасной. Описано в блоге Autodesk про ленту:
  «the xaml and name file must reside in the sub-folder of the plug-in dll. The
  sub-folder name needs to be a Language Code». Проверено на этой машине: с разметкой
  в папке рядом со сборкой вкладка появляется, в корне бандла — нет. Ни `EmbeddedResource`
  с XAML, ни контейнер `<сборка>.Resources.resources` Navisworks не читает.
- **Лента и встроенная вкладка Add-Ins — несовместимы в одном классе.** Лента требует
  `CommandHandlerPlugin`, а вывод кнопки в Add-Ins — `AddInPlugin` с
  `[AddInPlugin(AddInLocation.AddIn)]`, а наследование в C# одинарное. Сейчас плагин живёт
  только на своей вкладке, класса-наследника `AddInPlugin` в проекте нет. Плата: если
  Navisworks перестанет читать разметку, кнопок не будет нигде и лога тоже не будет —
  сборка просто не загрузится.
- `Plugin.OnUnloading()` — `protected`, а не `public`. Если переопределяешь, менять
  модификатор доступа нельзя.
- `CommandState` — класс, а не enum. Создаётся через `new CommandState { IsEnabled = … }`.
- `Autodesk.Navisworks.Api.MessageBox` **не существует**. Сообщить пользователю, что
  действие невозможно, можно только через `CommandState.IsEnabled` — кнопка гаснет.
- `PickItemFromPoint` возвращает `null`, если под курсором ничего нет или координаты
  некорректны — это нормальный сценарий «курсор в пустоте», не ошибка.
- `MouseMove` вызывается с частотой кадров. Вызов `PickItemFromPoint` на каждом событии
  уронит FPS на большой модели — нужен троттлинг по дельте координат и таймеру.
- `DocumentModels.ResetTemporaryMaterials()` требует `IEnumerable<ModelItem>`: сбрасывай
  только свои элементы, иначе снимешь подсветку, поставленную другим плагином.
- `Model.Units` отдаёт единицы модели (не документа) — узнавать модель-владельца
  элемента нужно подъёмом по `ModelItem.Parent` до корня и сверкой с `Model.RootItem`.
  Это знание нужно для **перевода** в метры, а не для показа: отметка выводится всегда
  в метрах, и проверено, что пользователю хватает этого одного числа.
- **Уровни Navisworks плагин не использует, и это решение осознанное.**
  `Document.Grids.ActiveSystem.Levels` → `GridLevel.Elevation` в API существует, но
  строки уровня в подписи не было ни разу: в рабочей модели уровней нет, а без них
  искать ближайший нечего. Возвращать это не стоит — плагину нужна сама отметка.
  Если ноль модели не совпадёт с проектным, решение описано в `docs/future/project-zero.md`.
