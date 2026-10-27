# Editor Authored Inventory Implementation Plan

> Use superpowers:subagent-driven-development for the two independent runtime units while the main agent implements catalog and Unity integration. Keep this file as progress ledger.

**Goal:** Inspector-authored stations, live player context/debug controls, infinite item catalog, preserved user work and updated guide.
**Architecture:** Existing Shared transactional engine remains; serialized recipe assets feed it. Observable scene context keeps its preview fresh. Editor asset scan builds a runtime catalog.
**Tech Stack:** Unity 6000.6.3f1, uGUI, C#, NUnit.
**Spec:** docs/superpowers/specs/2026-09-27-editor-inventory.md

## Constraints and rulings
- Existing workspace is the integration target; preserve dirty user scene/assets and Shared edits. No commits.
- User explicitly requests these reversible changes; proceed without another design approval round.
- New item names and Campfire recipes come from current user assets, not the prior generator defaults.
- The runtime has no UnityEditor dependency; discovery uses an editor-generated catalog.

## Task 1: Observable player context (agent)
Files: new Context/*.cs, Views/PlayerContextView.cs; Components/CraftingStationComponent.cs; minimal Shared RecipeCraftingStation busy-state API if needed; new PlayerContextTests.cs.
- [x] Implement contracts in spec, debug mutators/experience and binding.
- [x] Generic context subscriptions support safe queued changes during commits and clean disposal.
- [x] Verify change propagation, replacement, unsubscribe and no callback loops.

## Task 2: Inspector recipes and static presentation (agent)
Files: new Recipes/*.cs and Components/ConfiguredCraftingStation.cs; Examples/GunWorkbench.cs,CampfireWorkbench.cs; Views/InventoryView.cs,InventorySlotView.cs,CraftingControls.cs; new RecipeDefinitionTests.cs.
- [x] Implement declared recipe schema, compile to Shared recipes, immutable level/quality result data and experience rewards after success.
- [x] Preserve script GUIDs; example workbenches become thin configured subclasses. Parent migrates existing serialized assets.
- [x] Editor-set placeholders/messages remain editable. Expose virtual InventoryView.GetQuantityText(int,ItemInstance) for catalog infinity label.
- [x] Verify authored matching/conditions/result data, reward only on successful commit and hints independent of rules.

## Task 3: Infinite catalog and preserved prefab integration (main)
Files: Items/ItemCatalog.cs; Views/InfiniteCatalogView.cs; Editor/Inventory/ItemCatalogBuilder.cs and InventoryPrefabBuilder.cs; test fixtures and migrated data/prefabs.
- [x] Catalog discovery and build refresh, unique materialization and validated target addition.
- [x] Migrate existing Gun/Campfire recipes, preserve user's item definitions and workbench geometry.
- [x] Add scene context plus runtime controls and catalog panels; maintain a usable layout.
- [x] Run Unity model/UI tests and inspect screenshots; review implementation.
- [x] Update docs/InventoryDemo.md and comments; verify main project compilation and final assets.

## Interface review
Task1 produces IPlayerContext and generic safe subscription; Task2 consumes it without editing generic base. Task2 produces recipe asset fields for Task3 migration and quantity text hook for Task3 catalog. Task1 debug view fields feed Task3 prefab assembly. No file ownership overlap except isolated test assembly shared by distinct files.

## Verification results
- Unity 6000.6.3f1 isolated project: 51/51 passed (50 repository tests + 1 temporary integration probe retaining the real scene's prefab overrides).
- Main project's actual compiler response files: Shared, ServerCore.Inventory, Assembly-CSharp-Editor and ServerCore.Inventory.Tests all exit 0. Three unrelated InputSystem serialization analyzer warnings (UAC1002/UAC1005/UAC1006); no inventory warnings or errors.
- Reviewed rendered initial/live UI and real scene prefab instance. Campfire's internal manual layout retained; root panels repositioned in the demo prefab.
- Independent review found two P2 issues: reward exceptions after commit and nested observable refresh. Both fixed and covered by regression tests.
- 170 source/metadata/data/prefab files matched the Unity-tested copy. Existing scene and item/tag definition files retained byte-for-byte.
- Full scene gameplay was not launched: the isolated integration probe validates the actual inventory prefab overrides without unrelated scene systems.
