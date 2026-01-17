using UnityEngine;

namespace Assets.Scripts.DebugAndTest
{
    public sealed class GameManager : MonoBehaviour
    {
        [SerializeField] private WorldDataHolder _worldDataHolder;
        [SerializeField] private BoxView _boxPrefab; // префаб ящика

        private void Start()
        {
            var data = _worldDataHolder.Data;
            foreach(var boxData in data.Boxes)
            {
                var view = Instantiate(_boxPrefab, transform);
                view.Initialize(boxData);
            }
        }
    }

}