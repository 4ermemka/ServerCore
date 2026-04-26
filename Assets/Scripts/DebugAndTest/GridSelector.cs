using Unity.VisualScripting;
using UnityEngine;

public class GridSelector : MonoBehaviour
{
    [SerializeField] GridHolder GridProvider;
    [SerializeField] private int NeighbourCount;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (GridProvider != null)
        {
            GridProvider.OnCellPointerEnter += Select;
            GridProvider.OnCellPointerExit += Deselect;
            GridProvider.OnCellPointerClick += Click;
        }
    }

    private void Select(IGridCell cell)
    {
        //Debug.Log($"Enter cell: {cell.GridPosition.ToString()}");
        var neighbours = GridProvider.GetNeighborsOfOrder(cell, NeighbourCount);
        cell.Selection.Select("Hover");
        foreach (var neighbour in neighbours)
        {
            neighbour.Selection.Select("Neighbour");
        }
    }

    private void Deselect(IGridCell cell)
    {
        //Debug.Log($"Exit cell: {cell.GridPosition.ToString()}");
        var neighbours = GridProvider.GetNeighborsOfOrder(cell, NeighbourCount);
        cell.Selection.Deselect("Hover");
        foreach (var neighbour in neighbours)
        {
            neighbour.Selection.Deselect("Neighbour");
        }
    }

    private void Click(IGridCell cell)
    {
        Debug.Log("Clicked!");
        if (cell.Selection.IsSelected("Click"))
        {
            cell.Selection.Deselect("Click");
        }
        else
        { 
            cell.Selection.Select("Click");
        }
    }
}
