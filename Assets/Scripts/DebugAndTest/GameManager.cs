using Assets.Scripts.DebugAndTest;
using Assets.Shared.Model;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed class GameManager : MonoBehaviour
{
    [SerializeField] private WorldDataHolder _worldDataHolder;
    [SerializeField] public BoxDataView _prefab;

    private List<BoxDataView> _boxes = new();
    //[SerializeField] public TextMeshProUGUI TextSlot;

    protected WorldState _worldState;

    private void Start()
    {
        _worldState = _worldDataHolder.WorldState;

        // Подписываемся на изменения для отправки в сеть
        _worldState.Changed += OnLocalStateChanged;
        _worldState.Boxes.Changed += OnLocalStateChanged;

        _worldState.Patched += Redraw;
        _worldState.Boxes.Patched += Redraw;

        //int n = 5;
        //
        //for (int i = 0; i < n; i++)
        //{
        //    BoxData newBoxData = new BoxData();
        //    newBoxData.Position.Value = new Vector2Dto((i-n/2)*3f, 0f);
        //    _worldState.Boxes.Add(newBoxData);
        //}

        Redraw(null, null);
    }

    public void SpawnBox()
    {
        BoxData newBoxData = new BoxData();
        newBoxData.Position.Value = new Vector2Dto(0,0);
        _worldState.Boxes.Add(newBoxData);
    }

    public void DeleteLastBox()
    {
        var lastBox = _worldState.Boxes.LastOrDefault();
        if (lastBox != null)
        { 
            _worldState.Boxes.Remove(lastBox);
        }
    }

    private void OnLocalStateChanged(string path, object oldValue, object newValue)
    {
        // Формируем и отправляем сетевой патч
        var patch = new
        {
            Type = "patch",
            Path = path,
            OldValue = oldValue,
            NewValue = newValue
        };

        Debug.Log($"LocalPatch: {JsonConvert.SerializeObject(patch)}");
        Redraw("LOCAL " + path, patch);
    }

    private void Redraw(string path, object newValue)
    {
        Debug.Log($"Redrawing due to patch on {path}");
        if (_boxes != null)
        {
            foreach (var box in _boxes.ToList())
            {
                _boxes.Remove(box);
                Destroy(box.gameObject);
            }
            _boxes.Clear();
        }

        foreach (var boxData in _worldState.Boxes.ToList())
        { 
            var box = Instantiate(_prefab, transform);
            box.Initialize(boxData);
            _boxes.Add(box);
        }
    }
}