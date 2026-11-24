using System;
using System.Collections.Generic;
using UnityEngine;

// Maps manifest asset ids to prefabs, so the loader works in any app that carries the prefabs and
// this asset (built by IMRAI > Build city prefabs and manifest).
[CreateAssetMenu(menuName = "IMRAI/Prefab Registry")]
public class PrefabRegistry : ScriptableObject
{
    [Serializable] public class Entry { public string id; public GameObject prefab; }
    public List<Entry> entries = new List<Entry>();

    public GameObject Find(string id)
    {
        foreach (var e in entries) if (e.id == id) return e.prefab;
        return null;
    }
}
