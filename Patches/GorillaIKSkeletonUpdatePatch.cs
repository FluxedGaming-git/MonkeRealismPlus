using HarmonyLib;
using MonkeRealism;
using UnityEngine;

[HarmonyPatch(typeof(GorillaIK), "SkeletonUpdate")]
internal static class GorillaIKSkeletonUpdatePatch
{
    private static bool Prefix(GorillaIK __instance)
    {
        var plugin = Plugin.Instance;
        if (plugin == null || !plugin.ShouldUseElbowTracking.Value)
            return true; // Let original run

        // Force everything on
        __instance.usingUpdatedIK = true;
        __instance.canUseUpdatedIK = true;
        __instance.TickRunning = true;

        if (GorillaIKMgr.playerIK == __instance)
            GorillaIKMgr.playerIK.usingUpdatedIK = true;

        // Skip the subscription checks
        return false;
    }
}