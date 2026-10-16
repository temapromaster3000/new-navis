using System;
using System.Globalization;

namespace NavisElevation.Core
{
    /// <summary>
    /// Превращает сырую координату Z из Navisworks в строки для подписи на экране.
    /// </summary>
    /// <remarks>
    /// В API Navisworks нет базовой точки проекта, поэтому «правильность» отметки нельзя
    /// проверить заранее — она проверяется сверкой с уровнями. Из-за этого форматтер
    /// показывает <b>две</b> величины: пересчитанную в метры (так отметки записывают
    /// в чертежах) и исходное число в единицах модели (чтобы можно было сверить
    /// с эталоном вручную).
    ///
    /// <see cref="ProjectSettings.ProjectOffset"/> — калибровочное смещение в метрах,
    /// прибавляемое к отметке. При первом прогоне на реальной модели пользователь сверяет
    /// отметку элемента с известной и вписывает сюда разницу.
    /// </remarks>
    public sealed class ElevationFormatter
    {
        /// <summary>Отметки в чертежах пишут в метрах с точностью до миллиметра.</summary>
        private const string MeterFormat = "+0.000;-0.000;0.000";

        /// <summary>Сырое значение показываем без лишних нулей: «12345,678» вместо «12345,678000».</summary>
        private const string RawFormat = "0.###";

        /// <summary>
        /// Подпись отметки всегда в метрах, независимо от единиц модели.
        /// </summary>
        /// <remarks>
        /// Модель может прийти в футах или дюймах, но отметки в чертежах пишут в метрах.
        /// Подписывать переведённое значение единицами модели нельзя: получается «9.843 фт»
        /// вместо «3.000 м», то есть число, которое никто не может прочитать как высоту.
        /// </remarks>
        private const string MetersSuffix = "м";

        private readonly LengthUnit _unit;
        private readonly double _offset;

        public ElevationFormatter(LengthUnit unit, double offset)
        {
            _unit = unit;
            _offset = offset;
        }

        public LengthUnit Unit
        {
            get { return _unit; }
        }

        public double Offset
        {
            get { return _offset; }
        }

        /// <summary>Перевести значение из единиц модели в метры.</summary>
        public double ToMeters(double rawValue)
        {
            return rawValue * LengthUnits.ToMeters(_unit);
        }

        /// <summary>
        /// Отметка в метрах с учётом калибровочного смещения.
        /// </summary>
        /// <remarks>
        /// Смещение всегда в метрах и прибавляется <b>после</b> перевода. Порядок важен:
        /// вычитать метры из миллиметров или футов бессмысленно. Например, при модели
        /// в футах и смещении 0.5 отметка 9.843 фута превращается в 3.000 м, а не в 9.343.
        /// </remarks>
        public double GetElevationMeters(double rawValue)
        {
            return ToMeters(rawValue) + _offset;
        }

        /// <summary>Главная строка подписи: «+3.000 м».</summary>
        public string FormatMeters(double rawValue)
        {
            return GetElevationMeters(rawValue).ToString(MeterFormat, CultureInfo.InvariantCulture) + " " + MetersSuffix;
        }

        /// <summary>Исходное число в единицах модели — для ручной сверки: «9843,000 фт».</summary>
        public string FormatRaw(double rawValue)
        {
            return rawValue.ToString(RawFormat, CultureInfo.InvariantCulture) + " " + LengthUnits.GetShortName(_unit);
        }

        /// <summary>Главная строка плюс исходное значение: «+3.000 м (9.843 фт)».</summary>
        public string FormatElevationWithRaw(double rawValue)
        {
            return FormatMeters(rawValue) + " (" + FormatRaw(rawValue) + ")";
        }

        /// <summary>Диапазон по вертикали: «+2.900 м … +3.000 м».</summary>
        public string FormatMetersSpan(double rawMin, double rawMax)
        {
            return FormatMeters(rawMin) + " … " + FormatMeters(rawMax);
        }

        /// <summary>Отклонение от уровня со знаком: «+0.345 м», «-0.120 м», «на уровне».</summary>
        public string FormatDelta(double rawDelta)
        {
            if (Math.Abs(ToMeters(rawDelta)) < 0.0005)
            {
                return "на уровне";
            }

            string sign = rawDelta > 0 ? "+" : "-";
            double meters = Math.Abs(ToMeters(rawDelta));
            return sign + meters.ToString("0.000", CultureInfo.InvariantCulture) + " " + MetersSuffix;
        }

        /// <summary>Отметка уровня для показа: «+12.000 м».</summary>
        public string FormatLevelElevation(double rawElevation)
        {
            return GetElevationMeters(rawElevation).ToString(MeterFormat, CultureInfo.InvariantCulture) + " " + MetersSuffix;
        }
    }
}
