using System;
using System.Runtime.InteropServices;
using System.Text;
using NavisElevation.Core;

namespace NavisElevation.Navis
{
    /// <summary>
    /// Передача фокуса клавиатуры в 3D-вид Navisworks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// После нажатия кнопки на ленте клавиатурный фокус остаётся на ленте, и клавиши до
    /// плагина не доходят: Esc не выключает режим, пока пользователь не кликнет в вид.
    /// То же самое после закрытия окон плагина: фокус остаётся там, где был, и режим
    /// отметки нельзя выключить с клавиатуры.
    /// </para>
    /// <para>
    /// Управлять фокусом через API нельзя: у <c>View</c> есть только
    /// <c>FocusOnCurrentSelection</c>, <c>StartInteractive</c> и <c>EndInteractive</c>, и
    /// ни один из них не переключает клавиатурный фокус. Поэтому используется Win32:
    /// окно 3D-вида у Navisworks имеет собственный класс <c>NavisWorksRenderWindow</c>,
    /// и на него ставится фокус.
    /// </para>
    /// <para>
    /// <b>Главная тонкость: <c>SetFocus</c> работает только внутри одной очереди
    /// ввода.</b> Плагин живёт в своём потоке, а окно вида — в потоке Navisworks, и
    /// простой <c>SetFocus</c> из чужого потока молча ничего не делает, но возвращает
    /// ненулевое значение: выглядит так, будто фокус перешёл. Поэтому очереди ввода
    /// склеиваются через <c>AttachThreadInput</c>, а результат проверяется через
    /// <c>GetFocus</c>.
    /// </para>
    /// <para>
    /// Каждый шаг переноса пишется в лог <b>до</b> того, как он выполнен. Иначе при
    /// падении не видно, где именно остановилось, а молчаливый <c>catch</c> выглядит
    /// как «фокус перешёл, но Esc почему-то не работает».
    /// </para>
    /// </remarks>
    internal static class ViewFocus
    {
        private const string RenderWindowClass = "NavisWorksRenderWindow";

        /// <summary>
        /// Handle главного окна Navisworks, запомненный заранее.
        /// </summary>
        /// <remarks>
        /// Окно плагина закрывается уже после того, как API Navisworks «отпустил» его
        /// поток, и обращаться к API оттуда нельзя. Handle запоминается заранее, из
        /// потока плагина, а в обработчике закрытия окна используется он.
        /// </remarks>
        public static IntPtr MainWindowHandle { get; set; }

        private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool SetFocus(IntPtr window);

        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

        /// <summary>
        /// Идентификатор текущего потока.
        /// </summary>
        /// <remarks>
        /// Функция живёт в <c>kernel32.dll</c>, а не в <c>user32.dll</c>: с неверной
        /// библиотекой P/Invoke бросает <c>EntryPointNotFoundException</c> на первой же
        /// строке переноса фокуса, и безмолвливый <c>catch</c> выглядит как «фокус не
        /// переносится вообще».
        /// </remarks>
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        /// <summary>
        /// Поток, создавший окно, и идентификатор процесса.
        /// </summary>
        /// <remarks>
        /// Функция возвращает <c>DWORD</c>, а не указатель: при объявлении как
        /// <c>IntPtr</c> на 64-битной системе в значение попадает мусор из старших битов,
        /// и сравнение потоков даёт неправильный ответ.
        /// </remarks>
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr window);

        /// <summary>
        /// Запомнить handle главного окна Navisworks.
        /// </summary>
        /// <remarks>
        /// Вызывается из потока плагина, где обращения к API Navisworks разрешены.
        /// Понадобится позже, в обработчике закрытия окна: там API уже недоступен, а
        /// handle нужен, чтобы вернуть фокус в вид.
        /// </remarks>
        public static void RememberMainWindow()
        {
            try
            {
                Autodesk.Navisworks.Api.ApplicationParts.IApplicationGui gui =
                    Autodesk.Navisworks.Api.Application.Gui;

                if (gui != null && gui.MainWindow != null)
                {
                    MainWindowHandle = gui.MainWindow.Handle;
                }
            }
            catch (Exception exception)
            {
                DiagnosticLog.Warn("Не удалось запомнить главное окно Navisworks: " + exception.Message);
            }
        }

        /// <summary>
        /// Передать фокус клавиатуры в 3D-вид, если окно вида нашлось.
        /// </summary>
        /// <returns><c>true</c>, если фокус удалось поставить.</returns>
        public static bool FocusActiveView(IntPtr mainWindow)
        {
            if (mainWindow == IntPtr.Zero)
            {
                DiagnosticLog.Info("Перенос фокуса: главное окно Navisworks недоступно.");
                return false;
            }

            try
            {
                IntPtr view = FindRenderWindow(mainWindow);

                DiagnosticLog.Info("Перенос фокуса: поток плагина " + GetCurrentThreadId()
                                   + ", окно 3D-вида " + (view != IntPtr.Zero ? "найдено" : "не найдено")
                                   + " (поток " + ThreadIdOf(view) + ")"
                                   + ", главное окно (поток " + ThreadIdOf(mainWindow) + ")");

                if (view != IntPtr.Zero && TrySetFocus(view, mainWindow))
                {
                    DiagnosticLog.Info("Фокус передан в 3D-вид: Esc будет работать.");
                    return true;
                }

                if (TrySetFocus(mainWindow, mainWindow))
                {
                    DiagnosticLog.Info("Фокус передан в главное окно Navisworks: Esc будет работать.");
                    return true;
                }

                DiagnosticLog.Info("Фокус не перешёл: Esc сработает после клика в 3D-вид.");
                return false;
            }
            catch (Exception exception)
            {
                DiagnosticLog.Error("Не удалось перенести фокус в 3D-вид.", exception);
                return false;
            }
        }

        /// <summary>
        /// Поставить фокус на окно, склеив очереди ввода с потоком Navisworks.
        /// </summary>
        private static bool TrySetFocus(IntPtr target, IntPtr mainWindow)
        {
            if (target == IntPtr.Zero)
            {
                return false;
            }

            uint ownerThread = ThreadIdOf(target);
            uint currentThread = GetCurrentThreadId();
            bool attached = false;

            try
            {
                if (ownerThread != 0 && ownerThread != currentThread)
                {
                    attached = AttachThreadInput(currentThread, ownerThread, true);

                    DiagnosticLog.Info("Склейка очередей ввода " + currentThread + " и "
                                       + ownerThread + ": " + (attached ? "удалась" : "НЕ удалась"));
                }
                else
                {
                    DiagnosticLog.Info("Поток окна совпадает с потоком плагина (" + currentThread
                                       + ") — склейка очередей не нужна");
                }

                SetForegroundWindow(mainWindow);
                SetFocus(target);

                IntPtr focused = GetFocus();

                DiagnosticLog.Info("SetFocus сделан, GetFocus вернул: "
                                   + (focused == IntPtr.Zero ? "пусто" : focused.ToString()));

                return focused != IntPtr.Zero;
            }
            finally
            {
                if (attached)
                {
                    AttachThreadInput(currentThread, ownerThread, false);
                }
            }
        }

        /// <summary>Поток, создавший окно; 0, если окна нет.</summary>
        private static uint ThreadIdOf(IntPtr window)
        {
            if (window == IntPtr.Zero)
            {
                return 0;
            }

            try
            {
                uint processId;
                return GetWindowThreadProcessId(window, out processId);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>Найти видимое окно 3D-вида среди потомков главного окна.</summary>
        private static IntPtr FindRenderWindow(IntPtr mainWindow)
        {
            IntPtr found = IntPtr.Zero;

            EnumChildWindows(mainWindow, (window, parameter) =>
            {
                if (found != IntPtr.Zero)
                {
                    return false;
                }

                if (!IsWindowVisible(window))
                {
                    return true;
                }

                var className = new StringBuilder(256);
                GetClassName(window, className, className.Capacity);

                if (string.Equals(className.ToString(), RenderWindowClass, StringComparison.Ordinal))
                {
                    found = window;
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            return found;
        }
    }
}
