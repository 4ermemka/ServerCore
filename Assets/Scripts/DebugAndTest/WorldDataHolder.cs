using Assets.Shared.ChangeDetector;
using Assets.Shared.Model;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class WorldDataHolder : MonoBehaviour
{
    public WorldData Data { get; private set; }

    private void Awake()
    {
        Data = new WorldData();

        for (int i = -2; i <= 2; i++)
        {
            var newBox = new BoxData()
            {
                Position = new Vector2(0f + i, 0f)
            };
            Data.Boxes.Add(newBox);
        }

    }
}

