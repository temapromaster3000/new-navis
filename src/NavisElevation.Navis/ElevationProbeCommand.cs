using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using NavisElevation.Core;
using Navisworks = Autodesk.Navisworks.Api;

namespace NavisElevation.Navis
{
    /// <summary>
    /// Кнопка, включающая и выключающая режим показа отметки.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Почему обязателен <c>[AddInPlugin(AddInLocation.AddIn)]</c>.</b> Без него
    /// Navisworks загружает сборку, но кнопку не показывает нигде: значение
    /// <c>AddInLocation</c> по умолчанию равно <c>None</c>, и плагин не привязывается
    /// ни к одному месту в интерфейсе. Молча, без ошибки и без записи в лог — самый
    /// неприятный вид отказа. Именно так выглядел первый запуск плагина.
    /// </para>
    /// <para>
    /// Собственной вкладки на ленте пока нет, и это сделано намеренно. Вкладку создаёт
    /// <c>[RibbonTab]</c>, но он работает только в паре с <c>[RibbonLayout]</c>, а тот
    /// принимает <b>путь к XAML-файлу</b> с разметкой ленты, вшитым в сборку. Без
    /// настоящего XAML-файла вкладка тоже не появляется и тоже молча. Поэтому сейчас
    /// кнопка живёт во встроенной вкладке Add-Ins, куда Navisworks сам помещает любой
    /// <c>AddInPlugin</c> с атрибутом <c>[Command]</c> и явной локацией.
    /// Возврат к собственной вкладке — отдельная задача, см. план.
    /// </para>
    /// <para>
    /// Кнопка гаснет через <see cref="CanExecute"/>, когда не открыт документ.
    /// В API Navisworks нет <c>MessageBox</c>, поэтому сообщить пользователю
    /// о невозможности нажатия можно только таким способом.
    /// </para>
    /// </remarks>
    [Plugin(PluginIds.CommandId, PluginIds.DeveloperId, DisplayName = "Отметка элемента")]
    [AddInPlugin(AddInLocation.AddIn)]
    [Command("NavisElevation.ToggleCommand", DisplayName = "Показать отметку", CanToggle = true,
        ToolTip = "Показать отметку элемента",
        ExtendedToolTip = "Включает режим показа отметки элемента.\r\n\r\n"
                          + "После включения наведите курсор на любой элемент модели — рядом с ним "
                          + "появится отметка, границы по высоте и ближайший уровень.\r\n\r\n"
                          + "Повторное нажатие кнопки, Esc или правая кнопка мыши выключают режим.")]
    public class ElevationProbeCommand : AddInPlugin
    {
        /// <summary>
        /// Сработает в момент, когда Navisworks впервые обратится к этому типу.
        /// </summary>
        /// <remarks>
        /// Это точка, по которой можно отличить «плагин не загрузился» от «загрузился,
        /// но кнопки нет»: обе ситуации внешне выглядят одинаково — просто вкладка
        /// Add-Ins не появляется. Статический конструктор здесь единственное место,
        /// где плагин может сообщить, что он вообще оказался в процессе.
        /// </remarks>
        static ElevationProbeCommand()
        {
            DiagnosticLog.RecordPluginLoad(typeof(ElevationProbeCommand).FullName);
        }

        protected override void OnLoaded()
        {
            DiagnosticLog.Info("Плагин команды загружен, OnLoaded. " + DescribeEnvironment()
                               + ", сборка " + GetType().Assembly.GetName().Version);
        }

        public override int Execute(string[] parameters)
        {
            DiagnosticLog.Info("Кнопка нажата. " + DescribeEnvironment()
                               + ", документ открыт: " + (Application.ActiveDocument != null));

            Navisworks.Document document = Application.ActiveDocument;
            if (document == null || document.Tool == null)
            {
                DiagnosticLog.Warn("Документ не открыт — режим отметки не включается.");
                return 1;
            }

            // Повторное нажатие — выключение. Иначе кнопка только включала бы режим,
            // а выключать пришлось бы Esc, о чём не написано в подсказке.
            if (IsProbeActive())
            {
                DiagnosticLog.Info("Режим уже включён — выключаю.");
                RestorePreviousTool();
                return 0;
            }

            try
            {
                Navisworks.Tool previousTool = document.Tool.Value;

                DiagnosticLog.Info("Включаю режим отметки. Предыдущий инструмент: " + previousTool
                                  + ", ищу инструмент плагина: " + PluginIds.ToolPluginFullId);

                // Экземпляр инструмента обязан создавать Navisworks, а не мы.
                // Базовый класс Plugin хранит ссылку на PluginRecord, и у объекта,
                // созданного через new, она null — SetCustomToolPlugin падает с
                // NullReferenceException, ничем не объясняя это.
                PluginRecord record = Application.Plugins.FindPlugin(PluginIds.ToolPluginFullId);
                if (record == null)
                {
                    DiagnosticLog.Error("Инструмент плагина не найден в реестре Navisworks. Проверь, "
                                        + "что у класса ElevationProbeTool есть атрибут [Plugin] с id \""
                                        + PluginIds.ToolId + "\" и id разработчика \"" + PluginIds.DeveloperId
                                        + "\", а также что сборка загружена.", null);
                    return 3;
                }

                ToolPlugin tool = record.LoadPlugin() as ToolPlugin;
                if (tool == null)
                {
                    DiagnosticLog.Error("Navisworks не смог создать инструмент плагина. Тип: "
                                        + record.Id, null);
                    return 3;
                }

                // Инструмент создан Navisworks, поэтому передать ему предыдущий инструмент
                // в конструктор невозможно — отдаём через статическое поле.
                ElevationProbeTool.PendingPreviousTool = previousTool;

                document.Tool.SetCustomToolPlugin(tool);
                return 0;
            }
            catch (System.Exception exception)
            {
                DiagnosticLog.Error("Не удалось включить режим отметки.", exception);
                return 2;
            }
        }

        public override CommandState CanExecute()
        {
            Navisworks.Document document = Application.ActiveDocument;

            return new CommandState
            {
                IsEnabled = document != null && document.Tool != null,
                IsVisible = true,
                IsChecked = IsProbeActive(),
                OverrideDisplayName = IsProbeActive() ? "Выключить отметку" : "Показать отметку"
            };
        }

        private static bool IsProbeActive()
        {
            try
            {
                Navisworks.Document document = Application.ActiveDocument;
                if (document == null || document.Tool == null)
                {
                    return false;
                }

                // Идентификатор возвращается в виде "<id>.<id разработчика>", поэтому
                // сравнивать его с одним только PluginIds.ToolId бесполезно — не совпадёт
                // никогда и режим всегда будет считаться выключенным.
                return document.Tool.CustomToolPluginId == PluginIds.ToolPluginFullId;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Описание версии Navisworks для диагностического лога.
        /// </summary>
        /// <remarks>
        /// Отдельный метод, потому что у <c>ApplicationVersion</c> нет переопределения
        /// <c>ToString()</c>: в лог попадает имя типа вместо версии, а разбираться с этим
        /// каждый раз не хочется.
        /// </remarks>
        private static string DescribeEnvironment()
        {
            try
            {
                var version = Application.Version;
                return "Navisworks " + version.RuntimeProductName + " " + version.RuntimeMajor + "."
                       + version.RuntimeMinor + ", API " + version.ApiMajor + "." + version.ApiMinor;
            }
            catch (System.Exception exception)
            {
                return "Navisworks версия неизвестна (" + exception.GetType().Name + ")";
            }
        }

        /// <summary>
        /// Выключить режим отметки повторным нажатием кнопки.
        /// </summary>
        /// <remarks>
        /// Возвращается не жёстко <c>CommonOrbit</c>, а тот инструмент, который был активен
        /// до включения режима — он хранится в <see cref="ElevationProbeTool.PendingPreviousTool"/>.
        /// </remarks>
        private static void RestorePreviousTool()
        {
            try
            {
                Navisworks.Document document = Application.ActiveDocument;
                if (document != null && document.Tool != null)
                {
                    Navisworks.Tool restore = ElevationProbeTool.PendingPreviousTool == Navisworks.Tool.None
                                              || ElevationProbeTool.PendingPreviousTool == Navisworks.Tool.CustomToolPlugin
                        ? Navisworks.Tool.CommonOrbit
                        : ElevationProbeTool.PendingPreviousTool;

                    document.Tool.Set(restore);
                    DiagnosticLog.Info("Режим выключен кнопкой, инструмент переключён на: " + restore);
                }
            }
            catch (System.Exception exception)
            {
                DiagnosticLog.Error("Не удалось вернуть стандартный инструмент.", exception);
            }
        }
    }
}
