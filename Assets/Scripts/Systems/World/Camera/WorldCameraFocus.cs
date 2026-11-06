using UnityEngine;

namespace ServerCore.World
{
    /// <summary>Стабильный фокус по подтверждённой Cell; временный drag-preview его не двигает.</summary>
    public sealed class WorldCameraFocus : MonoBehaviour
    {
        [SerializeField] private WorldSceneController world;
        [SerializeField] private string objectId = "scene-player";
        [SerializeField] private Vector3 offset;

        private void LateUpdate()
        {
            if (world == null || !world.TryGetView(objectId, out var view) || view.Data == null) return;
            var cell = view.Grid.Resolve(view.Data.Cell.Value);
            if (cell != null)
                transform.position = cell.GetWorldPosition(view.Grid.Provider.CellSize)
                    + view.transform.position - view.AnchorWorld + offset;
        }
    }
}
