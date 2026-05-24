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

        var rig = VRRig.LocalRig;
        if (rig == null) return;

        var gorillaIK = rig.GetComponent<GorillaIK>();
        if (gorillaIK == null) return;

        // === FORCE THE IK MODE ===
        gorillaIK.usingUpdatedIK = true;
        gorillaIK.canUseUpdatedIK = true;

        if (GorillaIKMgr.playerIK == gorillaIK)
            GorillaIKMgr.playerIK.usingUpdatedIK = true;

        var scale = rig.scaleFactor;
        var bodyRot = plugin.TrackerFollower?.transform.rotation ?? rig.transform.rotation;

        Vector3? chestPos = plugin.TrackerObject?.transform.position;
        Quaternion? chestRot = plugin.TrackerObject?.transform.rotation;

        FeedElbow(gorillaIK, plugin, rig, scale, bodyRot, chestPos, chestRot, true);
        FeedElbow(gorillaIK, plugin, rig, scale, bodyRot, chestPos, chestRot, false);
    }

    private static void FeedElbow(GorillaIK gorillaIK, Plugin plugin, VRRig rig,
    float scale, Quaternion bodyRot, Vector3? chestPos, Quaternion? chestRot, bool isLeft)
    {
        string trackerName = isLeft ? plugin.LeftElbowTrackerName.Value : plugin.RightElbowTrackerName.Value;
        if (string.IsNullOrEmpty(trackerName)) return;

        var trackerRot = TrackerManager.GetTrackerRotation(trackerName);
        if (!trackerRot.HasValue) return;

        // Apply user offset
        trackerRot = trackerRot.Value * (isLeft ? plugin.LeftElbowOffset : plugin.RightElbowOffset);

        // Get hand position
        Vector3 handPos = GetHandWorldPosition(rig, isLeft);

        // Get shoulder parent for space conversion
        var shoulderParent = isLeft
            ? gorillaIK.leftUpperArm?.parent
            : gorillaIK.rightUpperArm?.parent;

        if (shoulderParent == null) return;

        // Use tracker's downward axis as the elbow hint direction
        // Try Vector3.down, Vector3.forward, Vector3.right — we'll start with down
        Vector3 trackerDown = trackerRot.Value * Vector3.down;

        // Convert to shoulder-parent local space
        Vector3 elbowDir = shoulderParent.InverseTransformDirection(trackerDown);

        if (isLeft)
        {
            gorillaIK.leftElbowDirection = elbowDir;
            gorillaIK.lerpLeftElbowDirection = elbowDir;
        }
        else
        {
            gorillaIK.rightElbowDirection = elbowDir;
            gorillaIK.lerpRightElbowDirection = elbowDir;
        }
    }

    private static Vector3 GetHandWorldPosition(VRRig rig, bool isLeft)
    {
        var hand = isLeft ? rig.leftHand : rig.rightHand;
        return hand.overrideTarget != null ? hand.overrideTarget.position :
               hand.rigTarget != null ? hand.rigTarget.position :
               rig.transform.TransformPoint(hand.syncPos);
    }

    // Simple but effective elbow direction solver
    private static Vector3 CalculateElbowDirection(Vector3 shoulder, Vector3 hand, Quaternion trackerRot)
    {
        Vector3 armVector = hand - shoulder;
        float armLength = armVector.magnitude;

        if (armLength < 0.01f) return Vector3.forward;

        Vector3 midPoint = shoulder + armVector * 0.5f;

        // Pull elbow slightly toward tracker forward direction
        Vector3 trackerForward = trackerRot * Vector3.forward;
        Vector3 elbowTarget = midPoint + trackerForward * (armLength * 0.35f);

        Vector3 elbowDir = (elbowTarget - shoulder).normalized;

        return elbowDir;
    }
}