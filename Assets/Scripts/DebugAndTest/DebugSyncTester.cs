using System.Linq;
using UnityEngine;

public sealed class DebugSyncTester : MonoBehaviour
{
    public WorldState World;
    public int MyId;
    private float _timer;

    private void Start()
    {
        World = new WorldState();

        World.Changed += change =>
        {
            var path = string.Join(".", change.Path.Select(p => p.Name));
            Debug.Log($"[WORLD CHANGE] {path}: {change.OldValue} -> {change.NewValue}");
        };

        if (!World.Counters.TryGetValue(MyId, out var counter))
            World.Counters[MyId] = new DebugCounter { Label = $"P{MyId}", Value = 0 };
    }

    private void Update()
    {
        _timer += Time.deltaTime;
        if (_timer >= 1f)
        {
            _timer = 0f;
            if (World.Counters.TryGetValue(MyId, out var counter))
            {
                counter.Value++;
                Debug.Log($"[LOCAL {MyId}] {counter.Value}");
            }
        }
    }
}

