// PrefabReplacerPreset ScriptableObject
// Place anywhere under Assets (not necessarily under Editor)
using UnityEngine;

[CreateAssetMenu(fileName = "PrefabReplacerPreset", menuName = "Meno Tools/Prefab Replacer Preset")]
public class PrefabReplacerPreset : ScriptableObject
{
    [Header("Copy Options")]
    public bool copyMaterials = true;
    public bool copyParticleMaterials = true;
    public bool copyNames = false;

    [Header("Prefab Paths")]
    public string[] sourcePaths = new string[0];
    public string[] targetPaths = new string[0];
}
