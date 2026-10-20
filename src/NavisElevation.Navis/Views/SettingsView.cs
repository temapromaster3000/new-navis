using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NavisElevation.Core;
using NavisElevation.Navis.Updates;

namespace NavisElevation.Navis.Views
{
    /// <summary>
    /// Окно настроек плагина.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Показывает название, краткое описание, версию плагина и позволяет проверить
    /// обновления на GitHub. Если новая версия есть — её можно скачать и
    /// установить: файлы плагина подменит отдельная программа после закрытия
    /// Navisworks.
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
        private readonly UpdateService _updateService = new UpdateService();

        private readonly TextBlock _statusText;
        private readonly TextBox _notesText;
        private readonly ScrollViewer _notesScroll;
        private readonly ProgressBar _progressBar;
        private readonly Button _checkButton;
        private readonly Button _downloadButton;

        private ReleaseInfo _pendingRelease;

        /// <summary>Обновление уже скачано и разложено — осталось запустить обновлятель.</summary>
        private bool _staged;

        private SettingsView()
        {
            Title = "Navis Elevation — Настройки";
            Width = 520;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA));

            _statusText = new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                Text = "Нажмите «Проверить обновления», чтобы узнать, не вышла ли новая версия."
            };

            _checkButton = new Button
            {
                Content = "Проверить обновления",
                Width = 170,
                Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            _checkButton.Click += CheckButton_Click;

            _downloadButton = new Button
            {
                Content = "Скачать и установить",
                Width = 200,
                Margin = new Thickness(10, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Visibility = Visibility.Collapsed
            };
            _downloadButton.Click += DownloadButton_Click;

            _progressBar = new ProgressBar
            {
                Height = 6,
                Margin = new Thickness(0, 10, 0, 0),
                Visibility = Visibility.Collapsed
            };

            _notesText = new TextBox
            {
                Margin = new Thickness(0, 6, 0, 0),
                Height = 110,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                IsReadOnly = true,
                Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF2, 0xF5)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xD8, 0xDC, 0xE0)),
                Visibility = Visibility.Collapsed
            };

            _notesScroll = new ScrollViewer
            {
                Content = _notesText,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Visibility = Visibility.Collapsed
            };

            Content = BuildContent();

            ShowPendingUpdateIfAny();

            Loaded += (sender, args) => DiagnosticLog.Info("Окно настроек открыто и загружено.");
        }

        /// <summary>
        /// Показать скачанное, но не применённое обновление.
        /// </summary>
        /// <remarks>
        /// Маркер остаётся на диске, если Navisworks не закрыли вовремя. Чтобы
        /// обновление не потерялось молча, окно сообщает о нём и предлагает запустить
        /// обновлятель.
        /// </remarks>
        private void ShowPendingUpdateIfAny()
        {
            string version = UpdateService.GetPendingVersion();
            if (version == null)
            {
                return;
            }

            _staged = true;
            _statusText.Text = "Обновление " + version + " скачано, но ещё не установлено.";
            _downloadButton.Content = "Запустить обновление";
            _downloadButton.Visibility = Visibility.Visible;

            DiagnosticLog.Info("Найдено скачанное, но не установленное обновление: " + version);
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

            panel.Children.Add(new Border
            {
                Height = 1,
                Margin = new Thickness(0, 14, 0, 14),
                Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xE4, 0xE8))
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Обновления",
                FontWeight = FontWeights.Bold
            });

            panel.Children.Add(_statusText);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(_checkButton);
            buttons.Children.Add(_downloadButton);
            panel.Children.Add(buttons);

            panel.Children.Add(_progressBar);

            panel.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 12, 0, 0),
                Text = "Что нового в последней версии:",
                FontWeight = FontWeights.SemiBold,
                Visibility = Visibility.Collapsed,
                Tag = "notesHeader"
            });

            panel.Children.Add(_notesScroll);

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

        /// <summary>
        /// Спросить у GitHub, есть ли новая версия.
        /// </summary>
        private async void CheckButton_Click(object sender, RoutedEventArgs e)
        {
            _pendingRelease = null;
            _downloadButton.Visibility = Visibility.Collapsed;
            HideNotes();
            _statusText.Text = "Проверяем обновления...";

            _checkButton.IsEnabled = false;
            _progressBar.IsIndeterminate = true;
            _progressBar.Visibility = Visibility.Visible;

            try
            {
                ReleaseInfo release = await _updateService.CheckForUpdateAsync();

                if (release == null)
                {
                    _statusText.Text = "У вас установлена последняя версия.";
                }
                else
                {
                    _pendingRelease = release;
                    _statusText.Text = "Доступна новая версия: " + release.TagName + ".";
                    ShowNotes(release.ReleaseNotes);
                    _downloadButton.Content = "Скачать и установить " + release.TagName;
                    _downloadButton.Visibility = Visibility.Visible;
                }
            }
            catch (Exception exception)
            {
                DiagnosticLog.Error("Не удалось проверить обновления.", exception);
                _statusText.Text = "Не удалось проверить обновления: " + exception.Message;
            }
            finally
            {
                _progressBar.IsIndeterminate = false;
                _progressBar.Visibility = Visibility.Collapsed;
                _checkButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Скачать новую версию и подготовить её подмену.
        /// </summary>
        /// <remarks>
        /// После скачивания кнопка не исчезает, а меняет смысл: если обновлятель
        /// отложен, его можно запустить позже — из этого же окна.
        /// </remarks>
        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingRelease == null)
            {
                return;
            }

            if (_staged)
            {
                // Обновление уже скачано — осталось только запустить обновлятель.
                LaunchUpdater();
                return;
            }

            string version = _pendingRelease.TagName;

            _checkButton.IsEnabled = false;
            _downloadButton.IsEnabled = false;
            _downloadButton.Content = "Скачивание...";
            _statusText.Text = "Скачиваем " + version + "...";

            _progressBar.IsIndeterminate = false;
            _progressBar.Value = 0;
            _progressBar.Visibility = Visibility.Visible;

            try
            {
                var progress = new Progress<double>(value => _progressBar.Value = value * 100);
                await _updateService.DownloadAndStageAsync(_pendingRelease, progress);

                _staged = true;
                _statusText.Text = "Обновление " + version + " скачано.";

                bool confirmed = MessageBox.Show(
                    this,
                    "Обновление " + version + " скачано и готово к установке." + Environment.NewLine
                    + Environment.NewLine
                    + "Файлы плагина подменятся, когда Navisworks закроется." + Environment.NewLine
                    + "Закройте Navisworks — обновление применится само." + Environment.NewLine
                    + Environment.NewLine
                    + "Запустить обновление сейчас?",
                    "Navis Elevation — обновление",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information) == MessageBoxResult.Yes;

                if (confirmed)
                {
                    LaunchUpdater();
                }
                else
                {
                    _statusText.Text = "Обновление " + version + " скачано и ждёт. Закройте Navisworks и "
                                     + "запустите обновление позже — кнопка «Запустить обновление» "
                                     + "останется здесь.";
                }

                _downloadButton.Content = "Запустить обновление";
                _downloadButton.Visibility = Visibility.Visible;
            }
            catch (Exception exception)
            {
                DiagnosticLog.Error("Не удалось скачать обновление.", exception);
                _statusText.Text = "Не удалось скачать обновление: " + exception.Message;
                _downloadButton.Content = "Скачать и установить " + version;
            }
            finally
            {
                _progressBar.Visibility = Visibility.Collapsed;
                _checkButton.IsEnabled = true;
                _downloadButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Запустить обновлятель: он дождётся закрытия Navisworks и подменит файлы.
        /// </summary>
        private void LaunchUpdater()
        {
            try
            {
                _updateService.LaunchUpdater();
                _statusText.Text = "Обновление скачано. Закройте Navisworks — обновление применится "
                                 + "при выходе.";
            }
            catch (Exception exception)
            {
                DiagnosticLog.Error("Не удалось запустить обновлятор.", exception);
                _statusText.Text = "Не удалось запустить обновление: " + exception.Message;
            }
        }

        private void ShowNotes(string notes)
        {
            _notesText.Text = notes ?? string.Empty;
            _notesScroll.Visibility = Visibility.Visible;

            TextBlock header = FindNotesHeader();
            if (header != null)
            {
                header.Visibility = Visibility.Visible;
            }
        }

        private void HideNotes()
        {
            _notesText.Text = string.Empty;
            _notesScroll.Visibility = Visibility.Collapsed;

            TextBlock header = FindNotesHeader();
            if (header != null)
            {
                header.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Найти заголовок «Что нового в последней версии».
        /// </summary>
        /// <remarks>
        /// Заголовок помечен в <c>Tag</c>, чтобы не тянуть его отдельным полем: он
        /// нужен только для показа и скрытия, а искать его в дереве по тексту —
        /// хрупко.
        /// </remarks>
        private TextBlock FindNotesHeader()
        {
            var panel = Content as Panel;
            if (panel == null)
            {
                return null;
            }

            foreach (UIElement child in panel.Children)
            {
                var textBlock = child as TextBlock;
                if (textBlock != null && string.Equals(textBlock.Tag as string, "notesHeader", StringComparison.Ordinal))
                {
                    return textBlock;
                }
            }

            return null;
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
