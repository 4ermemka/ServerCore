using Assets.Shared.Model;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Assets.Scripts.DebugAndTest
{
    public sealed class BoxView : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler
    {
        private BoxData _data;
        private Camera _camera;
        private Vector3 _offset;

        public void Initialize(BoxData data)
        {
            _data = data;

            _data.Patched += () => {
                MoveTo(_data.Position);
                Debug.Log("Box moved due to patch!");
            };

            _camera = Camera.main;

            // начальная позиция визуала по данным
            transform.position = new Vector3(_data.Position.Value.x, _data.Position.Value.y, 0f);
        }

        public void MoveTo(Vector2 newPos)
        { 
            transform.position = new Vector2(newPos.x, newPos.y);
        }

        // Простейшее перетаскивание для дебага
        public void OnBeginDrag(PointerEventData eventData)
        {
            var world = ScreenToWorld(eventData.position);
            _offset = transform.position - world;
        }

        public void OnDrag(PointerEventData eventData)
        {
            var world = ScreenToWorld(eventData.position);
            var target = world + _offset;
            transform.position = new Vector2(target.x, target.y);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_data == null)
                return;
            // ВАЖНО: меняем только данные, визуал сам подтянется в LateUpdate
            var world = ScreenToWorld(eventData.position);
            var target = world + _offset;
            _data.Position.Value = new Vector2(target.x, target.y);
        }

        private Vector3 ScreenToWorld(Vector2 screenPos)
        {
            var ray = _camera.ScreenPointToRay(screenPos);
            var plane = new Plane(Vector3.forward, Vector3.zero);
            if (plane.Raycast(ray, out var enter))
            {
                return ray.GetPoint(enter);
            }
            return transform.position;
        }
    }

}