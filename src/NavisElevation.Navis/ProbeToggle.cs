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

        /// <summary>Переключить режим отметки: включить, а если включён — выключить.</summary>
        public static int Toggle()
        {
            DiagnosticLog.Info("Кнопка отметки нажата. " + DescribeEnvironment()
                               + ", документ открыт: " + (Application.ActiveDocument != null));

            Navisworks.Document document = Application.ActiveDocument;
            if (document == null || document.Tool == null)
            {
                DiagnosticLog.Warn("Документ не открыт — режим отметки не включается.");
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

        /// <summary>Включить режим отметки.</summary>
        private static int Activate(Navisworks.Document document)
        {
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

        /// <summary>Вернуть инструмент, который был активен до включения режима.</summary>
        public static void RestorePreviousTool()
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