using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace NavisElevation.Updater
{
    /// <summary>
    /// Программа, которая подменяет файлы плагина после закрытия Navisworks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Зачем отдельная программа: сборка плагина лежит в бандле и на время работы
    /// Navisworks заблокирована — Windows не даёт её перезаписать. Изнутри процесса
    /// Navisworks подменить файл нельзя в принципе, поэтому подмену делает отдельная
    /// программа, запущенная из плагина и ждущая закрытия хоста.
    /// </para>
    /// <para>
    /// Схема перенесена из плагина для Revit
    /// (<c>AssemblingManager.Updater/Program.cs</c>), где она уже обкатана.
    /// </para>
    /// <para>
    /// Программа ничего не спрашивает и ничего не показывает: её запускает плагин,
    /// а пользователю уже сказано, что надо закрыть Navisworks. Всё, что происходит,
    /// пишется в <c>%APPDATA%\NavisElevation\updater-log.txt</c>.
    /// </para>
    /// </remarks>
    internal static class Program
    {
        /// <summary>Процесс Navisworks Manage 2021.</summary>
        private const string NavisworksProcessName = "Roamer";

        /// <summary>Префикс имени файлов самой программы: их копировать нельзя.</summary>
        private const string UpdaterPrefix = "NavisElevation.Updater";

        private const string MarkerFileName = "update-pending.txt";

        /// <summary>Сколько ждать закрытия Navisworks, прежде чем сдаться.</summary>
        private static readonly TimeSpan WaitTimeout = TimeSpan.FromMinutes(5);

        /// <summary>Сколько раз пробовать скопировать занятый файл.</summary>
        private const int CopyAttempts = 5;

        private static string _logPath;

        internal static int Main(string[] args)
        {
            string rootDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NavisElevation");

            string markerPath = args != null && args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
                ? args[0]
                : Path.Combine(rootDir, MarkerFileName);

            // Лог пишем рядом с маркером: в обычной работе это
            // %APPDATA%\NavisElevation\updater-log.txt, а в проверках - рядом с
            // временным маркером, и настоящая папка пользователя не затрагивается.
            string markerFolder = Path.GetDirectoryName(markerPath);
            _logPath = Path.Combine(string.IsNullOrEmpty(markerFolder) ? rootDir : markerFolder, "updater-log.txt");

            Log("=== NavisElevation.Updater запущен ===");

            try
            {
                if (!File.Exists(markerPath))
                {
                    Log("Маркер ожидающего обновления не найден: " + markerPath + ". Нечего делать.");
                    return 0;
                }

                List<Artifact> artifacts = ParseMarker(File.ReadAllLines(markerPath));
                if (artifacts.Count == 0)
                {
                    Log("В маркере нет ни одного файла для подмены. Маркер удалён.");
                    SafeDeleteFile(markerPath);
                    return 0;
                }

                Log("Найдено файлов для подмены: " + artifacts.Count + ".");

                WaitForNavisworksExit();

                int copied = ApplyArtifacts(artifacts);
                CleanupStaging(artifacts);

                SafeDeleteFile(markerPath);

                Log("Обновление применено, скопировано файлов: " + copied + ". Маркер удалён.");
                return 0;
            }
            catch (Exception exception)
            {
                Log("ОШИБКА: " + exception);
                return 1;
            }
            finally
            {
                Log("=== NavisElevation.Updater завершён ===");
            }
        }

        /// <summary>
        /// Прочитать маркер: список папок «откуда → куда».
        /// </summary>
        private static List<Artifact> ParseMarker(string[] lines)
        {
            var artifacts = new List<Artifact>();
            Artifact current = null;

            foreach (string rawLine in lines)
            {
                string line = rawLine == null ? string.Empty : rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(line, "[Artifact]", StringComparison.OrdinalIgnoreCase))
                {
                    current = new Artifact();
                    artifacts.Add(current);
                    continue;
                }

                int separator = line.IndexOf('=');
                if (separator <= 0 || current == null)
                {
                    continue;
                }

                string key = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1).Trim();

                if (string.Equals(key, "StagingDir", StringComparison.OrdinalIgnoreCase))
                {
                    current.StagingDir = value;
                }
                else if (string.Equals(key, "TargetDir", StringComparison.OrdinalIgnoreCase))
                {
                    current.TargetDir = value;
                }
            }

            var result = new List<Artifact>();
            foreach (Artifact artifact in artifacts)
            {
                if (!string.IsNullOrWhiteSpace(artifact.StagingDir) &&
                    !string.IsNullOrWhiteSpace(artifact.TargetDir))
                {
                    result.Add(artifact);
                }
            }

            return result;
        }

        /// <summary>
        /// Дождаться, пока Navisworks закроется.
        /// </summary>
        /// <remarks>
        /// Пока Navisworks открыт, его файлы заняты, и подменять их бессмысленно.
        /// Ждём пять минут: если пользователь забыл закрыть программу, обновление
        /// лучше применить как есть, чем не применить совсем, — Navisworks в этот
        /// момент всё равно не держит файл сборки.
        /// </remarks>
        private static void WaitForNavisworksExit()
        {
            int waitedSeconds = 0;
            int maxSeconds = (int)WaitTimeout.TotalSeconds;

            while (waitedSeconds < maxSeconds)
            {
                Process[] processes = Process.GetProcessesByName(NavisworksProcessName);
                if (processes.Length == 0)
                {
                    Log("Navisworks закрыт.");
                    return;
                }

                if (waitedSeconds % 10 == 0)
                {
                    Log("Ждём закрытия Navisworks (" + processes.Length + " шт.)... прошло "
                        + waitedSeconds + " с.");
                }

                foreach (Process process in processes)
                {
                    process.Dispose();
                }

                Thread.Sleep(1000);
                waitedSeconds++;
            }

            Log("ВНИМАНИЕ: Navisworks не закрылся за отведённое время. Продолжаем.");
        }

        /// <summary>
        /// Скопировать содержимое staging в папку плагина, сохранив структуру папок.
        /// </summary>
        /// <returns>Сколько файлов скопировано.</returns>
        private static int ApplyArtifacts(List<Artifact> artifacts)
        {
            int copied = 0;
            int failed = 0;

            foreach (Artifact artifact in artifacts)
            {
                if (!Directory.Exists(artifact.StagingDir))
                {
                    Log("ПРОПУЩЕНО: папка с распакованным обновлением не найдена: " + artifact.StagingDir);
                    continue;
                }

                if (!Directory.Exists(artifact.TargetDir))
                {
                    Directory.CreateDirectory(artifact.TargetDir);
                    Log("Создана папка плагина: " + artifact.TargetDir);
                }

                Log("Подмена: " + artifact.StagingDir + " → " + artifact.TargetDir);

                foreach (string source in Directory.GetFiles(artifact.StagingDir, "*", SearchOption.AllDirectories))
                {
                    string relative = MakeRelative(artifact.StagingDir, source);
                    string destination = Path.Combine(artifact.TargetDir, relative);

                    string fileName = Path.GetFileName(source);

                    // Саму программу-обновлятель подменить нельзя: она сейчас
                    // запущена, и файл заблокирован. Обновление обновлятеля — только
                    // через полную переустановку плагина.
                    if (fileName.StartsWith(UpdaterPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        Log("ПРОПУЩЕНО: " + relative + " (программу-обновлятель подменять нельзя)");
                        continue;
                    }

                    if (CopyWithRetry(source, destination))
                    {
                        copied++;
                        Log("Скопирован: " + relative);
                    }
                    else
                    {
                        failed++;
                    }
                }
            }

            Log("Скопировано файлов: " + copied + ", не удалось: " + failed + ".");
            return copied;
        }

        /// <summary>
        /// Скопировать файл, повторяя попытку, если он занят.
        /// </summary>
        private static bool CopyWithRetry(string source, string destination)
        {
            for (int attempt = 1; attempt <= CopyAttempts; attempt++)
            {
                try
                {
                    string directory = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.Copy(source, destination, true);
                    return true;
                }
                catch (IOException exception)
                {
                    Log("Файл занят (" + attempt + "/" + CopyAttempts + "): " + destination + " — " + exception.Message);
                }
                catch (UnauthorizedAccessException exception)
                {
                    Log("Нет прав на файл: " + destination + " — " + exception.Message);
                    return false;
                }

                Thread.Sleep(1000);
            }

            Log("НЕ СКОПИРОВАНО: " + destination);
            return false;
        }

        /// <summary>
        /// Путь файла относительно корня папки: <c>Contents\2021\NavisElevation.dll</c>.
        /// </summary>
        private static string MakeRelative(string root, string path)
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(path);

            if (fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath.Substring(fullRoot.Length);
            }

            return Path.GetFileName(fullPath);
        }

        /// <summary>
        /// Убрать за собой: staging больше не нужен.
        /// </summary>
        /// <remarks>
        /// Удаляются только те папки, что перечислены в маркере. Папку «staging» по
        /// умолчанию не трогаем: маркер может указывать на другое место, и тогда
        /// удаление не той папки было бы лишней ошибкой.
        /// </remarks>
        private static void CleanupStaging(List<Artifact> artifacts)
        {
            var directories = new List<string>();

            foreach (Artifact artifact in artifacts)
            {
                if (!directories.Contains(artifact.StagingDir))
                {
                    directories.Add(artifact.StagingDir);
                }
            }

            foreach (string directory in directories)
            {
                try
                {
                    if (Directory.Exists(directory))
                    {
                        Directory.Delete(directory, true);
                        Log("Папка с обновлением удалена: " + directory);
                    }
                }
                catch (Exception exception)
                {
                    Log("Не удалось удалить папку " + directory + ": " + exception.Message);
                }
            }
        }

        private static void SafeDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception)
            {
                Log("Не удалось удалить " + path + ": " + exception.Message);
            }
        }

        private static void Log(string message)
        {
            string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message;

            try
            {
                Console.WriteLine(line);
            }
            catch (Exception)
            {
            }

            try
            {
                string directory = Path.GetDirectoryName(_logPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(_logPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception)
            {
            }
        }

        private sealed class Artifact
        {
            public string StagingDir { get; set; }
            public string TargetDir { get; set; }
        }
    }
}
