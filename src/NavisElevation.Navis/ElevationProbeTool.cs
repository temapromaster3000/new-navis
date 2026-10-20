using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using NavisElevation.Core;
using NavisElevation.Navis.Calibration;
using NavisElevation.Navis.Views;
using Navisworks = Autodesk.Navisworks.Api;

namespace NavisElevation.Navis
{
    /// <summary>
    /// Инструмент, который следит за курсором и рисует отметку элемента прямо в 3D-виде.
    /// </summary>
    /// <remarks>
    /// Включается кнопкой на ленте через <c>Document.Tool.SetCustomToolPlugin</c> и
    /// выключается по Esc или правой кнопке мыши, после чего возвращается инструмент,
    /// который был активен до включения.
    ///
    /// В API Navisworks нет способа встроить свою панель в 3D-вид (у <c>View</c> нет
    /// ни одного метода для этого, а <c>ViewControl</c> — просто WinForms-контрол без
    /// способа его подвесить). Поэтому подпись рисуется в оверлее вида через
    /// <c>Graphics.Text2D</c>: она привязана к курсору и двигается вместе с ним.
    /// </remarks>
    [Plugin(PluginIds.ToolId, PluginIds.DeveloperId, DisplayName = "Отметка элемента (инструмент)")]
    public class ElevationProbeTool : ToolPlugin
    {
        /// <summary>Сработает, когда Navisworks впервые обратится к типу инструмента.</summary>
        static ElevationProbeTool()
        {
            DiagnosticLog.RecordPluginLoad(typeof(ElevationProbeTool).FullName);
        }

        /// <summary>Поля подписи от левого и верхнего края вида, в пикселях.</summary>
        /// <remarks>
        /// Подпись стоит в углу вида и не следует за курсором. Раньше она смещалась от курсора,
        /// а за серединой экрана прыгала на левый край, потому что ширину текста не мерили и
        /// наличие места оценивали по половине экрана. В углу доступна вся ширина вида, и
        /// длинное имя модели обрезаться не будет.
        /// </remarks>
        private const int CaptionMarginX = 12;

        private const int CaptionMarginY = 20;

        /// <summary>Межстрочный интервал подписи в пикселях.</summary>
        private const int LineHeight = 18;

        private const int FontPointSize = 14;
        private const string FontTypeface = "Segoe UI";

        /// <summary>Ниже этого расстояния курсор считается «не двигался».</summary>
        private const int MinPixelDelta = 3;

        /// <summary>
        /// Не чаще, чем раз в миллисекунд. Событие MouseMove приходит с частотой кадров,
        /// а PickItemFromPoint перебирает геометрию — на большой модели без такого
        /// ограничения просмотр встаёт колом.
        /// </summary>
        private const int MinIntervalMs = 40;

        private const ushort EscapeKey = 0x1B;
        private const ushort RightMouseButton = 2;

        private static readonly Color ShadowColor = new Color(0.0, 0.0, 0.0);
        private static readonly Color TextColor = new Color(1.0, 0.94, 0.55);

        /// <summary>
        /// Цвет главной строки подписи в режиме калибровки.
        /// </summary>
        /// <remarks>
        /// Красным: в этом режиме плагин не показывает отметку, а просит выбрать
        /// точку, и спутать это с обычной работой нельзя.
        /// </remarks>
        private static readonly Color AccentColor = new Color(1.0, 0.28, 0.28);

        private ElevationFormatter _formatter;

        /// <summary>
        /// Инструмент, который был активен до включения режима отметки.
        /// </summary>
        /// <remarks>
        /// Статическое поле, а не поле экземпляра, потому что экземпляр теперь создаёт
        /// сам Navisworks через <c>PluginRecord.LoadPlugin()</c> — передать ему что-либо
        /// в конструктор нельзя. Команда записывает значение непосредственно перед
        /// включением режима, инструмент читает его при выключении.
        /// </remarks>
        internal static Navisworks.Tool PendingPreviousTool = Navisworks.Tool.None;

        /// <summary>
        /// Записывался ли уже промах мыши в лог.
        /// </summary>
        /// <remarks>
        /// Флаг статический, потому что экземпляр инструмента Navisworks переиспользует
        /// между включениями. Без ограничения лог засорялся бы десятками одинаковых
        /// строк в секунду: при движении мыши по краю элементы и пустота чередуются.
        /// </remarks>
        private static bool _emptyReported;

        /// <summary>Сбросить признак промаха: вызывается при каждом включении режима.</summary>
        internal static void ResetEmptyReport()
        {
            _emptyReported = false;
        }

        /// <summary>
        /// Документ, в котором работает режим отметки.
        /// </summary>
        /// <remarks>
        /// Экземпляр инструмента создаёт Navisworks, передать ему документ нельзя, поэтому
        /// он всегда берётся из активного документа на момент обращения. Это же правильно
        /// по сути: инструмент работает именно в том документе, который открыт сейчас.
        /// </remarks>
        private Navisworks.Document Document
        {
            get { return Application.ActiveDocument; }
        }

        /// <summary>
        /// Настройки плагина, прочитанные из файла один раз за сеанс работы режима.
        /// </summary>
        private string[] _lines;

        private int _lastPickX = -10000;
        private int _lastPickY = -10000;
        private DateTime _lastPickTime = DateTime.MinValue;

        private ModelItem _highlightedItem;
        private bool _hadItem;
        private TextFontInfo _font;

        /// <summary>
        /// Последний найденный элемент и высота точки попадания в него.
        /// </summary>
        /// <remarks>
        /// Нужны, чтобы пересобрать подпись без нового движения мыши: после калибровки
        /// число в подписи меняется сразу, а ждать, пока пользователь дёрнет мышью,
        /// незачем. Хранится только высота: <c>Point3D</c> в API Navisworks — класс,
        /// а не структура, и держать ссылку на него в поле рискованно.
        /// </remarks>
        private ModelItem _lastItem;
        private double _lastZ;

        /// <summary>
        /// Номер строки подписи, которую рисуем красным; -1 — рисуем всё жёлтым.
        /// </summary>
        private int _accentLine = -1;

        /// <summary>
        /// Пропустить следующий <c>MouseUp</c>.
        /// </summary>
        /// <remarks>
        /// Клик в режиме калибровки обрабатывается в <c>MouseDown</c>, и Navisworks
        /// присылает следом <c>MouseUp</c> того же клика. Если не пропустить его,
        /// правый клик выключил бы режим сразу после выбора точки.
        /// </remarks>
        private bool _ignoreMouseUp;

        /// <summary>
        /// Просить следующее движение мыши пересобрать подпись, даже если курсор не
        /// двигался.
        /// </summary>
        private static bool _forcePick;

        /// <summary>
        /// Экземпляр инструмента, который сейчас работает.
        /// </summary>
        /// <remarks>
        /// Экземпляр создаёт Navisworks, ссылку на него извне получить нельзя, поэтому
        /// инструмент запоминает себя сам. Нужен, чтобы после калибровки обновить
        /// подпись, не дожидаясь нового события мыши.
        /// </remarks>
        internal static ElevationProbeTool Active { get; private set; }

        /// <summary>
        /// Обязателен: Navisworks создаёт плагин через <c>Activator.CreateInstance</c>.
        /// </summary>
        /// <remarks>
        /// Конструктора с параметрами у инструмента быть не может — тип создаёт не мы, а
        /// Navisworks, и она вызовет конструктор без аргументов. Всё состояние, которое
        /// раньше передавалось аргументами, доступно через статическое поле
        /// <see cref="PendingPreviousTool"/> и свойства <see cref="Document"/> и <see cref="Settings"/>.
        /// </remarks>
        public ElevationProbeTool()
        {
            Active = this;
        }

        public override bool MouseMove(View view, KeyModifiers modifiers, int x, int y, double timeOffset)
        {
            if (!ShouldPick(x, y))
            {
                return false;
            }

            Probe(view, x, y);
            return false;
        }

        public override bool KeyDown(View view, KeyModifiers modifiers, ushort key, double timeOffset)
        {
            if (CalibrationSession.IsPicking)
            {
                // В режиме выбора точки Esc отменяет выбор. Других клавиш здесь нет:
                // Enter до плагина не доходит — его перехватывает сам Navisworks.
                if (key == EscapeKey)
                {
                    CancelPicking(view);
                    return true;
                }

                return false;
            }

            if (key == EscapeKey)
            {
                Deactivate(view, "Esc");
                return true;
            }

            return false;
        }

        public override bool MouseDown(View view, KeyModifiers modifiers, ushort button, int x, int y, double timeOffset)
        {
            if (!CalibrationSession.IsPicking)
            {
                return false;
            }

            DiagnosticLog.Info("Клик в режиме калибровки, кнопка " + button + ".");

            // Нумерация кнопок мыши в Navisworks не документирована: левая приходит не
            // как 0, и проверка на ноль отбрасывала настоящий клик молча. Поэтому берём
            // любую кнопку, кроме правой, а значение пишем в лог.
            if (button == RightMouseButton)
            {
                return false;
            }

            HandleCalibrationClick(view, x, y);

            // true — клик обработан нами, камера поворачиваться не должна.
            return true;
        }

        public override bool MouseUp(View view, KeyModifiers modifiers, ushort button, int x, int y, double timeOffset)
        {
            // Эхо того же клика, которым только что выбрана точка калибровки.
            if (_ignoreMouseUp)
            {
                _ignoreMouseUp = false;
                return false;
            }

            if (button == RightMouseButton)
            {
                Deactivate(view, "правая кнопка мыши");
                return true;
            }

            return false;
        }

        public override bool MouseLeave(View view, double timeOffset)
        {
            // Уводим курсор из вида — убираем подпись, чтобы она не «зависла» на экране.
            ClearCaption();
            ClearHighlight();
            _lastItem = null;
            RequestRedraw(view);
            return false;
        }

        public override void OverlayRender(View view, Graphics graphics)
        {
            if (_lines == null || _lines.Length == 0 || graphics == null)
            {
                return;
            }

            try
            {
                EnsureFont();

                graphics.BeginWindowContext();

                // Подпись рисуется дважды: тёмная со смещением в пиксель и светлая поверх.
                // Так текст читается на любом фоне модели — и на светлой стене, и на небе.
                for (int pass = 0; pass < 2; pass++)
                {
                    bool shadow = pass == 0;

                    int dx = shadow ? 1 : 0;

                    // Минус, а не плюс: сдвиг вниз по экрану — это меньший y.
                    int dy = shadow ? -1 : 0;

                    for (int i = 0; i < _lines.Length; i++)
                    {
                        string line = _lines[i];
                        if (string.IsNullOrEmpty(line))
                        {
                            continue;
                        }

                        // Цвет задаётся перед каждой строкой: главная строка в режиме
                        // калибровки красная, остальные — жёлтые.
                        Color color = shadow
                            ? ShadowColor
                            : (i == _accentLine ? AccentColor : TextColor);

                        graphics.Color(color, 1.0);

                        // Координаты в Text2D отсчитываются от нижнего левого угла окна, и y растёт
                        // вверх: 0 — низ, view.Height — верх. Верхний край подписи — это
                        // view.Height, а не ноль. Написать здесь CaptionMarginY + i *
                        // LineHeight нельзя: подпись уедет в нижний угол, а строки
                        // перевернутся по вертикали. Проверено по официальному примеру
                        // Autodesk в блоге про RenderPlugin и по скриншоту из Navisworks.
                        int topY = view.Height - CaptionMarginY;

                        graphics.Text2D(_font, line, Point2D.Origin, CaptionMarginX + dx, topY - i * LineHeight + dy);
                    }
                }

                graphics.EndWindowContext();
            }
            catch (Exception)
            {
                // Отрисовка не должна ронять инструмент: если шрифт или контекст недоступны,
                // просто молча пропускаем кадр.
            }
        }

        protected override void OnUnloading()
        {
            DiagnosticLog.Info("Инструмент выгружается Navisworks.");
            ClearHighlight();
            _lines = null;
        }

        private bool ShouldPick(int x, int y)
        {
            // После калибровки подпись обязана смениться сразу, даже если курсор не
            // двигался: иначе старая отметка останется на экране до первого движения мыши.
            if (_forcePick)
            {
                _forcePick = false;
                _lastPickX = x;
                _lastPickY = y;
                _lastPickTime = DateTime.UtcNow;
                return true;
            }

            int dx = x - _lastPickX;
            int dy = y - _lastPickY;

            if (dx > -MinPixelDelta && dx < MinPixelDelta && dy > -MinPixelDelta && dy < MinPixelDelta)
            {
                return false;
            }

            if ((DateTime.UtcNow - _lastPickTime).TotalMilliseconds < MinIntervalMs)
            {
                return false;
            }

            _lastPickX = x;
            _lastPickY = y;
            _lastPickTime = DateTime.UtcNow;
            return true;
        }

        private void Probe(View view, int x, int y)
        {
            PickItemResult hit = null;

            try
            {
                hit = view.PickItemFromPoint(x, y);
            }
            catch (Exception exception)
            {
                DiagnosticLog.Error("PickItemFromPoint бросил исключение в (" + x + "," + y + ")", exception);
                hit = null;
            }

            if (hit == null || hit.ModelItem == null || hit.Point == null)
            {
                // Курсор в пустоте: снимаем подпись, чтобы не показывать чужие данные.
                if (_hadItem)
                {
                    _hadItem = false;

                    // Записываем один раз за включение режима. При движении мыши по краю
                    // элементы и пустота чередуются, и без ограничения лог засорялся бы
                    // десятками одинаковых строк в секунду.
                    if (!_emptyReported)
                    {
                        _emptyReported = true;
                        DiagnosticLog.Info("Курсор в пустоте — подпись снята. Следующая запись "
                                          + "появится только если элемент снова найдётся.");
                    }
                }

                ClearCaption();
                ClearHighlight();

                // Элемента под курсором нет: сбрасываем и его, иначе Enter подтвердил бы
                // точку, найденную раньше, а не ту, что под курсором сейчас.
                _lastItem = null;

                RequestRedraw(view);
                return;
            }

            _hadItem = true;

            _lastItem = hit.ModelItem;
            _lastZ = hit.Point.Z;

            if (CalibrationSession.IsPicking)
            {
                BuildPickCaption(hit.ModelItem, hit.Point.Z);
            }
            else
            {
                BuildCaption(hit.ModelItem, hit.Point.Z);
            }

            UpdateHighlight(hit.ModelItem);
            RequestRedraw(view);
        }

        /// <summary>
        /// Собрать строки подписи для элемента под курсором.
        /// </summary>
        /// <remarks>
        /// Параметров вида и координат здесь нет намеренно: подпись прибита к углу, её
        /// позиция не зависит от того, где находится курсор.
        /// </remarks>
        private void BuildCaption(ModelItem item, double z)
        {
            try
            {
                EnsureFormatter(item);

                var lines = new List<string>();
                int accent = -1;

                // Калибровка сдвигает все отметки модели, поэтому об этом надо сказать
                // прямо и первым: иначе число выглядит как ошибка. Строка идёт
                // красным — так её не спутать с обычной подписью.
                bool calibrated = CalibrationStore.IsCalibrated(Document);

                if (calibrated)
                {
                    lines.Add("Отметка откалибрована");
                    accent = 0;
                }

                string name = item.DisplayName;
                if (string.IsNullOrEmpty(name))
                {
                    name = item.ClassDisplayName;
                }

                if (!string.IsNullOrEmpty(name))
                {
                    lines.Add(name);
                }

                lines.Add("Отметка: " + _formatter.FormatMeters(z));

                if (calibrated)
                {
                    lines.Add("Смещение: " + NavisWindow.FormatMeters(_formatter.Offset));
                }

                BoundingBox3D box = item.BoundingBox();
                if (box != null && !box.IsEmpty)
                {
                    lines.Add("Границы: " + _formatter.FormatMetersSpan(box.Min.Z, box.Max.Z));
                }

                // Строка «Модель» убрана: имя файла модели-источника проектировщику
                // ничего не говорит, а место в подписи занимает.

                _accentLine = accent;
                _lines = lines.ToArray();
            }
            catch (Exception)
            {
                ClearCaption();
            }
        }

        /// <summary>
        /// Собрать подпись режима выбора точки калибровки.
        /// </summary>
        /// <remarks>
        /// В режиме калибровки обычная подпись не подходит: она показывала бы отметку
        /// как будто всё в порядке, а пользователь сейчас не смотрит на отметку, а
        /// выбирает точку, отметка которой ему известна.
        /// </remarks>
        private void BuildPickCaption(ModelItem item, double z)
        {
            try
            {
                EnsureFormatter(item);

                var lines = new List<string>
                {
                    "Выберите точку калибровки",
                    "Отметка сейчас: " + _formatter.FormatMeters(z)
                };

                string name = item.DisplayName;
                if (string.IsNullOrEmpty(name))
                {
                    name = item.ClassDisplayName;
                }

                if (!string.IsNullOrEmpty(name))
                {
                    lines.Add(name);
                }

                lines.Add("ЛКМ — выбрать точку, Esc — отмена");

                // Главная строка режима калибровки рисуется красным.
                _accentLine = 0;
                _lines = lines.ToArray();
            }
            catch (Exception)
            {
                ClearCaption();
            }
        }

        /// <summary>
        /// Обработать клик в режиме выбора точки калибровки.
        /// </summary>
        private void HandleCalibrationClick(View view, int x, int y)
        {
            PickItemResult hit = null;

            try
            {
                hit = view.PickItemFromPoint(x, y);
            }
            catch (Exception exception)
            {
                DiagnosticLog.Error("PickItemFromPoint бросил исключение при выборе точки калибровки.", exception);
                hit = null;
            }

            if (hit == null || hit.ModelItem == null || hit.Point == null)
            {
                DiagnosticLog.Warn("Клик в режиме калибровки не попал в элемент — жду следующего клика.");
                return;
            }

            HandleCalibrationPick(view, hit.ModelItem, hit.Point.Z);

            // Следом придёт MouseUp того же клика — его надо пропустить, иначе правый
            // клик выключит режим сразу после выбора точки.
            _ignoreMouseUp = true;
        }

        /// <summary>
        /// Сделать образец точки калибровки и открыть окно ввода проектной отметки.
        /// </summary>
        private void HandleCalibrationPick(View view, ModelItem item, double z)
        {
            try
            {
                EnsureFormatter(item);

                Model owner = DocumentCatalog.FindOwningModel(Document, item);

                var sample = new CalibrationSample
                {
                    RawZ = z,
                    RawMeters = _formatter.ToMeters(z),
                    CurrentOffset = _formatter.Offset,
                    ModelGuid = owner == null ? string.Empty : owner.Guid.ToString(),
                    ModelName = DocumentCatalog.ReadModelName(owner),
                    ModelPath = CalibrationStore.DocumentPath(Document)
                };

                CalibrationSession.SetSample(sample);
                CalibrationSession.End();

                DiagnosticLog.Info("Точка калибровки выбрана: " + sample.Describe());

                ClearCaption();
                RequestRedraw(view);

                // Окно ввода открывается прямо здесь, из обработчика мыши Navisworks.
                // Отложенный вызов через диспетчер не годится: очередь сообщений вне
                // модальных окон Navisworks не качает, и окно просто не появлялось.
                CalibrationController.OpenEntry(sample);
            }
            catch (Exception exception)
            {
                CalibrationSession.End();
                DiagnosticLog.Error("Не удалось обработать выбор точки калибровки.", exception);
            }
        }

        /// <summary>Отменить выбор точки калибровки по Esc.</summary>
        private void CancelPicking(View view)
        {
            CalibrationSession.End();
            DiagnosticLog.Info("Выбор точки калибровки отменён: Esc.");

            ClearCaption();
            ClearHighlight();
            RequestRedraw(view);
        }

        /// <summary>
        /// Обновить подпись после изменения калибровки.
        /// </summary>
        /// <remarks>
        /// Подпись пересобирается сразу, без ожидания нового движения мыши: иначе
        /// старая отметка висела бы на экране и вводила в заблуждение.
        /// </remarks>
        internal static void RefreshCaption()
        {
            _forcePick = true;

            ElevationProbeTool tool = Active;

            if (tool != null)
            {
                tool.RebuildCaption();
            }

            try
            {
                Navisworks.Document document = Application.ActiveDocument;

                if (document != null && document.ActiveView != null)
                {
                    document.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.OverlayRender);
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>Пересобрать подпись по последнему найденному элементу.</summary>
        private void RebuildCaption()
        {
            if (_lastItem == null)
            {
                return;
            }

            if (CalibrationSession.IsPicking)
            {
                BuildPickCaption(_lastItem, _lastZ);
            }
            else
            {
                BuildCaption(_lastItem, _lastZ);
            }
        }

        private void EnsureFormatter(ModelItem item)
        {
            LengthUnit unit = DocumentCatalog.ReadUnit(Document, item);
            double offset = CalibrationStore.ReadOffset(Document);

            if (_formatter == null || _formatter.Unit != unit || _formatter.Offset != offset)
            {
                _formatter = new ElevationFormatter(unit, offset);
            }
        }

        private void EnsureFont()
        {
            if (_font == null)
            {
                _font = new TextFontInfo(FontTypeface, FontPointSize, 10, false, false);
            }
        }

        /// <summary>
        /// Подсветить элемент под курсором.
        /// </summary>
        /// <remarks>
        /// Сброс подсветки делается только когда элемент реально сменился, а не на каждом
        /// движении мыши: перебор геометрии при смене цвета заметно дороже, чем при чтении.
        /// Сбрасывается ровно наш элемент — <c>ResetTemporaryMaterials</c> принимает список
        /// элементов, так что временные цвета, поставленные другим плагином, не затрагиваются.
        /// </remarks>
        private void UpdateHighlight(ModelItem item)
        {
            if (ReferenceEquals(item, _highlightedItem))
            {
                return;
            }

            ClearHighlight();

            try
            {
                Navisworks.Document document = Document;
                if (document != null && document.Models != null)
                {
                    document.Models.OverrideTemporaryColor(new[] { item }, new Color(0.2, 0.85, 1.0));
                    _highlightedItem = item;
                }
            }
            catch (Exception)
            {
                _highlightedItem = null;
            }
        }

        private void ClearHighlight()
        {
            if (_highlightedItem == null)
            {
                return;
            }

            try
            {
                Navisworks.Document document = Document;
                if (document != null && document.Models != null)
                {
                    document.Models.ResetTemporaryMaterials(new[] { _highlightedItem });
                }
            }
            catch (Exception)
            {
            }

            _highlightedItem = null;
        }

        private void ClearCaption()
        {
            _lines = null;
        }

        private static void RequestRedraw(View view)
        {
            try
            {
                if (view != null)
                {
                    view.RequestDelayedRedraw(ViewRedrawRequests.OverlayRender);
                }
            }
            catch (Exception)
            {
            }
        }

        private void Deactivate(View view, string reason)
        {
            DiagnosticLog.Info("Режим отметки выключен: " + reason);

            // Выбор точки калибровки тоже отменяется: иначе состояние «идёт калибровка»
            // осталось бы навсегда, и кнопка «Показать отметку» перестала бы
            // подсвечиваться даже при включённом режиме.
            CalibrationSession.End();

            ClearCaption();
            ClearHighlight();
            RequestRedraw(view);

            try
            {
                Navisworks.Document document = Document;
                if (document == null || document.Tool == null)
                {
                    return;
                }

                Navisworks.Tool restore = PendingPreviousTool == Navisworks.Tool.None
                                          || PendingPreviousTool == Navisworks.Tool.CustomToolPlugin
                    ? Navisworks.Tool.CommonOrbit
                    : PendingPreviousTool;

                document.Tool.Set(restore);
                DiagnosticLog.Info("Инструмент переключён на: " + restore);
            }
            catch (Exception exception)
            {
                DiagnosticLog.Error("Не удалось вернуть стандартный инструмент.", exception);
            }
        }
    }
}
