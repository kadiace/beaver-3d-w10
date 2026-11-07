using System;
using System.Collections.Generic;

using UnityEngine;

namespace RestoreCastle3D
{
    internal readonly struct CastleFloodVolume3D
    {
        private const float MINIMUM_DEPTH = 6f;

        public float CenterX { get; }
        public float Width { get; }
        public float StartZ { get; }
        public float EndZ { get; }
        public float CenterY { get; }
        public float Depth { get; }
        public float Length => StartZ - EndZ;

        /// <summary>
        /// 수면과 강바닥 높이, XZ 범위를 받아 홍수 큐브의 중심과 크기를 저장한다.
        /// 저장된 영역은 실제 홍수와 미리보기의 표시 및 접촉 판정에 함께 사용한다.
        /// </summary>
        public CastleFloodVolume3D(float centerX, float width, float startZ, float endZ,
            float surfaceY, float groundY)
        {
            CenterX = centerX;
            Width = Mathf.Max(1f, width);
            StartZ = startZ;
            EndZ = Mathf.Min(endZ, startZ - 0.1f);
            float bottomY = Mathf.Min(groundY - MINIMUM_DEPTH, surfaceY - MINIMUM_DEPTH);
            Depth = Mathf.Max(0.1f, surfaceY - bottomY);
            CenterY = (surfaceY + bottomY) * 0.5f;
        }

        /// <summary>
        /// 이전과 현재의 채움 길이를 받아 새로 채워진 구간의 중심과 반경을 반환한다.
        /// 채움 길이가 증가하지 않았으면 false를 반환한다.
        /// </summary>
        public bool TryGetFillSegment(float previousLength, float currentLength,
            out Vector3 center, out Vector3 halfExtents)
        {
            float current = Mathf.Clamp(currentLength, 0f, Length);
            float previous = Mathf.Clamp(previousLength, 0f, current);
            float length = current - previous;
            if (length <= 0f)
            {
                center = Vector3.zero;
                halfExtents = Vector3.zero;
                return false;
            }

            center = new Vector3(CenterX, CenterY, StartZ - previous - length * 0.5f);
            halfExtents = new Vector3(Width * 0.5f, Depth * 0.5f, length * 0.5f);
            return true;
        }
    }

    internal sealed class CastleFloodQuery3D
    {
        private const float HORIZONTAL_MARGIN = 8f;

        [Header("Query buffers")]
        private readonly HashSet<CastlePiece3D> _contactedPieces = new HashSet<CastlePiece3D>();
        private readonly Stack<CastlePiece3D> _frontier = new Stack<CastlePiece3D>();
        private Collider[] _colliders = new Collider[64];

        /// <summary>
        /// 현재 자재와 강바닥 및 카메라 범위, 높이 설정을 받아 홍수 영역을 반환한다.
        /// 비기반 자재의 최하단 루트 높이로 수면을 정하고 강바닥 전체를 XZ 범위에 포함한다.
        /// </summary>
        public static CastleFloodVolume3D Capture(IEnumerable<CastlePiece3D> pieces,
            Camera camera, Bounds riverBounds, float minimumSurfaceY,
            float bottomLayerHeight, float groundY)
        {
            float lowestY = float.PositiveInfinity;
            float leftX = riverBounds.min.x;
            float rightX = riverBounds.max.x;
            float nearZ = riverBounds.min.z;
            float farZ = riverBounds.max.z;

            foreach (CastlePiece3D piece in pieces)
            {
                if (piece == null) continue;
                Vector3 position = piece.transform.position;
                leftX = Mathf.Min(leftX, position.x);
                rightX = Mathf.Max(rightX, position.x);
                nearZ = Mathf.Min(nearZ, position.z);
                farZ = Mathf.Max(farZ, position.z);
                if (piece.MaterialId != "foundation" && !piece.RiverAnchored)
                    lowestY = Mathf.Min(lowestY, position.y);
            }

            float surfaceY = float.IsPositiveInfinity(lowestY)
                ? minimumSurfaceY
                : Mathf.Max(minimumSurfaceY, lowestY + bottomLayerHeight);
            Plane surface = new Plane(Vector3.up, new Vector3(0f, surfaceY, 0f));
            for (int x = 0; x <= 1; x++)
            {
                for (int y = 0; y <= 1; y++)
                {
                    Ray ray = camera.ViewportPointToRay(new Vector3(x, y, 0f));
                    if (!surface.Raycast(ray, out float distance)) continue;
                    Vector3 point = ray.GetPoint(distance);
                    leftX = Mathf.Min(leftX, point.x);
                    rightX = Mathf.Max(rightX, point.x);
                    nearZ = Mathf.Min(nearZ, point.z);
                    farZ = Mathf.Max(farZ, point.z);
                }
            }

            return new CastleFloodVolume3D((leftX + rightX) * 0.5f,
                rightX - leftX + HORIZONTAL_MARGIN * 2f,
                farZ + HORIZONTAL_MARGIN, nearZ - HORIZONTAL_MARGIN, surfaceY, groundY);
        }

        /// <summary>
        /// 홍수 영역에서 새로 채워진 구간을 조회해 생존 중인 비기반 자재를 결과에 추가한다.
        /// 콜라이더 버퍼가 가득 차면 확장하여 재조회하고 같은 자재의 중복 접촉은 합친다.
        /// </summary>
        public void CollectContacts(CastleFloodVolume3D volume, float previousLength,
            float currentLength, HashSet<CastlePiece3D> livePieces,
            HashSet<CastlePiece3D> result)
        {
            if (!volume.TryGetFillSegment(previousLength, currentLength,
                out Vector3 center, out Vector3 halfExtents))
                return;

            int count;
            while (true)
            {
                count = Physics.OverlapBoxNonAlloc(center, halfExtents, _colliders,
                    Quaternion.identity, Physics.AllLayers, QueryTriggerInteraction.Ignore);
                if (count < _colliders.Length) break;
                Array.Resize(ref _colliders, _colliders.Length * 2);
            }

            for (int i = 0; i < count; i++)
            {
                CastlePiece3D piece = _colliders[i].GetComponentInParent<CastlePiece3D>();
                if (piece != null && piece.MaterialId != "foundation" &&
                    !piece.RiverAnchored && livePieces.Contains(piece))
                    result.Add(piece);
            }
            Array.Clear(_colliders, 0, count);
        }

        /// <summary>
        /// 현재 전체 홍수 영역과 생존 자재를 받아 직접 접촉 및 영구 부착 묶음을 결과에 저장한다.
        /// 기존 결과를 비우고 실제 제거 규칙과 같은 연결 탐색을 수행한다.
        /// </summary>
        public void CollectDestructionTargets(CastleFloodVolume3D volume,
            HashSet<CastlePiece3D> livePieces, HashSet<CastlePiece3D> result)
        {
            result.Clear();
            _contactedPieces.Clear();
            CollectContacts(volume, 0f, volume.Length, livePieces, _contactedPieces);
            foreach (CastlePiece3D piece in _contactedPieces)
                CollectAttachmentGroup(piece, livePieces, result, _frontier, true);
            _contactedPieces.Clear();
        }

        /// <summary>
        /// 시작 자재와 생존 목록을 받아 영구 부착으로 연결된 자재들을 결과에 추가한다.
        /// 전달된 스택을 탐색 버퍼로 재사용하며 이미 방문한 자재는 건너뛴다.
        /// </summary>
        public static void CollectAttachmentGroup(CastlePiece3D piece,
            HashSet<CastlePiece3D> livePieces, HashSet<CastlePiece3D> result,
            Stack<CastlePiece3D> frontier, bool preserveRiverAnchors = false)
        {
            frontier.Clear();
            frontier.Push(piece);
            while (frontier.Count > 0)
            {
                CastlePiece3D current = frontier.Pop();
                if (current == null || !livePieces.Contains(current) ||
                    (preserveRiverAnchors && current.RiverAnchored) || !result.Add(current))
                    continue;
                foreach (CastlePiece3D attached in current.AttachedPieces)
                    if (attached != null && !result.Contains(attached))
                        frontier.Push(attached);
            }
        }
    }
}
