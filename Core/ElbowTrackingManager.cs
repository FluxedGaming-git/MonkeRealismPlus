using System.Linq;
using UnityEngine;

namespace MonkeRealism.Core
{
    /// <summary>
    /// Manages optional left and right elbow tracker objects,
    /// mirroring the waist tracker pattern used by Plugin/TrackerManager.
    /// </summary>
    public static class ElbowTrackingManager
    {
        public static GameObject LeftElbowObject;
        public static GameObject RightElbowObject;

        /// <summary>
        /// Call this once after TrackerManager.Initialize() to set up elbow GameObjects
        /// parented under the VR rig so they move with the player.
        /// </summary>
        public static void Initialize(Transform rigRoot)
        {
            LeftElbowObject  = new GameObject("MonkeRealism_LeftElbow");
            RightElbowObject = new GameObject("MonkeRealism_RightElbow");

            LeftElbowObject.transform.SetParent(rigRoot, false);
            RightElbowObject.transform.SetParent(rigRoot, false);
        }

        /// <summary>
        /// Apply tracker rotations every frame.  Pass the tracker names from config.
        /// Safe to call when either name is empty or the tracker is not yet online.
        /// </summary>
        public static void ApplyRotations(string leftName, string rightName, Quaternion offset)
        {
            if (LeftElbowObject != null && !string.IsNullOrEmpty(leftName))
            {
                Quaternion? rot = TrackerManager.GetTrackerRotation(leftName);
                if (rot.HasValue)
                    LeftElbowObject.transform.localRotation = rot.Value * offset;
            }

            if (RightElbowObject != null && !string.IsNullOrEmpty(rightName))
            {
                Quaternion? rot = TrackerManager.GetTrackerRotation(rightName);
                if (rot.HasValue)
                    RightElbowObject.transform.localRotation = rot.Value * offset;
            }
        }
    }
}
