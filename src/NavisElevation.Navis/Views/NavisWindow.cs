using System;
using System.Windows;
using System.Windows.Interop;
using NavisElevation.Core;

namespace NavisElevation.Navis.Views
{
    /// <summary>
    /// Общие приёмы для окон плагина.
    /// </summary>
    internal static class NavisWindow
    {
        /// <summary>
        /// Сделать окно дочерним для главного окна Navisworks, иначе оно откроется сзади.
        /// </summary>
        /// <remarks>
        /// У WPF свойство <c>Owner</c> принимает объект <c>Window</c>, а не дескриптор
        /// <c>HWND</c>, поэтому родителя задаём через <c>WindowInteropHelper</c>: это
        /// единственный способ привязать окно к нативному родителю.
        /// </remarks>
        public static void AttachOwner(Window window)
        {
            if (window == null)
            {
                return;
            }

            try
            {
                Autodesk.Navisworks.Api.ApplicationParts.IApplicationGui gui =
                    Autodesk.Navisworks.Api.Application.Gui;

                if (gui == null || gui.MainWindow == null)
                {
                    DiagnosticLog.Warn("Главное окно Navisworks недоступно — окно откроется без родителя.");
                    return;
                }

                new WindowInteropHelper(window).Owner = gui.MainWindow.Handle;
            }
            catch (Exception exception)
            {
                DiagnosticLog.Warn("Не удалось привязать окно к главному окну Navisworks: " + exception.Message);
            }
        }

        /// <summary>Открыть окно поверх Navisworks.</summary>
        /// <remarks>
        /// Окно показывается модальным: пока оно открыто, Navisworks не должен
        /// обрабатывать клики мышью по виду.
        /// </remarks>
        public static void ShowModal(Window window)
        {
            AttachOwner(window);
            FocusViewOnClose(window);
            window.ShowDialog();
        }

        /// <summary>
        /// Вернуть фокус в 3D-вид после закрытия окна.
        /// </summary>
        /// <remarks>
        /// <para>
        /// После закрытия окна клавиатурный фокус остаётся на ленте, и Esc до плагина
        /// не доходит: режим отметки нельзя выключить с клавиатуры, приходится наводить
        /// курсор на кнопку.
        /// </para>
        /// <para>
        /// Обработчик <c>Closed</c> выполняется в потоке самого окна. Если этот поток
        /// совпадает с потоком окна 3D-вида, то <c>SetFocus</c> срабатывает напрямую, без
        /// склейки очередей ввода.
        /// </para>
        /// </remarks>
        public static void FocusViewOnClose(Window window)
        {
            if (window == null)
            {
                return;
            }

            window.Closed += (sender, args) =>
            {
                try
                {
                    // Handle главного окна был запомнен заранее, из потока плагина:
                    // отсюда обращаться к API Navisworks уже нельзя.
                    IntPtr mainWindow = ViewFocus.MainWindowHandle;

                    if (mainWindow == IntPtr.Zero)
                    {
                        return;
                    }

                    ViewFocus.FocusActiveView(mainWindow);
                }
                catch (Exception exception)
                {
                    DiagnosticLog.Warn("Не удалось вернуть фокус в 3D-вид после закрытия окна: "
                                       + exception.Message);
                }
            };
        }

        /// <summary>Число с отметкой в метрах: «+2.640 м».</summary>
        public static string FormatMeters(double value)
        {
            return value.ToString("+0.000;-0.000;0.000", System.Globalization.CultureInfo.InvariantCulture)
                   + " м";
        }
    }
}
