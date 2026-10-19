namespace NavisElevation.Navis
{
    /// <summary>
    /// Идентификаторы плагинов в Navisworks.
    /// </summary>
    /// <remarks>
    /// В Navisworks плагины находятся не по файлу, а по атрибуту <c>[Plugin]</c>:
    /// первый аргумент — уникальный id, второй — идентификатор разработчика.
    /// Navisworks читает сборку из бандла в <c>%APPDATA%\Autodesk\ApplicationPlugins</c>,
    /// находит в ней эти атрибуты и регистрирует плагины, поэтому имя файла на
    /// идентификаторы не влияет.
    /// Autodesk для своих плагинов использует <c>ADSK</c>; сторонним разработчикам
    /// положено подставить свой идентификатор вендора — здесь пока имя проекта.
    /// </remarks>
    internal static class PluginIds
    {
        /// <summary>Идентификатор разработчика (второй аргумент [Plugin]).</summary>
        public const string DeveloperId = "new-navis";

        /// <summary>Сам инструмент, который следит за курсором.</summary>
        public const string ToolId = "new-navis.ElevationProbeTool";

        /// <summary>
        /// Полный идентификатор инструмента в виде, в котором его ищет Navisworks.
        /// </summary>
        /// <remarks>
        /// Именно в таком виде идентификатор возвращает <c>Document.Tool.CustomToolPluginId</c>
        /// и именно в таком виде его принимает <c>Application.Plugins.FindPlugin</c> — с точкой
        /// между id плагина и id разработчика. Сравнение с одним только <see cref="ToolId"/>
        /// никогда не сойдётся.
        /// </remarks>
        public const string ToolPluginFullId = ToolId + "." + DeveloperId;

        /// <summary>Класс собственной вкладки плагина на ленте.</summary>
        /// <remarks>
        /// Отдельный от <see cref="ToolId"/>: идентификатор плагина и идентификатор
        /// инструмента — разные вещи. У встроенных плагинов Navisworks так же: id класса
        /// <c>GlueCommandHandler</c> и id вкладки <c>Glue_BIM360Tab</c> не совпадают.
        /// </remarks>
        public const string RibbonPluginId = "NavisElevation.Ribbon";

        /// <summary>Своя вкладка плагина на ленте.</summary>
        /// <remarks>
        /// Этот id обязан совпадать с атрибутом <c>Id</c> элемента <c>RibbonTab</c> в
        /// <c>Ribbon\RibbonDefinition.xaml</c> и со значением в <c>[RibbonTab]</c>.
        /// Расхождение означает, что вкладка молча не появляется, без всякой ошибки.
        /// </remarks>
        public const string TabId = "NavisElevation.Tab";

        /// <summary>Кнопка «Показать отметку» на собственной вкладке.</summary>
        /// <remarks>
        /// Этот id обязан совпадать с атрибутом <c>Id</c> кнопки в разметке. Кнопки нет
        /// во встроенной вкладке Add-Ins: чтобы она там появилась, нужен атрибут
        /// <c>[AddInPlugin(AddInLocation.AddIn)]</c>, а его у класса ленты нет.
        /// </remarks>
        public const string ToggleCommandId = "NavisElevation.ToggleCommand";

        /// <summary>Кнопка «Настройки» на собственной вкладке.</summary>
        public const string SettingsCommandId = "NavisElevation.SettingsCommand";
    }
}