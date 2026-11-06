using System;
using Shared.WorldObjects;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ServerCore.World
{
    /// <summary>
    /// Ввод через EventSystem + PhysicsRaycaster. До отпускания модель не меняется.
    /// Escape/потеря фокуса/выключение/чужой patch отменяют незавершённый перенос.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GridObjectDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField, Min(0.1f), Tooltip("Максимальная дистанция до центра ячейки, в размерах клетки.")]
        private float snapDistance = 1.5f;
        [SerializeField] private LayerMask surfaceMask = ~0;
        private WorldObjectView view;
        private GridMoveSession<ObjectData> session;
        private Camera dragCamera;
        private Plane fallbackPlane;
        private Vector3 pointerOffset;
        private RaycastHit[] hits = new RaycastHit[32];
        private bool validPreview;
        private IDisposable cancelSubscription;
        public bool IsDragging => session != null;

        private void Awake() => view = GetComponent<WorldObjectView>();
        private void OnEnable()
        {
            if (view == null) view = GetComponent<WorldObjectView>();
            if (view != null) view.DataPositionChanged += Cancel;
            InputManager.OnStarted += BindCancel;
            BindCancel();
        }
        private void OnDisable()
        {
            Cancel();
            if (view != null) view.DataPositionChanged -= Cancel;
            InputManager.OnStarted -= BindCancel;
            cancelSubscription?.Dispose();
            cancelSubscription = null;
        }
        private void BindCancel()
        {
            if (cancelSubscription == null) cancelSubscription = InputManager.Instance?.GetAction("Cancel")?.Subscribe(Cancel);
        }
        private void OnApplicationFocus(bool focused) { if (!focused) Cancel(); }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || view == null || view.Data == null) return;
            Cancel();
            if (view.Interaction != null && !view.Interaction.CanEdit(view.Data)) return;
            view.Interaction?.Select(view.Data.Id.Value);
            dragCamera = eventData.pressEventCamera != null ? eventData.pressEventCamera : Camera.main;
            if (dragCamera == null) return;
            fallbackPlane = new Plane(Vector3.up, view.AnchorWorld);
            var ray = dragCamera.ScreenPointToRay(eventData.position);
            pointerOffset = fallbackPlane.Raycast(ray, out var distance) ? view.AnchorWorld - ray.GetPoint(distance) : Vector3.zero;
            // Только XZ offset: высота всегда определяется поверхностью выбранной ячейки.
            pointerOffset.y = 0;
            session = new GridMoveSession<ObjectData>(view.Data, view.Grid, view.ValidateMove);
            OnDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (session == null) return;
            validPreview = false;
            if (IsOverUI(eventData) || !TryProject(eventData.position, out var point)) return;
            var cell = view.Grid.Provider.GetClosestCell(point + pointerOffset);
            if (cell == null || Vector3.Distance(cell.GetWorldPosition(view.Grid.Provider.CellSize), point + pointerOffset) > snapDistance * view.Grid.Provider.CellSize) return;
            var coordinate = UnityGridSpace.Address(cell);
            validPreview = session.Preview(coordinate, out _);
            if (validPreview) view.ShowPosition(coordinate);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (session == null) return;
            OnDrag(eventData);
            var pending = session;
            session = null; // собственный Changed во время Commit уже не является отменой drag.
            if (validPreview && view.Interaction != null)
            {
                pending.Cancel();
                if (view.Interaction.CanEdit(view.Data)) view.Interaction.PlanMove(view.Data.Id.Value, pending.Candidate);
            }
            else if (validPreview) pending.Commit(out _);
            else pending.Cancel();
            view.RestorePosition();
        }

        public void Cancel()
        {
            if (session == null) return;
            session.Cancel();
            session = null;
            validPreview = false;
            view.RestorePosition();
        }

        private static bool IsOverUI(PointerEventData data)
        {
            var target = data.pointerCurrentRaycast.gameObject;
            return target != null && target.GetComponentInParent<Canvas>() != null;
        }

        private bool TryProject(Vector2 pointer, out Vector3 point)
        {
            var ray = dragCamera.ScreenPointToRay(pointer);
            int count;
            // NonAlloc без молчаливого усечения плотной сцены; буфер растёт только при необходимости.
            while ((count = Physics.RaycastNonAlloc(ray, hits, float.PositiveInfinity, surfaceMask, QueryTriggerInteraction.Ignore)) == hits.Length)
                Array.Resize(ref hits, hits.Length * 2);
            float closest = float.PositiveInfinity;
            point = default;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (hit.collider.GetComponentInParent<WorldObjectView>() != null || hit.distance >= closest) continue;
                closest = hit.distance;
                point = hit.point;
            }
            if (closest < float.PositiveInfinity) return true;
            if (!fallbackPlane.Raycast(ray, out float distance)) return false;
            point = ray.GetPoint(distance);
            return true;
        }
    }
}
