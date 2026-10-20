using System;

namespace NavisElevation.Core
{
    /// <summary>
    /// Разбор и сравнение номеров версий вида <c>major.minor.patch</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Нужен механизму обновления: он спрашивает у GitHub список релизов и должен
    /// понять, какой из них новее той версии, что стоит у пользователя. Сравнивать
    /// строки обычным <c> string.Compare </c> нельзя — «0.10.0» окажется меньше
    /// «0.9.0», потому что десять идёт раньше девяти по алфавиту.
    /// </para>
    /// <para>
    /// Класс живёт в ядре, а не в проекте плагина, потому что не зависит ни от
    /// Navisworks, ни от .NET Framework: это чистые строки и целые числа. Ядро
    /// вшивается в сборку плагина, поэтому тип не дублируется.
    /// </para>
    /// <para>
    /// Класс перенесён из плагина для Revit (<c>AssemblingManager.Core.Utils.PluginVersion</c>)
    /// почти без изменений: там схема уже обкатана.
    /// </para>
    /// </remarks>
    public static class PluginVersion
    {
        /// <summary>
        /// Разобрать строку версии на три числа.
        /// </summary>
        /// <param name="text">
        /// Версия в любом из видов: <c>1.2.3</c>, <c>v1.2.3</c>, <c>1.2</c>,
        /// <c>1.2.3+хвост</c> (хеш коммита, который дописывает сборка SDK).
        /// </param>
        /// <param name="major">Старшая часть.</param>
        /// <param name="minor">Младшая часть.</param>
        /// <param name="patch">Патч.</param>
        /// <returns>Разобралось ли.</returns>
        public static bool TryParse(string text, out int major, out int minor, out int patch)
        {
            major = 0;
            minor = 0;
            patch = 0;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string trimmed = text.Trim().TrimStart('v', 'V');

            // Сборка дописывает к версии хеш коммита: "1.0.0+abcdef". Пользователю он
            // не нужен, для сравнения он тоже мешает.
            int plusIndex = trimmed.IndexOf('+');
            if (plusIndex >= 0)
            {
                trimmed = trimmed.Substring(0, plusIndex);
            }

            string[] parts = trimmed.Split('.');
            if (parts.Length == 0 || parts.Length > 3)
            {
                return false;
            }

            for (int i = 0; i < parts.Length; i++)
            {
                int value;
                if (!int.TryParse(parts[i], out value) || value < 0)
                {
                    return false;
                }

                if (i == 0) major = value;
                else if (i == 1) minor = value;
                else patch = value;
            }

            return true;
        }

        /// <summary>
        /// Сравнить две версии: больше нуля, если <paramref name="left"/> новее.
        /// </summary>
        /// <remarks>
        /// Если хотя бы одну версию разобрать не удалось, сравнение идёт как по
        /// строкам. Это лучше, чем выбросить исключение: версия приходит из
        /// интернета, и непонятная строка не должна ронять проверку обновлений.
        /// </remarks>
        public static int Compare(string left, string right)
        {
            bool leftParsed = TryParse(left, out int leftMajor, out int leftMinor, out int leftPatch);
            bool rightParsed = TryParse(right, out int rightMajor, out int rightMinor, out int rightPatch);

            if (!leftParsed || !rightParsed)
            {
                return string.CompareOrdinal(left, right);
            }

            int result = leftMajor.CompareTo(rightMajor);
            if (result != 0) return result;

            result = leftMinor.CompareTo(rightMinor);
            if (result != 0) return result;

            return leftPatch.CompareTo(rightPatch);
        }
    }
}
