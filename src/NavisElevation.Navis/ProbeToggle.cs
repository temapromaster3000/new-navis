using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using NavisElevation.Core;
using Navisworks = Autodesk.Navisworks.Api;

namespace NavisElevation.Navis
{
    /// <summary>
    /// Включение и выключение режима отметки — общая логика для всех кнопок.
    /// </summary>
    /// <remarks>
    /// Кнопка есть в двух местах: во встроенной вкладке Add-Ins (класс
    /// <see cref="ElevationProbeCommand"/>) и на собственной вкладке плагина
    /// (класс <see cref="Ribbon.ElevationRibbon"/>). Логика живёт здесь, чтобы
    /// не было двух копий, которые со временем разойдутся.
    /// </remarks>
    internal static class ProbeToggle
    {
        /// <summary>Включён ли сейчас режим отметки.</summary>
        public static bool IsActive()
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

        /// <summary>Открыта ли модель, которую вообще есть о чём показывать.</summary>
        /// <remarks>
        /// Проверять надо не документ, а модель. В Navisworks пустой документ «Без имени» -
        /// это тоже документ, <c>Application.ActiveDocument</c> в нём не null, но моделей в
        /// нём ноль. Отсюда и ошибка: кнопка считалась активной, а режим включался в
        /// пустоте и ничего не находил.
        /// </remarks>
        public static bool HasModel()
        {
            try
            {
                Navisworks.Document document = Application.ActiveDocument;

                return document != null
                       && document.Models != null
                       && document.Models.Count > 0;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        /// <summary>Переключить режим отметки: включить, а если включён — выключить.</summary>
        public static int Toggle()
        {
            DiagnosticLog.Info("Кнопка отметки нажата. " + DescribeEnvironment()
                               + ", модель открыта: " + HasModel());

            Navisworks.Document document = Application.ActiveDocument;
            if (document == null || document.Tool == null)
            {
                DiagnosticLog.Warn("Документ не открыт — режим отметки не включается.");
                return 1;
            }

            if (!HasModel())
            {
                DiagnosticLog.Warn("Нет открытой модели — режим отметки не включается.");
                return 1;
            }

            // Повторное нажатие — выключение. Иначе кнопка только включала бы режим,
            // а выключать пришлось бы Esc, о чём не написано в подсказке.
            if (IsActive())
            {
                DiagnosticLog.Info("Режим уже включён — выключаю.");
                RestorePreviousTool();
                return 0;
            }

            return Activate(document);
        }

        /// <summary>
        /// Включить режим отметки, если он ещё не включён.
        /// </summary>
        /// <remarks>
        /// Нужен калибровке: режим выбора точки живёт в том же инструменте, что и
        /// показ отметки, а включается отдельной кнопкой на ленте.
        /// </remarks>
        public static int EnsureActive()
        {
            if (IsActive())
            {
                return 0;
            }

            Navisworks.Document document = Application.ActiveDocument;

            if (document == null || document.Tool == null)
            {
                return 1;
            }

            return Activate(document);
        }

        /// <summary>
        /// Перевключить инструмент плагина, не теряя запомненный предыдущий.
        /// </summary>
        /// <remarks>
        /// Нужен как последний довод, когда фокус в 3D-вид перевести не удалось:
        /// Navisworks при переключении инструмента сам возвращает фокус в вид.
        /// </remarks>
        public static int Reactivate()
        {
            Navisworks.Document document = Application.ActiveDocument;

            if (document == null || document.Tool == null)
            {
                return 1;
            }

            return Activate(document);
        }

        /// <summary>Включить режим отметки.</summary>
        internal static int Activate(Navisworks.Document document)
        {
            try
            {
                Navisworks.Tool previousTool = document.Tool.Value;

                // Handle главного окна запоминаем заранее: после закрытия окон плагина
                // API Navisworks уже недоступен, а handle нужен, чтобы вернуть фокус.
                ViewFocus.RememberMainWindow();

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
                // Переключение инструмента на сам наш не считается новым предыдущим:
                // иначе при перевключении терялся бы инструмент, который был до нас.
                if (previousTool != Navisworks.Tool.CustomToolPlugin)
                {
                    ElevationProbeTool.PendingPreviousTool = previousTool;
                }

                ElevationProbeTool.ResetEmptyReport();

                document.Tool.SetCustomToolPlugin(tool);

                // Клавиатурный фокус после нажатия кнопки остаётся на ленте, и Esc до
                // плагина не доходит. Возвращаем фокус в 3D-вид, чтобы режим можно было
                // выключить сразу, не кликая в вид.
                bool focused = ViewFocus.FocusActiveView(Application.Gui.MainWindow.Handle);
                DiagnosticLog.Info(focused
                    ? "Фокус передан в 3D-вид, Esc будет работать."
                    : "Фокус в 3D-вид не перешёл, Esc сработает после клика в вид.");

                return 0;
            }
            catch (System.Exception exception)
            {
                DiagnosticLog.Error("Не удалось включить режим отметки.", exception);
                return 2;
            }
        }

        /// <summary>Вернуть инструмент, который был активен до включения режима.</summary>
        public static void RestorePreviousTool()
        {
            // Режим выключили — значит, и выбор точки калибровки отменяется. Иначе
            // состояние «идёт калибровка» залипнет, и кнопка «Показать отметку» не
            // будет подсвечиваться при следующем включении.
            Calibration.CalibrationSession.End();

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
                    DiagnosticLog.Info("Инструмент переключён на: " + restore);
                }
            }
            catch (System.Exception exception)
            {
                DiagnosticLog.Error("Не удалось вернуть стандартный инструмент.", exception);
            }
        }

        /// <summary>Описание версии Navisworks для диагностического лога.</summary>
        /// <remarks>
        /// Отдельный метод, потому что у <c>ApplicationVersion</c> нет переопределения
        /// <c>ToString()</c>: в лог попадает имя типа вместо версии, а разбираться с этим
        /// каждый раз не хочется.
        /// </remarks>
        public static string DescribeEnvironment()
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
    }
}