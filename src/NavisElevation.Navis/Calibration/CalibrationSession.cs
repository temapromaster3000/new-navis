using System;

namespace NavisElevation.Navis.Calibration
{
    /// <summary>
    /// Состояние калибровки, общее для всех экземпляров инструмента.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Экземпляр инструмента создаёт Navisworks сам, передать ему состояние в
    /// конструктор нельзя. Поэтому состояние живёт здесь, в статическом классе, — так
    /// же, как <see cref="ElevationProbeTool.PendingPreviousTool"/>.
    /// </para>
    /// <para>
    /// Раньше здесь же жил диспетчер интерфейса: окно ввода отметки планировалось
    /// открывать отложенно, через <c>Dispatcher.BeginInvoke</c>. Так не вышло: очередь
    /// сообщений вне модальных окон Navisworks не качает, и окно не появлялось. Теперь
    /// окно открывается напрямую, и диспетчер не нужен.
    /// </para>
    /// </remarks>
    internal static class CalibrationSession
    {
        /// <summary>Идёт ли сейчас выбор точки калибровки.</summary>
        public static bool IsPicking { get; private set; }

        /// <summary>Выбранная точка; null, пока точку не выбрали.</summary>
        public static CalibrationSample Sample { get; private set; }

        /// <summary>Начать выбор точки.</summary>
        public static void Begin()
        {
            IsPicking = true;
            Sample = null;
        }

        /// <summary>Отменить выбор точки.</summary>
        public static void End()
        {
            IsPicking = false;
            Sample = null;
        }

        /// <summary>Запомнить выбранную точку.</summary>
        public static void SetSample(CalibrationSample sample)
        {
            Sample = sample;
        }
    }
}
