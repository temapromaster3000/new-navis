using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NavisElevation.Navis
{
    /// <summary>
    /// Передача фокуса клавиатуры в 3D-вид Navisworks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// После нажатия кнопки на ленте клавиатурный фокус остаётся на ленте, и клавиши до
    /// плагина не доходят: Esc не выключает режим, пока пользователь не кликнет в вид.
    /// Проверено на практике - с кликом в вид Esc работает сразу.
    /// </para>
    /// <para>
    /// Управлять фокусом через API нельзя: у <c>View</c> есть только
    /// <c>FocusOnCurrentSelection</c>, <c>StartInteractive</c> и <c>EndInteractive</c>.
    /// Поэтому используется Win32: окно 3D-вида у Navisworks имеет собственный класс
    /// <c>NavisWorksRenderWindow</c>, и на него ставится фокус.
    /// </para>
    /// <para>
    /// Если класс окна изменится в другой версии Navisworks, ничего не сломается: окно
    /// просто не найдётся, фокус останется на ленте, и Esc будет работать после клика
    /// в вид, как сейчас. На передний план окно не выводится - фокус меняется внутри
    /// уже активного окна.
    /// </para>
    /// </remarks>
    internal static class ViewFocus
    {
        private const string RenderWindowClass = "NavisWorksRenderWindow";

        private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool SetFocus(IntPtr window);

        /// <summary>
        /// Передать фокус клавиатуры в 3D-вид, если окно вида нашлось.
        /// </summary>
        /// <returns><c>true</c>, если фокус передан.</returns>
        public static bool FocusActiveView(IntPtr mainWindow)
        {
            if (mainWindow == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                IntPtr view = FindRenderWindow(mainWindow);
                if (view == IntPtr.Zero)
                {
                    return false;
                }

                return SetFocus(view);
            }
            catch (Exception)
            {
                // Фокус - удобство, а не необходимость: сорвавшийся вызов не должен
                // мешать работать режиму отметки.
                return false;
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