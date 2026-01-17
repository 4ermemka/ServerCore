using Assets.Shared.Model;
using UnityEngine;

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
            newBox.Patched += () => { Debug.Log($"One of boxes patched: {newBox.Position.ToString()}"); };

        }
        
        Data.Patched += () => { Debug.Log($"Data.Patched"); };
        Data.Boxes.Patched += () => { Debug.Log($"Data.Boxes.Patched"); };
    }
}

