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

        /// <summary>Кнопка на ленте, включающая режим отметки.</summary>
        public const string CommandId = "new-navis.ElevationProbeCommand";

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
    }
}
