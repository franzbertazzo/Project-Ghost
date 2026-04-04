using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generic object pool for RingMarker instances. Avoids runtime Instantiate/Destroy overhead.
/// </summary>
public class MarkerPool<T> where T : RingMarker
{
    private readonly T prefab;
    private readonly Transform parent;
    private readonly Stack<T> inactive = new Stack<T>();
    private readonly List<T> active = new List<T>();

    public IReadOnlyList<T> ActiveMarkers => active;
    public int ActiveCount => active.Count;

    public MarkerPool(T prefab, Transform parent, int preloadCount)
    {
        this.prefab = prefab;
        this.parent = parent;

        for (int i = 0; i < preloadCount; i++)
        {
            T marker = Object.Instantiate(prefab, parent);
            marker.Deactivate();
            inactive.Push(marker);
        }
    }

    public T Get()
    {
        T marker;
        if (inactive.Count > 0)
        {
            marker = inactive.Pop();
        }
        else
        {
            marker = Object.Instantiate(prefab, parent);
        }

        active.Add(marker);
        return marker;
    }

    public void Return(T marker)
    {
        marker.Deactivate();
        active.Remove(marker);
        inactive.Push(marker);
    }

    public void ReturnAll()
    {
        for (int i = 0; i < active.Count; i++)
        {
            active[i].Deactivate();
            inactive.Push(active[i]);
        }
        active.Clear();
    }
}
