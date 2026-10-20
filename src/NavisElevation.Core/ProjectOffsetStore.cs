using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace NavisElevation.Core
{
    /// <summary>
    /// Хранилище калибровок отметки: по одной записи на модель.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Файл <c>%APPDATA%\NavisElevation\offsets.ini</c> — обычный текст в UTF-8, формат
    /// «раздел + ключ=значение», как в настройках. Библиотеки не нужны: в .NET
    /// Framework-плагине лишняя зависимость — лишний риск при загрузке сборки.
    /// </para>
    /// <para>
    /// В API Navisworks нет способа записать что-либо внутрь NWF: у <c>ModelItem</c> нет
    /// <c>SetNamedData</c>, а у <c>Plugin</c> — только чтение строк. Поэтому калибровка
    /// живёт на стороне пользователя и при переносе модели коллеге не передаётся.
    /// </para>
    /// <para>
    /// Запись привязана к модели двумя ключами сразу: путь к файлу и Guid модели внутри
    /// него. Guid переживает переименование и перемещение файла, а путь спасает, когда
    /// модель перелинковали и Guid сменился. Поиск идёт сначала по Guid, потом по пути.
    /// </para>
    /// <para>
    /// Любая ошибка чтения или записи молча сводится к пустому хранилищу: плагин не
    /// должен падать из-за настроек.
    /// </para>
    /// </remarks>
    public sealed class ProjectOffsetStore
    {
        private const string PathKey = "Path";
        private const string GuidKey = "Guid";
        private const string OffsetKey = "Offset";

        private readonly List<Record> _records = new List<Record>();

        /// <summary>
        /// Одна калибровка: путь к файлу модели, Guid модели и смещение в метрах.
        /// </summary>
        public sealed class Record
        {
            /// <summary>Полный путь к файлу модели. Может быть пустым.</summary>
            public string ModelPath { get; set; }

            /// <summary>Guid модели внутри NWF. Может быть пустым.</summary>
            public string ModelGuid { get; set; }

            /// <summary>Смещение отметки в метрах, прибавляемое после перевода в метры.</summary>
            public double Offset { get; set; }
        }

        /// <summary>
        /// Путь к файлу хранилища: <c>%APPDATA%\NavisElevation\offsets.ini</c>.
        /// </summary>
        /// <remarks>
        /// <c>%APPDATA%</c>, а не Program Files: файл принадлежит пользователю, и запись
        /// туда не требует прав администратора.
        /// </remarks>
        public static string DefaultPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "NavisElevation",
                    "offsets.ini");
            }
        }

        /// <summary>Все записи хранилища.</summary>
        public IReadOnlyList<Record> Records
        {
            get { return _records.AsReadOnly(); }
        }

        /// <summary>
        /// Прочитать хранилище из файла. Любая ошибка даёт пустое хранилище.
        /// </summary>
        public static ProjectOffsetStore Load(string path)
        {
            var store = new ProjectOffsetStore();
            string file = string.IsNullOrEmpty(path) ? DefaultPath : path;

            try
            {
                if (!File.Exists(file))
                {
                    return store;
                }

                Record current = null;

                foreach (string rawLine in File.ReadAllLines(file))
                {
                    string line = rawLine == null ? null : rawLine.Trim();

                    if (string.IsNullOrEmpty(line) || line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                    {
                        current = new Record();
                        store._records.Add(current);
                        continue;
                    }

                    int separator = line.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    string key = line.Substring(0, separator).Trim();
                    string value = line.Substring(separator + 1).Trim();

                    // Ключи вне раздела бессмысленны: без раздела некуда их записать.
                    if (current == null)
                    {
                        continue;
                    }

                    if (string.Equals(key, PathKey, StringComparison.OrdinalIgnoreCase))
                    {
                        current.ModelPath = value;
                    }
                    else if (string.Equals(key, GuidKey, StringComparison.OrdinalIgnoreCase))
                    {
                        current.ModelGuid = value;
                    }
                    else if (string.Equals(key, OffsetKey, StringComparison.OrdinalIgnoreCase))
                    {
                        double parsed;
                        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                        {
                            current.Offset = parsed;
                        }
                    }
                }
            }
            catch (IOException)
            {
                // Файла нет или он занят — работаем без калибровок.
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (NotSupportedException)
            {
            }

            return store;
        }

        /// <summary>Запись по Guid модели; null, если такой записи нет.</summary>
        public Record FindByGuid(string modelGuid)
        {
            if (string.IsNullOrEmpty(modelGuid))
            {
                return null;
            }

            foreach (Record record in _records)
            {
                if (string.Equals(record.ModelGuid, modelGuid, StringComparison.OrdinalIgnoreCase))
                {
                    return record;
                }
            }

            return null;
        }

        /// <summary>
        /// Запись по пути к файлу модели; null, если такой записи нет.
        /// </summary>
        /// <remarks>
        /// Путь сравнивается без учёта регистра и в нормализованном виде: один и тот же
        /// файл, открытый разными способами, должен давать одну и ту же запись.
        /// </remarks>
        public Record FindByPath(string modelPath)
        {
            string wanted = NormalizePath(modelPath);

            if (string.IsNullOrEmpty(wanted))
            {
                return null;
            }

            foreach (Record record in _records)
            {
                if (string.Equals(NormalizePath(record.ModelPath), wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return record;
                }
            }

            return null;
        }

        /// <summary>
        /// Создать или обновить запись модели и вернуть её.
        /// </summary>
        /// <remarks>
        /// Запись ищется по Guid, а если он не задан — по пути. Это позволяет вызвать
        /// метод повторно для той же модели и не плодить дубликаты.
        /// </remarks>
        public Record Save(string modelPath, string modelGuid, double offset)
        {
            Record record = FindByGuid(modelGuid) ?? FindByPath(modelPath);

            if (record == null)
            {
                record = new Record();
                _records.Add(record);
            }

            record.ModelPath = modelPath;
            record.ModelGuid = modelGuid;
            record.Offset = offset;

            return record;
        }

        /// <summary>Удалить запись. Возвращает false, если такой записи не было.</summary>
        public bool Remove(Record record)
        {
            return record != null && _records.Remove(record);
        }

        /// <summary>
        /// Записать хранилище в файл.
        /// </summary>
        /// <remarks>
        /// Запись атомарная: сначала во временный файл, потом замена. Если Navisworks
        /// или компьютер выключатся в середине, старый файл останется целым.
        /// </remarks>
        public void Flush(string path)
        {
            string file = string.IsNullOrEmpty(path) ? DefaultPath : path;

            try
            {
                string folder = Path.GetDirectoryName(file);
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                string temp = file + ".tmp";
                File.WriteAllText(temp, BuildText(), new UTF8Encoding(false));

                if (File.Exists(file))
                {
                    try
                    {
                        File.Replace(temp, file, null);
                        return;
                    }
                    catch (IOException)
                    {
                        // Замена не вышла — падаем на удаление и перенос.
                    }
                    catch (PlatformNotSupportedException)
                    {
                    }

                    File.Delete(file);
                }

                File.Move(temp, file);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private string BuildText()
        {
            var text = new StringBuilder();
            text.AppendLine("# Navis Elevation - калибровка отметки по моделям.");
            text.AppendLine("#");
            text.AppendLine("# Формат: раздел [model] и ключи Path, Guid, Offset.");
            text.AppendLine("# Offset - смещение в метрах, прибавляемое к отметке после перевода в метры.");
            text.AppendLine("#");

            int index = 0;

            foreach (Record record in _records)
            {
                index++;

                string section = string.IsNullOrEmpty(record.ModelGuid)
                    ? "model" + index.ToString(CultureInfo.InvariantCulture)
                    : record.ModelGuid;

                text.AppendLine("[" + section + "]");
                text.AppendLine(PathKey + "=" + (record.ModelPath ?? string.Empty));
                text.AppendLine(GuidKey + "=" + (record.ModelGuid ?? string.Empty));
                text.AppendLine(OffsetKey + "="
                                + record.Offset.ToString("0.###", CultureInfo.InvariantCulture));
                text.AppendLine();
            }

            return text.ToString();
        }

        /// <summary>
        /// Привести путь к виду, по которому его можно сравнивать.
        /// </summary>
        /// <remarks>
        /// Некорректный путь не роняет плагин: возвращается исходная строка, и запись
        /// просто не найдётся.
        /// </remarks>
        private static string NormalizePath(string modelPath)
        {
            if (string.IsNullOrEmpty(modelPath))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(modelPath);
            }
            catch (ArgumentException)
            {
                return modelPath;
            }
            catch (NotSupportedException)
            {
                return modelPath;
            }
            catch (PathTooLongException)
            {
                return modelPath;
            }
        }
    }
}
