using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Exercises the same PoolManager.Pool.Pop/Push path used by ResourceManager,
/// including initial preloaded objects, reused objects, and new pool misses.
/// </summary>
internal static class PoolSpawnPoseRegression
{
    private const int Frames = 100;
    private const int SpawnsPerFrame = 7;
    private const float PhysicsStep = 0.02f;
    private const float PositionTolerance = 0.08f;
    private const BindingFlags Flags = BindingFlags.Instance |
        BindingFlags.Public | BindingFlags.NonPublic;

    [MenuItem("Tools/Beaver/Test 0.02s Pooled Rigidbody Spawn Pose")]
    private static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("Run the pooled-spawn regression from Edit Mode.");
            return;
        }

        GameObject root = null;
        GameObject prefab = null;
        var physicsMode = Physics.simulationMode;
        try
        {
            root = new GameObject("Pooled Pose Regression Root");
            prefab = new GameObject("Pooled Pose Regression Prefab");
            Rigidbody source = prefab.AddComponent<Rigidbody>();
            source.interpolation = RigidbodyInterpolation.Interpolate;
            source.useGravity = true;

            // Pool is an implementation detail; reflect to isolate it from
            // Managers' scene/EventSystem initialization.
            Type poolType = typeof(PoolManager).GetNestedType("Pool",
                BindingFlags.NonPublic);
            if (poolType == null) throw new InvalidOperationException("Pool type missing");
            object pool = Activator.CreateInstance(poolType, true);
            MethodInfo init = poolType.GetMethod("Init", Flags);
            MethodInfo pop = poolType.GetMethod("Pop", Flags);
            MethodInfo push = poolType.GetMethod("Push", Flags);
            if (init == null || pop == null || push == null)
                throw new MissingMethodException("Pool Init/Pop/Push missing");

            init.Invoke(pool, new object[] { "regression", prefab, root.transform, 5 });
            // Exercise 100 groups of 7 spawn/recycle operations at a 0.02s
            // fixed simulation step, exceeding the preloaded 5-object pool.
            Physics.simulationMode = SimulationMode.Script;
            int verified = 0;
            var pending = new List<(GameObject item, Vector3 position, Quaternion rotation)>();
            for (int frame = 0; frame < Frames; frame++)
            {
                pending.Clear();
                for (int i = 0; i < SpawnsPerFrame; i++)
                {
                    float x = 20f + (frame % 11) * 2f + i * 1.5f;
                    Vector3 target = new Vector3(x, 30f, 45f + i);
                    Quaternion rotation = Quaternion.Euler(0f, (frame * 7 + i * 19) % 360, 0f);
                    var item = (GameObject)pop.Invoke(pool,
                        new object[] { null, (Vector3?)target, (Quaternion?)rotation });
                    AssertPose(item, target, rotation, "immediately after Pop", frame, i, false);
                    pending.Add((item, target, rotation));
                    verified++;
                }
                Physics.Simulate(PhysicsStep);
                foreach (var entry in pending)
                    AssertPose(entry.item, entry.position, entry.rotation,
                        "after physics step", frame, 0, true);
                foreach (var entry in pending)
                    push.Invoke(pool, new object[] { entry.item });
            }
            Debug.Log($"Pooled spawn pose regression PASS: {verified} spawns " +
                $"across {Frames} physics steps ({PhysicsStep}s); no reset to origin.");
        }
        catch (Exception e)
        {
            Debug.LogError("Pooled spawn pose regression FAILED: " + e);
            throw;
        }
        finally
        {
            Physics.simulationMode = physicsMode;
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            if (prefab != null) UnityEngine.Object.DestroyImmediate(prefab);
        }
    }

    private static void AssertPose(GameObject item, Vector3 expected, Quaternion rotation,
        string phase, int frame, int index, bool afterStep)
    {
        Rigidbody body = item.GetComponent<Rigidbody>();
        if (body == null) throw new InvalidOperationException("Spawned object missing Rigidbody");

        // Only gravity may change Y during simulation; X/Z must remain at the spawn target.
        Vector3 transformPosition = item.transform.position;
        Vector3 physicsPosition = body.position;
        bool positionOk = afterStep
            ? Mathf.Abs(transformPosition.x - expected.x) < PositionTolerance &&
              Mathf.Abs(transformPosition.z - expected.z) < PositionTolerance &&
              Mathf.Abs(physicsPosition.x - expected.x) < PositionTolerance &&
              Mathf.Abs(physicsPosition.z - expected.z) < PositionTolerance
            : Vector3.Distance(transformPosition, expected) < PositionTolerance &&
              Vector3.Distance(physicsPosition, expected) < PositionTolerance;
        if (!positionOk || Quaternion.Angle(body.rotation, rotation) > 1f)
            throw new InvalidOperationException(
                $"Pose mismatch {phase} (frame {frame}, index {index}): " +
                $"expected {expected}, Transform {transformPosition}, Rigidbody {physicsPosition}");
    }
}
