using System;
using System.Collections.Generic;
using Shared.TurnPlanning;
using Shared.WorldObjects;
using UnityEngine;

namespace ServerCore.World.Presentation
{
    public sealed class PlanVisualContext
    {
        public PlannedAction Step;
        public int Number;
        public Color Color;
        public GridCoordinate From;
        public WorldObjectView View;
        public Transform Root;
        public Material Material;
    }

    public sealed class PlanPresentation
    {
        public Func<PlannedAction, string> Label;
        public Func<PlannedAction, GridCoordinate, GridCoordinate> Project;
        public Func<PlanVisualContext, IDisposable> CreateVisual;
    }

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
