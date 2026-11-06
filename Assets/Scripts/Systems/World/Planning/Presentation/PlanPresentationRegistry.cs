using System;
using System.Collections.Generic;

namespace ServerCore.World.Presentation
{

    /// <summary>Реестр функций View. Можно регистрировать новые действия в runtime без правок меню/renderer.</summary>
    public sealed class PlanPresentationRegistry
    {
        private readonly Dictionary<string, PlanPresentation> items = new(StringComparer.Ordinal);
        public void Register(string actionId, PlanPresentation presentation)
        {
            if (string.IsNullOrWhiteSpace(actionId) || presentation?.Label == null || presentation.Project == null || presentation.CreateVisual == null)
                throw new ArgumentException("An action presentation requires label, projection and visual factory.");
            items.Add(actionId, presentation);
        }
        public bool TryGet(string id, out PlanPresentation value) => items.TryGetValue(id, out value);
    }
}
