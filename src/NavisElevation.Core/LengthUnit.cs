using System;
using System.Collections.Generic;

namespace NavisElevation.Core
{
    /// <summary>
    /// Единицы длины, в которых Navisworks хранит координаты модели.
    /// </summary>
    /// <remarks>
    /// Перечисление намеренно повторяет <c>Autodesk.Navisworks.Api.Units</c>, а не использует его
    /// напрямую: проект Core не должен зависеть от сборки Navisworks, иначе его нельзя будет
    /// ни протестировать, ни переиспользовать. Сопоставление живёт в слое Navis.
    /// Порядок значений совпадает с API — при добавлении новых единиц правьте оба места.
    /// </remarks>
    public enum LengthUnit
    {
        Meters,
        Centimeters,
        Millimeters,
        Feet,
        Inches,
        Yards,
        Kilometers,
        Miles,
        Micrometers,
        Mils,
        Microinches
    }

    /// <summary>
    /// Перевод единиц Navisworks в метры.
    /// </summary>
    /// <remarks>
    /// Коротких подписей единиц («м», «мм», «фт») здесь нет намеренно: отметка в
    /// подписи всегда выводится в метрах, а сырое значение в единицах модели убрано,
    /// поэтому подписывать было бы нечем.
    /// </remarks>
    public static class LengthUnits
    {
        private static readonly Dictionary<LengthUnit, double> MetersPerUnit = new Dictionary<LengthUnit, double>
        {
            { LengthUnit.Meters, 1.0 },
            { LengthUnit.Centimeters, 0.01 },
            { LengthUnit.Millimeters, 0.001 },
            { LengthUnit.Feet, 0.3048 },
            { LengthUnit.Inches, 0.0254 },
            { LengthUnit.Yards, 0.9144 },
            { LengthUnit.Kilometers, 1000.0 },
            { LengthUnit.Miles, 1609.344 },
            { LengthUnit.Micrometers, 0.000001 },
            { LengthUnit.Mils, 0.0000254 },
            { LengthUnit.Microinches, 0.0000000254 }
        };

        /// <summary>Сколько метров в одной единице.</summary>
        public static double ToMeters(LengthUnit unit)
        {
            double factor;
            return MetersPerUnit.TryGetValue(unit, out factor) ? factor : 1.0;
        }
    }
}
