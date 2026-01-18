using Assets.Shared.Model;
using UnityEngine;

public sealed class WorldDataHolder : MonoBehaviour
{
    public WorldData Data { get; private set; }

    private void Awake()
    {
        Data = new WorldData();
    }
}

