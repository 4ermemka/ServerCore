using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ServerCore.Inventory.Tests
{
    public class InventoryCatalogTests
    {
        private GameObject _catalogRoot, _targetRoot;
        private ItemDefinition _definition;
        private ItemCatalog _data;
        private InfiniteCatalogView _catalog;
        private InventoryView _target;

        [SetUp]
        public void SetUp()
        {
            _definition = ScriptableObject.CreateInstance<ItemDefinition>();
            _definition.Id = "catalog-test";
            _definition.DisplayName = "Ресурс";
            _definition.MaxStack = 7;
            _data = ScriptableObject.CreateInstance<ItemCatalog>();
            _data.SetItems(new[] { _definition });
            _catalogRoot = new GameObject("Catalog");
            _catalogRoot.SetActive(false);
            _catalog = _catalogRoot.AddComponent<InfiniteCatalogView>();
            _catalog.Catalog = _data;
            _catalog.QuantityPerDrag = 10;
            _catalogRoot.SetActive(true);
            _targetRoot = new GameObject("Target");
            _targetRoot.SetActive(false);
            var inventory = _targetRoot.AddComponent<InventoryComponent>();
            inventory.SlotCount = 2;
            _target = _targetRoot.AddComponent<InventoryView>();
            _target.Source = inventory;
            _targetRoot.SetActive(true);
            _catalog.Rebind();
            _target.Rebind();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_catalogRoot);
            Object.DestroyImmediate(_targetRoot);
            Object.DestroyImmediate(_data);
            Object.DestroyImmediate(_definition);
        }

        [Test]
        public void SelectedIssueQuantityMergesWithoutConsumingTheTemplate()
        {
            var template = _catalog.GetItem(0);
            Assert.That(_catalog.TryTransferTo(0, _target, 0, 2).Succeeded, Is.True);
            string identity = _target.GetItem(0).ItemId;
            Assert.That(_catalog.TryTransferTo(0, _target, 0, 3).Succeeded, Is.True);
            Assert.That(_target.GetItem(0).Quantity, Is.EqualTo(5));
            Assert.That(_target.GetItem(0).ItemId, Is.EqualTo(identity));
            Assert.That(_catalog.GetItem(0), Is.SameAs(template));
            Assert.That(_catalog.TryTransferTo(0, _target, 0, 4).Succeeded, Is.False);
            Assert.That(_target.GetItem(0).Quantity, Is.EqualTo(5));
        }
        [Test]
        public void RepeatedIssueKeepsTemplateAndCreatesDistinctBoundedStacks()
        {
            var template = _catalog.GetItem(0);
            Assert.That(_catalog.TryTransferTo(0, _target, 0).Succeeded, Is.True);
            Assert.That(_catalog.TryTransferTo(0, _target, 1).Succeeded, Is.True);
            Assert.That(_catalog.GetItem(0), Is.SameAs(template));
            Assert.That(_target.BoundModel[0].Quantity, Is.EqualTo(7));
            Assert.That(_target.BoundModel[0].ItemId, Is.Not.EqualTo(template.ItemId));
            Assert.That(_target.BoundModel[1].ItemId, Is.Not.EqualTo(_target.BoundModel[0].ItemId));
            Assert.That(_catalog.GetQuantityText(0, template), Is.EqualTo("∞"));
        }

        [Test]
        public void FailedIssueAndIncomingDropPreserveBothInventories()
        {
            Assert.That(_catalog.TryTransferTo(0, _target, 0).Succeeded, Is.True);
            var item = _target.BoundModel[0];
            Assert.That(_catalog.TryTransferTo(0, _target, 0).Succeeded, Is.False);
            Assert.That(_target.TryTransferTo(0, _catalog, 0).Succeeded, Is.False);
            Assert.That(_target.BoundModel[0], Is.SameAs(item));
            Assert.That(_catalog.BoundModel.SlotCount, Is.EqualTo(1));
            _target.BoundModel.SetRule(1, new Assets.Shared.Systems.InventorySystem.Slots.SlotRule<ItemInstance>(accepts: _ => false));
            Assert.That(_catalog.TryTransferTo(0, _target, 1).Succeeded, Is.False);
            Assert.That(_target.BoundModel[1], Is.Null);
        }

        [Test]
        public void DataDiscoveryIncludesNewDerivedDefinitionAndRemovesDeletedAsset()
        {
            const string path = "Assets/Data/Inventory/__CatalogProbe.asset";
            const string catalogPath = "Assets/Resources/Inventory/ItemCatalog.asset";
            Assert.That(AssetDatabase.LoadMainAssetAtPath(path), Is.Null);
            var definition = ScriptableObject.CreateInstance<CatalogProbeItemDefinition>();
            definition.Id = "catalog-probe";
            definition.DisplayName = "Новый тип";
            try
            {
                AssetDatabase.CreateAsset(definition, path);
                AssetDatabase.SaveAssets();
                Assert.That(EditorApplication.ExecuteMenuItem("Tools/Inventory/Refresh Item Catalog"), Is.True);
                var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(catalogPath);
                Assert.That(catalog.Items, Does.Contain(definition));
                AssetDatabase.DeleteAsset(path);
                EditorApplication.ExecuteMenuItem("Tools/Inventory/Refresh Item Catalog");
                Assert.That(System.Array.Exists(catalog.Items, item => item != null && item.Id == "catalog-probe"), Is.False);
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
                EditorApplication.ExecuteMenuItem("Tools/Inventory/Refresh Item Catalog");
            }
        }
    }
}
