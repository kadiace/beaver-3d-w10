# Endless creek: Joint synergy and settlement

This mode is enabled by `CastleGame3D.Start()` for the continuous-spawn scene.

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
