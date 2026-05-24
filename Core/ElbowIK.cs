using UnityEngine;

namespace MonkeRealism.Core
{
    /// <summary>
    /// Two-bone IK solver for elbow tracking.
    /// Inputs: shoulder position (inferred), hand position (from controller),
    /// elbow tracker world position + rotation (from SteamVR tracker).
    /// Outputs: world-space rotations for upper_arm and forearm bones.
    /// </summary>
    public static class ElbowIK
    {
        // Arm segment ratios — upper arm is slightly shorter than forearm
        public const float UpperArmRatio      = 0.48f;
        private const float ForearmRatio        = 0.52f;
        // How much the physical tracker position pulls the IK elbow vs the pure math solution
        private const float TrackerDirectWeight = 0.85f;
        // How much the tracker's roll axis twists the forearm
        private const float TwistInfluence      = 0.7f;

        // Shoulder geometry offsets relative to chest/head origin
        public const float ShoulderWidth  = 0.15f;  // half-width from spine to shoulder
        public const float ShoulderDrop   = 0.05f;  // below head origin when no chest tracker
        public const float ShoulderRaise  = 0.10f;  // above chest tracker position

        // Bone alignment offset — Gorilla Tag's rig bones point along local X, not Z
        public static readonly Quaternion BoneAlignOffset = Quaternion.Euler(0f, -90f, 0f);

        // ─────────────────────────────────────────────────────────────────────
        // PUBLIC ENTRY POINT
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Infer shoulder world position from the rig and current body rotation.
        /// If a chest tracker is available pass its world position and rotation;
        /// otherwise pass null and the head position is used as fallback.
        /// </summary>
        public static Vector3 GetShoulderPosition(
            VRRig rig,
            Quaternion bodyRotation,
            bool isLeft,
            Vector3? chestTrackerPos,
            Quaternion? chestTrackerRot,
            float scaleFactor)
        {
            var shoulderDir = isLeft ? Vector3.left : Vector3.right;

            if (chestTrackerPos.HasValue && chestTrackerRot.HasValue)
            {
                return chestTrackerPos.Value
                     + chestTrackerRot.Value * shoulderDir * (ShoulderWidth * scaleFactor)
                     + Vector3.up * (ShoulderRaise * scaleFactor);
            }

            // Fallback: derive from head position
            var headPos = rig.head.rigTarget != null
                ? rig.head.rigTarget.position
                : rig.transform.position + Vector3.up * (0.5f * scaleFactor);

            return headPos
                 + bodyRotation * shoulderDir * (ShoulderWidth * scaleFactor)
                 + Vector3.down * (ShoulderDrop * scaleFactor);
        }

        /// <summary>
        /// Solve the full arm chain. Returns upper-arm and forearm world rotations
        /// ready to be stamped onto the rig bones (with BoneAlignOffset applied).
        /// </summary>
        public static void SolveArm(
            Vector3 shoulderPos,
            Vector3 handPos,
            float armLength,
            Vector3? trackerPosition,
            Quaternion? trackerRotation,
            Quaternion bodyRotation,
            bool isLeft,
            out Quaternion upperArmRot,
            out Quaternion forearmRot)
        {
            var elbowPos = SolveElbowPosition(
                shoulderPos, handPos, armLength,
                trackerPosition, trackerRotation, isLeft);

            upperArmRot = SolveUpperArmRotation(shoulderPos, elbowPos, handPos, bodyRotation);
            forearmRot  = SolveForearmRotation(elbowPos, handPos, shoulderPos, trackerRotation);
        }

        // ─────────────────────────────────────────────────────────────────────
        // ELBOW POSITION SOLVER
        // ─────────────────────────────────────────────────────────────────────

        private static Vector3 SolveElbowPosition(
            Vector3 shoulder,
            Vector3 hand,
            float totalArmLength,
            Vector3? trackerPosition,
            Quaternion? trackerRotation,
            bool isLeft)
        {
            var shoulderToHand = hand - shoulder;
            var distance       = shoulderToHand.magnitude;
            var upperLen       = totalArmLength * UpperArmRatio;
            var foreLen        = totalArmLength * ForearmRatio;
            var maxReach       = upperLen + foreLen;

            // Arm fully extended — elbow lies on the shoulder-to-hand line
            if (distance >= maxReach * 0.999f)
                return shoulder + shoulderToHand.normalized * upperLen;

            // Hand too close to shoulder — push elbow out sideways
            if (distance < 0.02f)
            {
                var outDir = isLeft ? Vector3.left : Vector3.right;
                return shoulder + (outDir + Vector3.down * 0.3f).normalized * (upperLen * 0.5f);
            }

            // Pure analytical two-bone IK with natural pole vector
            var ikElbow = SolveTwoBoneIK(shoulder, hand, upperLen, foreLen, distance, isLeft);

            // If no tracker, return pure IK result
            if (!trackerPosition.HasValue)
                return ikElbow;

            // Constrain tracker position to the valid elbow arc
            var trackerElbow = ConstrainElbowToArc(
                shoulder, hand, trackerPosition.Value, upperLen, foreLen, distance);

            // Optionally nudge constrained position using tracker forward direction
            if (trackerRotation.HasValue)
            {
                var trackerForward = trackerRotation.Value * Vector3.forward;
                var forearmDir     = (hand - trackerElbow).normalized;
                var misalignment   = Vector3.Dot(trackerForward, forearmDir);

                if (Mathf.Abs(misalignment) < 0.95f)
                {
                    var correctionDir = Vector3.Cross(forearmDir, trackerForward).normalized;
                    var correction    = correctionDir * (upperLen * 0.15f * (1f - Mathf.Abs(misalignment)));
                    trackerElbow     += correction;
                    // Re-constrain after nudge
                    trackerElbow = ConstrainElbowToArc(
                        shoulder, hand, trackerElbow, upperLen, foreLen, distance);
                }
            }

            // Blend analytical IK with tracker-driven result
            return Vector3.Lerp(ikElbow, trackerElbow, TrackerDirectWeight);
        }

        /// <summary>
        /// Project desiredElbow onto the circle of valid elbow positions
        /// defined by the triangle shoulder–elbow–hand with fixed side lengths.
        /// </summary>
        private static Vector3 ConstrainElbowToArc(
            Vector3 shoulder, Vector3 hand, Vector3 desiredElbow,
            float upperLen, float foreLen, float shoulderHandDist)
        {
            var axis      = (hand - shoulder).normalized;
            var cosAngle  = Mathf.Clamp(
                (upperLen * upperLen + shoulderHandDist * shoulderHandDist - foreLen * foreLen)
                / (2f * upperLen * shoulderHandDist), -1f, 1f);
            var projDist  = upperLen * cosAngle;
            var center    = shoulder + axis * projDist;
            var sinAngle  = Mathf.Sqrt(Mathf.Max(0f, 1f - cosAngle * cosAngle));
            var radius    = upperLen * sinAngle;

            if (radius < 0.001f) return center;

            var toDesired = desiredElbow - center;
            var onPlane   = toDesired - Vector3.Dot(toDesired, axis) * axis;

            if (onPlane.sqrMagnitude < 0.0001f)
            {
                onPlane = Vector3.Cross(axis, Vector3.up);
                if (onPlane.sqrMagnitude < 0.001f)
                    onPlane = Vector3.Cross(axis, Vector3.forward);
            }

            return center + onPlane.normalized * radius;
        }

        /// <summary>
        /// Standard analytical two-bone IK using a natural pole vector
        /// that mimics how a human arm bends at rest.
        /// </summary>
        private static Vector3 SolveTwoBoneIK(
            Vector3 shoulder, Vector3 hand,
            float upperLen, float foreLen, float distance, bool isLeft)
        {
            var cosAngle = Mathf.Clamp(
                (upperLen * upperLen + distance * distance - foreLen * foreLen)
                / (2f * upperLen * distance), -1f, 1f);
            var angle    = Mathf.Acos(cosAngle);

            var forward  = (hand - shoulder).normalized;
            var pole     = BuildNaturalPoleVector(forward, isLeft);

            var planeNormal = Vector3.Cross(forward, pole).normalized;
            if (planeNormal.sqrMagnitude < 0.001f)
                planeNormal = Vector3.Cross(forward, Vector3.up).normalized;
            if (planeNormal.sqrMagnitude < 0.001f)
                planeNormal = Vector3.Cross(forward, Vector3.forward).normalized;

            var elbowDir = Quaternion.AngleAxis(-angle * Mathf.Rad2Deg, planeNormal) * forward;
            return shoulder + elbowDir * upperLen;
        }

        /// <summary>
        /// Builds a pole vector that produces natural-looking elbow bend:
        /// elbows point backward-and-down when arms are at rest, forward
        /// when reaching up, outward when reaching to the side.
        /// </summary>
        private static Vector3 BuildNaturalPoleVector(Vector3 armForward, bool isLeft)
        {
            var side         = isLeft ? -1f : 1f;
            var armUp        = Vector3.Dot(armForward, Vector3.up);
            var downBias     = Mathf.Lerp(0.3f, 0.8f, Mathf.Clamp01(armUp + 0.5f));
            var backBias     = Mathf.Lerp(0.6f, 0.1f, Mathf.Clamp01(armUp + 0.5f));
            var forwardReach = Mathf.Clamp01(Vector3.Dot(armForward, Vector3.forward));
            var sideBias     = Mathf.Lerp(0.2f, 0.5f, forwardReach);

            var pole         = Vector3.down * downBias
                             + -Vector3.forward * backBias
                             + new Vector3(side * sideBias, 0f, 0f);

            var perp = Vector3.Cross(armForward, pole.normalized);
            if (perp.sqrMagnitude < 0.001f)
                perp = Vector3.Cross(armForward, Vector3.up);

            return Vector3.Cross(perp, armForward).normalized;
        }

        // ─────────────────────────────────────────────────────────────────────
        // BONE ROTATION SOLVERS
        // ─────────────────────────────────────────────────────────────────────

        private static Quaternion SolveUpperArmRotation(
            Vector3 shoulder, Vector3 elbow, Vector3 hand, Quaternion bodyRotation)
        {
            var upperArmDir = elbow - shoulder;
            if (upperArmDir.sqrMagnitude < 0.0001f) return bodyRotation;
            upperArmDir.Normalize();

            var forearmDir  = (hand - elbow).normalized;
            var bendNormal  = Vector3.Cross(upperArmDir, forearmDir).normalized;

            if (bendNormal.sqrMagnitude < 0.001f)
                bendNormal = Vector3.Cross(upperArmDir, bodyRotation * Vector3.up).normalized;
            if (bendNormal.sqrMagnitude < 0.001f)
                bendNormal = Vector3.Cross(upperArmDir, Vector3.up).normalized;

            var upperArmUp  = Vector3.Cross(bendNormal, upperArmDir).normalized;
            if (upperArmUp.sqrMagnitude < 0.001f)
                upperArmUp = bodyRotation * Vector3.up;

            return Quaternion.LookRotation(upperArmDir, upperArmUp);
        }

        private static Quaternion SolveForearmRotation(
            Vector3 elbow, Vector3 hand, Vector3 shoulder, Quaternion? trackerRotation)
        {
            var forearmDir = hand - elbow;
            if (forearmDir.sqrMagnitude < 0.0001f) return Quaternion.identity;
            forearmDir.Normalize();

            var upperArmDir = (elbow - shoulder).normalized;
            var bendNormal  = Vector3.Cross(upperArmDir, forearmDir).normalized;
            var elbowUp     = Vector3.Cross(forearmDir, bendNormal).normalized;
            if (elbowUp.sqrMagnitude < 0.001f) elbowUp = Vector3.up;

            var baseRot = Quaternion.LookRotation(forearmDir, elbowUp);

            if (!trackerRotation.HasValue) return baseRot;

            // Use tracker roll to twist the forearm (pronation / supination)
            var trackerUp    = trackerRotation.Value * Vector3.up;
            var projectedUp  = Vector3.ProjectOnPlane(trackerUp, forearmDir);
            if (projectedUp.sqrMagnitude < 0.001f) return baseRot;
            projectedUp.Normalize();

            var twistedRot = Quaternion.LookRotation(forearmDir, projectedUp);
            return Quaternion.Slerp(baseRot, twistedRot, TwistInfluence);
        }
    }
}