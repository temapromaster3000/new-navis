using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using NavisElevation.Core;

namespace NavisElevation.Navis.Views
{
    /// <summary>
    /// Окно настроек плагина.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Пока это дымовой тест: доказывает, что WPF-окно вообще может быть показано поверх
    /// Navisworks из плагина. Если не покажется — откат на WinForms <c>Form.ShowDialog</c> с
    /// главным окном Navisworks в качестве родителя, и больше ничего в плане менять не
    /// придётся: лента, иконки и обновление от UI-технологии не зависят.
    /// </para>
    /// <para>
    /// WPF в процессе Navisworks работает: сборка GUI самого Navisworks
    /// (<c>navisworks.gui.roamer.dll</c>) ссылается на PresentationFramework,
    /// PresentationCore и WindowsBase, то есть интерфейс программы сам на WPF.
    /// </para>
    /// <para>
    /// Интерфейс собирается в коде, а не в XAML: проекту нужна разметка только для ленты,
    /// а компилятор разметки WPF в проектах под .NET Framework недоступен — проверено
    /// сборкой. Для окна с десятком элементов это даже проще, чем заводить отдельный файл
    /// разметки, который пришлось бы потом собирать руками.
    /// </para>
    /// </remarks>
    public sealed class SettingsView : Window
    {
        private SettingsView()
        {
            Title = "Navis Elevation — Настройки";
            Width = 460;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA));
            Content = BuildContent();

            Loaded += (sender, args) => DiagnosticLog.Info("Окно настроек открыто и загружено.");
        }

        /// <summary>Показать окно настроек поверх Navisworks.</summary>
        /// <remarks>
        /// Имя не <c>ShowDialog</c>: статический метод с таким именем скрывал бы
        /// instance-метод базового <c>Window</c> и путал бы вызов из кода плагина.
        /// </remarks>
        public static int Open()
        {
            try
            {
                var window = new SettingsView();
                AttachOwner(window);

                window.ShowDialog();
                return 0;
            }
            catch (Exception exception)
            {
                DiagnosticLog.Error("Не удалось открыть окно настроек.", exception);
                return 2;
            }
        }

        /// <summary>
        /// Сделать окно дочерним для главного окна Navisworks, иначе оно откроется сзади.
        /// </summary>
        /// <remarks>
        /// У WPF свойство <c>Owner</c> принимает объект <c>Window</c>, а не дескриптор
        /// <c>HWND</c>, поэтому родителя задаём через <c>WindowInteropHelper</c>: это
        /// единственный способ привязать окно к нативному родителю.
        /// </remarks>
        private static void AttachOwner(Window window)
        {
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

        private UIElement BuildContent()
        {
            var panel = new StackPanel { Margin = new Thickness(20) };

            panel.Children.Add(new TextBlock
            {
                Text = "Navis Elevation",
                FontSize = 16,
                FontWeight = FontWeights.Bold
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Версия " + CurrentVersion(),
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55))
            });

            panel.Children.Add(new Border
            {
                Height = 1,
                Margin = new Thickness(0, 14, 0, 14),
                Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xE4, 0xE8))
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Это окно — дымовой тест."
            });

            panel.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                Text = "Оно доказывает, что WPF-окно открывается поверх Navisworks из плагина. "
                       + "Дальше здесь появятся калибровка отметки и проверка обновлений."
            });

            var close = new Button
            {
                Content = "Закрыть",
                Width = 110,
                Margin = new Thickness(0, 18, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                IsCancel = true
            };
            close.Click += (sender, args) => Close();
            panel.Children.Add(close);

            return panel;
        }

        /// <summary>Версия плагина из метаданных сборки.</summary>
        private static string CurrentVersion()
        {
            try
            {
                AssemblyName name = typeof(SettingsView).Assembly.GetName();
                return name.Version == null ? "неизвестна" : name.Version.ToString();
            }
            catch (Exception)
            {
                return "неизвестна";
            }
        }
    }
}