using System;
using Autodesk.Navisworks.Api.Plugins;
using NavisElevation.Core;
using NavisElevation.Navis.Views;

namespace NavisElevation.Navis.Ribbon
{
    /// <summary>
    /// Собственная вкладка плагина на ленте Navisworks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Класс обязан наследовать <c>CommandHandlerPlugin</c>, а не <c>AddInPlugin</c>:
    /// только <c>CommandHandlerPlugin</c> несёт <c>[RibbonLayout]</c> и <c>[RibbonTab]</c>.
    /// Наследование в C# одинарное, поэтому отдельный класс для встроенной вкладки
    /// Add-Ins невозможен — отсюда и решение обойтись только своей вкладкой.
    /// </para>
    /// <para>
    /// <b>Кнопок во встроенной вкладке Add-Ins нет</b>, и это осознанно: плагин живёт
    /// на своей вкладке. Чтобы команда попала в Add-Ins, нужен атрибут
    /// <c>[AddInPlugin(AddInLocation.AddIn)]</c>, а его у класса ленты нет. Плата за это
    /// названа в разделе рисков плана: если Navisworks перестанет читать файл разметки,
    /// кнопок не будет нигде, и лога тоже не будет — сборка просто не загрузится.
    /// </para>
    /// <para>
    /// <b>Где Navisworks ищет файл разметки.</b> Это не ресурс внутри сборки, а файл
    /// в языковой подпапке рядом с плагином: <c>ru-RU</c> для русского интерфейса,
    /// <c>en-US</c> как запасной. Так описано в блоге Autodesk про ленту Navisworks:
    /// «the xaml and name file must reside in the sub-folder of the plug-in dll. The
    /// sub-folder name needs to be a Language Code». Файл <c>*.name</c> в той же папке
    /// задаёт локализованные названия, а атрибут <c>[Strings]</c> указывает, какой
    /// файл искать. Проверено на этой машине: разметка в папке рядом со сборкой работает,
    /// в корне бандла — нет.
    /// </para>
    /// </remarks>
    [Plugin(PluginIds.RibbonPluginId, PluginIds.DeveloperId, DisplayName = "Navis Elevation")]
    [RibbonLayout("RibbonDefinition.xaml")]
    [RibbonTab(PluginIds.TabId, DisplayName = "Navis Elevation")]
    [Strings("NavisElevation.name")]
    [Command(PluginIds.ToggleCommandId, DisplayName = "Показать отметку", CanToggle = true,
        Icon = "Elevation16.png", LargeIcon = "Elevation32.png",
        LoadForCanExecute = true,
        ToolTip = "Показать отметку элемента",
        ExtendedToolTip = "Включает режим показа отметки элемента.\r\n\r\n"
                        + "После включения наведите курсор на любой элемент модели — рядом с ним "
                        + "появится отметка, границы по высоте и имя модели.\r\n\r\n"
                        + "Выключить режим можно повторным нажатием кнопки, клавишей Esc "
                        + "или правой кнопкой мыши.")]
    [Command(PluginIds.SettingsCommandId, DisplayName = "Настройки плагина", CanToggle = false,
        Icon = "Settings16.png", LargeIcon = "Settings32.png",
        ToolTip = "Настройки плагина",
        ExtendedToolTip = "Открывает окно настроек: версия плагина и проверка обновлений.")]
    public class ElevationRibbon : CommandHandlerPlugin
    {
        /// <summary>Последнее записанное состояние кнопок, чтобы не писать в лог каждый раз.</summary>
        private static string _lastState;

        /// <summary>
        /// Сработает в момент, когда Navisworks впервые обратится к этому типу.
        /// </summary>
        /// <remarks>
        /// Единственный признак того, что сборка вообще оказалась в процессе. Без него
        /// при сломанной разметке логи не было бы совсем: плагин не загрузился, и
        /// спросить его негде. Класс ленты Navisworks создаёт лениво, при первом
        /// нажатии кнопки, так что отметка может появиться не сразу.
        /// </remarks>
        static ElevationRibbon()
        {
            DiagnosticLog.RecordPluginLoad(typeof(ElevationRibbon).FullName);
        }

        protected override void OnLoaded()
        {
            DiagnosticLog.Info("Класс ленты загружен, OnLoaded. " + ProbeToggle.DescribeEnvironment()
                               + ", файл разметки: RibbonDefinition.xaml");
        }

        public override int ExecuteCommand(string commandId, string[] parameters)
        {
            DiagnosticLog.Info("Команда ленты вызвана: " + commandId);

            if (string.Equals(commandId, PluginIds.ToggleCommandId, StringComparison.Ordinal))
            {
                return ProbeToggle.Toggle();
            }

            if (string.Equals(commandId, PluginIds.SettingsCommandId, StringComparison.Ordinal))
            {
                return SettingsView.Open();
            }

            DiagnosticLog.Warn("Неизвестная команда ленты: " + commandId);
            return 1;
        }

        public override CommandState CanExecuteCommand(string commandId)
        {
            bool isToggle = string.Equals(commandId, PluginIds.ToggleCommandId, StringComparison.Ordinal);

            // «Показать отметку» без открытой модели делать нечего, поэтому гасим.
            // «Настройки плагина» от модели не зависят: версию плагина можно посмотреть
            // и без неё.
            bool enabled = !Autodesk.Navisworks.Api.Application.IsAutomated
                           && (!isToggle || ProbeToggle.HasModel());

            LogStateChange(commandId, isToggle, enabled);

            return new CommandState
            {
                IsEnabled = enabled,
                IsVisible = true,
                IsChecked = isToggle && ProbeToggle.IsActive(),
                OverrideDisplayName = isToggle && ProbeToggle.IsActive()
                    ? "Выключить отметку"
                    : "Показать отметку"
            };
        }

        /// <summary>
        /// Записать в лог, что состояние кнопки изменилось.
        /// </summary>
        /// <remarks>
        /// Само состояние Navisworks спрашивает часто, а писать каждый раз - значит
        /// засорять лог. Поэтому пишем только при смене состояния: так видно, вызывает ли
        /// Navisworks CanExecuteCommand и обновляет ли кнопку при открытии модели.
        /// </remarks>
        private static void LogStateChange(string commandId, bool isToggle, bool enabled)
        {
            string key = commandId + ":" + enabled;

            if (string.Equals(key, _lastState, StringComparison.Ordinal))
            {
                return;
            }

            _lastState = key;

            DiagnosticLog.Info("Состояние кнопки «" + (isToggle ? "Показать отметку" : commandId)
                               + "»: " + (enabled ? "активна" : "серая")
                               + (isToggle && !enabled ? " (нет открытой модели)" : ""));
        }
    }
}