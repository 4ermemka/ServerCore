using Assets.Shared.Model;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Assets.Scripts.DebugAndTest
{

    public sealed class BoxView : MonoBehaviour, IDragHandler, IBeginDragHandler
    {
        private BoxData _data;
        private Camera _camera;
        private Vector3 _offset;

        public void Initialize(BoxData data)
        {
            _data = data;
            _camera = Camera.main;

            // начальная позиция визуала по данным
            transform.position = new Vector3(_data.Position.x, _data.Position.y, 0f);
        }

        private void LateUpdate()
        {
            if (_data == null)
                return;

            // каждый кадр подтягиваем позицию из данных
            var pos = _data.Position;
            transform.position = new Vector3(pos.x, pos.y, transform.position.z);
        }

        // Простейшее перетаскивание для дебага
        public void OnBeginDrag(PointerEventData eventData)
        {
            var world = ScreenToWorld(eventData.position);
            _offset = transform.position - world;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_data == null)
                return;

            var world = ScreenToWorld(eventData.position);
            var target = world + _offset;

            // ВАЖНО: меняем только данные, визуал сам подтянется в LateUpdate
            _data.Position = new Vector2(target.x, target.y);
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