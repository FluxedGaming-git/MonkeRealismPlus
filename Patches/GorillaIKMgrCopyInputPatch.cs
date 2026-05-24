using HarmonyLib;
using MonkeRealism;
using MonkeRealism.Core;
using UnityEngine;

[HarmonyPatch(typeof(GorillaIKMgr), "CopyInput")]
internal static class GorillaIKMgrCopyInputPatch
{
    private static void Prefix()
    {
        var plugin = Plugin.Instance;
        if (plugin == null || !plugin.ShouldUseElbowTracking.Value) return;

        // Find the local rig's GorillaIK
        var gorillaIK = VRRig.LocalRig?.GetComponent<GorillaIK>();
        if (gorillaIK == null) return;

        // Force the flag so CopyInput passes usingNewIK = true into the job
        gorillaIK.usingUpdatedIK = true;

        // Feed our solved elbow directions
        var scaleFactor = VRRig.LocalRig.scaleFactor;
        var bodyRot = plugin.TrackerFollower != null
            ? plugin.TrackerFollower.transform.rotation
            : VRRig.LocalRig.transform.rotation;

        Vector3? chestPos = plugin.TrackerObject != null
            ? (Vector3?)plugin.TrackerObject.transform.position : null;
        Quaternion? chestRot = plugin.TrackerObject != null
            ? (Quaternion?)plugin.TrackerObject.transform.rotation : null;

        FeedElbowDirection(gorillaIK, plugin, VRRig.LocalRig, scaleFactor, bodyRot, chestPos, chestRot, isLeft: true);
        FeedElbowDirection(gorillaIK, plugin, VRRig.LocalRig, scaleFactor, bodyRot, chestPos, chestRot, isLeft: false);
    }

    private static void FeedElbowDirection(
        GorillaIK gorillaIK, Plugin plugin, VRRig rig,
        float scaleFactor, Quaternion bodyRot,
        Vector3? chestPos, Quaternion? chestRot, bool isLeft)
    {
        string trackerName = isLeft
            ? plugin.LeftElbowTrackerName.Value
            : plugin.RightElbowTrackerName.Value;

        if (string.IsNullOrEmpty(trackerName)) return;

        Quaternion? trackerRot = TrackerManager.GetTrackerRotation(trackerName);
        Vector3? trackerPos = TrackerManager.GetTrackerPosition(trackerName);

        Quaternion elbowOffset = isLeft ? plugin.LeftElbowOffset : plugin.RightElbowOffset;
        if (trackerRot.HasValue)
            trackerRot = trackerRot.Value * elbowOffset;

        var vrMap = isLeft ? rig.leftHand : rig.rightHand;
        Vector3 hand = vrMap.overrideTarget != null
            ? vrMap.overrideTarget.position
            : vrMap.rigTarget != null
                ? vrMap.rigTarget.position
                : rig.transform.TransformPoint(vrMap.syncPos);

        var shoulder = ElbowIK.GetShoulderPosition(
            rig, bodyRot, isLeft, chestPos, chestRot, scaleFactor);

        var armLength = plugin.ElbowArmLength.Value * scaleFactor;

        ElbowIK.SolveArm(
            shoulder, hand, armLength,
            trackerPos, trackerRot,
            bodyRot, isLeft,
            out var upperArmRot,
            out _); // forearm rotation not needed here

        // Reconstruct elbow world position from solved upper arm
        var elbowWorldPos = shoulder + (upperArmRot * Vector3.forward) * (armLength * ElbowIK.UpperArmRatio);

        // Convert to the local direction space GorillaIKMgr.CopyInput expects:
        // it reads elbowDir relative to the shoulder parent transform
        var shoulderParent = isLeft
            ? gorillaIK.leftUpperArm?.parent
            : gorillaIK.rightUpperArm?.parent;

        if (shoulderParent == null) return;

        var elbowLocalDir = shoulderParent.InverseTransformDirection(
            (elbowWorldPos - hand).normalized);

        if (isLeft)
        {
            gorillaIK.leftElbowDirection = elbowLocalDir;
            gorillaIK.lerpLeftElbowDirection = elbowLocalDir;
        }
        else
        {
            gorillaIK.rightElbowDirection = elbowLocalDir;
            gorillaIK.lerpRightElbowDirection = elbowLocalDir;
        }
    }
}