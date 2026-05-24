using GorillaTagScripts;
using HarmonyLib;
using MonkeRealism;

[HarmonyPatch(typeof(SubscriptionManager))]
internal static class SubscriptionManagerPatch
{
    // Force local player as subscribed
    [HarmonyPrefix]
    [HarmonyPatch("IsLocalSubscribed")]
    private static bool IsLocalSubscribed(ref bool __result)
    {
        var plugin = Plugin.Instance;
        if (plugin != null && plugin.ShouldUseElbowTracking.Value)
        {
            __result = true;
            return false; // Skip original method
        }
        return true;
    }

    // Force IOBT (Inside Out Body Tracking) setting to true
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

    // Allow IOBT on any headset (optional but helpful)
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
}