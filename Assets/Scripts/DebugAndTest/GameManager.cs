using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts.DebugAndTest
{
    public sealed class GameManager : MonoBehaviour
    {
        [SerializeField] private WorldDataHolder _worldDataHolder;
        [SerializeField] private BoxView _boxPrefab; // префаб ящика

        private readonly List<BoxView> _boxes = new List<BoxView>();

        private void Start()
        {
            var data = _worldDataHolder.Data;

            // Для каждого BoxData — создаём визуальный ящик и даём ему ссылку на данные
            for (int i = 0; i < data.Boxes.Count; i++)
            {
                var boxData = data.Boxes[i];

                var view = Instantiate(_boxPrefab, transform);
                view.Initialize(boxData);

                _boxes.Add(view);
            }
        }
    }

}