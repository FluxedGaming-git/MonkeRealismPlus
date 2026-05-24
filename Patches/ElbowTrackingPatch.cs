using HarmonyLib;
using MonkeRealism.Core;
using UnityEngine;

namespace MonkeRealism.Patches
{
    /// <summary>
    /// Hooks into VRRig.PostTick (local rig only) to apply elbow IK
    /// after the game's own rig update has run, preventing it from
    /// being overwritten this frame.
    /// </summary>
    [HarmonyPatch(typeof(VRRig), nameof(VRRig.PostTick))]
    internal static class ElbowTrackingPatch
    {
        private static void Postfix(VRRig __instance)
        {
            // Only process the local player's rig
            if (!__instance.isOfflineVRRig) return;

            var plugin = Plugin.Instance;
            if (plugin == null) return;
            if (!plugin.ShouldUseElbowTracking.Value) return;

            // If GorillaIK is still active on this rig, don't fight it
            var gorillaIK = __instance.GetComponent<GorillaIK>();
            if (gorillaIK != null && gorillaIK.enabled) return;

            ApplyElbowIK(__instance, plugin);
        }

        private static void ApplyElbowIK(VRRig rig, Plugin plugin)
        {
            var scaleFactor = rig.scaleFactor;

            // Body rotation — use the waist tracker's driven rotation as the body frame
            var bodyRot = plugin.TrackerFollower != null
                ? plugin.TrackerFollower.transform.rotation
                : rig.transform.rotation;

            // Chest tracker position/rotation for shoulder inference
            // MonkeRealism drives TrackerObject from the waist tracker, so we use that.
            // If you add a dedicated chest tracker later, substitute it here.
            Vector3? chestPos = plugin.TrackerObject != null
                ? (Vector3?)plugin.TrackerObject.transform.position : null;
            Quaternion? chestRot = plugin.TrackerObject != null
                ? (Quaternion?)plugin.TrackerObject.transform.rotation : null;

            SolveAndApplyArm(rig, plugin, scaleFactor, bodyRot, chestPos, chestRot, isLeft: true);
            SolveAndApplyArm(rig, plugin, scaleFactor, bodyRot, chestPos, chestRot, isLeft: false);
        }

        private static void SolveAndApplyArm(
            VRRig rig, Plugin plugin, float scaleFactor,
            Quaternion bodyRot, Vector3? chestPos, Quaternion? chestRot,
            bool isLeft)
        {
            // Pull tracker data from MonkeRealism's TrackerManager
            string trackerName = isLeft
                ? plugin.LeftElbowTrackerName.Value
                : plugin.RightElbowTrackerName.Value;

            if (string.IsNullOrEmpty(trackerName)) return;

            Quaternion? trackerRot = TrackerManager.GetTrackerRotation(trackerName);
            // Position lookup — add GetTrackerPosition() to TrackerManager if not present
            Vector3? trackerPos = TrackerManager.GetTrackerPosition(trackerName);

            // Hand position from the controller (already tracked by the game)
            var vrMap    = isLeft ? rig.leftHand : rig.rightHand;
            Vector3 hand = vrMap.overrideTarget != null
                ? vrMap.overrideTarget.position
                : vrMap.rigTarget != null
                    ? vrMap.rigTarget.position
                    : rig.transform.TransformPoint(vrMap.syncPos);

            var shoulder = ElbowIK.GetShoulderPosition(
                rig, bodyRot, isLeft, chestPos, chestRot, scaleFactor);

            // Arm length: configurable, scaled by rig scale factor
            var armLength = plugin.ElbowArmLength.Value * scaleFactor;

            ElbowIK.SolveArm(
                shoulder, hand, armLength,
                trackerPos, trackerRot,
                bodyRot, isLeft,
                out var upperArmRot,
                out var forearmRot);

            ApplyToBones(rig, upperArmRot, forearmRot, isLeft);
        }

        private static void ApplyToBones(VRRig rig, Quaternion upperArmRot, Quaternion forearmRot, bool isLeft)
        {
            // Bone paths confirmed from GorillaBody source
            var upperArm = rig.transform.Find(isLeft
                ? "rig/body/shoulder.L/upper_arm.L"
                : "rig/body/shoulder.R/upper_arm.R");

            if (upperArm == null) return;

            var forearm = upperArm.Find(isLeft ? "forearm.L" : "forearm.R");
            if (forearm == null) return;

            // BoneAlignOffset corrects for GT rig bones pointing along local X not Z
            upperArm.rotation = upperArmRot * ElbowIK.BoneAlignOffset;
            forearm.rotation  = forearmRot  * ElbowIK.BoneAlignOffset;
        }
    }
}