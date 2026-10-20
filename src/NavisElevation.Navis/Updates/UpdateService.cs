using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using NavisElevation.Core;

namespace NavisElevation.Navis.Updates
{
    /// <summary>
    /// Проверка обновлений: спрашивает у GitHub, какой релиз вышел, скачивает его
    /// и раскладывает по файлам, чтобы потом подменить их в установленном плагине.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Схема перенесена из плагина для Revit
    /// (<c>AssemblingManager.Revit.Updates.UpdateService</c>) и там уже обкатана.
    /// </para>
    /// <para>
    /// <b>Почему здесь, а не в ядре.</b> Ядро собирается под netstandard2.0 и не имеет
    /// доступа ни к <c>System.Web.Extensions</c> (разбор JSON), ни к
    /// <c>System.IO.Compression.FileSystem</c> (распаковка ZIP) — это всё сборки
    /// .NET Framework. А ядро вшивается в сборку плагина как исходники, и его
    /// зависимости не доезжают до плагина. Поэтому сервис живёт в проекте плагина,
    /// где нужные сборки и так есть в .NET Framework 4.8.
    /// </para>
    /// <para>
    /// <b>Почему без новых пакетов NuGet.</b> Наивный <c>System.Text.Json</c> тянул бы
    /// за собой семь лишних DLL рядом с плагином, а Navisworks может их не подхватить —
    /// и всё упало бы молча, без единого сообщения. Взят разбор JSON из
    /// <c>System.Web.Extensions</c>: это сборка самого .NET Framework, она не требует
    /// ничего ставить рядом.
    /// </para>
    /// </remarks>
    public sealed class UpdateService
    {
        private const string GitHubOwner = "temapromaster3000";
        private const string GitHubRepo = "new-navis";
        private const string ReleasesApiUrl =
            "https://api.github.com/repos/" + GitHubOwner + "/" + GitHubRepo + "/releases?per_page=20";

        // --- Швы для автоматических проверок -------------------------------------
        //
        // Обновление нельзя проверить, не скачав его по-настоящему, а настоящая
        // установка трогать нельзя. Поэтому у сервиса есть три приватных поля,
        // которые подменяются через отражение: адрес API, папка с данными и папка
        // бандла. Публичный API при этом не меняется, а проверка работает с тем же
        // кодом, что и плагин.
        private static string _releasesApiUrl = ReleasesApiUrl;
        private static string _rootDirectoryOverride = null;
        private static string _bundleDirectoryOverride = null;

        /// <summary>Имя файла релиза, который качает плагин.</summary>
        public const string AssetFileName = "NavisElevation-2021.zip";

        /// <summary>Имя программы, которая подменяет файлы после закрытия Navisworks.</summary>
        public const string UpdaterFileName = "NavisElevation.Updater.exe";

        /// <summary>Имя маркера ожидающего обновления.</summary>
        public const string MarkerFileName = "update-pending.txt";

        private static readonly Regex AssetNameRegex =
            new Regex("^" + Regex.Escape(AssetFileName) + "$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly HttpClient _httpClient;

        /// <summary>
        /// Создать сервис проверки обновлений.
        /// </summary>
        public UpdateService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            // GitHub отклоняет запросы без User-Agent: ответ 403 с текстом про
            // "administrative rules" выглядит как загадочный отказ, поэтому заголовок
            // обязателен.
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "NavisElevation-UpdateService");
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        }

        #region Пути

        /// <summary>
        /// Папка с данными плагина: <c>%APPDATA%\NavisElevation</c>.
        /// </summary>
        /// <remarks>
        /// Там же лежат настройки и калибровки, там же будет обновлятель, staging
        /// и маркер обновления. <c>%APPDATA%</c>, а не Program Files: запись туда не
        /// требует прав администратора.
        /// </remarks>
        public static string RootDir
        {
            get
            {
                if (!string.IsNullOrEmpty(_rootDirectoryOverride))
                {
                    return _rootDirectoryOverride;
                }

                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "NavisElevation");
            }
        }

        /// <summary>Папка, куда распаковывается скачанный релиз.</summary>
        public static string StagingDir
        {
            get { return Path.Combine(RootDir, "staging"); }
        }

        /// <summary>
        /// Папка с распакованным релизом: повторяет бандл целиком.
        /// </summary>
        /// <remarks>
        /// Внутри — <c>PackageContents.xml</c> и <c>Contents\2021\</c> со сборкой,
        /// иконками и разметкой ленты. Бандл обновляется целиком, поэтому описание
        /// бандла тоже должно обновляться, а не остаться от прошлой версии.
        /// </remarks>
        public static string StagingBundleDir
        {
            get { return Path.Combine(StagingDir, "bundle"); }
        }

        /// <summary>Маркер ожидающего обновления.</summary>
        public static string MarkerPath
        {
            get { return Path.Combine(RootDir, MarkerFileName); }
        }

        /// <summary>Путь к программе-обновлятелю.</summary>
        public static string UpdaterPath
        {
            get { return Path.Combine(RootDir, UpdaterFileName); }
        }

        /// <summary>
        /// Папка бандла плагина: <c>...\NavisElevation.bundle</c>.
        /// </summary>
        /// <remarks>
        /// Берётся из места, откуда Navisworks загрузил сборку, — так путь узнаётся
        /// сам, без знания о том, куда именно положил бандл установщик. Ищем вверх
        /// папку с <c>PackageContents.xml</c>: он лежит в корне бандла, а сборка — на
        /// уровень ниже, в <c>Contents\2021</c>. Если сборка загружена из памяти и путь
        /// недоступен, берётся стандартное расположение бандла.
        /// </remarks>
        public static string GetBundleDirectory()
        {
            if (!string.IsNullOrEmpty(_bundleDirectoryOverride))
            {
                return _bundleDirectoryOverride;
            }

            try
            {
                string location = typeof(UpdateService).Assembly.Location;
                if (!string.IsNullOrEmpty(location))
                {
                    string directory = Path.GetDirectoryName(location);
                    while (!string.IsNullOrEmpty(directory))
                    {
                        if (File.Exists(Path.Combine(directory, "PackageContents.xml")))
                        {
                            return directory;
                        }

                        string parent = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
                        if (parent == directory)
                        {
                            break;
                        }

                        directory = parent;
                    }
                }
            }
            catch (Exception)
            {
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Autodesk", "ApplicationPlugins", "NavisElevation.bundle");
        }

        #endregion

        /// <summary>
        /// Версия установленного плагина — та же строка, что в <c>Version.txt</c>.
        /// </summary>
        public string GetCurrentVersion()
        {
            try
            {
                Assembly assembly = typeof(UpdateService).Assembly;

                var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                if (informational != null && !string.IsNullOrWhiteSpace(informational.InformationalVersion))
                {
                    return informational.InformationalVersion.Split('+')[0].Trim();
                }

                Version version = assembly.GetName().Version;
                return version == null
                    ? "0.0.0"
                    : string.Format("{0}.{1}.{2}", version.Major, version.Minor, version.Build);
            }
            catch (Exception)
            {
                return "0.0.0";
            }
        }

        /// <summary>
        /// Спросить у GitHub, есть ли релиз новее установленной версии.
        /// </summary>
        /// <returns>
        /// Описание релиза или <c>null</c>, если обновлений нет. Исключение — если
        /// запрос не удался или в релизе нет нужного файла.
        /// </returns>
        public async Task<ReleaseInfo> CheckForUpdateAsync()
        {
            string currentVersion = GetCurrentVersion();
            DiagnosticLog.Info("Проверка обновлений: установленная версия " + currentVersion + ", запрос " + _releasesApiUrl);

            using (HttpResponseMessage response = await _httpClient.GetAsync(_releasesApiUrl).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw BuildApiException(response);
                }

                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                object parsed = serializer.DeserializeObject(json);

                ReleaseInfo release = ParseLatestStableRelease(parsed, currentVersion);

                if (release == null)
                {
                    DiagnosticLog.Info("Проверка обновлений: новых версий нет.");
                    return null;
                }

                DiagnosticLog.Info("Проверка обновлений: найден релиз v" + release.Version + " (" + release.TagName + ").");
                return release;
            }
        }

        /// <summary>
        /// Скачать релиз и разложить его по файлам, чтобы обновлятель потом подменил
        /// их в установленном плагине.
        /// </summary>
        /// <param name="release">Релиз, найденный проверкой.</param>
        /// <param name="progress">Ход скачивания от 0 до 1.</param>
        public async Task DownloadAndStageAsync(ReleaseInfo release, IProgress<double> progress)
        {
            if (release == null)
            {
                throw new InvalidOperationException("Нечего скачивать: релиз не найден.");
            }

            ReleaseAsset asset = release.Assets.FirstOrDefault(a => AssetNameRegex.IsMatch(a.Name ?? string.Empty));
            if (asset == null)
            {
                throw new InvalidOperationException(
                    "В релизе v" + release.Version + " нет файла " + AssetFileName + ".");
            }

            DiagnosticLog.Info("Скачивание обновления: " + asset.Url);

            DeleteDirectory(StagingDir);
            Directory.CreateDirectory(StagingBundleDir);

            string zipPath = Path.Combine(StagingDir, asset.Name);

            using (HttpResponseMessage response =
                   await _httpClient.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw BuildApiException(response);
                }

                long total = asset.Size;
                long downloaded = 0;

                using (Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (FileStream target = File.Create(zipPath))
                {
                    byte[] buffer = new byte[81920];
                    int read;

                    while ((read = await source.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                    {
                        await target.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                        downloaded += read;

                        if (progress != null && total > 0)
                        {
                            progress.Report(Math.Min(1.0, (double)downloaded / total));
                        }
                    }
                }
            }

            DiagnosticLog.Info("Скачано " + new FileInfo(zipPath).Length + " байт, распаковка.");

            ZipFile.ExtractToDirectory(zipPath, StagingBundleDir);
            File.Delete(zipPath);

            WriteMarker(release);

            DiagnosticLog.Info("Обновление v" + release.Version + " разложено в " + StagingBundleDir
                              + ", маркер записан: " + MarkerPath);
        }

        /// <summary>
        /// Версия обновления, которое уже скачано, но ещё не применено.
        /// </summary>
        /// <returns>Номер версии или <c>null</c>, если ожидающего обновления нет.</returns>
        /// <remarks>
        /// Маркер остаётся на диске, если Navisworks не закрыли вовремя или обновлятель
        /// не смог применить файлы. При следующем запуске плагина это видно, и
        /// обновление можно запустить снова.
        /// </remarks>
        public static string GetPendingVersion()
        {
            try
            {
                if (!File.Exists(MarkerPath))
                {
                    return null;
                }

                foreach (string rawLine in File.ReadAllLines(MarkerPath))
                {
                    string line = rawLine == null ? null : rawLine.Trim();
                    if (line == null || line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int separator = line.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    if (string.Equals(line.Substring(0, separator).Trim(), "Version", StringComparison.OrdinalIgnoreCase))
                    {
                        string version = line.Substring(separator + 1).Trim();
                        return string.IsNullOrEmpty(version) ? null : version;
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return null;
        }

        /// <summary>
        /// Найти программу-обновлятель.
        /// </summary>
        /// <returns>Путь к обновлятелю или <c>null</c>, если его нет.</returns>
        public string FindUpdaterExe()
        {
            if (File.Exists(UpdaterPath))
            {
                return UpdaterPath;
            }

            // Запасной вариант: обновлятель рядом с загруженной сборкой — на случай
            // если его положили в бандл вручную.
            try
            {
                string location = typeof(UpdateService).Assembly.Location;
                if (!string.IsNullOrEmpty(location))
                {
                    string directory = Path.GetDirectoryName(location);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        string candidate = Path.Combine(directory, UpdaterFileName);
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            return null;
        }

        /// <summary>
        /// Запустить обновлятель: он дождётся закрытия Navisworks и подменит файлы.
        /// </summary>
        public void LaunchUpdater()
        {
            string updaterPath = FindUpdaterExe();
            if (updaterPath == null)
            {
                throw new InvalidOperationException(
                    "Не найден файл " + UpdaterFileName + ". Переустановите плагин.");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = updaterPath,
                Arguments = "\"" + MarkerPath + "\"",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process.Start(startInfo);

            DiagnosticLog.Info("Запущен обновлятель: " + updaterPath);
        }

        #region Разбор ответа GitHub

        private ReleaseInfo ParseLatestStableRelease(object releasesRoot, string currentVersion)
        {
            var list = releasesRoot as IEnumerable;
            if (list == null)
            {
                return null;
            }

            Dictionary<string, object> latest = null;
            string latestVersion = null;

            foreach (object item in list)
            {
                var release = item as Dictionary<string, object>;
                if (release == null)
                {
                    continue;
                }

                if (AsBool(Get(release, "draft")))
                {
                    continue;
                }

                if (AsBool(Get(release, "prerelease")))
                {
                    continue;
                }

                string tagName = AsString(Get(release, "tag_name"));
                string version = (tagName ?? string.Empty).TrimStart('v', 'V');

                if (PluginVersion.Compare(version, currentVersion) <= 0)
                {
                    continue;
                }

                if (latestVersion == null || PluginVersion.Compare(version, latestVersion) > 0)
                {
                    latestVersion = version;
                    latest = release;
                }
            }

            if (latestVersion == null)
            {
                return null;
            }

            var assets = new List<ReleaseAsset>();
            var assetsList = Get(latest, "assets") as IEnumerable;
            if (assetsList != null)
            {
                foreach (object item in assetsList)
                {
                    var asset = item as Dictionary<string, object>;
                    if (asset == null)
                    {
                        continue;
                    }

                    string name = AsString(Get(asset, "name"));
                    if (name == null || !AssetNameRegex.IsMatch(name))
                    {
                        continue;
                    }

                    string url = AsString(Get(asset, "browser_download_url"));
                    if (string.IsNullOrEmpty(url))
                    {
                        continue;
                    }

                    assets.Add(new ReleaseAsset
                    {
                        Name = name,
                        Url = url,
                        Size = AsLong(Get(asset, "size"))
                    });
                }
            }

            if (assets.Count == 0)
            {
                throw new InvalidOperationException(
                    "В релизе v" + latestVersion + " нет файла " + AssetFileName
                    + ". Обновление невозможно: плагину нужен именно этот файл.");
            }

            string body = AsString(Get(latest, "body"));

            return new ReleaseInfo
            {
                Version = latestVersion,
                TagName = AsString(Get(latest, "tag_name")) ?? ("v" + latestVersion),
                ReleaseNotes = string.IsNullOrWhiteSpace(body) ? "(описание релиза отсутствует)" : body.Trim(),
                Assets = assets
            };
        }

        private static object Get(Dictionary<string, object> source, string key)
        {
            if (source == null)
            {
                return null;
            }

            object value;
            return source.TryGetValue(key, out value) ? value : null;
        }

        private static string AsString(object value)
        {
            return value as string;
        }

        private static bool AsBool(object value)
        {
            if (value is bool)
            {
                return (bool)value;
            }

            if (value is string)
            {
                return string.Equals((string)value, "true", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static long AsLong(object value)
        {
            if (value is long)
            {
                return (long)value;
            }

            if (value is int)
            {
                return (int)value;
            }

            if (value is decimal)
            {
                return (long)(decimal)value;
            }

            if (value is double)
            {
                return (long)(double)value;
            }

            long parsed;
            if (value != null && long.TryParse(value.ToString(), out parsed))
            {
                return parsed;
            }

            return 0;
        }

        private static InvalidOperationException BuildApiException(HttpResponseMessage response)
        {
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                IEnumerable<string> remaining;
                IEnumerable<string> resets;

                if (response.Headers.TryGetValues("X-RateLimit-Remaining", out remaining) &&
                    remaining.FirstOrDefault() == "0" &&
                    response.Headers.TryGetValues("X-RateLimit-Reset", out resets))
                {
                    long resetUnix;
                    if (resets.FirstOrDefault() != null && long.TryParse(resets.FirstOrDefault(), out resetUnix))
                    {
                        DateTime resetTime = DateTimeOffset.FromUnixTimeSeconds(resetUnix).LocalDateTime;
                        return new InvalidOperationException(
                            "Превышен лимит обращений к GitHub. Повторите после "
                            + resetTime.ToString("HH:mm") + ".");
                    }
                }

                return new InvalidOperationException(
                    "GitHub отклонил запрос (403). Попробуйте позже.");
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new InvalidOperationException(
                    "Релизы не найдены: " + GitHubOwner + "/" + GitHubRepo + ".");
            }

            return new InvalidOperationException(
                "GitHub вернул ошибку " + (int)response.StatusCode + " " + response.StatusCode + ".");
        }

        #endregion

        #region Маркер и вспомогательное

        /// <summary>
        /// Записать маркер ожидающего обновления: обновлятель прочитает его и поймёт,
        /// что и куда подставлять.
        /// </summary>
        private void WriteMarker(ReleaseInfo release)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# NavisElevation pending update");
            builder.AppendLine("Version=" + release.Version);
            builder.AppendLine("[Artifact]");
            builder.AppendLine("StagingDir=" + StagingBundleDir);
            builder.AppendLine("TargetDir=" + GetBundleDirectory());

            Directory.CreateDirectory(RootDir);
            File.WriteAllText(MarkerPath, builder.ToString(), new UTF8Encoding(false));
        }

        private static void DeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        #endregion
    }
}
