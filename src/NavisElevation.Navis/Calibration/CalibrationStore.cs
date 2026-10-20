using System;
using System.Collections.Generic;
using NavisElevation.Core;
using Navisworks = Autodesk.Navisworks.Api;

namespace NavisElevation.Navis.Calibration
{
    /// <summary>
    /// Хранение калибровки отметки: привязка записи к открытому документу.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Запись в файле привязана к модели, а решение «какая запись относится к текущему
    /// документу» принимается здесь: в API Navisworks у документа нет своего Guid, зато
    /// есть путь к файлу и список моделей внутри него, у каждой из которых Guid свой.
    /// </para>
    /// <para>
    /// Порядок поиска: сначала по Guid моделей документа — он переживает переименование
    /// и перемещение файла, — потом по пути к файлу. Если модель перелинковали и Guid
    /// сменился, сработает путь.
    /// </para>
    /// <para>
    /// Хранилище держится в памяти: файл маленький, а читать его при каждом движении
    /// мыши нельзя. На диск пишем только в момент калибровки и сброса.
    /// </para>
    /// </remarks>
    internal static class CalibrationStore
    {
        private static ProjectOffsetStore _store;
        private static double? _defaultOffset;

        /// <summary>
        /// Смещение меньше этого считается нулём: полмиллиметра — порядок точности
        /// подписи.
        /// </summary>
        private const double MinimumOffset = 0.0005;

        private static Navisworks.Document _guidCacheDocument;
        private static int _guidCacheCount = -1;
        private static IList<string> _guidCache = new List<string>();

        /// <summary>Забыть содержимое хранилища и прочитать файл заново.</summary>
        public static void Invalidate()
        {
            _store = null;
            _defaultOffset = null;
        }

        /// <summary>Хранилище в памяти; файл читается при первом обращении.</summary>
        private static ProjectOffsetStore Store
        {
            get
            {
                if (_store == null)
                {
                    _store = ProjectOffsetStore.Load(null);
                }

                return _store;
            }
        }

        /// <summary>Путь к файлу документа; пустая строка, если документ не сохранён.</summary>
        public static string DocumentPath(Navisworks.Document document)
        {
            if (document == null)
            {
                return string.Empty;
            }

            try
            {
                return document.FileName ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>Запись калибровки для документа; null, если калибровки нет.</summary>
        public static ProjectOffsetStore.Record FindRecord(Navisworks.Document document)
        {
            if (document == null)
            {
                return null;
            }

            ProjectOffsetStore store = Store;

            foreach (string guid in DocumentGuids(document))
            {
                ProjectOffsetStore.Record record = store.FindByGuid(guid);

                if (record == null)
                {
                    continue;
                }

                // Файл могли переименовать или переместить — запоминаем новый путь,
                // чтобы в следующий раз искать запись по нему.
                string path = DocumentPath(document);
                if (!string.IsNullOrEmpty(path))
                {
                    record.ModelPath = path;
                }

                return record;
            }

            return store.FindByPath(DocumentPath(document));
        }

        /// <summary>Смещение отметки для документа, в метрах.</summary>
        public static double ReadOffset(Navisworks.Document document)
        {
            ProjectOffsetStore.Record record = FindRecord(document);

            if (record != null)
            {
                return record.Offset;
            }

            return ReadDefaultOffset();
        }

        /// <summary>Есть ли у документа калибровка.</summary>
        /// <remarks>
        /// Запись со смещением ноль калибровкой не считается: отметка при этом не
        /// сдвигается, ничего не меняется, а плагин писал бы «Отметка откалибрована».
        /// Такая запись могла остаться от прежних версий — её можно удалить кнопкой
        /// «Сбросить калибровку».
        /// </remarks>
        public static bool IsCalibrated(Navisworks.Document document)
        {
            ProjectOffsetStore.Record record = FindRecord(document);

            return record != null && Math.Abs(record.Offset) > MinimumOffset;
        }

        /// <summary>
        /// Есть ли запись о калибровке — даже если смещение нулевое.
        /// </summary>
        /// <remarks>
        /// Нужна, чтобы кнопка «Сбросить калибровку» осталась доступной и позволила
        /// убрать бесполезную запись.
        /// </remarks>
        public static bool HasRecord(Navisworks.Document document)
        {
            return FindRecord(document) != null;
        }

        /// <summary>Записать смещение для документа и сохранить файл.</summary>
        public static bool SaveOffset(Navisworks.Document document, double offset, string modelGuid)
        {
            try
            {
                ProjectOffsetStore store = Store;
                store.Save(DocumentPath(document), modelGuid, offset);
                store.Flush(null);
                return true;
            }
            catch (Exception exception)
            {
                DiagnosticLog.Error("Не удалось сохранить калибровку.", exception);
                return false;
            }
        }

        /// <summary>Убрать калибровку документа и сохранить файл.</summary>
        /// <returns>true, если запись была и удалена.</returns>
        public static bool ClearOffset(Navisworks.Document document)
        {
            try
            {
                ProjectOffsetStore store = Store;
                ProjectOffsetStore.Record record = FindRecord(document);

                if (record == null)
                {
                    return false;
                }

                store.Remove(record);
                store.Flush(null);
                return true;
            }
            catch (Exception exception)
            {
                DiagnosticLog.Error("Не удалось сбросить калибровку.", exception);
                return false;
            }
        }

        /// <summary>
        /// Guid всех моделей документа.
        /// </summary>
        /// <remarks>
        /// Список кэшируется: он спрашивается на каждом движении мыши, а перебор
        /// моделей документа с выделением строк на каждой строке — лишняя работа.
        /// Кэш сбрасывается, когда меняется число моделей: значит, состав документа
        /// изменился.
        /// </remarks>
        private static IList<string> DocumentGuids(Navisworks.Document document)
        {
            int count = 0;

            try
            {
                count = document.Models == null ? 0 : document.Models.Count;
            }
            catch (Exception)
            {
                count = 0;
            }

            if (ReferenceEquals(document, _guidCacheDocument) && count == _guidCacheCount)
            {
                return _guidCache;
            }

            var result = new List<string>();

            try
            {
                if (document.Models != null)
                {
                    foreach (Navisworks.Model model in document.Models)
                    {
                        if (model == null)
                        {
                            continue;
                        }

                        result.Add(model.Guid.ToString());
                    }
                }
            }
            catch (Exception exception)
            {
                DiagnosticLog.Warn("Не удалось получить список моделей документа: " + exception.Message);
            }

            _guidCacheDocument = document;
            _guidCacheCount = count;
            _guidCache = result;

            return result;
        }

        /// <summary>
        /// Смещение по умолчанию из файла настроек.
        /// </summary>
        /// <remarks>
        /// Читается один раз за сеанс: файл настроек пользователь правит вручную и
        /// в середине работы плагина обычно не трогает.
        /// </remarks>
        private static double ReadDefaultOffset()
        {
            if (_defaultOffset == null)
            {
                try
                {
                    _defaultOffset = ProjectSettings.Load(null).DefaultOffset;
                }
                catch (Exception exception)
                {
                    DiagnosticLog.Warn("Не удалось прочитать настройки по умолчанию: " + exception.Message);
                    _defaultOffset = 0;
                }
            }

            return _defaultOffset.Value;
        }
    }
}
