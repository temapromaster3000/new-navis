using System;
using Autodesk.Navisworks.Api;
using NavisElevation.Core;
using NavisElevation.Navis.Views;
using Navisworks = Autodesk.Navisworks.Api;

namespace NavisElevation.Navis.Calibration
{
    /// <summary>
    /// Порядок работы калибровки: окно, выбор точки, применение смещения.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Сценарий: кнопка на ленте → окно с двумя действиями → режим выбора точки →
    /// клик по элементу → окно ввода проектной отметки → смещение сохранено.
    /// </para>
    /// <para>
    /// Окно ввода проектной отметки открывается не прямо из обработчика мыши, а через
    /// диспетчер: модальное окно, открытое изнутри обработчика события Navisworks,
    /// запускает вложенный цикл сообщений и умеет намертво повесить программу.
    /// </para>
    /// </remarks>
    internal static class CalibrationController
    {
        /// <summary>Открыть окно калибровки — точку входа с кнопки ленты.</summary>
        public static int OpenMenu()
        {
            try
            {
                CalibrationMenuView.Choice choice = CalibrationMenuView.Open();

                if (choice == CalibrationMenuView.Choice.StartNew)
                {
                    return StartPicking();
                }

                return 0;
            }
            catch (Exception exception)
            {
                DiagnosticLog.Error("Не удалось открыть окно калибровки.", exception);
                return 2;
            }
        }

        /// <summary>Включить режим выбора точки калибровки.</summary>
        public static int StartPicking()
        {
            Navisworks.Document document = Application.ActiveDocument;

            if (document == null || document.Tool == null)
            {
                DiagnosticLog.Warn("Документ не открыт — калибровка не запущена.");
                return 1;
            }

            if (!ProbeToggle.HasModel())
            {
                DiagnosticLog.Warn("Нет открытой модели — калибровка не запущена.");
                return 1;
            }

            CalibrationSession.Begin();
            DiagnosticLog.Info("Включён режим выбора точки калибровки.");

            int code = ProbeToggle.EnsureActive();

            if (code != 0)
            {
                CalibrationSession.End();
                DiagnosticLog.Warn("Инструмент показа отметки включить не удалось — калибровка отменена.");
            }
            else
            {
                FocusView();
            }

            return code;
        }

        /// <summary>
        /// Вернуть клавиатурный фокус в 3D-вид.
        /// </summary>
        /// <remarks>
        /// После закрытия окна фокус остаётся на ленте, и Esc до плагина не доходит:
        /// режим отметки нельзя выключить с клавиатуры, приходится наводить курсор на
        /// кнопку. Фокус возвращается явно.
        /// </remarks>
        private static void FocusView()
        {
            try
            {
                ViewFocus.RememberMainWindow();

                IntPtr mainWindow = ViewFocus.MainWindowHandle;

                if (mainWindow == IntPtr.Zero)
                {
                    return;
                }

                bool focused = ViewFocus.FocusActiveView(mainWindow);

                if (focused)
                {
                    DiagnosticLog.Info("Фокус возвращён в 3D-вид: Esc выключит режим отметки.");
                    return;
                }

                // Запасной путь: переключаем инструмент заново — Navisworks при этом
                // иногда возвращает фокус в вид сам. Гарантии тут нет, так и пишем.
                bool reactivated = ProbeToggle.Reactivate() == 0;

                DiagnosticLog.Info(reactivated
                    ? "Фокус в 3D-вид перенести не удалось, инструмент перевключён: "
                      + "Esc может не сработать, режим выключается кнопкой."
                    : "Фокус в 3D-вид перенести не удалось, инструмент перевключить не удалось: "
                      + "Esc может не сработать, режим выключается кнопкой.");
            }
            catch (Exception exception)
            {
                DiagnosticLog.Warn("Не удалось вернуть фокус в 3D-вид: " + exception.Message);
            }
        }

        /// <summary>
        /// Открыть окно ввода проектной отметки для выбранной точки.
        /// </summary>
        /// <remarks>
        /// Вызывается из обработчика мыши Navisworks, и окно открывается прямо здесь.
        /// Отложенный вызов через диспетчер не годится: очередь сообщений вне
        /// модальных окон Navisworks не качает, и окно просто не появлялось.
        ///
        /// Цена решения — вложенный цикл сообщений внутри события мыши. Если Navisworks
        /// на этом зависнет, порядок шагов придётся поменять: сначала ввод отметки из
        /// окна на ленте, потом клик по точке.
        /// </remarks>
        public static void OpenEntry(CalibrationSample sample)
        {
            if (sample == null)
            {
                return;
            }

            try
            {
                double? projectElevation = CalibrationEntryView.Open(sample);

                if (projectElevation.HasValue)
                {
                    Apply(sample, projectElevation.Value);
                }
                else
                {
                    DiagnosticLog.Info("Ввод проектной отметки отменён — калибровка не изменилась.");
                }
            }
            catch (Exception exception)
            {
                DiagnosticLog.Error("Ошибка при вводе проектной отметки.", exception);
            }
            finally
            {
                // Окно закрылось — фокус вернулся на ленту, и Esc перестал доходить до
                // плагина. Возвращаем фокус в вид, чтобы режим выключался с клавиатуры.
                FocusView();
            }
        }

        /// <summary>Сохранить смещение и обновить подпись.</summary>
        public static void Apply(CalibrationSample sample, double projectElevation)
        {
            double newOffset = sample.NewOffset(projectElevation);

            Navisworks.Document document = Application.ActiveDocument;
            bool saved = CalibrationStore.SaveOffset(document, newOffset, sample.ModelGuid);

            DiagnosticLog.Info("Калибровка применена: проектная отметка "
                               + projectElevation.ToString("0.###") + " м, смещение "
                               + newOffset.ToString("0.###") + " м, сохранено: " + saved);

            ElevationProbeTool.RefreshCaption();
        }

        /// <summary>Сбросить калибровку документа.</summary>
        public static bool Reset()
        {
            Navisworks.Document document = Application.ActiveDocument;
            bool cleared = CalibrationStore.ClearOffset(document);

            DiagnosticLog.Info(cleared
                               ? "Калибровка сброшена: отметки снова считаются от нуля модели."
                               : "Сбрасывать было нечего: калибровки для этой модели нет.");

            ElevationProbeTool.RefreshCaption();
            return cleared;
        }
    }
}
