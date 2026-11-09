using System.Collections.Generic;
using UnityEngine;

public class PoolManager
{
    private class Pool
    {
        private readonly Stack<GameObject> _items = new();
        private GameObject _prefab;
        private Transform _root;

        public void Init(string key, GameObject prefab, Transform parent, int preloadCount)
        {
            _prefab = prefab;
            _root = new GameObject($"{key}_Pool").transform;
            _root.SetParent(parent, false);

            for (int i = 0; i < preloadCount; i++)
                Push(Create());
        }

        /// <summary>
        /// Place a pooled object in both Transform and physics space before reactivating it.
        /// Interpolated Rigidbodies otherwise can restore their previous physics pose (often
        /// prefab origin) after Transform.position has already been assigned.
        /// </summary>
        public GameObject Pop(Transform parent, Vector3? position, Quaternion? rotation)
        {
            GameObject item = _items.Count > 0 ? _items.Pop() : Create();

            // Create() instantiates active prefabs. Keep even new objects inactive while
            // reparenting and setting their spawn pose, just like previously pooled ones.
            if (item.activeSelf) item.SetActive(false);

            Transform itemTransform = item.transform;
            itemTransform.SetParent(parent != null ? parent : _root, false);
            if (position.HasValue) itemTransform.position = position.Value;
            if (rotation.HasValue) itemTransform.rotation = rotation.Value;

            Vector3 spawnPosition = itemTransform.position;
            Quaternion spawnRotation = itemTransform.rotation;

            Rigidbody body = item.GetComponent<Rigidbody>();
            RigidbodyInterpolation interpolation = RigidbodyInterpolation.None;
            if (body != null)
            {
                interpolation = body.interpolation;
                // Clear the cached interpolation pose when teleporting a reused Rigidbody.
                body.interpolation = RigidbodyInterpolation.None;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.position = spawnPosition;
                body.rotation = spawnRotation;
            }

            item.SetActive(true);

            if (body != null)
            {
                // Rigidbody.position can be updated by physics during activation; explicitly
                // resynchronize both representations before interpolation is enabled again.
                body.position = spawnPosition;
                body.rotation = spawnRotation;
                itemTransform.SetPositionAndRotation(spawnPosition, spawnRotation);
                body.interpolation = interpolation;
            }

            return item;
        }

        public void Push(GameObject item)
        {
            item.SetActive(false);
            item.transform.SetParent(_root, false);
            _items.Push(item);
        }

        public void Clear()
        {
            Object.Destroy(_root.gameObject);
            _items.Clear();
        }

        private GameObject Create()
        {
            GameObject item = Object.Instantiate(_prefab);
            item.name = _prefab.name;
            return item;
        }
    }

    private readonly Dictionary<string, Pool> _pools = new();
    private readonly Dictionary<GameObject, string> _keys = new();
    private Transform _root;

    public void Init(Transform appRoot)
    {
        GameObject root = new("@Pool_Root");
        root.transform.SetParent(appRoot, false);
        _root = root.transform;
    }

    /// <summary>프리팹 키의 풀에서 기물을 꺼내 지정된 위치에 활성화한다.</summary>
    public GameObject Pop(string prefabPath, Transform parent = null, int preloadCount = 5,
        Vector3? position = null, Quaternion? rotation = null)
    {
        if (!_pools.TryGetValue(prefabPath, out Pool pool))
        {
            GameObject prefab = Managers.Resource.Load<GameObject>(prefabPath);
            pool = CreatePool(prefabPath, prefab, preloadCount);
        }

        GameObject item = pool.Pop(parent, position, rotation);
        _keys[item] = prefabPath;
        Poolable poolable = item.GetComponent<Poolable>();
        poolable.PoolKey = prefabPath;
        return item;
    }

    public void Push(GameObject item)
    {
        string key = item.GetComponent<Poolable>().PoolKey;
        _pools[key].Push(item);
    }

    public void Clear()
    {
        foreach (Pool pool in _pools.Values)
            pool.Clear();

        _pools.Clear();
        _keys.Clear();
    }

    private Pool CreatePool(string key, GameObject prefab, int preloadCount)
    {
        Pool pool = new();
        pool.Init(key, prefab, _root, preloadCount);
        _pools.Add(key, pool);
        return pool;
    }
}
