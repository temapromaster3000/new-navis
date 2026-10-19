using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using NavisElevation.Core;
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

        /// <summary>Смещение подписи от курсора, чтобы не перекрывать сам элемент.</summary>
        private const int AnchorOffsetX = 18;

        private const int AnchorOffsetY = 20;

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

        private ProjectSettings _settings;
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
        private ProjectSettings Settings
        {
            get
            {
                if (_settings == null)
                {
                    try
                    {
                        _settings = ProjectSettings.Load(null);
                    }
                    catch (Exception exception)
                    {
                        DiagnosticLog.Error("Не удалось прочитать настройки, беру значения по умолчанию.", exception);
                    }

                    if (_settings == null)
                    {
                        _settings = new ProjectSettings();
                    }
                }

                return _settings;
            }
        }

        private string[] _lines;
        private int _anchorX;
        private int _anchorY;

        private int _lastPickX = -10000;
        private int _lastPickY = -10000;
        private DateTime _lastPickTime = DateTime.MinValue;

        private ModelItem _highlightedItem;
        private bool _hadItem;
        private TextFontInfo _font;

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
            if (key == EscapeKey)
            {
                Deactivate(view, "Esc");
                return true;
            }

            return false;
        }

        public override bool MouseUp(View view, KeyModifiers modifiers, ushort button, int x, int y, double timeOffset)
        {
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
                    graphics.Color(shadow ? ShadowColor : TextColor, 1.0);

                    int dx = shadow ? 1 : 0;
                    int dy = shadow ? 1 : 0;

                    for (int i = 0; i < _lines.Length; i++)
                    {
                        string line = _lines[i];
                        if (string.IsNullOrEmpty(line))
                        {
                            continue;
                        }

                        graphics.Text2D(_font, line, Point2D.Origin, _anchorX + dx, _anchorY + i * LineHeight + dy);
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

            if (hit == null || hit.ModelItem == null)
            {
                // Курсор в пустоте: снимаем подпись, чтобы не показывать чужие данные.
                if (_hadItem)
                {
                    _hadItem = false;
                    DiagnosticLog.Info("Курсор в пустоте — подпись снята.");
                }

                ClearCaption();
                ClearHighlight();
                RequestRedraw(view);
                return;
            }

            _hadItem = true;

            BuildCaption(hit, view, x, y);
            UpdateHighlight(hit.ModelItem);
            RequestRedraw(view);
        }

        private void BuildCaption(PickItemResult hit, View view, int x, int y)
        {
            try
            {
                EnsureFormatter(hit.ModelItem);

                var lines = new List<string>();

                string name = hit.ModelItem.DisplayName;
                if (string.IsNullOrEmpty(name))
                {
                    name = hit.ModelItem.ClassDisplayName;
                }

                if (!string.IsNullOrEmpty(name))
                {
                    lines.Add(name);
                }

                lines.Add("Отметка: " + _formatter.FormatMeters(hit.Point.Z));

                BoundingBox3D box = hit.ModelItem.BoundingBox();
                if (box != null && !box.IsEmpty)
                {
                    lines.Add("Границы: " + _formatter.FormatMetersSpan(box.Min.Z, box.Max.Z));
                }

                Model owner = DocumentCatalog.FindOwningModel(Document, hit.ModelItem);
                string modelName = DocumentCatalog.ReadModelName(owner);
                if (!string.IsNullOrEmpty(modelName))
                {
                    lines.Add("Модель: " + modelName);
                }

                _lines = lines.ToArray();
                _anchorX = ComputeAnchorX(view, x);
                _anchorY = Math.Min(y + AnchorOffsetY, Math.Max(0, view.Height - _lines.Length * LineHeight));
            }
            catch (Exception)
            {
                ClearCaption();
            }
        }

        /// <summary>
        /// Слева от курсора или у левого края вида.
        /// </summary>
        /// <remarks>
        /// Точную ширину текста пришлось бы измерять вызовом Text2DExtents на каждом кадре,
        /// поэтому решение грубее и дешевле: если курсор в левой половине вида, текст идёт
        /// справа от него, иначе прижимается к левому краю. Так подпись не обрезается.
        /// </remarks>
        private static int ComputeAnchorX(View view, int x)
        {
            return x > view.Width / 2 ? 12 : x + AnchorOffsetX;
        }

        private void EnsureFormatter(ModelItem item)
        {
            LengthUnit unit = DocumentCatalog.ReadUnit(Document, item);

            if (_formatter == null || _formatter.Unit != unit || _formatter.Offset != Settings.ProjectOffset)
            {
                _formatter = new ElevationFormatter(unit, Settings.ProjectOffset);
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
