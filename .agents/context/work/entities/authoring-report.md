# Entity authoring implementation report

Owned runtime files (namespace `ServerCore.World.Entities`):
- `Assets/Scripts/Systems/World/Entities/Authoring/EntityKind.cs`: Player/Furniture discriminator.
- `Assets/Scripts/Systems/World/Entities/Authoring/EntityParameter.cs`: serializable Key/Value parameter entry.
- `Assets/Scripts/Systems/World/Entities/Authoring/EntityRecord.cs`: public persistent record fields; `CreateData()` and `FromData(ObjectData)` preserve IDs, owner, display name, cell, absolute root local pose, parameters.
- `Assets/Scripts/Systems/World/Entities/Authoring/WorldDataObject.cs`: serialized `_gridProvider`, `_entitiesRoot`, `_prefabs`, `_entities`; public `GridProvider`, `EntitiesRoot` (falls back to self), `Prefabs`, `Entities`, `RuntimeState`; `Bind(WorldState)`, `SeedInto(WorldState)`, `ValidateRecords(IEnumerable<EntityRecord>)`, `GetPrefab(EntityRecord)`.

Owned editor files (namespace `ServerCore.World.Entities.Editor`):
- `EntityAuthoringSync.cs`: public static `Synchronize(WorldDataObject)`, `CreateEntity(WorldDataObject, BaseEntity, Vector3)`, `GetValidationError(WorldDataObject)`; guarded 200 ms editor polling, scene save synchronization, full proposal validation before mutations, baseline change detection, record-first initial load, grid snapping, actual ObjectData binding, duplicate GUID regeneration, bidirectional deletion, prefab-link-preserving instantiation and Undo recording.
- `EntityAuthoringMenus.cs`: GameObject/Entity/Player and Furniture plus WorldDataObject context commands. Defaults point to Assets/Prefabs/Entities/{Player,Furniture}.prefab. Missing scene data or catalog produces actionable errors.
- `WorldDataObjectEditor.cs`: editable serialized records/configuration, create actions and validation help.

Tests in `Assets/Tests/Editor/World/EntityAuthoringTests.cs`:
1. Record roundtrip with owner/pose/parameters.
2. Invalid later record leaves WorldState completely untouched.
3. Data-to-scene and scene-to-data names/transforms, with actual model binding.
4. Duplication fresh IDs and deletion in either direction.
5. Matching persisted SceneId reuse on initial reconciliation.
6. Scene save/reopen preserves entity IDs and parameters.
7. Creation Undo/Redo restores records and view together.
Editor helpers are called by reflection only in the fixture, respecting existing test assembly boundaries.

All newly created sources have unique .meta files. Authoring and editor folder metas created when absent. The shared editor folder's meta pass also supplied EntitySceneBuilder.cs.meta only when absent; parent owns its source.

Validation: sources reviewed against existing GridProvider, WorldObjectView and pure model APIs; no Unity process started by this subagent as requested. Parent owns Unity import/build and tests; no success claim for unexecuted tests. Test fixtures were written before implementation, but not run for RED because the parent explicitly owns the single Unity process.

Behavior/limits: conflicting edits to both a record and its view favor the record. On initial load persisted records win; unmatched scene entities are adopted. Undo rebases the baseline after restoring the grouped state. Direct edits to a bound runtime model's nonvisual OwnerId/Parameters are outside authoring record observation; edit serialized EntityRecord fields for persistent authoring. Invalid scenes log a deduplicated validation error and retain the previous scene/data state. One WorldDataObject owns its configured entity subtree; nested BaseEntity roots are excluded. No production world data stores view references.

## Follow-up integration review

Unity 6000.6 import reported GetInstanceID as an error. Replaced all authoring identifiers with `GetEntityId()` and typed `UnityEngine.EntityId` keys/values. Parent owns the rerun.

Added `WorldRuntimeDataEditing.cs` (+ unique meta), exposing `CanEdit` and `Apply(WorldDataObject, EntityRecord)`. The live Data inspector enumerates `RuntimeState.Objects`, including spawned players; it shows name, owner GUID, cell, root local rotation/scale and editable parameter rows with drafts and Apply/Reset. A present session requires `IsHost && IsReady`; no session allows local live editing. Apply validates the complete proposed runtime record set before assigning the existing model's Sync fields, preserving WorldState, model identity and Planning. Identity/kind/definition changes are rejected.

Player records now require unique nonempty owner GUIDs; empty ownership remains valid and furniture owners may be shared. Duplicating a Player clears copied OwnerId while preserving other metadata and generating a fresh scene ID.

Tests expanded to ten: explicit data-to-scene rotation/scale, copied owner clearing, normalized duplicate owner rejection, transform Undo/Redo and live in-place editing with invalid proposal rejection. Test-only editor helper resolution uses the known Assembly-CSharp-Editor identity rather than scanning loaded assemblies. Parent is running import/build/test verification; these changes remain unverified until that run completes.

## Current verification and complete file inventory

Parent reran Unity after the EntityId fix: build exited 0 and produced EntityTestScene with 285 cells and five furniture records. This confirms authoring/runtime/editor source compilation and builder integration. Parent's full EditMode suite (including Play transitions) is running; pass/fail results are not yet available.

Current owned source files:
- Assets/Scripts/Systems/World/Entities/Authoring/EntityKind.cs
- Assets/Scripts/Systems/World/Entities/Authoring/EntityParameter.cs
- Assets/Scripts/Systems/World/Entities/Authoring/EntityRecord.cs
- Assets/Scripts/Systems/World/Entities/Authoring/WorldDataObject.cs
- Assets/Editor/Systems/World/Entities/EntityAuthoringSync.cs
- Assets/Editor/Systems/World/Entities/EntityAuthoringMenus.cs
- Assets/Editor/Systems/World/Entities/WorldDataObjectEditor.cs
- Assets/Editor/Systems/World/Entities/WorldRuntimeDataEditing.cs
- Assets/Tests/Editor/World/EntityAuthoringTests.cs
Each has its own unique .meta; authoring/editor folder metadata are present.

Public API inventory:
- EntityKind.Player / Furniture.
- EntityParameter.Key / Value.
- EntityRecord: Id, DefinitionId, Kind, OwnerId, DisplayName, Cell, Rotation, Scale, Parameters; CreateData(); static FromData(ObjectData).
- WorldDataObject: GridProvider, EntitiesRoot, Prefabs, Entities, RuntimeState; Bind(WorldState), SeedInto(WorldState), ValidateRecords(IEnumerable<EntityRecord>), GetPrefab(EntityRecord).
- EntityAuthoringSync: Synchronize(WorldDataObject), CreateEntity(WorldDataObject, BaseEntity, Vector3), GetValidationError(WorldDataObject).
- EntityAuthoringMenus.Create(WorldDataObject, EntityKind).
- WorldRuntimeDataEditing.CanEdit, Apply(WorldDataObject, EntityRecord).

Behavior intended for permanent docs: outside Play the Data component's authored entity list and the entity subtree synchronize both ways; records are authoritative on initial scene load and conflicts. Scene duplicate IDs become GUIDs; duplicated players lose inherited owner identity. View edits snap the anchor to the closest actual grid cell. Existing matching scene IDs are reused. Entity creation uses catalog prefabs with preserved prefab links; edits/deletions and induced record/view changes share Undo groups. During Play the inspector shows live state separately, keeps authored data disabled, permits complete validated entity drafts to be applied only by a ready host (or a scene without a session), and mutates existing Sync models without replacing planning/state. Runtime edits are runtime state changes, not automatic writes back to persisted authoring records.

## Live Inspector reverse synchronization and draft concurrency follow-up

Initial full parent suite passed 176/176, including the original ten authoring tests. Parent then ran new RED regressions: live Inspector transform/name edits, invalid-scale restoration, missing optimistic Apply overload and missing dirty-draft tracking failed as expected. Existing programmatic-preview/network test passed. An isolated persistence fixture also revealed an untitled-scene dependency; fixture now creates its own SceneManager.CreateScene and reopens the saved scene as a preview, leaving an existing untitled scene unchanged and cleaning preview scenes with ClosePreviewScene.

New owned source files:
- Assets/Editor/Systems/World/Entities/EntityRuntimeAuthoringSync.cs (+ unique meta): InitializeOnLoad Undo.postprocessModifications bridge. Only changed root BaseEntity Transform channels or GameObject name are captured in Play. Position edits map to nearest grid Cell; rotation and scale retain absolute local root semantics. Name/scale/rotation edits do not capture a concurrent gameplay position preview. Ownership requires matching runtime model reference in a WorldDataObject in the same loaded scene and configured entity subtree. A ready host or offline scene may edit; unauthorized/invalid input restores model pose/name and logs a warning. The bridge unregisters on assembly reload/quitting, skips play-mode transitions, excludes prefab assets/stages/nested entity artwork and never polls scene poses during Play.
- Assets/Editor/Systems/World/Entities/WorldRuntimeDataDraft.cs (+ unique meta): public constructor(ObjectData), Record, SourceSignature, HasConflict, IsDirty, Refresh(ObjectData), Reset(ObjectData). Clean drafts track current data; dirty drafts survive external changes and expose a conflict until explicitly reset.
- Assets/Tests/Editor/World/EntityRuntimeEditingTests.cs (+ unique meta): five focused Play regressions using actual Undo.RecordObject/FlushUndoRecordObjects rather than invoking a test-only bridge.

WorldRuntimeDataEditing adds public Apply(WorldDataObject, EntityRecord, string expectedSourceSignature). Existing two-argument Apply remains compatible. The optimistic overload checks the current complete model signature before any mutation, preventing stale drafts from overwriting unrelated newer live changes. WorldDataObjectEditor now uses that signature, retains dirty drafts and shows a conflict/reset warning.

Parent owns the GREEN run. Source is frozen awaiting its results; no Unity process or commits run by this subagent.

## Final persistence fixture isolation repair

Parent's full GREEN run executed 183 tests: 182 passed. All live reverse synchronization, draft concurrency and scene transition regressions passed. The sole remaining failure was fixture setup: SceneManager.CreateScene is Play-only in Unity 6000.6.3f1.

Changed only EntityAuthoringTests.SceneSaveAndReopenPreservesRecordAndInstanceIdentity. It now uses the Editor's existing empty scene asset factory through exact test-only reflection (`EditorSceneManager.CreateSceneAsset(string,bool)`), with a fail-fast missing-method assertion. The method's exact signature and Assembly/Static metadata were verified in the installed UnityEditor.CoreModule.dll using Unity's bundled Cecil library; this inspection launches no Unity process. Official source also describes the same factory: https://github.com/Unity-Technologies/UnityCsReference/blob/master/Editor/Mono/EditorSceneManager.cs.

The fixture creates a normal temporary .unity asset before opening it, so public OpenScene(Additive) does not create a forbidden second Untitled scene. It performs actual authoring synchronization, saves/closes/reopens a normal scene, asserts persistent IDs/parameters and nonnull bound Data, then edits name and Cell and verifies live scene/model updates. The original active scene is retained/restored, never saved or unloaded. Temporary assets/scenes are scoped to the fixture and cleaned by its teardown. No production guards or APIs changed, and no tests bypass synchronization via preview scenes.

Source is frozen for the parent's isolated fresh-empty-scene fixture run. The full run's 182 passes are confirmed; this final fixture correction is pending parent verification.

Final verification: Unity 6000.6.3f1 full EditMode suite including Play-mode transitions passed 183/183, 0 failed, 0 skipped (.utmp/Entities/tests-verified.xml). The isolated EntityAuthoringTests suite passed 10/10. All three review findings have regression coverage and are resolved. Scene capture visually verified; supplied FBX hash matches imported source. New metadata GUIDs validated, documentation local links resolve, root and Shared diff checks pass, no manifest/lock changes. No commits/push or cleanup of unrelated work.
