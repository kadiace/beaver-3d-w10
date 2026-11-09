using System;
using System.Collections.Generic;
using System.Reflection;
using RestoreCastle3D;
using UnityEditor;
using UnityEngine;

internal static class CreekSynergySmokeTests
{
    private sealed class Fixture : IDisposable
    {
        private static readonly FieldInfo IdField = typeof(CastlePiece3D).GetField(
            "materialId", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo NeighborsField = typeof(CastlePiece3D).GetField(
            "_attachedPieces", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly CastleCatalog3D _catalog = new CastleCatalog3D();

        public CastlePiece3D Make(string id)
        {
            var go = new GameObject("Score test: " + id);
            _created.Add(go);
            go.AddComponent<Rigidbody>();
            CastlePiece3D piece = go.AddComponent<CastlePiece3D>();
            IdField.SetValue(piece, id);
            return piece;
        }

        public void Link(CastlePiece3D a, CastlePiece3D b)
        {
            ((HashSet<CastlePiece3D>)NeighborsField.GetValue(a)).Add(b);
            ((HashSet<CastlePiece3D>)NeighborsField.GetValue(b)).Add(a);
        }

        public int Score(CastlePiece3D any)
        {
            return CastleSynergy3D.Evaluate(CastleSynergy3D.Collect(any), _catalog).Total;
        }

        public void Dispose()
        {
            foreach (GameObject go in _created)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _created.Clear();
        }
    }

    [MenuItem("Tools/Beaver/Run Creek Synergy Smoke Tests")]
    private static void Run()
    {
        int passed = 0;
        Check("Standalone piece", 0, f => f.Score(f.Make("pebble")), ref passed);
        Check("Two pebbles", 2, f =>
        {
            var a = f.Make("pebble"); var b = f.Make("pebble");
            f.Link(a, b);
            return f.Score(a);
        }, ref passed);
        Check("Large stone with two direct stone neighbors", 9, f =>
        {
            var a = f.Make("large_stone"); var b = f.Make("pebble");
            var c = f.Make("pebble"); f.Link(a, b); f.Link(a, c);
            return f.Score(a);
        }, ref passed);
        Check("Two fossils", 6, f =>
        {
            var a = f.Make("fossil"); var b = f.Make("fossil");
            f.Link(a, b); return f.Score(a);
        }, ref passed);
        Check("Trash bag half score", 1, f =>
        {
            var a = f.Make("pebble"); var b = f.Make("pebble");
            var bag = f.Make("waste_bag_small");
            f.Link(a, b); f.Link(b, bag); return f.Score(a);
        }, ref passed);
        Check("Foul lump can create negative points", -3, f =>
        {
            var a = f.Make("foul_smelling_lump"); var b = f.Make("shoe");
            f.Link(a, b); return f.Score(a);
        }, ref passed);
        Check("Vine and stone synergy", 6, f =>
        {
            var a = f.Make("growing_vine"); var b = f.Make("pebble");
            var c = f.Make("fossil"); f.Link(a, b); f.Link(a, c);
            return f.Score(a);
        }, ref passed);
        Check("Barrel has zero scoring ability", 0, f =>
        {
            var a = f.Make("barrel"); var b = f.Make("pebble");
            f.Link(a, b); return f.Score(a);
        }, ref passed);
        Check("Bag plus foul lump stack", -8, f =>
        {
            var a = f.Make("pebble"); var b = f.Make("pebble");
            var bag = f.Make("waste_bag_small"); var lump = f.Make("foul_smelling_lump");
            f.Link(a, b); f.Link(b, bag); f.Link(b, lump);
            return f.Score(a);
        }, ref passed);
        Check("Indirect stones do not count as direct", 4, f =>
        {
            var stone = f.Make("large_stone"); var mud = f.Make("mud");
            var a = f.Make("pebble"); var b = f.Make("pebble");
            f.Link(stone, mud); f.Link(mud, a); f.Link(a, b);
            return f.Score(stone);
        }, ref passed);
        Debug.Log("Creek synergy smoke tests passed: " + passed + "/10");
    }

    private static void Check(string name, int expected, Func<Fixture, int> scenario, ref int passed)
    {
        using (var fixture = new Fixture())
        {
            int actual = scenario(fixture);
            if (actual != expected)
                throw new Exception(name + ": expected " + expected + ", got " + actual);
            passed++;
        }
    }
}
