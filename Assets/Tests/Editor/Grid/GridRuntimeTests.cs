using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class GridRuntimeTests
{
    private GameObject root;

    [UnitySetUp]
    public IEnumerator SetUp() { yield return new EnterPlayMode(); }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (root != null) Object.Destroy(root);
        yield return null;
        LogAssert.NoUnexpectedReceived();
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator ReplacingGridInSameFrameDisablesOldCellsBeforeDeferredDestruction()
    {
        root = new GameObject("RuntimeGridTest");
        var provider = Child("Provider").AddComponent<GridProvider>();
        var cells = Child("Cells").transform;
        var targets = Child("Targets").transform;
        var prefab = Child("Prefab").AddComponent<GridCell>();
        prefab.GetComponent<Selectable>().enabled = false;
        var surface = Child("Surface"); surface.layer = 30; surface.transform.SetParent(targets);
        surface.AddComponent<BoxCollider>().size = new Vector3(.8f, .1f, .8f);
        var generator = Child("Generator").AddComponent<GridGenerator>();
        Set(provider, "cellsContainer", cells);
        Set(generator, "gridHolder", provider); Set(generator, "cellsContainer", cells);
        Set(generator, "targetsContainer", targets); Set(generator, "cellPrefab", prefab);
        Set(generator, "scanLayerMask", (LayerMask)(1 << 30)); Set(generator, "minClearanceHeight", 0f);
        generator.GenerateGrid();
        var old = cells.GetChild(0).gameObject;
        generator.GenerateGrid();
        Assert.That(old.activeSelf, Is.False);
        Assert.That(provider.AllCells.Count, Is.EqualTo(1));
        Assert.That(cells.GetComponentsInChildren<GridCell>().Length, Is.EqualTo(1));
        yield return null;
        Assert.That(old == null, Is.True);
        Assert.That(provider.AllCells.Count, Is.EqualTo(1), "Destruction of old cells must not unregister the replacements.");
        generator.ClearExisting();
        Assert.That(provider.AllCells, Is.Empty);
        yield return null;
        Assert.That(cells.childCount, Is.Zero);
    }

    private GameObject Child(string name)
    {
        var child = new GameObject(name); child.transform.SetParent(root.transform); return child;
    }
    private static void Set(object instance, string name, object value) => instance.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(instance, value);
}
