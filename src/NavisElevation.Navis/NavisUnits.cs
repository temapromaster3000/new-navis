using NavisElevation.Core;
using Navisworks = Autodesk.Navisworks.Api;

namespace NavisElevation.Navis
{
    /// <summary>
    /// Сопоставление единиц Navisworks с <see cref="LengthUnit"/> ядра.
    /// </summary>
    /// <remarks>
    /// Единственное место в проекте, где знание о Navisworks протекает в предметную область.
    /// Значения перечислений совпадают по порядку, но сопоставление сделано явно через
    /// switch, а не приведением типа: иначе добавление одной единицы сдвинет остальные
    /// и ошибка проявится только на реальной модели.
    /// </remarks>
    internal static class NavisUnits
    {
        public static LengthUnit Map(Navisworks.Units units)
        {
            switch (units)
            {
                case Navisworks.Units.Meters:
                    return LengthUnit.Meters;
                case Navisworks.Units.Centimeters:
                    return LengthUnit.Centimeters;
                case Navisworks.Units.Millimeters:
                    return LengthUnit.Millimeters;
                case Navisworks.Units.Feet:
                    return LengthUnit.Feet;
                case Navisworks.Units.Inches:
                    return LengthUnit.Inches;
                case Navisworks.Units.Yards:
                    return LengthUnit.Yards;
                case Navisworks.Units.Kilometers:
                    return LengthUnit.Kilometers;
                case Navisworks.Units.Miles:
                    return LengthUnit.Miles;
                case Navisworks.Units.Micrometers:
                    return LengthUnit.Micrometers;
                case Navisworks.Units.Mils:
                    return LengthUnit.Mils;
                case Navisworks.Units.Microinches:
                    return LengthUnit.Microinches;
                default:
                    // Модель без определённых единиц: миллиметры — самый частый случай в BIM.
                    return LengthUnit.Millimeters;
            }
        }
    }
}
