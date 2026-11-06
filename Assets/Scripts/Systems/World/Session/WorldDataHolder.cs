using ServerCore.World.Data;
using UnityEngine;

/// <summary>
/// Единственный владелец Data в сцене. Первый доступ безопасен до Awake/Start;
/// ссылка не заменяется после подключения GameClient и представлений.
/// </summary>
[DisallowMultipleComponent]
public sealed class WorldDataHolder : MonoBehaviour
{
    private WorldState state;
    public WorldState WorldState => state ??= new WorldState();
    private void Awake() { _ = WorldState; }
}
