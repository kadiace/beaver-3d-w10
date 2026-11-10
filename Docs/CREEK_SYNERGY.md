# Endless creek: Joint synergy and settlement

This mode is enabled by `CastleGame3D.Start()` for the continuous-spawn scene.

## Input actions

- `Assets/InputSystem_Actions.inputactions` is the shared project-wide asset for gameplay and UI. `Managers.Input` reads its actions without adding or overriding bindings at runtime; the EventSystem prefab uses its UI map.
- The Player map defines `PointerPosition`, `PointerDelta`, `PrimaryClick` (left mouse), `SecondaryClick` (right mouse), `CameraZoom` (wheel), `Cancel` (Escape), `RotateLeft` (Q), `RotateRight` (E), and `ToggleFloodPreview` (Tab). The rotation and flood-preview actions also cover the retained stage-placement mode.
- Left click picks up and releases a piece. Right drag pans the camera; a short right click tracks a piece, and the wheel changes zoom. Escape closes the effects panel. Existing UI actions remain enabled while gameplay actions are active so buttons and popups continue to work during pauses.

## Runtime responsibilities

- `CastleSynergy3D`: traverses actual attachment edges, evaluates direct vs connected-group abilities, assigns each member an integer score contribution, and applies trash penalties without clamping negative totals.
- `CastleCreekScore3D`: tracks spawned objects, reacts to new/broken edges, highlights contributing members on hover, shows individual score delta labels, and settles connected components at Z = -15 exactly once. A fall beyond Y = -10 is discarded without an award.
- `CastlePiece3D`: keeps the existing high-strength collision-enabled SpringJoint, emits edge changes and arms the barrel only on attachment. A barrel attached to an existing group can join and dismantle it.
- `CastleFloatingText3D`: reused for preview deltas and larger actual settlement awards.

Standalone pieces have 0 points and no points popup. The total score can be negative. Offscreen settlement awards use an edge indicator, with nearby-in-time events aggregated.

## Score formula

```
positive = sum(member ability scores)
discounted = floor(positive * max(0, 1 - 0.5 * bagCount))
score = discounted - 3 * foulLumpCount * (groupSize - 1)
```

The score engine attaches the discount to bag entries and flat deductions to foul-lump entries so all UI views share the same calculation.

## Validation in Unity Editor

1. Open the Unity project and allow script compilation and asset import.
2. Select **Tools > Beaver > Run Creek Synergy Smoke Tests** to run 10 deterministic rule scenarios. This tool intentionally creates temporary GameObjects and removes them.
3. Start the game scene. Confirm endless spawns, grab/throw, and original spring strength and collision retention.
4. Build a small 2–5 piece group, hover it, and verify that connected score contributors receive outlines and popup numbers update immediately after attachment and breakage.
5. Attach a barrel to an existing group. Verify its 1-second fuse, group detachment, outward impulse, barrel disposal, and 0.5-second reattachment delay.
6. Add a trash bag or foul lump. Check 50% bag penalties and potentially negative lump scores. Break the corresponding edge to verify removal of the penalty.
7. Pan the camera away from the Z = -15 boundary; verify an edge score indicator and animated cumulative score when a group leaves. A standalone item must not create a point popup.
8. Leave the scene running with repeated spawns and pooling. Watch Console for missing references, stale joint state, unexpected awarding on Y-fallout, and increased memory use.

> Editor compilation, runtime physics and visual QA have to be performed in Unity; they were not available in the remote editing environment.

## Legacy compatibility

The previous stage/round-based `CastleGame3D` and `CastleHud3D` implementations remain in source for compatibility, but the endless-spawn branch does not enter them. New material data does not carry `baseReinforcement`; this no longer contributes to endless-creek scoring. Legacy-stage cleanup remains independent of the endless-creek scoring path.

## 0.02-second spawning and pooled Rigidbody placement

The continuous spawner uses a 0.02-second interval. The pooled prefab's Rigidbody uses `Interpolate`.
Previously, `PoolManager.Pool.Pop()` set only `Transform.position/rotation` after
reparenting, and a pool miss created an **active** prefab at its original (0,0,0)
position. The Rigidbody's cached physics/interpolation pose could remain at that
old location and override the intended spawn position on a physics step.

`Pool.Pop()` now deactivates new objects for placement, synchronizes both
the Transform and Rigidbody positions/rotations, clears dynamic Rigidbody
velocities, and resets/restores interpolation around the teleport.

**Regression check in Unity Editor (Edit Mode):**

1. Select **Tools > Beaver > Test 0.02s Pooled Rigidbody Spawn Pose**.
2. The test runs 100 physics steps of 0.02 seconds, spawning/recycling 7
   interpolated Rigidbodies per step (700 placements), including pool misses
   beyond the default 5-item preload.
3. The test fails if Transform/Rigidbody world positions or Rigidbody rotation
   diverge from the requested spawn pose, before or after the physics step.
4. Then run the actual Game scene at 0.02 seconds per spawn and check that
   freshly spawned pieces no longer move back to (0,0,0).

The editor tool is provided but was not executed in the remote environment.

## Two-second settled-group state and focused inspection

When any member of a spring-attached group crosses **Z = -15**:

1. Snapshot the whole connected component and its score before modifying physics.
2. Award the score exactly once and immediately unregister all of its members from
   hover/pickup/synergy-preview and camera targeting.
3. Freeze every Rigidbody in place, disable its collisions/colliders, hide its
   outline and prevent any gameplay action or new Joint.
4. Keep the entire group visible for **2 seconds**, then return all members to
   the resource pool in the same removal pass. Pool resets colliders and rigidbody
   settings for later spawns.
5. Independently fallen objects beyond Y = -10 still use the existing discard
   path and do not award points.

**Controls:** Hold left click on a pickup-eligible piece to highlight all
camera-visible pieces with a nonzero hypothetical attachment score change
(cyan for positive synergy, red for negative effect). The hypothetical link
does not change physics or real scores. Right click **without dragging** a
piece to make the camera smoothly follow it and pin a readable inspection
panel in the upper-left corner. Right-drag still manually pans the camera and
cancels tracking. Right-click empty space also cancels tracking. The panel
disappears immediately when the followed piece settles/is removed; it must
not reappear on a pooled instance.

**Editor regression checks:** Select
`Tools > Beaver > Run Creek Synergy Smoke Tests`. The 14 scenarios now
also cover virtual direct-edge point previews, trash penalties, preview
non-mutation, and collider/interaction locking with state restoration after
pooling.

**Play Mode checklist:**

- Attach 2–5 pieces, let **one** cross Z=-15 and confirm all become inert
  simultaneously; they should remain visible for about 2 seconds and then
  disappear together.
- During the two seconds, try left-click pickup, hover, outline, right-click
  follow, SpringJoint reattachment and explosion. None should reactivate.
- Confirm score increases once on first crossing and does not change on the
  later pool return or on a different member crossing the threshold.
- Check subsequent reuse from the pool has normal collider, Rigidbody,
  hover and pickup behavior again.
- Hold the mouse on a pebble/large stone/wood piece and inspect candidate
  outlines. Confirm the highlighted pieces actually alter the score on a
  hypothetical Joint and the displayed score remains unchanged until real
  attachment.
- Right-click a moving item without dragging: camera pans/follows and the
  tooltip remains readable as the item moves away from the cursor.
- Right-drag manually to cancel follow; zoom should keep following until
  the piece is gone or another target is chosen.

> The smoke tests and Play Mode checks are provided for Unity validation but
> have not been run inside this remote GitHub editing session.
