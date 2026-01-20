using UnityEngine;

namespace Assets.Scripts.DebugAndTest
{
    public sealed class GameManager : MonoBehaviour
    {
        [SerializeField] private WorldDataHolder _worldDataHolder;
        [SerializeField] private BoxView _boxPrefab; // префаб ящика

        private BoxView _boxesOnBoard;

        private void Start()
        {
            var data = _worldDataHolder.Data;

            data.SnapshotApplied += Redraw;
            data.Box.Patched += Redraw;
        }

        public void Redraw()
        {
            if (_boxesOnBoard != null)
            {
                Destroy(_boxesOnBoard.gameObject);
            }

            var data = _worldDataHolder.Data;
            
                var view = Instantiate(_boxPrefab, transform);
                view.Initialize(data.Box);
                _boxesOnBoard = view;

            Debug.Log($"REDRAWED");
        }
    }

}