using Assets.Shared.ChangeDetector;
using Assets.Shared.Model;
using UnityEngine;

public sealed class WorldDataHolder : MonoBehaviour
{
    [SerializeField]
    [SyncField]
    public WorldData Data { get; set; } = new();

    private void Awake()
    {
        Data = new WorldData();
    }
}

