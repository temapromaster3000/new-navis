using System.Globalization;

namespace NavisElevation.Core
{
    /// <summary>
    /// Превращает сырую координату Z из Navisworks в строку отметки для подписи на экране.
    /// </summary>
    /// <remarks>
    /// Отметка всегда выводится в метрах — так её записывают в чертежах, — независимо от
    /// единиц модели. Сырое значение в единицах модели не показывается: перевод сверен
    /// калибровкой по проектной отметке, и подпись вида «+3.000 м (9.843 фт)» только мешала
    /// прочитать число.
    ///
    /// <see cref="ProjectSettings.DefaultOffset"/> — калибровочное смещение в метрах,
    /// прибавляемое к отметке. Нужен для моделей, у которых ноль Navisworks не совпадает с
    /// проектным; на большинстве моделей остаётся равным нулю.
    /// </remarks>
    public sealed class ElevationFormatter
    {
        /// <summary>Отметки в чертежах пишут в метрах с точностью до миллиметра.</summary>
        private const string MeterFormat = "+0.000;-0.000;0.000";

        /// <summary>
        /// Подпись отметки всегда в метрах, независимо от единиц модели.
        /// </summary>
        /// <remarks>
        /// Модель может прийти в футах или дюймах, но отметки в чертежах пишут в метрах.
        /// Подписывать переведённое значение единицами модели нельзя: получается «9.843 фт»
        /// вместо «3.000 м», то есть число, которое никто не может прочитать как высоту.
        /// Сырое значение в единицах модели в подписи не показывается: перевод сверен
        /// калибровкой по проектной отметке, а «+3.000 м (9.843 фт)» мешало читать число.
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

        /// <summary>Диапазон по вертикали: «+2.700 м … +3.000 м».</summary>
        public string FormatMetersSpan(double rawMin, double rawMax)
        {
            return FormatMeters(rawMin) + " … " + FormatMeters(rawMax);
        }
    }
}
