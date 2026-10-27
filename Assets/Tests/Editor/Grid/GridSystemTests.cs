using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class GridSystemTests
{
    private readonly List<GameObject> objects = new();
    private GridProvider provider;
    private Transform container;

    [SetUp]
    public void SetUp()
    {
        provider = Create("Provider").AddComponent<GridProvider>();
        container = Create("Cells").transform;
        Set(provider, "cellsContainer", container);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var root in objects) if (root != null) Object.DestroyImmediate(root);
        objects.Clear();
        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void AllCellsCachesSnapshotAndEarlyQueryPopulatesOnce()
    {
        var first = Cell(0, 0, 0, register: false);
        Assert.That(provider.GetCellAt(Vector3.zero), Is.SameAs(first));
        var snapshot = provider.AllCells;
        Assert.That(provider.AllCells, Is.SameAs(snapshot));
        var second = Cell(1, 0, 0, register: false);
        Assert.That(provider.AllCells.Count, Is.EqualTo(1));
        provider.RegisterCell(second);
        Assert.That(provider.AllCells.Count, Is.EqualTo(2));
        Assert.That(snapshot.Count, Is.EqualTo(1), "Published snapshots are immutable.");
        provider.ClearAllCells();
        Assert.That(provider.AllCells, Is.Empty, "Cleared cells must not be rediscovered by a query.");
        provider.RebuildIndex();
        Assert.That(provider.AllCells.Count, Is.EqualTo(2));
    }

    [Test]
    public void EventsSupportLateSubscriptionReRegistrationAndRemoval()
    {
        var cell = Cell(0, 0, 0);
        int calls = 0;
        Action<IGridCell> handler = _ => calls++;
        provider.OnCellPointerEnter += handler;
        provider.RegisterCell(cell);
        cell.OnPointerEnter(null);
        Assert.That(calls, Is.EqualTo(1));
        provider.OnCellPointerEnter -= handler;
        cell.OnPointerEnter(null);
        Assert.That(calls, Is.EqualTo(1));
        provider.OnCellPointerEnter += handler;
        provider.RemoveCell(cell);
        cell.OnPointerEnter(null);
        Assert.That(calls, Is.EqualTo(1));
        provider.RegisterCell(cell);
        provider.RebuildIndex();
        cell.OnPointerEnter(null);
        Assert.That(calls, Is.EqualTo(2));
        provider.ClearAllCells();
        cell.OnPointerEnter(null);
        Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public void DuplicateRemovalReinitializationAndDestroyKeepIndexConsistent()
    {
        var first = Cell(0, 0, 0);
        var duplicate = Cell(0, 0, 0);
        provider.RemoveCell(duplicate);
        Assert.That(provider.GetCellAt(Vector3.zero), Is.SameAs(first));
        first.transform.position = new Vector3(2, 0, 0);
        first.Initialize(new Vector3Int(2, 0, 0), 0, Quaternion.identity, true);
        Assert.That(provider.GetCellAt(Vector3.zero), Is.Null);
        Assert.That(provider.GetCellAt(first.transform.position), Is.SameAs(first));
        Object.DestroyImmediate(first.gameObject);
        Assert.That(provider.AllCells, Is.Empty);
    }

    [Test]
    public void WorldCoordinatesIncludeOriginAndClosestUsesActualHeight()
    {
        provider.transform.position = new Vector3(17, 20, -11);
        Set(provider, "cellSize", 2f);
        var originCell = Cell(0, 1, 0, 2.3f);
        Assert.That(originCell.GetWorldPosition(2), Is.EqualTo(new Vector3(17, 2.3f, -11)));
        Assert.That(provider.GetCellAt(originCell.transform.position), Is.SameAs(originCell));
        Assert.That(provider.GetCellAt(new Vector3Int(0, 1, 0)), Is.SameAs(originCell), "Адрес из Data разрешается без обратного округления мировой позиции.");
        Assert.That(provider.GetCellAt(new Vector3Int(0, 99, 0)), Is.Null);
        Assert.That(provider.GetClosestCell(originCell.transform.position), Is.SameAs(originCell));
        var nextFloor = Cell(0, 2, 0, 3.01f);
        // Запрос округляется к y=1, но фактически второй этаж ближе.
        Assert.That(provider.GetClosestCell(new Vector3(17, 2.9f, -11)), Is.SameAs(nextFloor));
    }

    [Test]
    public void RadiusRetainsDiamondAndAllFloorsAndCanReuseOutput()
    {
        var center = Cell(0, 0, 0);
        var above = Cell(0, 3, 0);
        var right = Cell(1, 0, 0);
        var diagonal = Cell(1, 0, 1);
        var result = new List<IGridCell> { diagonal };
        provider.GetCellsInRadius(Vector3.zero, .1f, result);
        Assert.That(result, Is.EquivalentTo(new[] { center, above, right }));
        provider.GetCellsInRadius(Vector3.zero, 0, result);
        Assert.That(result, Is.EquivalentTo(new[] { center, above }));
        provider.GetCellsInRadius(Vector3.zero, -1, result);
        Assert.That(result, Is.Empty);
        provider.GetCellsInRadius(Vector3.zero, float.MaxValue, result);
        Assert.That(result.Count, Is.EqualTo(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => provider.GetCellsInRadius(Vector3.zero, float.NaN));
        // Тот же запрос при плотной сетке использует перебор координат, а не всех колонок.
        for (int x = 2; x < 12; x++) Cell(x, 0, 0);
        Assert.That(provider.GetCellsInRadius(Vector3.zero, 1), Is.EquivalentTo(new[] { center, above, right }));
    }

    [Test]
    public void GraphTraversalRespectsDepthDirectionPredicateAndRemovedCells()
    {
        var a = Cell(0, 0, 0); var b = Cell(1, 0, 0); var c = Cell(2, 0, 0); var d = Cell(1, 0, 1);
        a.SetNeighbors(new List<GridCell> { b, null });
        b.SetNeighbors(new List<GridCell> { a, c, d });
        d.SetNeighbors(new List<GridCell> { c });
        Assert.That(provider.GetNeighborsOfOrder(a, 1), Is.EquivalentTo(new[] { b }));
        Assert.That(provider.GetNeighborsOfOrder(a, 2), Is.EquivalentTo(new[] { b, c, d }));
        Assert.That(provider.GetNeighborsOfOrder(a, int.MaxValue).Count, Is.EqualTo(3));
        Assert.That(provider.FindPath(a, c, null), Is.EqualTo(new[] { a, b, c }));
        Assert.That(provider.FindPath(c, a, null), Is.Null);
        Assert.That(provider.FindPath(a, c, (_, to) => to != c), Is.Null);
        Assert.That(provider.FindPath(a, a, null), Is.EqualTo(new[] { a }));
        provider.RemoveCell(b);
        Assert.That(provider.FindPath(a, c, null), Is.Null);
        Assert.That(provider.GetNeighborsOfOrder(a, 3), Is.Empty);
    }

    [Test]
    public void RepeatedReadQueriesDoNotAllocateAfterWarmup()
    {
        Cell(0, 0, 0); Cell(1, 0, 0);
        var result = new List<IGridCell>(8);
        for (int i = 0; i < 10; i++) Query();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) Query();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.EqualTo(0), "Cached AllCells, closest lookup and buffer radius queries should not allocate.");

        void Query()
        {
            _ = provider.AllCells;
            provider.GetClosestCell(Vector3.zero);
            provider.GetCellsInRadius(Vector3.zero, 2, result);
        }
    }

    [Test]
    public void GeneratorUsesProviderOriginAndColliderBoundsWithoutRenderers()
    {
        provider.transform.position = new Vector3(12, 0, 8);
        var generator = Generator(out var targets);
        generator.transform.position = new Vector3(-50, 30, -40);
        Surface(targets, new Vector3(12, 0, 8));
        Surface(targets, new Vector3(13, .5f, 8));
        generator.GenerateGrid();
        Assert.That(provider.AllCells.Count, Is.EqualTo(2));
        foreach (GridCell cell in provider.AllCells)
        {
            Assert.That(provider.GetCellAt(cell.transform.position), Is.SameAs(cell));
            Assert.That(cell.Neighbors.Count(), Is.EqualTo(1));
        }
    }

    [Test]
    public void GeneratorGrowsRaycastBufferForManyFloorsAndDoesNotDuplicateOnRepeat()
    {
        var generator = Generator(out var targets);
        for (int level = 0; level < 24; level++) Surface(targets, new Vector3(0, level * 3, 0));
        generator.GenerateGrid();
        Assert.That(provider.AllCells.Count, Is.EqualTo(24));
        generator.GenerateGrid();
        Assert.That(provider.AllCells.Count, Is.EqualTo(24));
        Assert.That(container.GetComponentsInChildren<GridCell>(true).Length, Is.EqualTo(24));
    }

    [Test]
    public void GeneratorRejectsDuplicateKeysAndPreservesHelpersOnClear()
    {
        var generator = Generator(out var targets);
        Set(generator, "verticalMergeThreshold", 0f);
        Surface(targets, new Vector3(0, 0, 0)); Surface(targets, new Vector3(0, .2f, 0));
        var helper = Create("Helper"); helper.transform.SetParent(container);
        generator.GenerateGrid();
        Assert.That(provider.AllCells.Count, Is.EqualTo(1));
        Assert.That(container.GetComponentsInChildren<GridCell>().Length, Is.EqualTo(1));
        generator.ClearExisting();
        Assert.That(provider.AllCells, Is.Empty);
        Assert.That(helper != null, Is.True);
    }

    [Test]
    public void InvalidGenerationRetainsExistingGridAndEmptyTargetsDoNotScanFallbackArea()
    {
        var cell = Cell(0, 0, 0);
        var generator = Generator(out _);
        LogAssert.Expect(LogType.Warning, "Grid: no active non-trigger colliders in TargetsContainer matching ScanLayerMask. Existing grid retained.");
        generator.GenerateGrid();
        Assert.That(provider.AllCells, Does.Contain(cell));
        Set(generator, "cellPrefab", null);
        LogAssert.Expect(LogType.Error, "Grid: assign TargetsContainer, GridHolder, CellPrefab and CellsContainer.");
        generator.GenerateGrid();
        Assert.That(provider.AllCells, Does.Contain(cell));
    }

    [Test]
    public void ClearanceRejectsLowCeilingAndTriggersDoNotCreateFloors()
    {
        var generator = Generator(out var targets);
        Surface(targets, Vector3.zero);
        var trigger = Surface(targets, new Vector3(0, 4, 0)); trigger.isTrigger = true;
        Set(generator, "minClearanceHeight", 2f);
        var ceiling = Surface(Create("Obstacles").transform, new Vector3(0, 1, 0));
        generator.GenerateGrid();
        Assert.That(provider.AllCells, Is.Empty);
        Object.DestroyImmediate(ceiling.gameObject);
        generator.GenerateGrid();
        Assert.That(provider.AllCells.Count, Is.EqualTo(1));
    }

    [Test]
    public void OversizedScanIsRejectedBeforeDeletingExistingCells()
    {
        var old = Cell(0, 0, 0);
        var generator = Generator(out var targets);
        Surface(targets, Vector3.zero); Surface(targets, new Vector3(10000, 0, 10000));
        LogAssert.Expect(LogType.Error, "Grid: scan area exceeds MaxScanColumns or supported coordinates. Existing grid retained.");
        generator.GenerateGrid();
        Assert.That(provider.AllCells, Does.Contain(old));
    }

    [Test]
    public void GeneratedCellsAndConnectionsSurviveUndoRedo()
    {
        var generator = Generator(out var targets);
        Surface(targets, Vector3.zero); Surface(targets, Vector3.right);
        generator.GenerateGrid();
        UnityEditor.Undo.FlushUndoRecordObjects();
        Assert.That(provider.AllCells.Count, Is.EqualTo(2));
        UnityEditor.Undo.PerformUndo();
        Assert.That(provider.AllCells, Is.Empty);
        UnityEditor.Undo.PerformRedo();
        Assert.That(provider.AllCells.Count, Is.EqualTo(2));
        foreach (var cell in provider.AllCells) Assert.That(cell.Neighbors.Count(), Is.EqualTo(1));
    }

    private GridGenerator Generator(out Transform targets)
    {
        targets = Create("Targets").transform;
        var generator = Create("Generator").AddComponent<GridGenerator>();
        var prefab = Create("CellPrefab").AddComponent<GridCell>();
        prefab.GetComponent<Selectable>().enabled = false;
        Set(generator, "targetsContainer", targets); Set(generator, "gridHolder", provider);
        Set(generator, "cellPrefab", prefab); Set(generator, "cellsContainer", container);
        Set(generator, "minClearanceHeight", 0f); Set(generator, "scanLayerMask", (LayerMask)(1 << 30));
        return generator;
    }

    private BoxCollider Surface(Transform parent, Vector3 position)
    {
        var root = Create("Surface"); root.layer = 30; root.transform.SetParent(parent);
        root.transform.position = position;
        var collider = root.AddComponent<BoxCollider>(); collider.size = new Vector3(.8f, .1f, .8f);
        return collider;
    }

    private GridCell Cell(int x, int y, int z, float? worldY = null, bool register = true)
    {
        var root = Create("Cell"); root.transform.SetParent(container);
        var cell = root.AddComponent<GridCell>();
        cell.GetComponent<Selectable>().enabled = false;
        var position = new Vector3Int(x, y, z);
        float height = worldY ?? y * provider.CellSize;
        root.transform.position = provider.GridToWorldPos(position, height);
        cell.Initialize(position, height, Quaternion.identity, true);
        if (register) provider.RegisterCell(cell);
        return cell;
    }

    private GameObject Create(string name)
    {
        var root = new GameObject(name); objects.Add(root); return root;
    }

    private static void Set(object instance, string name, object value) => instance.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(instance, value);
}
