using System;
using System.Globalization;
using System.IO;

namespace NavisElevation.Core
{
    /// <summary>
    /// Настройки плагина, которые пользователь может поправить руками.
    /// </summary>
    /// <remarks>
    /// Сейчас здесь одно поле — калибровочное смещение отметки. В API Navisworks нет
    /// базовой точки проекта, поэтому правильность отметки проверяется сверкой с
    /// эталонным элементом, и разница между показанным и ожидаемым числом и есть это смещение.
    ///
    /// Формат файла простой, «ключ=значение», без библиотек и без JSON: настройка одна,
    /// а лишняя зависимость в .NET Framework-плагине — это лишний риск при загрузке.
    /// Любая ошибка чтения или записи молча сводится к значениям по умолчанию: плагин
    /// не должен падать из-за настроек.
    /// </remarks>
    public sealed class ProjectSettings
    {
        private const string OffsetKey = "ProjectOffset";

        /// <summary>Смещение в единицах документа. По умолчанию ноль.</summary>
        public double ProjectOffset { get; set; }

        /// <summary>
        /// Путь к файлу настроек: <c>%APPDATA%\NavisElevation\settings.txt</c>.
        /// </summary>
        /// <remarks>
        /// <c>%APPDATA%</c>, а не Program Files: настройка принадлежит пользователю,
        /// и запись туда не требует прав администратора.
        /// </remarks>
        public static string DefaultPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "NavisElevation",
                    "settings.txt");
            }
        }

        public static ProjectSettings Load(string path)
        {
            var settings = new ProjectSettings();
            string file = string.IsNullOrEmpty(path) ? DefaultPath : path;

            try
            {
                if (!File.Exists(file))
                {
                    return settings;
                }

                foreach (string rawLine in File.ReadAllLines(file))
                {
                    string line = rawLine == null ? null : rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int separator = line.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    string key = line.Substring(0, separator).Trim();
                    string value = line.Substring(separator + 1).Trim();

                    if (!string.Equals(key, OffsetKey, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    double parsed;
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                    {
                        settings.ProjectOffset = parsed;
                    }
                }
            }
            catch (IOException)
            {
                // Файла нет или он занят — работаем с нулевым смещением.
            }
            catch (UnauthorizedAccessException)
            {
            }

            return settings;
        }
    }
}
