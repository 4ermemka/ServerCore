using Assets.Shared.Model;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Assets.Scripts.DebugAndTest
{
    public sealed class GameManager : MonoBehaviour
    {
        [SerializeField] private WorldDataHolder _worldDataHolder;
        [SerializeField] private BoxView _boxPrefab; // префаб ящика

        private Dictionary<BoxData, BoxView> _boxesOnBoard = new Dictionary<BoxData, BoxView>();

        private void Start()
        {
            var data = _worldDataHolder.Data;

            data.SnapshotApplied += Redraw;
            data.Boxes.Patched += Redraw;
        }

        public void SpawnBox()
        {
            var newBox = new BoxData(Vector2.zero);
            _worldDataHolder.Data.Boxes.Add(newBox);

            var view = Instantiate(_boxPrefab, transform);
            view.Initialize(newBox);
            _boxesOnBoard.Add(newBox, view);
        }

        public void DeleteLastBox()
        {
            var data = _worldDataHolder.Data;
            var lastBoxData = data.Boxes.LastOrDefault();
            if (lastBoxData != null)
            {
                data.Boxes.Remove(lastBoxData);
                Destroy(_boxesOnBoard[lastBoxData].gameObject);
                _boxesOnBoard.Remove(lastBoxData);
            }
        }

        public void Redraw()
        {
            if (_boxesOnBoard.Count > 0)
            {
                var keys = _boxesOnBoard.Keys.ToList();
                foreach (BoxData box in keys)
                {
                    Destroy(_boxesOnBoard[box].gameObject);
                    _boxesOnBoard.Remove(box);
                }
            }

            var data = _worldDataHolder.Data;
            foreach (var boxData in data.Boxes)
            {
                var view = Instantiate(_boxPrefab, transform);
                view.Initialize(boxData);
                _boxesOnBoard.Add(boxData, view);
            }

            Debug.Log($"REDRAWED, count:{_boxesOnBoard.Count}!");
        }
    }

}