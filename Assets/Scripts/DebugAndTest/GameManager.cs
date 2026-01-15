using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts.DebugAndTest
{
    public sealed class GameManager : MonoBehaviour
    {
        [SerializeField] private WorldDataHolder _worldDataHolder;
        [SerializeField] private BoxView _boxPrefab; // префаб ящика

        private BoxView _box;

        private void Start()
        {
            var data = _worldDataHolder.Data;

            var boxData = data.Box;

            var view = Instantiate(_boxPrefab, transform);
            view.Initialize(boxData);

            _box = view;
        }
    }

}