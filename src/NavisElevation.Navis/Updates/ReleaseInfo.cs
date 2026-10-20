using System;
using System.Collections.Generic;

namespace NavisElevation.Navis.Updates
{
    /// <summary>
    /// Один файл в релизе на GitHub.
    /// </summary>
    public sealed class ReleaseAsset
    {
        /// <summary>Имя файла, как он лежит в релизе.</summary>
        public string Name { get; set; }

        /// <summary>Прямая ссылка на скачивание.</summary>
        public string Url { get; set; }

        /// <summary>Размер файла в байтах. Ноль, если GitHub его не сообщил.</summary>
        public long Size { get; set; }
    }

    /// <summary>
    /// Релиз плагина на GitHub.
    /// </summary>
    public sealed class ReleaseInfo
    {
        /// <summary>Версия без буквы «v», например <c>1.0.0</c>.</summary>
        public string Version { get; set; }

        /// <summary>Тег релиза целиком, например <c>v1.0.0</c>.</summary>
        public string TagName { get; set; }

        /// <summary>Примечание к релизу — то, что автор написал на GitHub.</summary>
        public string ReleaseNotes { get; set; }

        /// <summary>Файлы релиза.</summary>
        public List<ReleaseAsset> Assets { get; set; }
    }
}
