using System.Collections.Generic;

namespace NavisElevation.Core
{
    /// <summary>Результат поиска ближайшего уровня к отметке.</summary>
    public sealed class LevelMatch
    {
        public LevelMatch(LevelInfo level, double rawDelta)
        {
            Level = level;
            Delta = rawDelta;
        }

        public LevelInfo Level { get; private set; }

        /// <summary>Отметка элемента минус отметка уровня, в единицах документа.</summary>
        public double Delta { get; private set; }
    }

    /// <summary>
    /// Ищет уровень, ближайший к отметке элемента.
    /// </summary>
    public static class LevelResolver
    {
        /// <summary>
        /// Ближайший уровень к <paramref name="rawValue"/>.
        /// </summary>
        /// <returns>
        /// <c>null</c>, если уровней нет или список пуст — это штатная ситуация для моделей
        /// без этажей, а не ошибка.
        /// </returns>
        public static LevelMatch FindNearest(IReadOnlyList<LevelInfo> levels, double rawValue)
        {
            if (levels == null || levels.Count == 0)
            {
                return null;
            }

            int bestIndex = -1;
            double bestDelta = 0.0;

            for (int i = 0; i < levels.Count; i++)
            {
                double delta = rawValue - levels[i].Elevation;

                // Первый уровень становится кандидатом всегда, дальше сравниваем по модулю:
                // уровень может оказаться как выше, так и ниже точки.
                if (bestIndex < 0 || System.Math.Abs(delta) < System.Math.Abs(bestDelta))
                {
                    bestIndex = i;
                    bestDelta = delta;
                }
            }

            return bestIndex < 0 ? null : new LevelMatch(levels[bestIndex], bestDelta);
        }
    }
}
