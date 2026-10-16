using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace NavisElevation.Core
{
    /// <summary>
    /// Диагностический лог плагина в файле.
    /// </summary>
    /// <remarks>
    /// Нужен по конкретной причине: агент не может запустить Navisworks и проверить
    /// плагин глазами. Единственный способ узнать, что происходит внутри, — заставить
    /// плагин самому писать о себе в файл: грузилась ли сборка, появилась ли кнопка,
    /// работает ли захват элемента под курсором.
    ///
    /// Файл пишется в <c>%LOCALAPPDATA%\NavisElevation\plugin.log</c>, а не рядом с
    /// плагином: папка бандла доступна только текущему пользователю, а Navisworks может
    /// быть запущен не от твоего пользователя. События мыши идут с частотой кадров,
    /// поэтому пишем по существу: загрузку сборки, нажатие кнопки, переключение
    /// инструмента и ошибки, но не каждый найденный под курсором элемент.
    /// </remarks>
    public static class DiagnosticLog
    {
        private const long MaxBytes = 512 * 1024;

        private static readonly object Gate = new object();

        private static bool _sessionStarted;

        /// <summary>Путь к файлу лога.</summary>
        public static string FilePath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NavisElevation",
                    "plugin.log");
            }
        }

        /// <summary>
        /// Отметить, что Navisworks загрузил сборку плагина.
        /// </summary>
        /// <remarks>
        /// Вызывается из статического конструктора класса плагина, то есть в тот самый
        /// момент, когда Navisworks впервые обращается к типу. Это единственная точка,
        /// которая отвечает на вопрос «загрузилась ли вообще сборка»: раньше лог
        /// начинался только по нажатию кнопки, и по его отсутствию нельзя было понять
        /// ничего — кнопки без загруженной сборки и не бывает.
        /// </remarks>
        public static void RecordPluginLoad(string typeName)
        {
            lock (Gate)
            {
                if (!_sessionStarted)
                {
                    StartSession();
                }
            }

            Info("Сборка плагина загружена процессом Navisworks: " + typeName);
        }

        /// <summary>
        /// Начать новую сессию: стереть старый лог.
        /// </summary>
        /// <remarks>
        /// Вызывается автоматически при первой загрузке сборки. Если стирание не удалось,
        /// просто дописываем — это не повод прерывать работу плагина.
        /// </remarks>
        public static void StartSession()
        {
            lock (Gate)
            {
                try
                {
                    string path = FilePath;
                    string directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.WriteAllText(path, Header(), Encoding.UTF8);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }

                _sessionStarted = true;
            }
        }

        public static void Info(string message)
        {
            Write("INFO", message, null);
        }

        public static void Warn(string message)
        {
            Write("WARN", message, null);
        }

        public static void Error(string message, Exception exception)
        {
            Write("ERROR", message, exception);
        }

        private static void Write(string level, string message, Exception exception)
        {
            lock (Gate)
            {
                try
                {
                    string path = FilePath;
                    string directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    // Лог не должен расти бесконечно и забивать диск.
                    if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                    {
                        File.WriteAllText(path, Header(), Encoding.UTF8);
                    }

                    StringBuilder line = new StringBuilder();
                    line.Append(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
                    line.Append(" [").Append(level).Append("] ");
                    line.Append(message);

                    if (exception != null)
                    {
                        line.Append(" | ").Append(exception.GetType().Name).Append(": ").Append(exception.Message);

                        // Стек обязателен: по типу и тексту исключения, тем более по
                        // NullReferenceException, место сбоя определить невозможно.
                        // Пишем каждый кадр стека на отдельной строке, чтобы он читался
                        // и не сливался с последующими записями.
                        if (!string.IsNullOrEmpty(exception.StackTrace))
                        {
                            line.Append(Environment.NewLine);
                            line.Append("    at ").Append(exception.StackTrace.Replace(
                                Environment.NewLine, Environment.NewLine + "    at "));
                        }

                        if (exception.InnerException != null)
                        {
                            line.Append(Environment.NewLine)
                                .Append("    inner: ").Append(exception.InnerException.GetType().Name)
                                .Append(": ").Append(exception.InnerException.Message);

                            if (!string.IsNullOrEmpty(exception.InnerException.StackTrace))
                            {
                                line.Append(Environment.NewLine)
                                    .Append("    at ").Append(exception.InnerException.StackTrace.Replace(
                                        Environment.NewLine, Environment.NewLine + "    at "));
                            }
                        }
                    }

                    File.AppendAllText(path, line.ToString() + Environment.NewLine, Encoding.UTF8);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private static string Header()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "=== NavisElevation, сессия начата {0} ==={1}Plugin: {2}{1}CLR: {3}{1}OS: {4}{1}{1}",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                Environment.NewLine,
                PluginVersion(),
                Environment.Version,
                Environment.OSVersion.VersionString);
        }

        /// <summary>
        /// Версия сборки плагина — та же строка, что в <c>Version.txt</c>.
        /// </summary>
        private static string PluginVersion()
        {
            try
            {
                System.Reflection.AssemblyName name = typeof(DiagnosticLog).Assembly.GetName();
                return name.Version == null ? "unknown" : name.Version.ToString();
            }
            catch (Exception)
            {
                return "unknown";
            }
        }
    }
}
