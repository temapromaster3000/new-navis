namespace NavisElevation.Core
{
    /// <summary>
    /// Уровень модели в его собственных единицах — копия того, что Navisworks хранит сам.
    /// </summary>
    /// <remarks>
    /// Это замена отсутствующей в API базовой точки проекта. Отметки уровней
    /// (GridLevel.Elevation) дают готовую точку отсчёта внутри самой модели, поэтому
    /// сверять Z элемента с ними можно без внешних источников.
    /// </remarks>
    public sealed class LevelInfo
    {
        public LevelInfo(string name, double elevation)
        {
            Name = name;
            Elevation = elevation;
        }

        /// <summary>Отображаемое имя уровня, например «Уровень 3».</summary>
        public string Name { get; private set; }

        /// <summary>Отметка уровня в единицах документа, без калибровочного смещения.</summary>
        public double Elevation { get; private set; }

        public override string ToString()
        {
            return Name + " = " + Elevation.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
