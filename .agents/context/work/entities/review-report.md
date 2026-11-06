# Entity implementation review

Reviewed the prepared working-tree baseline package and current sources. No code changes, commits, test runs, or Unity processes were performed. Existing unrelated root/Shared changes were left untouched. Findings below are source-traced; reproductions were not executed because the parent owns the active Unity test run.

## P1 — Defer player spawning until the requested scene is bound

Location: `Assets/Scripts/Systems/World/Session/GameSessionController.cs:182-186`.

`EnsurePlayerEntity` only checks that `scene` exists. A scene command publishes an empty replacement WorldState immediately (`ProcessCommand`, lines 320-324), while the old scene remains assigned until `LateUpdate` (lines 200-214). GameServer drains all queued command and ClientReady events in the same Update. If a reconnect/join becomes ready after the scene command in that drain, OnPeerReady calls the old scene spawner against the new empty state. The resulting player uses the old prefab and grid. The destination then skips its authored seed because Players.Count is nonzero (`WorldSceneController:77`); switching EntityTestScene -> Test additionally throws for unknown `entity.player` in the destination catalog.

Reproduction: host EntityTestScene; enqueue a valid ChangeScene(Test) command followed by a remote ClientReady event before the same network Update drains. Equivalently, a focused integration test can invoke accepted scene command processing then OnPeerReady before LateUpdate. Observe the replacement state acquiring an old-scene player before BindScene and destination initialization failing/skipping furniture.

Fix direction: before spawning, require the bound path/revision to match State.Scene. Leave the participant connected and allow the existing BindScene connected-participant loop to create the avatar in the destination. Do not use IsReady alone because initial host readiness is processed while Connect is busy.

## P2 — Preserve dirty live drafts when the model changes

Location: `Assets/Editor/Systems/World/Entities/WorldDataObjectEditor.cs:63-66`.

Every model signature change unconditionally replaces the Draft, even if the user has unsaved edits. During play, a movement/turn update of the same entity can silently erase an in-progress display-name, owner, transform, or parameters edit before Apply is clicked. This also removes the prior validation error. Constant inspector repaint makes the replacement immediate.

Reproduction: host a world, edit a live entity name or parameters without Apply, then change that entity Cell through gameplay (or another accepted authoritative update). The next Inspector draw restores all draft fields from the runtime model and loses the typed values.

Fix direction: track whether the draft differs from its source; only auto-refresh clean drafts. For dirty drafts retain edits and indicate conflict, or explicitly merge fields that the user has not changed.

## P2 — Direct host Transform edits in Play do not update entity data

Location: `Assets/Editor/Systems/World/Entities/EntityAuthoringSync.cs:48-49,62-64`.

The sole scene-to-data synchronization path is disabled throughout Play. WorldObjectView only subscribes in the opposite direction. Consequently direct scene model Transform changes during Play leave the authoritative Cell/Rotation/Scale unchanged, do not reach peers, and are later reverted by GameSessionController.RefreshPlan/WorldObjectView.RestorePosition. This is a requirement gap if the user's bidirectional editing requirement ('even outside runtime') includes direct model editing during runtime, as its wording indicates. Ordinary gameplay drag still follows the existing plan pipeline and should remain so.

Reproduction: start EntityTestScene as host, select a PlayerEntity root, and move/rotate/scale it using Unity Transform Inspector or scene tools. Inspect Data's live record or a connected peer: values remain unchanged. Trigger a world/planning update and the edited view snaps back.

Fix direction: add a host-authorized runtime editor adapter for direct scene-tool edits, distinct from gameplay drag/plan preview, that validates and applies root changes to the existing runtime model. A client should remain read-only.

## Additional checked behavior

Normal reconnect uses OwnerId and returns the existing model without invoking the factory; snapshot replacement retains that key. Entity root transforms intentionally use an identity base; legacy WorldObjectView behavior remains unchanged. Outside Play, moving the Entities parent changes child AnchorWorld and is captured as new cells. Child artwork/prefab geometry remains normal Unity authoring rather than serializable world geometry. No separate actionable defect was established in these paths by source review.

Final verification: Unity 6000.6.3f1 full EditMode suite including Play-mode transitions passed 183/183, 0 failed, 0 skipped (.utmp/Entities/tests-verified.xml). The isolated EntityAuthoringTests suite passed 10/10. All three review findings have regression coverage and are resolved. Scene capture visually verified; supplied FBX hash matches imported source. New metadata GUIDs validated, documentation local links resolve, root and Shared diff checks pass, no manifest/lock changes. No commits/push or cleanup of unrelated work.
