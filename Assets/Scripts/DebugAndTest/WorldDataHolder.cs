using Assets.Shared.Model;
using UnityEngine;

public sealed class WorldDataHolder : MonoBehaviour
{
    public WorldData Data { get; private set; }

    private void Awake()
    {
        Data = new WorldData();

        // В тесте создадим три бокса
        for (int i = -2; i <= 2; i++)
        {
            Data.Boxes.Add(new BoxData
            {
                Position = new Vector2(i * 3f, 0f)
            });
        }
    }
}

