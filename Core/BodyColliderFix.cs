using System.Linq;
using UnityEngine;

namespace MonkeRealism.Core
{
    /// <summary>
    /// Keeps the player's bodyCollider rotation matched to the VR rig's "body"
    /// bone so bending over / lying down doesn't cause the collider to float.
    /// Call Refresh() every frame (cheap if already set up).
    /// </summary>
    public static class BodyColliderFix
    {
        private static TransformFollow _tf;

        public static void Refresh()
        {
            // Find the "body" bone on the offline VR rig
            Transform bodyTransform = GorillaTagger.Instance?.offlineVRRig?.transform
                .GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name.ToLower() == "body");

            if (bodyTransform == null) return;

            var bodyCollider = GorillaLocomotion.GTPlayer.Instance?.bodyCollider;
            if (bodyCollider == null) return;

            if (_tf == null)
            {
                // First time: add the component
                _tf = bodyCollider.AddComponent<TransformFollow>();
                _tf.rotationOnly  = true;
                _tf.transformToFollow = bodyTransform;
            }
            else
            {
                // Subsequent frames: just keep the target fresh
                // (handles respawn / rig reload edge cases)
                _tf.transformToFollow = bodyTransform;
            }
        }

        /// <summary>Call on mod disable / unload to clean up.</summary>
        public static void Cleanup()
        {
            if (_tf != null)
            {
                Object.Destroy(_tf);
                _tf = null;
            }
        }
    }
}
