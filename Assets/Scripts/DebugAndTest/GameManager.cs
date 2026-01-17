using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts.DebugAndTest
{
    public sealed class GameManager : MonoBehaviour
    {
        [SerializeField] private WorldDataHolder _worldDataHolder;
        [SerializeField] private BoxView _boxPrefab; // префаб ящика

        private List<BoxView> _boxesOnBoard = new List<BoxView>();

        private void Start()
        {
            var data = _worldDataHolder.Data;

            //Subscribe();

            data.SnapshotApplied += Subscribe;
        }

        private void Subscribe()
        {
            if (_boxesOnBoard.Count > 0)
            { 
                foreach (BoxView box in _boxesOnBoard)
                {
                    Destroy(box.gameObject);
                }
            }

            var data = _worldDataHolder.Data;
            foreach (var boxData in data.Boxes)
            {
                var view = Instantiate(_boxPrefab, transform);
                view.Initialize(boxData);
                _boxesOnBoard.Add(view);
            }
            Debug.Log($"SUBSCRIBED!");
        }
    }

}