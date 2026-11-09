using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RestoreCastle3D
{
    /// <summary>Live Joint graph scoring. No physical contact or base durability contributes points.</summary>
    public static class CastleSynergy3D
    {
        public sealed class Result
        {
            public readonly Dictionary<CastlePiece3D, int> Points = new Dictionary<CastlePiece3D, int>();
            public readonly HashSet<CastlePiece3D> Contributors = new HashSet<CastlePiece3D>();
            public int Total => Points.Values.Sum();
        }

        public static List<CastlePiece3D> Collect(CastlePiece3D start)
        {
            var group = new List<CastlePiece3D>();
            if (start == null || !start.isActiveAndEnabled) return group;
            var seen = new HashSet<CastlePiece3D>();
            var pending = new Stack<CastlePiece3D>();
            pending.Push(start);
            while (pending.Count > 0)
            {
                CastlePiece3D piece = pending.Pop();
                if (piece == null || !piece.isActiveAndEnabled || !seen.Add(piece)) continue;
                group.Add(piece);
                foreach (CastlePiece3D other in piece.AttachedPieces)
                    if (other != null && other.isActiveAndEnabled) pending.Push(other);
            }
            return group;
        }

        /// <summary>
        /// A pair can satisfy part of a multi-piece condition even if this first link
        /// would award zero points. Use this for selection outlines, not real scoring.
        /// </summary>
        public static bool HasPotentialTagSynergy(CastlePiece3D a, CastlePiece3D b,
            CastleCatalog3D catalog)
        {
            if (a == null || b == null || catalog == null) return false;
            string aTag = catalog.Get(a.MaterialId)?.category;
            string bTag = catalog.Get(b.MaterialId)?.category;
            if (aTag == null || bTag == null) return false;

            bool Helps(CastlePiece3D source, CastlePiece3D other, string tag)
            {
                string id = source.MaterialId;
                string otherId = other.MaterialId;
                switch (id)
                {
                    case "pebble":
                    case "large_stone": return tag == "석재";
                    case "fossil": return otherId == "fossil";
                    case "gem": return otherId == "pebble" || otherId == "large_stone" ||
                            otherId == "fossil";
                    case "plank":
                    case "branch": return tag == "목재";
                    case "leafy_branch": return tag == "목재" && otherId != id;
                    case "log": return tag == "목재" || otherId == "mud";
                    case "sunflower": return tag == "식물";
                    case "bamboo": return tag == "목재" || tag == "식물";
                    case "growing_vine": return tag == "석재";
                    case "living_tree": return tag == "목재" || tag == "식물";
                    case "mud": return tag == "목재";
                    case "shoe":
                    case "life_jacket": return tag == "잡동사니";
                    case "bucket": return tag == "석재";
                    case "watering_can": return tag == "식물";
                    case "loose_work_glove": return tag == "목재";
                    case "broken_game_controller": return tag == "잡동사니" &&
                            otherId != "broken_game_controller";
                    case "empty_bottle": return tag != "잡동사니";
                    case "waste_bag_small":
                    case "foul_smelling_lump": return true;
                    default: return false;
                }
            }
            return Helps(a, b, bTag) || Helps(b, a, aTag);
        }

        /// <summary>Delta of attaching a held single piece to a target and its existing component.</summary>
        public static int PreviewAttachmentDelta(CastlePiece3D held,
            CastlePiece3D target, CastleCatalog3D catalog)
        {
            if (held == null || target == null || held == target || catalog == null ||
                held.IsSettling || target.IsSettling ||
                !held.isActiveAndEnabled || !target.isActiveAndEnabled) return 0;

            List<CastlePiece3D> component = Collect(target);
            if (component.Contains(held) || component.Exists(p => p.IsSettling)) return 0;
            int before = Evaluate(component, catalog).Total;
            // Picked-up pieces are singletons in the current game mode.
            List<CastlePiece3D> heldGroup = Collect(held);
            before += Evaluate(heldGroup, catalog).Total;
            var combined = new List<CastlePiece3D>(component);
            foreach (CastlePiece3D p in heldGroup)
                if (!combined.Contains(p)) combined.Add(p);
            return Evaluate(combined, catalog, held, target).Total - before;
        }

        /// <summary>Score live connections with optional virtual direct edge for previews.</summary>
        public static Result Evaluate(List<CastlePiece3D> group, CastleCatalog3D catalog,
            CastlePiece3D virtualA = null, CastlePiece3D virtualB = null)
        {
            var result = new Result();
            if (group == null || group.Count < 2) return result;
            foreach (var p in group) result.Points[p] = 0;
            string Id(CastlePiece3D p) => p.MaterialId;
            string Cat(CastlePiece3D p) => catalog.Get(p.MaterialId)?.category ?? "";
            int Count(string tag) => group.Count(p => Cat(p) == tag);
            int Types(string tag) => group.Where(p => Cat(p) == tag).Select(Id).Distinct().Count();
            bool Has(string id) => group.Any(p => Id(p) == id);
            foreach (CastlePiece3D piece in group)
            {
                var others = group.Where(p => p != piece).ToList();
                var direct = piece.AttachedPieces.Where(p => p != null && others.Contains(p)).ToList();
                CastlePiece3D virtualNeighbor = piece == virtualA ? virtualB :
                    piece == virtualB ? virtualA : null;
                if (virtualNeighbor != null && others.Contains(virtualNeighbor) &&
                    !direct.Contains(virtualNeighbor)) direct.Add(virtualNeighbor);
                int Other(string tag) => others.Count(p => Cat(p) == tag);
                int Direct(string tag) => direct.Count(p => Cat(p) == tag);
                bool OtherHas(string id) => others.Any(p => Id(p) == id);
                void Award(int score, System.Func<CastlePiece3D, bool> contributors)
                {
                    if (score <= 0) return;
                    result.Points[piece] += score;
                    result.Contributors.Add(piece);
                    foreach (var p in others)
                        if (contributors(p)) result.Contributors.Add(p);
                }
                switch (piece.MaterialId)
                {
                    case "pebble": Award(Other("석재"), p => Cat(p) == "석재"); break;
                    case "large_stone":
                        if (Direct("석재") >= 2) Award(5, p => direct.Contains(p) && Cat(p) == "석재");
                        break;
                    case "fossil":
                        Award(others.Count(p => Id(p) == "fossil") >= 2 ? 5 :
                            OtherHas("fossil") ? 3 : 0, p => Id(p) == "fossil");
                        break;
                    case "gem":
                        int types = (Has("pebble") ? 1 : 0) + (Has("large_stone") ? 1 : 0) +
                            (Has("fossil") ? 1 : 0);
                        Award(types >= 3 ? 8 : types >= 2 ? 5 : 0,
                            p => Id(p) == "pebble" || Id(p) == "large_stone" || Id(p) == "fossil");
                        break;
                    case "plank": Award(Other("목재"), p => Cat(p) == "목재"); break;
                    case "branch": Award(Direct("목재") * 2, p => direct.Contains(p) && Cat(p) == "목재"); break;
                    case "leafy_branch":
                        Award(Types("목재") >= 3 ? 5 : Types("목재") >= 2 ? 3 : 0,
                            p => Cat(p) == "목재"); break;
                    case "log":
                        Award(Other("목재") > 0 ? 5 + (Has("mud") ? 2 : 0) : 0,
                            p => Cat(p) == "목재" || Id(p) == "mud"); break;
                    case "sunflower": Award(Other("식물") > 0 ? 2 : 0, p => Cat(p) == "식물"); break;
                    case "bamboo":
                        Award(Other("목재") > 0 && Other("식물") > 0 ? 5 :
                            Other("목재") > 0 || Other("식물") > 0 ? 3 : 0,
                            p => Cat(p) == "목재" || Cat(p) == "식물"); break;
                    case "growing_vine":
                        Award(Count("석재") >= 2 ? 5 : Count("석재") >= 1 ? 3 : 0,
                            p => Cat(p) == "석재"); break;
                    case "living_tree":
                        Award(Other("목재") > 0 && Other("식물") > 0 ? 7 :
                            Other("목재") > 0 || Other("식물") > 0 ? 5 : 0,
                            p => Cat(p) == "목재" || Cat(p) == "식물"); break;
                    case "mud": Award(Count("목재"), p => Cat(p) == "목재"); break;
                    case "shoe": Award(Other("잡동사니") > 0 ? 2 : 0, p => Cat(p) == "잡동사니"); break;
                    case "bucket": Award(Mathf.Min(5, 2 * Count("석재")), p => Cat(p) == "석재"); break;
                    case "watering_can": Award(Mathf.Min(5, 2 * Count("식물")), p => Cat(p) == "식물"); break;
                    case "loose_work_glove": Award(Mathf.Min(5, 2 * Count("목재")), p => Cat(p) == "목재"); break;
                    case "life_jacket":
                        Award(Count("잡동사니") >= 3 ? 5 : Count("잡동사니") >= 2 ? 3 : 0,
                            p => Cat(p) == "잡동사니"); break;
                    case "broken_game_controller":
                        Award(Mathf.Min(5, 2 * others.Where(p => Cat(p) == "잡동사니")
                            .Select(Id).Distinct().Count()), p => Cat(p) == "잡동사니"); break;
                    case "empty_bottle":
                        Award(others.Any(p => Cat(p) != "잡동사니") ? 2 : 0,
                            p => Cat(p) != "잡동사니"); break;
                }
            }

            int raw = result.Total;
            var bags = group.Where(p => Id(p) == "waste_bag_small")
                .OrderBy(p => p.GetInstanceID()).ToList();
            int remainingPenalty = raw - Mathf.FloorToInt(raw *
                Mathf.Max(0f, 1f - bags.Count * 0.5f));
            foreach (CastlePiece3D bag in bags)
            {
                int deduction = Mathf.Min(remainingPenalty, Mathf.CeilToInt(raw * 0.5f));
                result.Points[bag] -= deduction;
                remainingPenalty -= deduction;
                if (deduction != 0) result.Contributors.UnionWith(group);
            }
            foreach (CastlePiece3D lump in group.Where(p => Id(p) == "foul_smelling_lump"))
            {
                result.Points[lump] -= 3 * (group.Count - 1);
                result.Contributors.UnionWith(group);
            }
            return result;
        }
    }
}
