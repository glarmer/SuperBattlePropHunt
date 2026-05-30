using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;

namespace PropHunt;

public class MeshBundleLoader
{
    private const string BundleFileName = "prophunt_export";

    private static LoadedPropHuntBundle? cachedBundle;

    public sealed class LoadedPropHuntBundle
    {
        public AssetBundle? Bundle { get; }
        public Dictionary<string, Mesh> MeshesByName { get; }
        public GameObject? PropHuntUIPrefab { get; }

        public LoadedPropHuntBundle(
            AssetBundle? bundle,
            Dictionary<string, Mesh> meshesByName)
        {
            Bundle = bundle;
            MeshesByName = meshesByName;
        }
    }

    public static LoadedPropHuntBundle LoadBundle()
    {
        if (cachedBundle != null)
        {
            Plugin.Log.LogInfo("[PropClone] Using cached PropHunt asset bundle.");
            return cachedBundle;
        }

        string? path = FindBundlePath();

        if (string.IsNullOrEmpty(path))
        {
            Plugin.Log.LogWarning(
                $"[PropClone] Bundle not found. Expected '{BundleFileName}' in either " +
                $"'{Path.Combine(Paths.PluginPath, "PropHunt")}' or '{Paths.PluginPath}'"
            );

            cachedBundle = new LoadedPropHuntBundle(
                null,
                new Dictionary<string, Mesh>(StringComparer.OrdinalIgnoreCase)
            );

            return cachedBundle;
        }

        AssetBundle bundle = AssetBundle.LoadFromFile(path);

        if (bundle == null)
        {
            Plugin.Log.LogError($"[PropClone] Failed to load bundle from '{path}'");

            cachedBundle = new LoadedPropHuntBundle(
                null,
                new Dictionary<string, Mesh>(StringComparer.OrdinalIgnoreCase)
            );

            return cachedBundle;
        }

        LogBundleContents(bundle);

        Dictionary<string, Mesh> meshesByName = LoadMeshes(bundle);

        cachedBundle = new LoadedPropHuntBundle(bundle, meshesByName);

        return cachedBundle;
    }

    public static Dictionary<string, Mesh> LoadMeshBundle()
    {
        return LoadBundle().MeshesByName;
    }

    public static GameObject? LoadPropHuntUIPrefab()
    {
        return LoadBundle().PropHuntUIPrefab;
    }

    public static void UnloadCachedBundle(bool unloadAllLoadedObjects = false)
    {
        if (cachedBundle?.Bundle != null)
        {
            cachedBundle.Bundle.Unload(unloadAllLoadedObjects);
            Plugin.Log.LogInfo("[PropClone] Unloaded cached PropHunt asset bundle.");
        }

        cachedBundle = null;
    }

    private static string? FindBundlePath()
    {
        string assemblyPath = typeof(Plugin).Assembly.Location;
        string assemblyDir = Path.GetDirectoryName(assemblyPath) ?? "";
        string path = Path.Join(assemblyDir, BundleFileName);

        if (File.Exists(path))
            return path;

        path = Path.Combine(Paths.PluginPath, BundleFileName);

        if (File.Exists(path))
            return path;

        return null;
    }

    private static Dictionary<string, Mesh> LoadMeshes(AssetBundle bundle)
    {
        Mesh[] meshes = bundle.LoadAllAssets<Mesh>();

        Dictionary<string, Mesh> bundledMeshesByName = meshes
            .Where(m => m != null)
            .GroupBy(m => m.name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (Mesh mesh in meshes
                     .Where(m => m != null)
                     .OrderBy(m => m.name)
                     .Take(200))
        {
            Plugin.Log.LogInfo(
                $"[PropClone] Bundled mesh: '{mesh.name}', " +
                $"subMeshes={mesh.subMeshCount}, bounds={mesh.bounds}"
            );
        }

        return bundledMeshesByName;
    }

    private static void LogBundleContents(AssetBundle bundle)
    {
        string[] assetNames = bundle.GetAllAssetNames();

        Plugin.Log.LogInfo($"[PropClone] Bundle contains {assetNames.Length} asset(s):");

        foreach (string assetName in assetNames.OrderBy(a => a).Take(300))
        {
            Plugin.Log.LogInfo($"[PropClone] Bundle asset: {assetName}");
        }

        if (assetNames.Length > 300)
        {
            Plugin.Log.LogInfo($"[PropClone] Bundle asset log truncated. Remaining: {assetNames.Length - 300}");
        }
    }
}