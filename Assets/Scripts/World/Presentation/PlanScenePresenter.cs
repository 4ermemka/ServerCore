using System;
using System.Collections.Generic;
using ServerCore.World.Data;
using UnityEngine;

namespace ServerCore.World.Presentation
{
    public sealed class PlanScenePresenter : IDisposable
    {
        private readonly List<IDisposable> visuals = new();
        private readonly GameObject root;
        private readonly Material material;
        private readonly PlanPresentationRegistry registry;
        private readonly PlanPalette palette;
        public PlanScenePresenter(PlanPresentationRegistry registry, PlanPalette palette)
        {
            this.registry = registry;
            this.palette = palette;
            root = new GameObject("Action plan previews");
            material = new Material(Shader.Find("Sprites/Default"));
        }
        public void Refresh(WorldState state, WorldSceneController scene)
        {
            Clear();
            if (scene == null) return;
            foreach (var pair in state.Planning.Value.Plans)
            {
                if (!scene.TryGetView(pair.Key, out var view)) continue;
                var from = view.Data.Cell.Value;
                int number = 0;
                foreach (var step in pair.Value)
                {
                    number++;
                    if (!registry.TryGet(step.ActionId, out var presentation)) continue;
                    visuals.Add(presentation.CreateVisual(new PlanVisualContext
                    {
                        Step = step, Number = number, Color = palette.Get(step.ColorIndex),
                        From = from, View = view, Root = root.transform, Material = material
                    }));
                    from = presentation.Project(step, from);
                }

            }
        }
        private void Clear() { foreach (var visual in visuals) visual.Dispose(); visuals.Clear(); }
        public void Dispose()
        {
            Clear();
            if (root != null) UnityEngine.Object.Destroy(root);
            UnityEngine.Object.Destroy(material);
        }
    }
}
