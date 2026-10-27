using System;
using Shared.WorldObjects;
using UnityEngine;

namespace ServerCore.World
{
    /// <summary>Единственное место преобразования сетевых индексов в Unity/GridSystem.</summary>
    public sealed class UnityGridSpace : IGridSpace
    {
        public GridProvider Provider { get; }
        public UnityGridSpace(GridProvider provider) => Provider = provider != null ? provider : throw new ArgumentNullException(nameof(provider));
        public bool Contains(GridCoordinate cell) => Resolve(cell) != null;
        public IGridCell Resolve(GridCoordinate cell) => Provider.GetCellAt(new Vector3Int(cell.X, cell.Y, cell.Z));
        public static GridCoordinate Address(IGridCell cell) => new(cell.GridPosition.x, cell.GridPosition.y, cell.GridPosition.z);
    }
}
