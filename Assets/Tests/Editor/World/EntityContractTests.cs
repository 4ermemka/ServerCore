using System.IO;
using System.Linq;
using NUnit.Framework;
using ServerCore.World.Entities;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ServerCore.World.Tests
{
    public sealed class EntityContractTests
    {
        [Test]
        public void EntityTestSceneIsAvailable()
        {
            Assert.That(File.Exists("Assets/Scenes/EntityTestScene.unity"), Is.True,
                "The new entity scene must exist as a saved Unity asset.");
            Assert.That(EditorBuildSettings.scenes.Any(s => s.enabled && s.path == "Assets/Scenes/EntityTestScene.unity"), Is.True);
        }

        [Test]
        public void PrefabsHaveRequestedHierarchyModelAndCircularStand()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Entities/Player.prefab");
            Assert.That(player, Is.Not.Null);
            Assert.That(player.GetComponent<PlayerEntity>(), Is.InstanceOf<PersonEntity>());
            Assert.That(typeof(PersonEntity).BaseType, Is.EqualTo(typeof(HumanoidEntity)));
            Assert.That(typeof(HumanoidEntity).BaseType, Is.EqualTo(typeof(AliveEntity)));
            Assert.That(typeof(AliveEntity).BaseType, Is.EqualTo(typeof(BaseEntity)));
            Assert.That(typeof(FurnitureEntity).BaseType, Is.EqualTo(typeof(BaseEntity)));
            var stand = player.transform.Find("Circular Base");
            Assert.That(stand, Is.Not.Null);
            Assert.That(stand.localScale.x, Is.EqualTo(stand.localScale.z));
            Assert.That(stand.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.GreaterThan(20));
            Assert.That(player.transform.Find("Model"), Is.Not.Null);
            Assert.That(player.GetComponent<PlayerEntity>().SceneId, Is.Empty);
            var furniture = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Entities/Furniture.prefab");
            Assert.That(furniture.GetComponentsInChildren<WorldObjectView>(true).Length, Is.EqualTo(1));
            Assert.That(furniture.GetComponent<FurnitureEntity>(), Is.Not.Null);
            Assert.That(furniture.GetComponentsInChildren<Renderer>().Length, Is.GreaterThan(0));
        }

        [Test]
        public void SavedSceneContainsGeneratedGridAndMatchingFurnitureData()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/EntityTestScene.unity");
            var data = Object.FindAnyObjectByType<WorldDataObject>();
            Assert.That(data.name, Is.EqualTo("Data"));
            Assert.That(LayerMask.LayerToName(data.gameObject.layer), Is.EqualTo("Data"));
            Assert.That(data.GridProvider.AllCells.Count, Is.GreaterThan(150));
            Assert.That(data.GridProvider.AllCells.All(cell => cell.IsValid), Is.True);
            Assert.That(data.Entities.Count, Is.EqualTo(5));
            Assert.That(data.Entities.All(record => record.Kind == EntityKind.Furniture), Is.True);
            var views = data.EntitiesRoot.GetComponentsInChildren<FurnitureEntity>();
            Assert.That(views.Select(view => view.SceneId), Is.EquivalentTo(data.Entities.Select(record => record.Id)));
            data.ValidateRecords(data.Entities);
        }
    }
}
