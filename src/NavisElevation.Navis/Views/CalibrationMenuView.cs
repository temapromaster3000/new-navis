using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NavisElevation.Core;

namespace NavisElevation.Navis.Views
{
    /// <summary>
    /// Окно калибровки отметки: что сделать с калибровкой этой модели.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Окно открывается кнопкой «Калибровка» на ленте и сразу показывает состояние:
    /// есть ли калибровка для текущей модели и какое смещение она даёт. Дальше —
    /// два действия: задать новую калибровку или сбросить существующую.
    /// </para>
    /// <para>
    /// Интерфейс собирается в коде, а не в XAML: компилятор разметки WPF в проектах
    /// под .NET Framework недоступен, а для окна с десятком элементов код проще
    /// отдельного файла разметки, который потом пришлось бы собирать руками.
    /// </para>
    /// </remarks>
    internal sealed class CalibrationMenuView : Window
    {
        /// <summary>Что выбрал пользователь в окне.</summary>
        internal enum Choice
        {
            /// <summary>Ничего не выбрано: окно закрыто.</summary>
            None,

            /// <summary>Начать выбор точки для новой калибровки.</summary>
            StartNew
        }

        private Choice _choice = Choice.None;

        private CalibrationMenuView()
        {
            Title = "Navis Elevation — Калибровка отметки";
            Width = 480;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA));
            Content = BuildContent();

            Loaded += (sender, args) => DiagnosticLog.Info("Окно калибровки открыто и загружено.");
        }

        /// <summary>Показать окно и вернуть выбор пользователя.</summary>
        public static Choice Open()
        {
            var window = new CalibrationMenuView();
            NavisWindow.ShowModal(window);
            return window._choice;
        }

        private UIElement BuildContent()
        {
            var panel = new StackPanel { Margin = new Thickness(20) };

            panel.Children.Add(new TextBlock
            {
                Text = "Калибровка отметки",
                FontSize = 16,
                FontWeight = FontWeights.Bold
            });

            panel.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                Text = "Отметки в Navisworks считаются от нуля модели. Если он не совпадает "
                      + "с проектным, отметки можно сдвинуть: выберите элемент, проектная "
                      + "отметка которого известна, и укажите её."
            });

            panel.Children.Add(new Border
            {
                Height = 1,
                Margin = new Thickness(0, 14, 0, 14),
                Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xE4, 0xE8))
            });

            var status = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeights.SemiBold
            };

            panel.Children.Add(status);

            var warning = new TextBlock
            {
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
                Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0x50, 0x00)),
                Text = "Файл модели не сохранён. Калибровка не переживёт закрытие Navisworks — "
                      + "сохраните файл и повторите калибровку."
            };

            panel.Children.Add(warning);

            panel.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 4, 0, 14),
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)),
                Text = "Калибровка хранится отдельно для каждой модели и не попадает внутрь "
                      + "файла: коллега откалибрует у себя сам."
            });

            var start = new Button
            {
                Content = "Установить новую калибровку",
                Width = 240,
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            start.Click += (sender, args) =>
            {
                _choice = Choice.StartNew;
                Close();
            };

            panel.Children.Add(start);

            var reset = new Button
            {
                Content = "Сбросить калибровку",
                Width = 240,
                Margin = new Thickness(0, 0, 0, 18),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            reset.Click += (sender, args) =>
            {
                if (Calibration.CalibrationController.Reset())
                {
                    UpdateState(status, reset, warning);
                }
            };

            panel.Children.Add(reset);

            var close = new Button
            {
                Content = "Закрыть",
                Width = 110,
                Margin = new Thickness(0, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                IsCancel = true
            };

            close.Click += (sender, args) => Close();
            panel.Children.Add(close);

            UpdateState(status, reset, warning);

            return panel;
        }

        /// <summary>Показать текущее состояние калибровки и разрешить или запретить сброс.</summary>
        private static void UpdateState(TextBlock status, Button reset, TextBlock warning)
        {
            var document = Autodesk.Navisworks.Api.Application.ActiveDocument;
            bool calibrated = Calibration.CalibrationStore.IsCalibrated(document);
            bool hasRecord = Calibration.CalibrationStore.HasRecord(document);

            // Без сохранённого файла привязать калибровку не к чему: она не переживёт
            // закрытие программы, и об этом надо сказать прямо.
            bool saved = !string.IsNullOrEmpty(Calibration.CalibrationStore.DocumentPath(document));
            warning.Visibility = saved ? Visibility.Collapsed : Visibility.Visible;

            if (!hasRecord)
            {
                status.Text = "Калибровка для этой модели: не задана.";
                reset.IsEnabled = false;
                return;
            }

            double offset = Calibration.CalibrationStore.ReadOffset(document);

            if (!calibrated)
            {
                // Запись есть, но смещение нулевое: отметка не сдвинута, и называть это
                // калибровкой нельзя. Запись можно удалить.
                status.Text = "Калибровка для этой модели: не задана (смещение нулевое, "
                              + "отметка не сдвинута).";
                reset.IsEnabled = true;
                return;
            }

            status.Text = "Калибровка для этой модели: задана, смещение "
                          + NavisWindow.FormatMeters(offset) + ".";
            reset.IsEnabled = true;
        }
    }
}
