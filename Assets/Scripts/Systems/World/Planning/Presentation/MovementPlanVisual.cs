using System;
using Shared.TurnPlanning;
using UnityEngine;

namespace ServerCore.World.Presentation
{
    /// <summary>Простые круг, стрелка и номер. Нет коллайдеров: маркер не перехватывает перенос.</summary>
    public sealed class MovementPlanVisual : IDisposable
    {
        private readonly GameObject root;
        public MovementPlanVisual(PlanVisualContext context)
        {
            root = new GameObject("Move " + context.Number);
            root.transform.SetParent(context.Root, false);
            if (!MovementAction.TryDestination(context.Step, out var cell)) return;
            var target = context.View.Grid.Resolve(cell);
            var source = context.View.Grid.Resolve(context.From);
            if (target == null || source == null) return;
            float unit = context.View.Grid.Provider.CellSize;
            var to = target.GetWorldPosition(unit) + Vector3.up * .035f;
            var from = source.GetWorldPosition(unit) + Vector3.up * .035f;
            float radius = unit * .28f;
            var circle = Line("Circle", context, 40, unit * .035f);
            circle.loop = true;
            for (int i = 0; i < 40; i++)
            {
                float a = i * Mathf.PI * 2 / 40;
                circle.SetPosition(i, to + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius);
            }
            var direction = (to - from).normalized;
            var side = Vector3.Cross(Vector3.up, direction);
            var arrow = Line("Arrow", context, 5, unit * .025f);
            arrow.SetPositions(new[] { from, to, to - direction * radius + side * radius * .45f, to, to - direction * radius - side * radius * .45f });
            var label = new GameObject("Step number");
            label.transform.SetParent(root.transform, false);
            label.transform.position = to + Vector3.up * unit * .17f;
            var text = label.AddComponent<TextMesh>();
            text.text = context.Number.ToString();
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 48;
            text.characterSize = unit * .055f;
            text.color = context.Color;
            label.AddComponent<PlanLabelBillboard>();
        }
        private LineRenderer Line(string name, PlanVisualContext context, int count, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = context.Material;
            line.startColor = line.endColor = context.Color;
            line.startWidth = line.endWidth = width;
            line.positionCount = count;
            line.useWorldSpace = true;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }
        public void Dispose() { if (root != null) { root.SetActive(false); UnityEngine.Object.Destroy(root); } }
    }
}
