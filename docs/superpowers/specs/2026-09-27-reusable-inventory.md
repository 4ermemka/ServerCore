# Reusable inventories and confirmed crafting

The user's six-point request replaces the fixed demo with reusable Unity prefabs
and extensible classes. Existing unrelated edits and Shared submodule changes
must be preserved. Target Unity 6000.6.3f1, uGUI, existing Shared InventorySystem.

## Model

Add an ordered `SlottedInventory<TItem>` beside the existing unordered storage
in Shared. It uses InventoryBase action validation/events, stable item IDs,
per-slot predicate rules, resizing, and atomic moves/swaps. A failed operation
must leave every participant unchanged. Rule changes cannot discard items.

`ICraftingItem<TItem>` adds Quantity and WithQuantity for immutable stack
remainders. `RecipeCraftingStation<TItem,TContext>` owns all matching, preview,
consumption and delivery logic. Recipes provide ingredient predicates, amounts,
optional fixed slot indices, a typed-context condition, and an output factory.
Unordered matching must find a valid allocation even when broad and specific
ingredient predicates overlap. A station can expose several matching recipes.

A preview is stable until inputs, selected recipe, context or rules change.
It is not a real stored item. Claiming it validates it again and atomically
consumes ingredients and delivers the shown result. Rejected/stale/cancelled
claims, full destinations, and failed rules do not consume resources. The
confirm button commits into a dedicated output slot; normal dragging takes
that real item afterwards. Context updates use SetContext, including updates
to mutable context objects. A recipe produces one item stack, whose data can
depend on the generic context and input items.

## Unity authoring

ItemDefinition ScriptableObjects contain ID, label, icon and tags; no item enum.
ItemInstance carries identity, definition and quantity; subclasses/generic data
can add gameplay state. Adding an item requires an asset, not edits to the UI.

InventoryComponent exposes slot count, per-slot overrides, and initial items.
CraftingStationComponent<TContext> extends it and exposes recipe/context hooks;
a concrete non-generic MonoBehaviour can be attached to any prefab. The sample
GunWorkbench defines recipe/rule details in one script and exposes asset and
player-context parameters in the Inspector.

InventoryView binds an inventory component and a slot prefab. Automatic mode
generates exactly the model's slot count under an inspector-configured layout.
Manual mode uses authored slot objects and their indices without moving them.
Slot placeholders may use a concrete item icon, a group icon/label, or nothing.
CraftingResultView displays either the selected preview or a committed output;
the station view offers recipe selection and confirmation.

Deliver real InventorySlot, InventoryPanel, GunWorkbench and demo prefabs plus
item assets. Replace the previous fixed demo scene component with instances of
these prefabs. Preserve the left/right test scene experience, existing icons,
starting resources/currency and scene's other objects.

## Verification

Model tests: moves/swaps and rule rejection, resize, stack consumption,
unordered overlap matching, ordered recipes, multiple recipes, context gates
and output data, preview stability/staleness, full destination, confirmation,
reentrancy and observer faults. Unity tests: prefab loading, scene/demo startup,
icons/placeholders, automatic/manual layouts, ghost raycasts, cancel/reject/drop
and button confirmation. Render the final prefab UI for visual inspection.
