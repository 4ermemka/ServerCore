using Assets.Shared.Model;
using UnityEngine;

public sealed class WorldDataHolder : MonoBehaviour
{
    [SerializeField]
    public WorldState WorldState;

    public void Start()
    {
        WorldState = new();
    }
}