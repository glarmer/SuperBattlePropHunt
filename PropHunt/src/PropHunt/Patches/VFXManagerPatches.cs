using HarmonyLib;
using UnityEngine;

namespace PropHunt.Patches;

public class VFXManagerPatches
{
    [HarmonyPatch(typeof(VfxManager), nameof(VfxManager.PlayDuelingPistolHitForAllClients))]
    [HarmonyPrefix]
    public static void PlayDuelingPistolHitForAllClients_Prefix(
        PlayerInventory shootingPlayer,
        VfxManager.GunShotHitVfxData hitData)
    {
        if (shootingPlayer == null) return;

        PlayerInfo shooter = shootingPlayer.PlayerInfo;
        string shooterName = shooter != null
            ? shooter.PlayerId.guid.ToString()
            : "unknown";

        Vector3 worldHitPoint = hitData.GetHitPoint();

        Hittable hitHittable = FindClosestHittableToPoint(worldHitPoint, 0.35f);

        if (hitHittable != null)
        {
            Plugin.Log.LogInfo(
                $"Dueling pistol hit hittable. " +
                $"Shooter={shooterName}, " +
                $"Hittable={hitHittable.name}, " +
                $"Object={hitHittable.gameObject.name}, " +
                $"Layer={LayerMask.LayerToName(hitHittable.gameObject.layer)}, " +
                $"Point={worldHitPoint}"
            );

            return;
        }

        Collider closestCollider = FindClosestColliderToPoint(worldHitPoint, 0.35f);

        if (closestCollider == null)
        {
            Plugin.Log.LogInfo(
                $"Dueling pistol hit non-hittable at {worldHitPoint}, but no nearby collider was found. " +
                $"Shooter={shooterName}"
            );

            return;
        }

        GameObject hitObject = closestCollider.gameObject;

        Plugin.Log.LogInfo(
            $"Dueling pistol hit non-hittable. " +
            $"Shooter={shooterName}, " +
            $"Object={hitObject.name}, " +
            $"Layer={LayerMask.LayerToName(hitObject.layer)}, " +
            $"Collider={closestCollider.GetType().Name}, " +
            $"Point={worldHitPoint}"
        );

        bool hasHitEnvironment = LayerMask.LayerToName(hitObject.layer).ToLower().Equals("environment")
                                 || LayerMask.LayerToName(hitObject.layer).ToLower().Equals("foliage");
        if (hasHitEnvironment)
        {
            PropHuntPlayer localPlayer = shooter.GetComponent<PropHuntPlayer>();
            localPlayer.ApplyIncorrectShotHealthPenalty();
        }
    }

    private static Collider FindClosestColliderToPoint(Vector3 point, float radius)
    {
        Collider[] colliders = Physics.OverlapSphere(
            point,
            radius,
            ~0,
            QueryTriggerInteraction.Ignore
        );

        Collider closestCollider = null;
        float closestDistance = float.MaxValue;

        foreach (Collider collider in colliders)
        {
            if (collider == null) continue;

            float distance = Vector3.Distance(
                collider.ClosestPoint(point),
                point
            );

            if (distance >= closestDistance) continue;

            closestDistance = distance;
            closestCollider = collider;
        }

        return closestCollider;
    }

    private static Hittable FindClosestHittableToPoint(Vector3 point, float radius)
    {
        Collider[] colliders = Physics.OverlapSphere(
            point,
            radius,
            ~0,
            QueryTriggerInteraction.Ignore
        );

        Hittable closestHittable = null;
        float closestDistance = float.MaxValue;

        foreach (Collider collider in colliders)
        {
            if (collider == null) continue;

            Hittable hittable = collider.GetComponentInParent<Hittable>();
            if (hittable == null) continue;

            float distance = Vector3.Distance(
                collider.ClosestPoint(point),
                point
            );

            if (distance >= closestDistance) continue;

            closestDistance = distance;
            closestHittable = hittable;
        }

        return closestHittable;
    }
}