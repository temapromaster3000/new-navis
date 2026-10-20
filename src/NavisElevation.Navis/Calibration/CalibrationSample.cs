using System;

namespace NavisElevation.Navis.Calibration
{
    /// <summary>
    /// Образец точки, по которой пользователь калибрует отметку.
    /// </summary>
    /// <remarks>
    /// <para>
    /// В образце хранится всё, что нужно, чтобы посчитать смещение и показать его
    /// пользователю: сырая координата Z в единицах модели, та же координата в метрах,
    /// текущее смещение модели и сведения о модели-источнике.
    /// </para>
    /// <para>
    /// Важно, что здесь лежит <b>чистое</b> значение Z в метрах, без смещения. Новое
    /// смещение считается от него, а не от показанной отметки: иначе повторная
    /// калибровка накапливала бы ошибку — каждая следующая прибавляла бы к предыдущей.
    /// </para>
    /// </remarks>
    internal sealed class CalibrationSample
    {
        /// <summary>Координата Z точки в единицах модели.</summary>
        public double RawZ { get; set; }

        /// <summary>Та же координата в метрах, без смещения.</summary>
        public double RawMeters { get; set; }

        /// <summary>Смещение, действующее для модели в этот момент.</summary>
        public double CurrentOffset { get; set; }

        /// <summary>Guid модели-источника, из которой взят элемент.</summary>
        public string ModelGuid { get; set; }

        /// <summary>Имя файла модели-источника — для подписи в окне.</summary>
        public string ModelName { get; set; }

        /// <summary>Путь к файлу документа — ключ записи в хранилище.</summary>
        public string ModelPath { get; set; }

        /// <summary>Отметка, которую плагин показывает сейчас: с учётом текущего смещения.</summary>
        public double Displayed
        {
            get { return RawMeters + CurrentOffset; }
        }

        /// <summary>
        /// Смещение, которое получится, если проектная отметка точки равна указанной.
        /// </summary>
        /// <param name="projectElevation">Проектная отметка точки в метрах.</param>
        public double NewOffset(double projectElevation)
        {
            return projectElevation - RawMeters;
        }

        /// <summary>Короткое описание образца для диагностического лога.</summary>
        public string Describe()
        {
            return "Z=" + RawZ.ToString("0.###") + " (" + RawMeters.ToString("0.###")
                   + " м), текущее смещение " + CurrentOffset.ToString("0.###")
                   + " м, модель " + (string.IsNullOrEmpty(ModelName) ? "неизвестна" : ModelName);
        }
    }
}
