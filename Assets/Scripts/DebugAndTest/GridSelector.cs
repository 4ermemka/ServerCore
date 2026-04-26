using UnityEngine;

public class GridSelector : Manager
{
    [SerializeField] GridProvider GridProvider;
    [SerializeField] private int NeighbourCount;

    public override void Init()
    {
        if (GridProvider != null)
        {
            GridProvider.OnCellPointerEnter += Select;
            GridProvider.OnCellPointerExit += Deselect;
            GridProvider.OnCellPointerClick += Click;
        }
        Debug.Log($"{Name} Подключение событий произведено!");
        base.Init();
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
        //Debug.Log("Clicked!");
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
