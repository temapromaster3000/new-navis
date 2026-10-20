using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NavisElevation.Core;
using NavisElevation.Navis.Calibration;

namespace NavisElevation.Navis.Views
{
    /// <summary>
    /// Окно ввода проектной отметки для выбранной точки.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Пользователь выбрал точку на модели; плагин знает, что он там показывает, а
    /// пользователь знает, какая отметка в проекте. Окно спрашивает второе и само
    /// считает смещение: вводить нужно одно число, а не разность, — ошибку в разности
    /// человек чаще всего и делает.
    /// </para>
    /// <para>
    /// Смещение всегда в метрах и заменяет прежнее, а не прибавляется к нему. Поэтому
    /// рядом показывается и «отметка сейчас» (с учётом старого смещения), и «отметка без
    /// калибровки» — по ней видно, откуда взялось новое смещение.
    /// </para>
    /// </remarks>
    internal sealed class CalibrationEntryView : Window
    {
        /// <summary>
        /// Минимальное отличие нового значения от текущего, в метрах.
        /// </summary>
        /// <remarks>
        /// Полмиллиметра — это порядок точности подписи: отметки показываются с
        /// точностью до миллиметра, и разница меньше половины миллиметра в ней не
        /// видна.
        /// </remarks>
        private const double MinimumDifference = 0.0005;

        private readonly CalibrationSample _sample;
        private readonly TextBox _input;
        private readonly TextBlock _result;
        private readonly Button _ok;

        private double? _value;

        private CalibrationEntryView(CalibrationSample sample)
        {
            _sample = sample;

            Title = "Navis Elevation — Проектная отметка точки";
            Width = 460;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA));
            Content = BuildContent(out _input, out _result, out _ok);

            Loaded += (sender, args) => DiagnosticLog.Info("Окно ввода проектной отметки открыто.");
        }

        /// <summary>
        /// Показать окно и вернуть введённую проектную отметку; null, если пользователь
        /// отказался.
        /// </summary>
        public static double? Open(CalibrationSample sample)
        {
            if (sample == null)
            {
                return null;
            }

            var window = new CalibrationEntryView(sample);
            NavisWindow.ShowModal(window);
            return window._value;
        }

        private UIElement BuildContent(out TextBox input, out TextBlock result, out Button ok)
        {
            var panel = new StackPanel { Margin = new Thickness(20) };

            panel.Children.Add(new TextBlock
            {
                Text = "Проектная отметка точки",
                FontSize = 16,
                FontWeight = FontWeights.Bold
            });

            panel.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 0),
                Text = "Отметка сейчас: " + NavisWindow.FormatMeters(_sample.Displayed)
            });

            // Без калибровки точка стояла бы по-другому — это и есть точка отсчёта
            // нового смещения. Показываем только когда модель уже калибрована: иначе
            // две строки об одном и том же числе сбивают с толку.
            if (Math.Abs(_sample.CurrentOffset) > 0.0000001)
            {
                panel.Children.Add(new TextBlock
                {
                    Margin = new Thickness(0, 4, 0, 0),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                    Text = "Отметка без калибровки: " + NavisWindow.FormatMeters(_sample.RawMeters)
                });
            }

            panel.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 14, 0, 4),
                TextWrapping = TextWrapping.Wrap,
                Text = "Введите проектную отметку этой точки в метрах:"
            });

            input = new TextBox
            {
                Width = 200,
                HorizontalAlignment = HorizontalAlignment.Left,
                FontSize = 14
            };

            // Поле пустое: плагин не знает проектную отметку и не должен подставлять
            // её за пользователя. Текущие значения показаны выше — их видно, а менять
            // приходится то, что знает проектировщик.
            input.TextChanged += (sender, args) => UpdateResult();
            panel.Children.Add(input);

            result = new TextBlock
            {
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeights.SemiBold
            };

            panel.Children.Add(result);

            panel.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 6, 0, 16),
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)),
                Text = "Смещение в метрах прибавляется к отметке после перевода в метры и "
                      + "заменяет прежнее смещение модели."
            });

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var cancel = new Button
            {
                Content = "Отмена",
                Width = 110,
                Margin = new Thickness(0, 0, 8, 0),
                IsCancel = true
            };

            cancel.Click += (sender, args) => Close();
            buttons.Children.Add(cancel);

            ok = new Button
            {
                Content = "ОК",
                Width = 110,
                IsDefault = true
            };

            ok.Click += (sender, args) =>
            {
                double parsed;

                if (!TryParse(_input.Text, out parsed))
                {
                    return;
                }

                // Та же проверка, что и при вводе: смещение обязано быть ненулевым,
                // иначе калибровка ничего не изменит.
                if (Math.Abs(_sample.NewOffset(parsed)) < MinimumDifference)
                {
                    UpdateResult();
                    return;
                }

                _value = parsed;
                Close();
            };

            buttons.Children.Add(ok);
            panel.Children.Add(buttons);

            UpdateResult();

            return panel;
        }

        /// <summary>Пересчитать и показать новое смещение модели.</summary>
        private void UpdateResult()
        {
            double parsed;

            if (!TryParse(_input.Text, out parsed))
            {
                _result.Text = "Новое смещение модели: введите число, например 3.000";
                _ok.IsEnabled = false;
                return;
            }

            // Калибровка «в ноль» бессмысленна: отметка не сдвинется, а плагин потом
            // напишет, что отметка откалибрована. Поэтому смещение обязано быть
            // ненулевым.
            //
            // Сравнивать надо именно смещение, а не введённое значение с показанной
            // отметкой: иначе нельзя было бы подтвердить уже сделанную калибровку
            // (введённое значение совпало бы с показанной отметкой), зато можно было
            // бы ввести изначальную отметку и получить нулевое смещение.
            if (Math.Abs(_sample.NewOffset(parsed)) < MinimumDifference)
            {
                _result.Text = "Введите значение, при котором отметка сдвинется: иначе "
                               + "калибровка ничего не изменит.";
                _ok.IsEnabled = false;
                return;
            }

            double offset = _sample.NewOffset(parsed);

            _result.Text = "Новое смещение модели: " + NavisWindow.FormatMeters(offset);
            _ok.IsEnabled = true;
        }

        /// <summary>
        /// Разобрать число, введённое пользователем.
        /// </summary>
        /// <remarks>
        /// Запятая и точка равнозначны: русская раскладка даёт запятую, и требовать
        /// пользователя переключать раскладку ради одного числа — лишняя заминка.
        /// </remarks>
        private static bool TryParse(string text, out double value)
        {
            value = 0;

            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string normalized = text.Trim().Replace(',', '.');

            return double.TryParse(
                normalized,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }
    }
}
