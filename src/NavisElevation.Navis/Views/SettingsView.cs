using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
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
                NavisWindow.AttachOwner(window);

                window.ShowDialog();
                return 0;
            }
            catch (Exception exception)
            {
                DiagnosticLog.Error("Не удалось открыть окно настроек.", exception);
                return 2;
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
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                Text = "Показывает отметку элемента под курсором прямо в 3D-виде."
                     + Environment.NewLine
                     + "Включается кнопкой «Показать отметку» на вкладке Navis Elevation."
                     + " Выключается по Esc, правой кнопке мыши или повторному нажатию кнопки."
                     + Environment.NewLine
                     + "Отметка переводится в метры: модель в футах, дюймах или миллиметрах — "
                     + "результат один."
            });

            panel.Children.Add(new Border
            {
                Height = 1,
                Margin = new Thickness(0, 14, 0, 14),
                Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xE4, 0xE8))
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Версия " + CurrentVersion(),
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55))
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
        /// <remarks>
        /// Берётся <c>InformationalVersion</c> и от неё отрезается всё после знака «+».
        /// Сборка SDK дописывает туда хеш коммита, а пользователю нужна чистая версия -
        /// такая же, как в <c>Version.txt</c>. Если атрибута нет, берётся
        /// <c>AssemblyVersion</c>.
        /// </remarks>
        private static string CurrentVersion()
        {
            try
            {
                Assembly assembly = typeof(SettingsView).Assembly;

                var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                if (informational != null && !string.IsNullOrWhiteSpace(informational.InformationalVersion))
                {
                    string version = informational.InformationalVersion;
                    int plus = version.IndexOf('+');

                    if (plus > 0)
                    {
                        version = version.Substring(0, plus);
                    }

                    return version;
                }

                AssemblyName name = assembly.GetName();
                return name.Version == null ? "неизвестна" : name.Version.ToString();
            }
            catch (Exception)
            {
                return "неизвестна";
            }
        }
    }
}