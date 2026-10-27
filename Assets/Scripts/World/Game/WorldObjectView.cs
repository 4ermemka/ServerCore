using System;
using ServerCore.World.Data;
using Shared.WorldObjects;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ServerCore.World
{
    /// <summary>
    /// Game-представление одного объекта. Модель — источник окончательной позиции;
    /// drag меняет Transform только временно. Перепривязка снимает старые подписки.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GridObjectDrag))]
    public abstract class WorldObjectView : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField, Tooltip("Ключ префаба в каталоге WorldSceneController.")] private string definitionId;
        [SerializeField, Tooltip("Постоянный ID экземпляра сцены; в prefab asset оставьте пустым.")] private string sceneId;
        [SerializeField, Tooltip("Локальная точка опоры, совмещаемая с центром GridCell.")] private Vector3 anchorLocal;

        public string DefinitionId => definitionId;
        public string SceneId => sceneId;
        public ObjectData Data { get; private set; }
        public UnityGridSpace Grid { get; private set; }
        public Vector3 AnchorWorld => transform.TransformPoint(anchorLocal);
        public event Action DataPositionChanged;
        public IWorldInteraction Interaction { get; set; }
        private Quaternion authorRotation;
        private Vector3 authorScale;
        private bool capturedTransform;
        public void OnPointerClick(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left && Data != null && !data.dragging)
                Interaction?.Select(Data.Id.Value);
        }

        public void RefreshPlan()
        {
            DataPositionChanged?.Invoke();
            RestorePosition();
        }

        public abstract bool Accepts(ObjectData data);
        public abstract ObjectData CreateData(string id, GridCoordinate cell);
        public abstract void AddToWorld(WorldState state, ObjectData data);

        public void Bind(ObjectData data, UnityGridSpace grid)
        {
            if (data == null || !Accepts(data)) throw new ArgumentException($"{name}: несовместимый тип Data.", nameof(data));
            if (data.DefinitionId.Value != DefinitionId) throw new ArgumentException($"{name}: DefinitionId не соответствует префабу.", nameof(data));
            Unbind();
            if (!capturedTransform)
            {
                authorRotation = transform.localRotation;
                authorScale = transform.localScale;
                capturedTransform = true;
            }
            Data = data;
            Grid = grid ?? throw new ArgumentNullException(nameof(grid));
            Data.Cell.Changed += OnLocalPosition;
            Data.Cell.Patched += OnRemotePosition;
            Data.Transform.Changed += OnLocalPosition;
            Data.Transform.Patched += OnRemotePosition;
            RestorePosition();
        }

        public void Unbind()
        {
            if (Data == null) return;
            GetComponent<GridObjectDrag>().Cancel();
            Data.Cell.Changed -= OnLocalPosition;
            Data.Cell.Patched -= OnRemotePosition;
            Data.Transform.Changed -= OnLocalPosition;
            Data.Transform.Patched -= OnRemotePosition;
            Data = null;
            Grid = null;
        }

        public bool ShowPosition(GridCoordinate cell)
        {
            var resolved = Grid?.Resolve(cell);
            if (resolved == null) return false;
            // AnchorLocal учитывает поворот и scale, поэтому prefab не обязан иметь pivot у ног.
            transform.position += resolved.GetWorldPosition(Grid.Provider.CellSize) - AnchorWorld;
            return true;
        }

        public void RestorePosition()
        {
            if (Data == null) return;
            var pose = Data.Transform.Value;
            transform.localRotation = authorRotation * Quaternion.Euler(pose.RotationX, pose.RotationY, pose.RotationZ);
            transform.localScale = Vector3.Scale(authorScale, new Vector3(pose.ScaleX, pose.ScaleY, pose.ScaleZ));
            var cell = Interaction != null && Interaction.TryPlannedPosition(Data.Id.Value, out var planned) ? planned : Data.Cell.Value;
            if (!ShowPosition(cell))
                Debug.LogError($"{name}: ячейка {Data.Cell.Value} отсутствует; проверьте версию сетки.", this);
        }

        /// <summary>Расширение: запрет занятой клетки, права владельца, размер мебели и т.п.</summary>
        public virtual string ValidateMove(ObjectData data, GridCoordinate target) => null;

        private void OnLocalPosition(string path, object oldValue, object value) => OnPositionChanged();
        private void OnRemotePosition(string path, object value) => OnPositionChanged();
        private void OnPositionChanged()
        {
            // В том числе отменяет preview, если во время drag пришла чужая команда.
            DataPositionChanged?.Invoke();
            RestorePosition();
        }

        protected virtual void OnDestroy() => Unbind();
    }

    /// <summary>При добавлении вида объекта повторяется только создание и выбор коллекции.</summary>
    public abstract class WorldObjectView<T> : WorldObjectView where T : ObjectData, new()
    {
        public override bool Accepts(ObjectData data) => data is T;
        public override ObjectData CreateData(string id, GridCoordinate cell)
        {
            var data = new T();
            data.Id.Value = id;
            data.DefinitionId.Value = DefinitionId;
            data.Cell.Value = cell;
            return data;
        }
    }
}
