using GorillaTagScripts;
using HarmonyLib;
using MonkeRealism;
using System;

[HarmonyPatch(typeof(SubscriptionManager))]
internal static class SubscriptionManagerPatch
{
    [HarmonyPrefix]
    [HarmonyPatch("IsLocalSubscribed")]
    private static bool IsLocalSubscribed(ref bool __result)
    {
        var plugin = Plugin.Instance;
        if (plugin != null && plugin.ShouldUseElbowTracking.Value)
        {
            __result = true;
            return false;
        }
        return true;
    }

    [HarmonyPrefix]
    [HarmonyPatch("GetSubscriptionSettingBool")]
    private static bool GetSubscriptionSettingBool(ref bool __result, SubscriptionManager.SubscriptionFeatures feature)
    {
        var plugin = Plugin.Instance;
        if (plugin != null && plugin.ShouldUseElbowTracking.Value && feature == SubscriptionManager.SubscriptionFeatures.IOBT)
        {
            __result = true;
            return false;
        }
        return true;
    }

    [HarmonyPrefix]
    [HarmonyPatch("IsSubscriptionFeatureAvailable")]
    private static bool IsSubscriptionFeatureAvailable(ref bool __result, SubscriptionManager.SubscriptionFeatures feature)
    {
        var plugin = Plugin.Instance;
        if (plugin != null && plugin.ShouldUseElbowTracking.Value && feature == SubscriptionManager.SubscriptionFeatures.IOBT)
        {
            __result = true;
            return false;
        }
        return true;
    }

    [HarmonyPrefix]
    [HarmonyPatch("GetSubscriptionDetails", new[] { typeof(VRRig) })]
    private static bool GetSubscriptionDetails_VRRig(ref SubscriptionManager.SubscriptionDetails __result, VRRig rig)
    {
        var plugin = Plugin.Instance;
        if (plugin != null && plugin.ShouldUseElbowTracking.Value && rig == VRRig.LocalRig)
        {
            __result = new SubscriptionManager.SubscriptionDetails
            {
                active = true,
                tier = 1,
                daysAccrued = 0,
                autoRenew = false,
                autoRenewMonths = 0,
                subscriptionActiveUntilDate = DateTime.MaxValue
            };
            return false;
        }
        return true;
    }

    [HarmonyPrefix]
    [HarmonyPatch("GetSubscriptionDetails", new[] { typeof(NetPlayer) })]
    private static bool GetSubscriptionDetails_NetPlayer(ref SubscriptionManager.SubscriptionDetails __result, NetPlayer np)
    {
        var plugin = Plugin.Instance;
        if (plugin != null && plugin.ShouldUseElbowTracking.Value
            && VRRig.LocalRig != null && np == VRRig.LocalRig.creator)
        {
            __result = new SubscriptionManager.SubscriptionDetails
            {
                active = true,
                tier = 1,
                daysAccrued = 0,
                autoRenew = false,
                autoRenewMonths = 0,
                subscriptionActiveUntilDate = DateTime.MaxValue
            };
            return false;
        }
        return true;
    }
}