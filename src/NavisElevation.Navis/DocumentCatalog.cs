using NavisElevation.Core;
using Navisworks = Autodesk.Navisworks.Api;

namespace NavisElevation.Navis
{
    /// <summary>
    /// Чтение данных, которые Navisworks отдаёт по документу: единицы и модель-источник.
    /// </summary>
    /// <remarks>
    /// Уровни Navisworks сознательно не используются. Строка уровня в подписи не появлялась
    /// ни разу: в рабочей модели уровней нет, а без них искать ближайший нечего. Плагину
    /// нужна сама отметка, и она берётся прямо из координаты точки попадания.
    /// </remarks>
    internal static class DocumentCatalog
    {
        /// <summary>
        /// Найти модель, которой принадлежит элемент — нужна для имени источника и единиц.
        /// </summary>
        /// <remarks>
        /// Единицы в Navisworks задаются на уровне модели, а не документа, поэтому у
        /// элемента спрашиваем модель-владельца: поднимаемся по родителям до корня.
        ///
        /// Сравнение корня модели с родителем — по <c>InstanceGuid</c>, а не по ссылке:
        /// обёртка <c>ModelItem</c> каждый раз новая, и сравнение ссылок всегда ложно.
        /// Из-за этого модель-владелец не находился: в подписи не было строки «Модель»,
        /// а калибровка сохранялась без Guid — только по пути к файлу.
        /// </remarks>
        public static Navisworks.Model FindOwningModel(Navisworks.Document document, Navisworks.ModelItem item)
        {
            if (document == null || document.Models == null || item == null)
            {
                return null;
            }

            try
            {
                Navisworks.ModelItem ancestor = item;
                int guard = 0;

                while (ancestor != null && guard++ < 256)
                {
                    foreach (Navisworks.Model model in document.Models)
                    {
                        if (IsSameItem(model.RootItem, ancestor))
                        {
                            return model;
                        }
                    }

                    ancestor = ancestor.Parent;
                }
            }
            catch (System.Exception)
            {
                // Не нашли — вернём null, подпись покажет меньше данных, но не сломается.
            }

            return null;
        }

        /// <summary>Один и тот же ли это элемент модели.</summary>
        private static bool IsSameItem(Navisworks.ModelItem left, Navisworks.ModelItem right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            if (ReferenceEquals(left, right))
            {
                return true;
            }

            try
            {
                return left.InstanceGuid == right.InstanceGuid;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        /// <summary>Единицы модели-владельца; при неудаче — единицы первой модели, иначе миллиметры.</summary>
        public static LengthUnit ReadUnit(Navisworks.Document document, Navisworks.ModelItem item)
        {
            Navisworks.Model owner = FindOwningModel(document, item);
            if (owner != null)
            {
                return NavisUnits.Map(owner.Units);
            }

            try
            {
                if (document != null && document.Models != null && document.Models.Count > 0)
                {
                    return NavisUnits.Map(document.Models[0].Units);
                }
            }
            catch (System.Exception)
            {
            }

            return LengthUnit.Millimeters;
        }

        /// <summary>Короткое имя файла модели-источника для подписи, либо пустая строка.</summary>
        public static string ReadModelName(Navisworks.Model model)
        {
            if (model == null)
            {
                return string.Empty;
            }

            try
            {
                if (!string.IsNullOrEmpty(model.FileName))
                {
                    return System.IO.Path.GetFileName(model.FileName);
                }

                return model.SourceFileName ?? string.Empty;
            }
            catch (System.Exception)
            {
                return string.Empty;
            }
        }
    }
}