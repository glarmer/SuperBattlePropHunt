using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Gamemode_Lib;
using UnityEngine;

namespace PropHunt;

internal class RaycastListener : MonoBehaviour
{
    private const bool VerboseCloneLogging = true;
    private const int MaxLoggedMeshesPerClone = 20;

    private Dictionary<string, Mesh> BundledMeshesByName { get; set; }

    public static RaycastListener? Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(this);
        }
        else
        {
            Destroy(this);
        }

        BundledMeshesByName = MeshBundleLoader.LoadMeshBundle();
        RaycastUtility.RaycastResultReceived += DoOnRaycastComplete;
    }

    private void OnDestroy()
    {
        Instance = null;
        RaycastUtility.RaycastResultReceived -= DoOnRaycastComplete;
    }

    private void DoOnRaycastComplete(RaycastUtility.RaycastResultReceivedEventArgs args)
    {
        Plugin.Log.LogInfo(
            $"[PropClone] Raycast complete. " +
            $"purpose='{args.Purpose}', root='{args.ClosestValidRootObjectName}', requester='{args.RequestingClientGuid}'"
        );

        if (!PlayerInfo.playerInfoPerPlayerGuid.TryGetValue(args.RequestingClientGuid, out var playerInfo))
        {
            Plugin.Log.LogWarning($"[PropClone] No PlayerInfo for requester guid '{args.RequestingClientGuid}'");
            return;
        }

        GameObject selectedObject = ResolveSelectedObject(args);

        if (selectedObject == null)
        {
            Plugin.Log.LogWarning($"[PropClone] Could not find selected object '{args.ClosestValidRootObjectName}'");
            return;
        }

        LogObjectSummary("[PropClone] Selected object", selectedObject);
        LogObjectSummary("[PropClone] Player object", playerInfo.gameObject);

        string purpose = args.Purpose.ToLowerInvariant();

        if (purpose.Contains("decoy"))
        {
            CopyObjectAsDecoy(selectedObject, playerInfo.transform);
        }
        else if (purpose.Contains("disguise"))
        {
            CopyObjectAsChild(selectedObject, playerInfo.gameObject);
        }
    }

    private void MatchCloneVisualSizeToOriginal(GameObject original, GameObject clone)
    {
        if (original == null || clone == null)
            return;

        if (!TryGetRenderableBounds(original, out Bounds originalBounds))
        {
            Plugin.Log.LogWarning($"[PropClone] Could not get original render bounds for '{original.name}'");
            return;
        }

        if (!TryGetRenderableBounds(clone, out Bounds cloneBounds))
        {
            Plugin.Log.LogWarning($"[PropClone] Could not get clone render bounds for '{clone.name}'");
            return;
        }

        Vector3 originalSize = originalBounds.size;
        Vector3 cloneSize = cloneBounds.size;

        if (cloneSize.x <= 0f || cloneSize.y <= 0f || cloneSize.z <= 0f)
        {
            Plugin.Log.LogWarning(
                $"[PropClone] Clone bounds invalid for size match. " +
                $"clone='{clone.name}', cloneSize={Fmt(cloneSize)}"
            );

            return;
        }

        Vector3 scaleMultiplier = new Vector3(
            originalSize.x / cloneSize.x,
            originalSize.y / cloneSize.y,
            originalSize.z / cloneSize.z
        );

        float uniformMultiplier = scaleMultiplier.y;

        if (float.IsNaN(uniformMultiplier) || float.IsInfinity(uniformMultiplier) || uniformMultiplier <= 0f)
        {
            uniformMultiplier = Mathf.Max(scaleMultiplier.x, scaleMultiplier.z);
        }

        if (float.IsNaN(uniformMultiplier) || float.IsInfinity(uniformMultiplier) || uniformMultiplier <= 0f)
        {
            Plugin.Log.LogWarning(
                $"[PropClone] Invalid size multiplier for '{clone.name}'. " +
                $"originalSize={Fmt(originalSize)}, cloneSize={Fmt(cloneSize)}, multiplier={Fmt(scaleMultiplier)}"
            );

            return;
        }

        Vector3 beforeScale = clone.transform.localScale;
        clone.transform.localScale = beforeScale * uniformMultiplier;

        Plugin.Log.LogInfo(
            $"[PropClone] Matched clone visual size.\n" +
            $"    original='{original.name}', clone='{clone.name}'\n" +
            $"    originalBounds.size={Fmt(originalSize)}\n" +
            $"    cloneBounds.sizeBefore={Fmt(cloneSize)}\n" +
            $"    scaleMultiplier={Fmt(scaleMultiplier)}\n" +
            $"    uniformMultiplier={uniformMultiplier:F4}\n" +
            $"    rootScaleBefore={Fmt(beforeScale)}\n" +
            $"    rootScaleAfter={Fmt(clone.transform.localScale)}"
        );
    }

    private bool TryGetRenderableBounds(GameObject obj, out Bounds bounds)
    {
        bounds = default;

        if (obj == null)
            return false;

        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);

        bool hasBounds = false;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            if (!renderer.enabled)
                continue;

            if (renderer is ParticleSystemRenderer)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds;
    }

    private GameObject CreateMovableVisualClone(GameObject original, bool copyColliders = true)
    {
        if (original == null)
            return null;

        Plugin.Log.LogInfo($"[PropClone] Creating visual clone for '{GetPath(original.transform)}'");
        LogObjectSummary("[PropClone] Original root", original);

        GameObject root = new GameObject(original.name + "_VisualClone");

        root.transform.position = Vector3.zero;
        root.transform.rotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;
        root.isStatic = false;
        root.layer = original.layer;
        root.tag = original.tag;

        Dictionary<Transform, Transform> sourceToCloneTransform = BuildCloneTransformHierarchy(original, root);

        MeshFilter[] sourceMeshFilters = original.GetComponentsInChildren<MeshFilter>(true);
        HashSet<MeshRenderer> renderersToCopy = GetHighestDetailMeshRenderers(original);

        Plugin.Log.LogInfo(
            $"[PropClone] Found {sourceMeshFilters.Length} MeshFilter(s) under '{original.name}'. " +
            $"Copying {renderersToCopy.Count} highest-detail MeshRenderer(s)."
        );

        int loggedMeshes = 0;
        int copiedMeshes = 0;
        int replacedStaticMeshes = 0;
        int skippedMeshes = 0;

        foreach (MeshFilter sourceMeshFilter in sourceMeshFilters)
        {
            MeshRenderer sourceRenderer = sourceMeshFilter.GetComponent<MeshRenderer>();

            if (sourceRenderer == null)
            {
                skippedMeshes++;
                Plugin.Log.LogInfo(
                    $"[PropClone] Skipping MeshFilter without MeshRenderer: '{GetPath(sourceMeshFilter.transform)}'");
                continue;
            }

            Mesh sourceMesh = sourceMeshFilter.sharedMesh;

            if (sourceMesh == null)
            {
                skippedMeshes++;
                Plugin.Log.LogInfo(
                    $"[PropClone] Skipping MeshFilter without sharedMesh: '{GetPath(sourceMeshFilter.transform)}'");
                continue;
            }

            if (!renderersToCopy.Contains(sourceRenderer))
            {
                skippedMeshes++;

                if (VerboseCloneLogging && loggedMeshes < MaxLoggedMeshesPerClone)
                {
                    loggedMeshes++;
                    Plugin.Log.LogInfo(
                        $"[PropClone:Mesh {loggedMeshes}] Skipping lower LOD renderer: '{GetPath(sourceMeshFilter.transform)}'"
                    );
                }

                continue;
            }

            if (!sourceToCloneTransform.TryGetValue(sourceMeshFilter.transform, out Transform cloneTransform) ||
                cloneTransform == null)
            {
                skippedMeshes++;
                Plugin.Log.LogWarning(
                    $"[PropClone] Skipping mesh because the clone transform was not created: '{GetPath(sourceMeshFilter.transform)}'"
                );
                continue;
            }

            Mesh meshToUse = sourceMesh;
            bool wasStaticCombined = IsBadStaticSceneMesh(sourceMesh);
            bool shouldUseNamedBoulderMesh = ShouldUseNamedBoulderMesh(sourceMeshFilter);

            if (wasStaticCombined || shouldUseNamedBoulderMesh)
            {
                Mesh replacement = FindReplacementMeshForStaticMesh(original, sourceMeshFilter);

                if (replacement == null)
                {
                    if (shouldUseNamedBoulderMesh && !wasStaticCombined)
                    {
                        Plugin.Log.LogWarning(
                            $"[PropClone] Could not find named boulder mesh replacement; keeping source mesh.\n" +
                            $"    sourcePath='{GetPath(sourceMeshFilter.transform)}'\n" +
                            $"    effectiveName='{GetEffectiveMeshObjectName(sourceMeshFilter)}'\n" +
                            $"    sourceMesh='{sourceMesh.name}'"
                        );
                    }
                    else
                    {
                        skippedMeshes++;

                        Plugin.Log.LogWarning(
                            $"[PropClone] Skipping static combined mesh because no replacement was found.\n" +
                            $"    original='{original.name}'\n" +
                            $"    sourcePath='{GetPath(sourceMeshFilter.transform)}'\n" +
                            $"    sourceMesh='{sourceMesh.name}'"
                        );

                        continue;
                    }
                }
                else
                {
                    meshToUse = replacement;
                    replacedStaticMeshes++;

                    Plugin.Log.LogInfo(
                        $"[PropClone] Replaced mesh.\n" +
                        $"    sourcePath='{GetPath(sourceMeshFilter.transform)}'\n" +
                        $"    sourceMesh='{sourceMesh.name}', staticCombined={wasStaticCombined}, namedBoulder={shouldUseNamedBoulderMesh}\n" +
                        $"    replacementMesh='{meshToUse.name}'"
                    );
                }
            }

            RemoveExistingComponent<MeshFilter>(cloneTransform.gameObject);
            RemoveExistingComponent<MeshRenderer>(cloneTransform.gameObject);

            MeshFilter newMeshFilter = cloneTransform.gameObject.AddComponent<MeshFilter>();
            MeshRenderer newRenderer = cloneTransform.gameObject.AddComponent<MeshRenderer>();

            newMeshFilter.sharedMesh = meshToUse;
            newRenderer.sharedMaterials = meshToUse == sourceMesh
                ? sourceRenderer.sharedMaterials
                : MatchMaterialsToMesh(meshToUse, sourceRenderer.sharedMaterials);

            newRenderer.enabled = sourceRenderer.enabled;
            newRenderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
            newRenderer.receiveShadows = sourceRenderer.receiveShadows;
            newRenderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
            newRenderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
            newRenderer.allowOcclusionWhenDynamic = false;

            copiedMeshes++;

            if (VerboseCloneLogging && loggedMeshes < MaxLoggedMeshesPerClone)
            {
                loggedMeshes++;

                Bounds sourceBounds = sourceRenderer.bounds;

                Plugin.Log.LogInfo(
                    $"[PropClone:Mesh {loggedMeshes}] " +
                    $"sourcePath='{GetPath(sourceMeshFilter.transform)}'\n" +
                    $"    sourceMesh='{sourceMesh.name}', replacementMesh='{meshToUse.name}', staticCombined={wasStaticCombined}\n" +
                    $"    meshReadable={SafeIsReadable(meshToUse)}, vertexCount={SafeVertexCount(meshToUse)}, subMeshes={meshToUse.subMeshCount}\n" +
                    $"    sourceLocalPos={Fmt(sourceMeshFilter.transform.localPosition)}, sourceLocalRot={Fmt(sourceMeshFilter.transform.localRotation.eulerAngles)}, sourceLocalScale={Fmt(sourceMeshFilter.transform.localScale)}\n" +
                    $"    sourceBounds.center={Fmt(sourceBounds.center)}, sourceBounds.size={Fmt(sourceBounds.size)}\n" +
                    $"    cloneLocalPos={Fmt(cloneTransform.localPosition)}, cloneLocalRot={Fmt(cloneTransform.localRotation.eulerAngles)}, cloneLocalScale={Fmt(cloneTransform.localScale)}\n" +
                    $"    materials=[{string.Join(", ", newRenderer.sharedMaterials.Where(m => m != null).Select(m => m.name))}]"
                );
            }
        }

        if (sourceMeshFilters.Length > MaxLoggedMeshesPerClone)
        {
            Plugin.Log.LogInfo(
                $"[PropClone] Mesh log truncated. Logged {MaxLoggedMeshesPerClone}/{sourceMeshFilters.Length} meshes."
            );
        }

        Plugin.Log.LogInfo(
            $"[PropClone] Clone mesh copy complete for '{original.name}'. " +
            $"copied={copiedMeshes}, replacedStaticMeshes={replacedStaticMeshes}, skipped={skippedMeshes}, cloneRootPos={Fmt(root.transform.position)}"
        );

        CopyParticleSystems(original, root);

        if (copyColliders)
        {
            CopyColliders(original, root, sourceToCloneTransform);
        }
        else
        {
            Plugin.Log.LogInfo(
                $"[PropClone] Skipping collider copy for '{original.name}' because this is the local player's disguise."
            );
        }

        EnsureVisible(root);

        if (copiedMeshes == 0)
        {
            Plugin.Log.LogWarning(
                $"[PropClone] Clone for '{original.name}' has no usable meshes. Destroying empty clone.");
            Destroy(root);
            return null;
        }

        return root;
    }

    private GameObject ResolveSelectedObject(RaycastUtility.RaycastResultReceivedEventArgs args)
    {
        GameObject selectedObject = GetEventGameObject(args, "ClosestValidRootObject");
        Vector3? hitPoint = GetEventVector3(args, "HitPoint");

        if (selectedObject == null)
        {
            selectedObject = FindBestSceneObjectByName(args.ClosestValidRootObjectName, hitPoint);
        }

        string hitObjectName = GetEventString(args, "HitObjectName");
        GameObject hitObject = FindBestSceneObjectByName(hitObjectName, hitPoint, selectedObject);
        GameObject narrowedObject = ChooseCloneSource(selectedObject, hitObject);

        if (narrowedObject != null && narrowedObject != selectedObject)
        {
            Plugin.Log.LogInfo(
                $"[PropClone] Narrowed clone source from '{GetPath(selectedObject != null ? selectedObject.transform : null)}' " +
                $"to '{GetPath(narrowedObject.transform)}' using hit object '{GetPath(hitObject != null ? hitObject.transform : null)}'"
            );
        }

        return narrowedObject != null ? narrowedObject : selectedObject;
    }

    private GameObject ChooseCloneSource(GameObject selectedObject, GameObject hitObject)
    {
        if (selectedObject == null)
            return hitObject;

        if (hitObject == null || hitObject == selectedObject)
            return selectedObject;

        if (!IsSameOrChildOf(hitObject.transform, selectedObject.transform))
            return selectedObject;

        Transform comboRoot = FindNamedAncestor(hitObject.transform, "Combo", selectedObject.transform);

        if (comboRoot != null)
            return comboRoot.gameObject;

        if (!LooksLikeSceneCollection(selectedObject))
            return selectedObject;

        Transform directChild = FindDirectChildUnder(selectedObject.transform, hitObject.transform);

        return directChild != null ? directChild.gameObject : selectedObject;
    }

    private GameObject FindBestSceneObjectByName(string objectName, Vector3? hitPoint, GameObject preferredRoot = null)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            return null;

        List<GameObject> candidates = Resources.FindObjectsOfTypeAll<GameObject>()
            .Where(obj => obj != null)
            .Where(obj => obj.scene.IsValid())
            .Where(obj => obj.name.Equals(objectName, StringComparison.Ordinal))
            .ToList();

        if (candidates.Count == 0)
            return null;

        if (preferredRoot != null)
        {
            List<GameObject> descendants = candidates
                .Where(obj => IsSameOrChildOf(obj.transform, preferredRoot.transform))
                .ToList();

            if (descendants.Count > 0)
                candidates = descendants;
        }

        if (hitPoint.HasValue)
        {
            Vector3 point = hitPoint.Value;
            return candidates
                .OrderBy(obj => DistanceToObjectBoundsSqr(obj, point))
                .ThenBy(obj => GetPath(obj.transform).Length)
                .FirstOrDefault();
        }

        return candidates
            .OrderByDescending(obj => obj.activeInHierarchy)
            .ThenBy(obj => GetPath(obj.transform).Length)
            .FirstOrDefault();
    }

    private float DistanceToObjectBoundsSqr(GameObject obj, Vector3 point)
    {
        if (obj == null)
            return float.MaxValue;

        if (TryGetRenderableBounds(obj, out Bounds renderBounds))
            return (renderBounds.ClosestPoint(point) - point).sqrMagnitude;

        Collider[] colliders = obj.GetComponentsInChildren<Collider>(true);

        bool hasBounds = false;
        Bounds colliderBounds = default;

        foreach (Collider collider in colliders)
        {
            if (collider == null)
                continue;

            if (!hasBounds)
            {
                colliderBounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                colliderBounds.Encapsulate(collider.bounds);
            }
        }

        if (hasBounds)
            return (colliderBounds.ClosestPoint(point) - point).sqrMagnitude;

        return (obj.transform.position - point).sqrMagnitude;
    }

    private bool LooksLikeSceneCollection(GameObject obj)
    {
        if (obj == null)
            return false;

        if (obj.name.Contains("Combo", StringComparison.OrdinalIgnoreCase))
            return false;

        if (obj.GetComponent<Renderer>() != null ||
            obj.GetComponent<MeshFilter>() != null ||
            obj.GetComponent<Collider>() != null ||
            obj.GetComponent<LODGroup>() != null)
        {
            return false;
        }

        int directRenderableChildren = 0;

        foreach (Transform child in obj.transform)
        {
            if (child.GetComponentsInChildren<Renderer>(true).Length > 0)
                directRenderableChildren++;

            if (directRenderableChildren > 4)
                return true;
        }

        return false;
    }

    private Transform FindDirectChildUnder(Transform ancestor, Transform descendant)
    {
        if (ancestor == null || descendant == null)
            return null;

        Transform current = descendant;
        Transform child = null;

        while (current != null && current != ancestor)
        {
            child = current;
            current = current.parent;
        }

        return current == ancestor ? child : null;
    }

    private Transform FindNamedAncestor(Transform start, string namePart, Transform stopAt)
    {
        Transform current = start;

        while (current != null)
        {
            if (current.name.Contains(namePart, StringComparison.OrdinalIgnoreCase))
                return current;

            if (current == stopAt)
                break;

            current = current.parent;
        }

        return null;
    }

    private bool IsSameOrChildOf(Transform possibleChild, Transform possibleParent)
    {
        Transform current = possibleChild;

        while (current != null)
        {
            if (current == possibleParent)
                return true;

            current = current.parent;
        }

        return false;
    }

    private GameObject GetEventGameObject(object args, string propertyName)
    {
        object value = GetEventValue(args, propertyName);

        if (value is GameObject gameObject)
            return gameObject;

        if (value is Transform transform)
            return transform.gameObject;

        return null;
    }

    private string GetEventString(object args, string propertyName)
    {
        return GetEventValue(args, propertyName) as string;
    }

    private Vector3? GetEventVector3(object args, string propertyName)
    {
        object value = GetEventValue(args, propertyName);

        if (value is Vector3 vector)
            return vector;

        return null;
    }

    private object GetEventValue(object args, string propertyName)
    {
        if (args == null || string.IsNullOrWhiteSpace(propertyName))
            return null;

        Type type = args.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        PropertyInfo property = type.GetProperty(propertyName, flags);

        if (property != null)
            return property.GetValue(args, null);

        FieldInfo field = type.GetField(propertyName, flags);

        return field != null ? field.GetValue(args) : null;
    }

    private HashSet<MeshRenderer> GetHighestDetailMeshRenderers(GameObject original)
    {
        HashSet<MeshRenderer> renderersToCopy = new HashSet<MeshRenderer>(
            original.GetComponentsInChildren<MeshRenderer>(true)
        );

        foreach (LODGroup lodGroup in original.GetComponentsInChildren<LODGroup>(true))
        {
            LOD[] lods = lodGroup.GetLODs();

            foreach (LOD lod in lods)
            {
                foreach (Renderer renderer in lod.renderers)
                {
                    if (renderer is MeshRenderer meshRenderer)
                        renderersToCopy.Remove(meshRenderer);
                }
            }

            if (lods.Length == 0)
                continue;

            foreach (Renderer renderer in lods[0].renderers)
            {
                if (renderer is MeshRenderer meshRenderer)
                    renderersToCopy.Add(meshRenderer);
            }
        }

        return renderersToCopy;
    }

    private Dictionary<Transform, Transform> BuildCloneTransformHierarchy(GameObject original, GameObject cloneRoot)
    {
        Dictionary<Transform, Transform> map = new Dictionary<Transform, Transform>();
        map[original.transform] = cloneRoot.transform;

        Transform[] sourceTransforms = original.GetComponentsInChildren<Transform>(true);

        foreach (Transform source in sourceTransforms.OrderBy(t => GetPath(t).Length))
        {
            if (source == null)
                continue;

            if (source == original.transform)
                continue;

            Transform sourceParent = source.parent;

            if (sourceParent == null)
                continue;

            if (!map.TryGetValue(sourceParent, out Transform cloneParent) || cloneParent == null)
                continue;

            GameObject cloneNode = new GameObject(source.gameObject.name);
            cloneNode.isStatic = false;
            cloneNode.layer = source.gameObject.layer;
            cloneNode.tag = source.gameObject.tag;

            cloneNode.transform.SetParent(cloneParent, false);
            cloneNode.transform.localPosition = source.localPosition;
            cloneNode.transform.localRotation = source.localRotation;
            cloneNode.transform.localScale = source.localScale;

            map[source] = cloneNode.transform;
        }

        return map;
    }

    private void RemoveExistingComponent<T>(GameObject obj) where T : Component
    {
        if (obj == null)
            return;

        T existing = obj.GetComponent<T>();

        if (existing != null)
        {
            Destroy(existing);
        }
    }

    private Mesh FindReplacementMeshForStaticMesh(GameObject original, MeshFilter sourceMeshFilter)
    {
        if (BundledMeshesByName == null || BundledMeshesByName.Count == 0)
            return null;

        string originalName = CleanName(original.name);
        string childName = CleanName(sourceMeshFilter.gameObject.name);
        string effectiveChildName = GetEffectiveMeshObjectName(sourceMeshFilter);

        List<string> candidates = BuildStrictStaticMeshCandidates(originalName, effectiveChildName, childName);

        Plugin.Log.LogInfo(
            $"[PropClone] Strict static mesh candidates:\n" +
            $"    original='{original.name}' cleanOriginal='{originalName}'\n" +
            $"    child='{sourceMeshFilter.gameObject.name}' cleanChild='{childName}' effectiveChild='{effectiveChildName}'\n" +
            $"    candidates=[{string.Join(", ", candidates)}]"
        );

        foreach (string candidate in candidates)
        {
            Mesh exact = FindBundledMeshExact(candidate);

            if (exact != null)
            {
                Plugin.Log.LogInfo($"[PropClone] Exact bundled mesh match: '{candidate}' -> '{exact.name}'");
                return exact;
            }
        }

        foreach (string candidate in candidates)
        {
            Mesh normalized = FindBundledMeshNormalized(candidate);

            if (normalized != null)
            {
                Plugin.Log.LogInfo($"[PropClone] Normalized bundled mesh match: '{candidate}' -> '{normalized.name}'");
                return normalized;
            }
        }

        Plugin.Log.LogWarning(
            $"[PropClone] Could not find strict bundled replacement mesh.\n" +
            $"    original='{original.name}' cleanOriginal='{originalName}'\n" +
            $"    child='{sourceMeshFilter.gameObject.name}' cleanChild='{childName}' effectiveChild='{effectiveChildName}'\n" +
            $"    tried=[{string.Join(", ", candidates)}]"
        );

        LogLikelyBundledMeshes(originalName, effectiveChildName);

        return null;
    }

    private string GetEffectiveMeshObjectName(MeshFilter sourceMeshFilter)
    {
        if (sourceMeshFilter == null)
            return string.Empty;

        Transform transform = sourceMeshFilter.transform;

        if (IsBoulderName(transform.name) &&
            transform.parent != null &&
            transform.parent.name.Equals("Meshes", StringComparison.OrdinalIgnoreCase) &&
            transform.parent.parent != null &&
            IsBoulderName(transform.parent.parent.name))
        {
            return CleanName(transform.parent.parent.name);
        }

        Transform current = transform.parent;

        while (current != null)
        {
            if (IsBoulderName(current.name))
                return CleanName(current.name);

            current = current.parent;
        }

        return CleanName(sourceMeshFilter.gameObject.name);
    }

    private bool IsBoulderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return CleanName(name).StartsWith("Boulder", StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldUseNamedBoulderMesh(MeshFilter sourceMeshFilter)
    {
        if (sourceMeshFilter == null)
            return false;

        return IsBoulderName(GetEffectiveMeshObjectName(sourceMeshFilter));
    }

    private List<string> BuildStrictStaticMeshCandidates(string originalName, string childName,
        string fallbackChildName = null)
    {
        List<string> candidates = new();

        string originalBase = RemoveInstanceSuffix(CleanName(originalName));
        string childBase = RemoveInstanceSuffix(CleanName(childName));
        string fallbackChildBase = RemoveInstanceSuffix(CleanName(fallbackChildName));
        bool preferUnpaddedChild = IsBoulderName(childBase);

        AddHighDetailNameFamily(candidates, childBase, preferUnpaddedChild);
        AddHighDetailNameFamily(candidates, RemoveLodSuffix(childBase), preferUnpaddedChild);

        if (!string.IsNullOrWhiteSpace(fallbackChildBase) &&
            !fallbackChildBase.Equals(childBase, StringComparison.OrdinalIgnoreCase))
        {
            AddHighDetailNameFamily(candidates, fallbackChildBase, IsBoulderName(fallbackChildBase));
            AddHighDetailNameFamily(candidates, RemoveLodSuffix(fallbackChildBase), IsBoulderName(fallbackChildBase));
        }

        if (!childBase.Equals(originalBase, StringComparison.OrdinalIgnoreCase))
        {
            AddHighDetailNameFamily(candidates, originalBase, IsBoulderName(originalBase));
            AddHighDetailNameFamily(candidates, RemoveLodSuffix(originalBase), IsBoulderName(originalBase));
        }

        return candidates;
    }

    private void AddHighDetailNameFamily(List<string> candidates, string baseName, bool preferUnpadded = false)
    {
        if (string.IsNullOrWhiteSpace(baseName))
            return;

        baseName = baseName.Trim();

        string noLod = RemoveLodSuffix(baseName);
        string unpadded = UnpadTrailingNumber(noLod.Replace(" ", "_"));

        if (preferUnpadded && !unpadded.Equals(noLod, StringComparison.OrdinalIgnoreCase))
        {
            AddCandidate(candidates, unpadded + "_LOD0");
            AddCandidate(candidates, unpadded + "_LOD0_0");
            AddCandidate(candidates, unpadded + "_LOD0_1");
            AddCandidate(candidates, unpadded);
        }

        AddCandidate(candidates, noLod + "_LOD0");
        AddCandidate(candidates, noLod + "_LOD0_0");
        AddCandidate(candidates, noLod + "_LOD0_1");
        AddCandidate(candidates, baseName);
        AddCandidate(candidates, noLod);

        string padded = PadTrailingNumber(noLod.Replace(" ", "_"));

        if (!padded.Equals(noLod, StringComparison.OrdinalIgnoreCase))
        {
            AddCandidate(candidates, padded + "_LOD0");
            AddCandidate(candidates, padded + "_LOD0_0");
            AddCandidate(candidates, padded + "_LOD0_1");
            AddCandidate(candidates, padded);
        }

        if (!preferUnpadded && !unpadded.Equals(noLod, StringComparison.OrdinalIgnoreCase))
        {
            AddCandidate(candidates, unpadded + "_LOD0");
            AddCandidate(candidates, unpadded + "_LOD0_0");
            AddCandidate(candidates, unpadded + "_LOD0_1");
            AddCandidate(candidates, unpadded);
        }
    }

    private string PadTrailingNumber(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;

        int lastSeparator = Math.Max(name.LastIndexOf('_'), name.LastIndexOf(' '));

        if (lastSeparator < 0 || lastSeparator >= name.Length - 1)
            return name;

        string prefix = name.Substring(0, lastSeparator + 1);
        string suffix = name.Substring(lastSeparator + 1);

        if (!int.TryParse(suffix, out int number))
            return name;

        return prefix + number.ToString("00");
    }

    private string UnpadTrailingNumber(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;

        int lastSeparator = Math.Max(name.LastIndexOf('_'), name.LastIndexOf(' '));

        if (lastSeparator < 0 || lastSeparator >= name.Length - 1)
            return name;

        string prefix = name.Substring(0, lastSeparator + 1);
        string suffix = name.Substring(lastSeparator + 1);

        if (!int.TryParse(suffix, out int number))
            return name;

        return prefix + number.ToString();
    }

    private Mesh FindBundledMeshExact(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        if (BundledMeshesByName.TryGetValue(candidate, out Mesh mesh) &&
            IsUsableReplacementMesh(mesh))
        {
            return mesh;
        }

        return null;
    }

    private Mesh FindBundledMeshNormalized(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        string normalizedCandidate = NormalizeMeshName(candidate);

        return BundledMeshesByName.Values
            .Where(IsUsableReplacementMesh)
            .OrderByDescending(m => ScoreMeshCandidate(m.name, candidate))
            .ThenByDescending(m => SafeVertexCount(m))
            .FirstOrDefault(m => NormalizeMeshName(m.name) == normalizedCandidate);
    }

    private Mesh FindBestFuzzyBundledMesh(string originalName, string childName)
    {
        string normalizedOriginal = NormalizeMeshName(originalName);
        string normalizedChild = NormalizeMeshName(childName);
        string normalizedChildNoLod = NormalizeMeshName(RemoveLodSuffix(childName));
        string normalizedOriginalNoLod = NormalizeMeshName(RemoveLodSuffix(originalName));

        return BundledMeshesByName.Values
            .Where(IsUsableReplacementMesh)
            .Select(mesh => new
            {
                Mesh = mesh,
                Score = ScoreFuzzyMeshMatch(
                    mesh.name,
                    normalizedOriginal,
                    normalizedOriginalNoLod,
                    normalizedChild,
                    normalizedChildNoLod
                )
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => SafeVertexCount(x.Mesh))
            .Select(x => x.Mesh)
            .FirstOrDefault();
    }

    private int ScoreFuzzyMeshMatch(
        string meshName,
        string normalizedOriginal,
        string normalizedOriginalNoLod,
        string normalizedChild,
        string normalizedChildNoLod
    )
    {
        string normalizedMesh = NormalizeMeshName(meshName);
        string normalizedMeshNoLod = NormalizeMeshName(RemoveLodSuffix(meshName));

        int score = 0;

        if (normalizedMesh == normalizedChild)
            score += 10000;

        if (normalizedMesh == normalizedOriginal)
            score += 9000;

        if (normalizedMesh == normalizedChildNoLod)
            score += 8000;

        if (normalizedMesh == normalizedOriginalNoLod)
            score += 7000;

        if (normalizedMeshNoLod == normalizedChildNoLod)
            score += 6000;

        if (normalizedMeshNoLod == normalizedOriginalNoLod)
            score += 5000;

        if (!string.IsNullOrWhiteSpace(normalizedChildNoLod) &&
            (normalizedMesh.Contains(normalizedChildNoLod) || normalizedChildNoLod.Contains(normalizedMesh)))
        {
            score += 3000;
        }

        if (!string.IsNullOrWhiteSpace(normalizedOriginalNoLod) &&
            (normalizedMesh.Contains(normalizedOriginalNoLod) || normalizedOriginalNoLod.Contains(normalizedMesh)))
        {
            score += 2000;
        }

        if (meshName.Contains("_LOD0", StringComparison.OrdinalIgnoreCase))
            score += 300;

        if (meshName.Contains("_LOD1", StringComparison.OrdinalIgnoreCase))
            score += 250;

        if (meshName.Contains("_LOD2", StringComparison.OrdinalIgnoreCase))
            score += 100;

        if (LooksLikeColliderMesh(meshName))
            score -= 100000;

        if (meshName.Contains("bounds", StringComparison.OrdinalIgnoreCase))
            score -= 100000;

        if (meshName.Contains("Spline", StringComparison.OrdinalIgnoreCase))
            score -= 50000;

        return score;
    }

    private int ScoreMeshCandidate(string meshName, string candidate)
    {
        return ScoreFuzzyMeshMatch(
            meshName,
            NormalizeMeshName(candidate),
            NormalizeMeshName(RemoveLodSuffix(candidate)),
            NormalizeMeshName(candidate),
            NormalizeMeshName(RemoveLodSuffix(candidate))
        );
    }

    private Material[] MatchMaterialsToMesh(Mesh mesh, Material[] sourceMaterials)
    {
        if (sourceMaterials == null || sourceMaterials.Length == 0)
            return sourceMaterials;

        if (mesh == null || mesh.subMeshCount <= 0)
            return sourceMaterials;

        Material[] validMaterials = sourceMaterials
            .Where(m => m != null)
            .ToArray();

        if (validMaterials.Length == 0)
            return sourceMaterials;

        Material[] expanded = new Material[mesh.subMeshCount];

        for (int i = 0; i < expanded.Length; i++)
        {
            expanded[i] = validMaterials[Mathf.Min(i, validMaterials.Length - 1)];
        }

        return expanded;
    }

    private bool IsUsableReplacementMesh(Mesh mesh)
    {
        if (mesh == null)
            return false;

        if (IsBadStaticSceneMesh(mesh))
            return false;

        if (LooksLikeColliderMesh(mesh.name))
            return false;

        if (mesh.name.Contains("bounds", StringComparison.OrdinalIgnoreCase))
            return false;

        if (mesh.name.Contains("Spline", StringComparison.OrdinalIgnoreCase))
            return false;

        if (SafeVertexCount(mesh) <= 0)
            return false;

        return true;
    }

    private bool IsBadStaticSceneMesh(Mesh mesh)
    {
        if (mesh == null)
            return false;

        string meshName = mesh.name;

        return meshName.Contains("Combined Mesh", StringComparison.OrdinalIgnoreCase) ||
               meshName.Contains("root: scene", StringComparison.OrdinalIgnoreCase);
    }

    private bool LooksLikeColliderMesh(string meshName)
    {
        if (string.IsNullOrWhiteSpace(meshName))
            return false;

        return meshName.Contains("collider", StringComparison.OrdinalIgnoreCase) ||
               meshName.Contains("collision", StringComparison.OrdinalIgnoreCase) ||
               meshName.EndsWith("_col", StringComparison.OrdinalIgnoreCase);
    }

    private string RemoveLodSuffix(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        int lodIndex = name.IndexOf("_LOD", StringComparison.OrdinalIgnoreCase);

        if (lodIndex >= 0)
            return name.Substring(0, lodIndex);

        return name;
    }

    private string RemoveInstanceSuffix(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        name = CleanName(name);

        int parenIndex = name.IndexOf(" (", StringComparison.Ordinal);

        if (parenIndex >= 0)
            name = name.Substring(0, parenIndex);

        return name.Trim();
    }

    private string NormalizeMeshName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        name = CleanName(name);

        if (name.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - ".asset".Length);

        int underscoreIndex = name.LastIndexOf('_');

        if (underscoreIndex >= 0 && underscoreIndex < name.Length - 1)
        {
            string suffix = name.Substring(underscoreIndex + 1);

            if (int.TryParse(suffix, out _))
                name = name.Substring(0, underscoreIndex);
        }

        return name
            .Replace(" ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("-", "", StringComparison.OrdinalIgnoreCase)
            .Replace("_", "", StringComparison.OrdinalIgnoreCase)
            .Replace(".", "", StringComparison.OrdinalIgnoreCase)
            .Trim()
            .ToLowerInvariant();
    }

    private void AddCandidate(List<string> candidates, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        value = value.Trim();

        if (!candidates.Any(c => c.Equals(value, StringComparison.OrdinalIgnoreCase)))
            candidates.Add(value);
    }

    private void LogLikelyBundledMeshes(string cleanOriginalName, string childName)
    {
        if (BundledMeshesByName == null || BundledMeshesByName.Count == 0)
            return;

        var matches = BundledMeshesByName.Values
            .Where(IsUsableReplacementMesh)
            .Select(mesh => new
            {
                Mesh = mesh,
                Score = ScoreFuzzyMeshMatch(
                    mesh.name,
                    NormalizeMeshName(cleanOriginalName),
                    NormalizeMeshName(RemoveLodSuffix(cleanOriginalName)),
                    NormalizeMeshName(childName),
                    NormalizeMeshName(RemoveLodSuffix(childName))
                )
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => SafeVertexCount(x.Mesh))
            .Take(75);

        Plugin.Log.LogInfo(
            "[PropClone] Likely bundled mesh candidates:\n" +
            string.Join(
                "\n",
                matches.Select(x =>
                    $"    score={x.Score}, mesh='{x.Mesh.name}', readable={SafeIsReadable(x.Mesh)}, verts={SafeVertexCount(x.Mesh)}, subMeshes={x.Mesh.subMeshCount}, bounds.center={Fmt(x.Mesh.bounds.center)}, bounds.size={Fmt(x.Mesh.bounds.size)}"
                )
            )
        );
    }

    private Vector3 DivideVector3(Vector3 a, Vector3 b)
    {
        return new Vector3(
            Mathf.Approximately(b.x, 0f) ? a.x : a.x / b.x,
            Mathf.Approximately(b.y, 0f) ? a.y : a.y / b.y,
            Mathf.Approximately(b.z, 0f) ? a.z : a.z / b.z
        );
    }

    private void CopyParticleSystems(GameObject original, GameObject root)
    {
        if (original == null || root == null)
            return;

        ParticleSystem[] sourceParticleSystems =
            original.GetComponentsInChildren<ParticleSystem>(true);

        Plugin.Log.LogInfo(
            $"[PropClone] Found {sourceParticleSystems.Length} ParticleSystem(s) under '{original.name}'");

        int loggedParticles = 0;

        foreach (ParticleSystem sourceParticleSystem in sourceParticleSystems)
        {
            GameObject particleObject = Instantiate(sourceParticleSystem.gameObject);

            particleObject.name = sourceParticleSystem.gameObject.name;
            particleObject.isStatic = false;

            Vector3 calculatedLocalPosition =
                original.transform.InverseTransformPoint(sourceParticleSystem.transform.position);

            Quaternion calculatedLocalRotation =
                Quaternion.Inverse(original.transform.rotation) * sourceParticleSystem.transform.rotation;

            Vector3 calculatedLocalScale =
                DivideVector3(sourceParticleSystem.transform.lossyScale, original.transform.lossyScale);

            particleObject.transform.SetParent(root.transform, false);

            particleObject.transform.localPosition = calculatedLocalPosition;
            particleObject.transform.localRotation = calculatedLocalRotation;
            particleObject.transform.localScale = calculatedLocalScale;

            foreach (Transform child in particleObject.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.isStatic = false;
            }

            ParticleSystem particleSystem = particleObject.GetComponent<ParticleSystem>();

            if (particleSystem != null)
            {
                particleSystem.Clear(true);
                particleSystem.Play(true);
            }

            if (VerboseCloneLogging && loggedParticles < MaxLoggedMeshesPerClone)
            {
                loggedParticles++;

                Plugin.Log.LogInfo(
                    $"[PropClone:Particle {loggedParticles}] " +
                    $"sourcePath='{GetPath(sourceParticleSystem.transform)}'\n" +
                    $"    sourceWorldPos={Fmt(sourceParticleSystem.transform.position)}, sourceLossyScale={Fmt(sourceParticleSystem.transform.lossyScale)}\n" +
                    $"    calculatedLocalPos={Fmt(calculatedLocalPosition)}, calculatedLocalScale={Fmt(calculatedLocalScale)}\n" +
                    $"    cloneWorldPosBeforeFinalMove={Fmt(particleObject.transform.position)}"
                );
            }
        }
    }

    private void CopyObjectAsChild(GameObject originalObject, GameObject targetParent)
    {
        if (originalObject == null || targetParent == null)
            return;

        Plugin.Log.LogInfo(
            $"[PropClone] CopyObjectAsChild original='{originalObject.name}' targetParent='{targetParent.name}'");

        LogObjectSummary("[PropClone] Before disguise original", originalObject);
        LogObjectSummary("[PropClone] Before disguise targetParent", targetParent);

        PlayerInfo targetPlayerInfo = targetParent.GetComponent<PlayerInfo>();

        if (targetPlayerInfo == null)
        {
            targetPlayerInfo = targetParent.GetComponentInParent<PlayerInfo>();
        }

        bool shouldCopyColliders = true;

        if (targetPlayerInfo != null && targetPlayerInfo.isLocalPlayer)
        {
            shouldCopyColliders = false;
        }

        GameObject clone = CreateMovableVisualClone(originalObject, shouldCopyColliders);

        if (clone == null)
            return;

        clone.name = originalObject.name + "_Disguise";
        MatchCloneVisualSizeToOriginal(originalObject, clone);

        LogObjectSummary("[PropClone] Disguise clone before parent", clone);

        foreach (Transform child in targetParent.transform)
        {
            GameObject childObject = child.gameObject;

            if (childObject.name.ToLower().Contains("disguise"))
            {
                Destroy(childObject);
                break;
            }
        }

        clone.transform.SetParent(targetParent.transform, false);

        EnsureVisible(clone);

        LogObjectSummary("[PropClone] Disguise clone after parent", clone);
        LogFirstRenderers("[PropClone] Disguise renderers after parent", clone);

        Plugin.Log.LogInfo(
            $"[PropClone] Created disguise from '{originalObject.name}' on '{targetParent.name}'. " +
            $"isLocalPlayer={(targetPlayerInfo != null ? targetPlayerInfo.isLocalPlayer.ToString() : "unknown")}, " +
            $"copiedColliders={shouldCopyColliders}"
        );

        PropHuntPlayer targetInfo = targetParent.GetComponent<PropHuntPlayer>();

        if (targetInfo)
        {
            targetInfo.SetCurrentDisguise(clone);
        }
    }

    private void CopyObjectAsDecoy(GameObject originalObject, Transform playerTransform)
    {
        if (originalObject == null || playerTransform == null)
            return;

        Plugin.Log.LogInfo(
            $"[PropClone] CopyObjectAtPositionAndFlatRotation original='{originalObject.name}' player='{playerTransform.name}'");
        LogObjectSummary("[PropClone] Before decoy original", originalObject);
        Plugin.Log.LogInfo(
            $"[PropClone] Player target pos={Fmt(playerTransform.position)}, rot={Fmt(playerTransform.rotation.eulerAngles)}, lossyScale={Fmt(playerTransform.lossyScale)}"
        );

        GameObject clone = CreateMovableVisualClone(originalObject);

        if (clone == null)
            return;

        clone.name = originalObject.name + "_Decoy";
        MatchCloneVisualSizeToOriginal(originalObject, clone);

        LogObjectSummary("[PropClone] Decoy clone before final move", clone);
        LogFirstRenderers("[PropClone] Decoy renderers before final move", clone);

        clone.transform.SetPositionAndRotation(
            playerTransform.position,
            Quaternion.Euler(0f, playerTransform.eulerAngles.y, 0f)
        );

        EnsureVisible(clone);

        LogObjectSummary("[PropClone] Decoy clone after final move", clone);
        LogFirstRenderers("[PropClone] Decoy renderers after final move", clone);

        Plugin.Log.LogInfo($"[PropClone] Created decoy from '{originalObject.name}' at '{playerTransform.position}'");
        PropHuntPlayer targetInfo = playerTransform.GetComponent<PropHuntPlayer>();
        if (targetInfo)
        {
            targetInfo.IncrementDecoyCount();
        }
    }

    private void EnsureVisible(GameObject obj)
    {
        if (obj == null)
            return;

        obj.SetActive(true);
        obj.isStatic = false;

        foreach (Transform child in obj.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.SetActive(true);
            child.gameObject.isStatic = false;
        }

        foreach (Renderer renderer in obj.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = true;
            renderer.allowOcclusionWhenDynamic = false;
        }

        foreach (LODGroup lodGroup in obj.GetComponentsInChildren<LODGroup>(true))
        {
            lodGroup.enabled = true;
        }
    }

    private string CleanName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        name = name.Replace("(Clone)", "").Trim();

        int parenIndex = name.IndexOf(" (", StringComparison.Ordinal);

        if (parenIndex >= 0)
            name = name.Substring(0, parenIndex);

        return name.Trim();
    }

    private void LogObjectSummary(string label, GameObject obj)
    {
        if (obj == null)
        {
            Plugin.Log.LogInfo($"{label}: null");
            return;
        }

        MeshFilter[] meshFilters = obj.GetComponentsInChildren<MeshFilter>(true);
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
        LODGroup[] lodGroups = obj.GetComponentsInChildren<LODGroup>(true);
        Collider[] colliders = obj.GetComponentsInChildren<Collider>(true);
        ParticleSystem[] particles = obj.GetComponentsInChildren<ParticleSystem>(true);

        Plugin.Log.LogInfo(
            $"{label}: path='{GetPath(obj.transform)}'\n" +
            $"    activeSelf={obj.activeSelf}, activeInHierarchy={obj.activeInHierarchy}, isStatic={obj.isStatic}\n" +
            $"    pos={Fmt(obj.transform.position)}, localPos={Fmt(obj.transform.localPosition)}\n" +
            $"    rot={Fmt(obj.transform.rotation.eulerAngles)}, localRot={Fmt(obj.transform.localRotation.eulerAngles)}\n" +
            $"    lossyScale={Fmt(obj.transform.lossyScale)}, localScale={Fmt(obj.transform.localScale)}\n" +
            $"    children={obj.transform.childCount}, meshFilters={meshFilters.Length}, renderers={renderers.Length}, lodGroups={lodGroups.Length}, colliders={colliders.Length}, particles={particles.Length}"
        );
    }

    private void LogFirstRenderers(string label, GameObject obj)
    {
        if (obj == null)
            return;

        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);

        Plugin.Log.LogInfo($"{label}: rendererCount={renderers.Length}");

        int count = Mathf.Min(renderers.Length, MaxLoggedMeshesPerClone);

        for (int i = 0; i < count; i++)
        {
            Renderer renderer = renderers[i];

            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;

            Plugin.Log.LogInfo(
                $"{label} [{i + 1}/{renderers.Length}] path='{GetPath(renderer.transform)}'\n" +
                $"    enabled={renderer.enabled}, activeInHierarchy={renderer.gameObject.activeInHierarchy}, isStatic={renderer.gameObject.isStatic}\n" +
                $"    rendererWorldPos={Fmt(renderer.transform.position)}, rendererLocalPos={Fmt(renderer.transform.localPosition)}\n" +
                $"    bounds.center={Fmt(renderer.bounds.center)}, bounds.size={Fmt(renderer.bounds.size)}\n" +
                $"    mesh='{(mesh != null ? mesh.name : "null")}', readable={(mesh != null ? SafeIsReadable(mesh).ToString() : "n/a")}, vertexCount={(mesh != null ? SafeVertexCount(mesh).ToString() : "n/a")}"
            );
        }

        if (renderers.Length > count)
        {
            Plugin.Log.LogInfo($"{label}: truncated renderer log {count}/{renderers.Length}");
        }
    }

    private void CopyColliders(
        GameObject original,
        GameObject root,
        Dictionary<Transform, Transform> sourceToCloneTransform = null
    )
    {
        if (original == null || root == null)
            return;

        Collider[] sourceColliders = original.GetComponentsInChildren<Collider>(true);

        Plugin.Log.LogInfo(
            $"[PropClone] Found {sourceColliders.Length} Collider(s) under '{original.name}'"
        );

        int copiedColliders = 0;
        int skippedColliders = 0;
        int loggedColliders = 0;

        foreach (Collider sourceCollider in sourceColliders)
        {
            if (sourceCollider == null)
            {
                skippedColliders++;
                continue;
            }

            GameObject colliderObject = null;
            Vector3 calculatedLocalPosition;
            Quaternion calculatedLocalRotation;
            Vector3 calculatedLocalScale;

            if (sourceToCloneTransform != null &&
                sourceToCloneTransform.TryGetValue(sourceCollider.transform, out Transform cloneTransform) &&
                cloneTransform != null)
            {
                colliderObject = cloneTransform.gameObject;
                calculatedLocalPosition = cloneTransform.localPosition;
                calculatedLocalRotation = cloneTransform.localRotation;
                calculatedLocalScale = cloneTransform.localScale;
            }
            else
            {
                colliderObject = new GameObject(sourceCollider.gameObject.name + "_Collider");
                colliderObject.isStatic = false;

                calculatedLocalPosition =
                    original.transform.InverseTransformPoint(sourceCollider.transform.position);

                calculatedLocalRotation =
                    Quaternion.Inverse(original.transform.rotation) * sourceCollider.transform.rotation;

                calculatedLocalScale =
                    DivideVector3(sourceCollider.transform.lossyScale, original.transform.lossyScale);

                colliderObject.transform.SetParent(root.transform, false);
                colliderObject.transform.localPosition = calculatedLocalPosition;
                colliderObject.transform.localRotation = calculatedLocalRotation;
                colliderObject.transform.localScale = calculatedLocalScale;
            }

            Collider newCollider = CopySingleCollider(sourceCollider, colliderObject);

            if (newCollider == null)
            {
                Destroy(colliderObject);
                skippedColliders++;
                continue;
            }

            newCollider.enabled = sourceCollider.enabled;
            newCollider.isTrigger = sourceCollider.isTrigger;
            newCollider.sharedMaterial = sourceCollider.sharedMaterial;

            copiedColliders++;

            if (VerboseCloneLogging && loggedColliders < MaxLoggedMeshesPerClone)
            {
                loggedColliders++;

                Plugin.Log.LogInfo(
                    $"[PropClone:Collider {loggedColliders}] " +
                    $"sourcePath='{GetPath(sourceCollider.transform)}'\n" +
                    $"    colliderType='{sourceCollider.GetType().Name}', enabled={sourceCollider.enabled}, isTrigger={sourceCollider.isTrigger}\n" +
                    $"    sourceWorldPos={Fmt(sourceCollider.transform.position)}, sourceWorldRot={Fmt(sourceCollider.transform.rotation.eulerAngles)}, sourceLossyScale={Fmt(sourceCollider.transform.lossyScale)}\n" +
                    $"    calculatedLocalPos={Fmt(calculatedLocalPosition)}, calculatedLocalRot={Fmt(calculatedLocalRotation.eulerAngles)}, calculatedLocalScale={Fmt(calculatedLocalScale)}\n" +
                    $"    cloneWorldPosBeforeFinalMove={Fmt(colliderObject.transform.position)}"
                );
            }
        }

        Plugin.Log.LogInfo(
            $"[PropClone] Collider copy complete for '{original.name}'. copied={copiedColliders}, skipped={skippedColliders}"
        );
    }

    private Collider CopySingleCollider(Collider sourceCollider, GameObject targetObject)
    {
        switch (sourceCollider)
        {
            case BoxCollider source:
            {
                BoxCollider copy = targetObject.AddComponent<BoxCollider>();
                copy.center = source.center;
                copy.size = source.size;
                return copy;
            }

            case SphereCollider source:
            {
                SphereCollider copy = targetObject.AddComponent<SphereCollider>();
                copy.center = source.center;
                copy.radius = source.radius;
                return copy;
            }

            case CapsuleCollider source:
            {
                CapsuleCollider copy = targetObject.AddComponent<CapsuleCollider>();
                copy.center = source.center;
                copy.radius = source.radius;
                copy.height = source.height;
                copy.direction = source.direction;
                return copy;
            }

            case MeshCollider source:
            {
                MeshCollider copy = targetObject.AddComponent<MeshCollider>();
                copy.sharedMesh = GetSafeColliderMesh(source);
                copy.convex = source.convex;
                copy.cookingOptions = source.cookingOptions;
                return copy;
            }

            case TerrainCollider source:
            {
                TerrainCollider copy = targetObject.AddComponent<TerrainCollider>();
                copy.terrainData = source.terrainData;
                return copy;
            }

            case WheelCollider source:
            {
                WheelCollider copy = targetObject.AddComponent<WheelCollider>();

                copy.center = source.center;
                copy.radius = source.radius;
                copy.suspensionDistance = source.suspensionDistance;
                copy.forceAppPointDistance = source.forceAppPointDistance;
                copy.mass = source.mass;
                copy.wheelDampingRate = source.wheelDampingRate;
                copy.suspensionSpring = source.suspensionSpring;
                copy.forwardFriction = source.forwardFriction;
                copy.sidewaysFriction = source.sidewaysFriction;

                return copy;
            }

            default:
            {
                Plugin.Log.LogWarning(
                    $"[PropClone] Unsupported collider type '{sourceCollider.GetType().Name}' at '{GetPath(sourceCollider.transform)}'"
                );

                return null;
            }
        }
    }

    private Mesh GetSafeColliderMesh(MeshCollider source)
    {
        if (source == null)
            return null;

        Mesh sourceMesh = source.sharedMesh;

        if (sourceMesh == null)
            return null;

        if (!IsBadStaticSceneMesh(sourceMesh))
            return sourceMesh;

        MeshFilter nearbyMeshFilter = source.GetComponent<MeshFilter>();

        if (nearbyMeshFilter != null && nearbyMeshFilter.sharedMesh != null)
            return nearbyMeshFilter.sharedMesh;

        return sourceMesh;
    }

    private string GetPath(Transform transform)
    {
        if (transform == null)
            return "null";

        string path = transform.name;
        Transform current = transform.parent;

        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }

    private string Fmt(Vector3 value)
    {
        return $"({value.x:F3}, {value.y:F3}, {value.z:F3})";
    }

    private bool SafeIsReadable(Mesh mesh)
    {
        if (mesh == null)
            return false;

        try
        {
            return mesh.isReadable;
        }
        catch
        {
            return false;
        }
    }

    private int SafeVertexCount(Mesh mesh)
    {
        if (mesh == null)
            return -1;

        try
        {
            return mesh.vertexCount;
        }
        catch
        {
            return -1;
        }
    }
}