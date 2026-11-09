using UnityEngine;

public class ResourceManager
{
    public T Load<T>(string path) where T : Object
    {
        return Resources.Load<T>(path);
    }

    public GameObject Instantiate(string path, Transform parent = null,
        Vector3? position = null, Quaternion? rotation = null)
    {
        GameObject prefab = Load<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogError($"Fail to Load original - path: Prefabs/{path}");
            return null;
        }

        if (prefab.GetComponent<Poolable>() != null)
            return Managers.Pool.Pop(path, parent, position: position, rotation: rotation);

        GameObject go = Object.Instantiate(prefab, parent);
        if (position.HasValue) go.transform.position = position.Value;
        if (rotation.HasValue) go.transform.rotation = rotation.Value;
        go.name = Util.RemoveUnityCloneSuffix(go.name);

        return go;
    }

    public void Destroy(GameObject target)
    {
        if (target.GetComponent<Poolable>() != null)
        {
            Managers.Pool.Push(target);
            return;
        }

        Object.Destroy(target);
    }
}
