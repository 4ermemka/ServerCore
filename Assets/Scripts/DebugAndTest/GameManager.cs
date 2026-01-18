using Assets.Shared.Model;
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

            data.SnapshotApplied += Subscribe;

            for (int i = -2; i <= 2; i++)
            {
                var newBox = new BoxData();
                newBox.Position.Value = new Vector2(i * 2f, 0f);
                data.Boxes.Add(newBox);
            }

            data.Boxes.Patched += Subscribe;
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