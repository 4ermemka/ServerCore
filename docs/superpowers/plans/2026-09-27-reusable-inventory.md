# Reusable Inventory Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Track task results below.

**Goal:** Replace the fixed inventory demo with editable prefabs and generic confirmed crafting.
**Architecture:** Shared owns slots, rules, recipes and atomic commits. Unity components own item assets and authoring hooks; views only bind models. A concrete sample configures gun recipes and typed player context.
**Tech Stack:** Unity 6000.6.3f1, uGUI, C# 9, NUnit.
**Spec:** `docs/superpowers/specs/2026-09-27-reusable-inventory.md`

## Global constraints

- Preserve unrelated changes and existing Shared modifications.
- No item enum, no recipe knowledge inside views, no dependency additions.
- Stay in the active workspace: the user is iterating on this scene and its uncommitted demo.
- Preview never consumes resources; commit validates destination before mutation.

## Review focus

- Stale or twice-used drag previews cannot duplicate outputs.
- Broad ingredient predicates must not greedily steal exact-match ingredients.
- Full destinations, rejecting rules and reentrant observers cannot lose inputs.
- Manual slot positions and automatically generated slot counts remain editable.
- Changing context changes the preview and invalidates an earlier drag.

## Task 1: Shared slots and recipe engine

Files: new `Slots` classes, `Items/ICraftingItem.cs`, recipe classes under
`Assets/Shared/Scripts/Systems/InventorySystem`, model tests under `Assets/Tests/Editor/Inventory`.

Interfaces: `SlottedInventory<TItem>` exposes SlotCount, Revision, Slots, indexer,
GetRule/SetRule, TryResize/TryAdd/TryRemove/TryMoveTo and SlotsChanged.
`RecipeCraftingStation<TItem,TContext>` exposes Input, Output, Previews, Preview,
SetContext, SelectRecipe, TryClaim, TryConfirm, PreviewChanged and Dispose.
Recipe ingredients specify predicate, amount and optional slot index. A recipe
factory accepts the ordered input snapshot and context and returns one item.

- [x] Write and run behavioral model tests for the specification.
- [x] Implement the shared engine through existing inventory action semantics.
- [x] Run model tests and review transactional behavior.

## Task 2: Item assets, components and generic views

Files: `Assets/Scripts/Inventory`, replacing the old `DebugAndTest/InventoryDemo` implementation.
Interfaces: ItemDefinition assets; ItemInstance implementing ICraftingItem;
InventoryComponent.Model; a non-generic CraftingStationComponent bridge and
generic CraftingStationComponent<TContext> with BuildRecipes/BuildContext hooks.
InventoryView supports manual and generated InventorySlotView bindings;
CraftingResultView delegates claims to the station; shared drag payload captures
source identity and preview token. GunWorkbench is the only gun-specific code.

- [x] Add data-driven definitions and component hooks.
- [x] Implement bound views, preview drag, confirmation and recipe selection.
- [x] Compile and exercise views against the engine.

## Task 3: Prefabs, scene migration and proof

Files: `Assets/Prefabs/Inventory`, `Assets/Data/Inventory`, Test.unity, docs/InventoryDemo.md.

- [x] Generate actual editable Unity prefabs/assets using Unity Editor APIs.
- [x] Replace the old demo with a prefab instance in the existing scene.
- [x] Run Unity model and interaction tests in the isolated validation project.
- [x] Inspect rendered output and request an independent review.
- [x] Document subclassing, recipe/context hooks, slot rules and layout authoring.

## Progress

Design reflects the user's requested architecture. Core/model work is delegated
while the main worker implements Unity authoring and prefab integration. No
commits or unrelated submodule changes are part of this task.

Validation: 25 model tests and 2 Unity prefab interaction tests passed (27/27).
Fresh Shared, runtime and Editor compilation with the main project dependencies passed.
Independent review findings were fixed: scrolling, context refresh priority and atomic configuration.
The previous enum-driven demo and tests were removed; Test.unity now references InventoryDemo.prefab.
