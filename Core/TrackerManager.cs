using System.Collections.Generic;
using System.Text;
using OVR.OpenVR;
using UnityEngine;

namespace MonkeRealism
{
    public static class TrackerManager
    {
        private static bool isInitialized;

        public static void Initialize()
        {
            EVRInitError error = EVRInitError.None;

            OpenVR.Init(
                    ref error,
                    EVRApplicationType.VRApplication_Background
            );

            isInitialized =
                    error         == EVRInitError.None &&
                    OpenVR.System != null;
        }

        public static List<string> GetTrackers()
        {
            List<string> trackers = new List<string>();

            if (!isInitialized || OpenVR.System == null)
                return trackers;

            TrackedDevicePose_t[] poses =
                    new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];

            OpenVR.System.GetDeviceToAbsoluteTrackingPose(
                    ETrackingUniverseOrigin.TrackingUniverseStanding,
                    0,
                    poses
            );

            for (uint i = 0; i < poses.Length; i++)
            {
                if (!poses[i].bDeviceIsConnected || !poses[i].bPoseIsValid)
                    continue;

                if (OpenVR.System.GetTrackedDeviceClass(i) !=
                    ETrackedDeviceClass.GenericTracker)
                    continue;

                string serial = GetDeviceSerial(i);

                if (!string.IsNullOrEmpty(serial))
                    trackers.Add(serial);
            }

            return trackers;
        }

        public static Quaternion? GetTrackerRotation(string trackerName)
        {
            if (!isInitialized || OpenVR.System == null)
                return null;

            TrackedDevicePose_t[] poses =
                    new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];

            OpenVR.System.GetDeviceToAbsoluteTrackingPose(
                    ETrackingUniverseOrigin.TrackingUniverseStanding,
                    0,
                    poses
            );

            for (uint i = 0; i < poses.Length; i++)
            {
                if (!poses[i].bDeviceIsConnected || !poses[i].bPoseIsValid)
                    continue;

                if (OpenVR.System.GetTrackedDeviceClass(i) !=
                    ETrackedDeviceClass.GenericTracker)
                    continue;

                string serial = GetDeviceSerial(i);

                if (string.IsNullOrEmpty(serial))
                    continue;

                if (!serial.ToLower().Contains(trackerName.ToLower()))
                    continue;

                Matrix4x4 matrix =
                        ConvertSteamVRMatrixToUnity(
                                poses[i].mDeviceToAbsoluteTracking
                        );

                Quaternion rotation = matrix.rotation;

                Vector3 euler = rotation.eulerAngles;

                rotation = Quaternion.Euler(
                        euler.z,
                        -euler.y,
                        euler.x
                );

                return rotation;
            }

            return null;
        }

        private static string GetDeviceSerial(uint i)
        {
            ETrackedPropertyError error =
                    ETrackedPropertyError.TrackedProp_Success;

            uint capacity =
                    OpenVR.System.GetStringTrackedDeviceProperty(
                            i,
                            ETrackedDeviceProperty.Prop_SerialNumber_String,
                            null,
                            0,
                            ref error
                    );

            if (capacity <= 1)
                return null;

            StringBuilder result =
                    new StringBuilder((int)capacity);

            OpenVR.System.GetStringTrackedDeviceProperty(
                    i,
                    ETrackedDeviceProperty.Prop_SerialNumber_String,
                    result,
                    capacity,
                    ref error
            );

            return result.ToString();
        }

        private static Matrix4x4 ConvertSteamVRMatrixToUnity(HmdMatrix34_t pose)
        {
            Matrix4x4 matrix = new Matrix4x4
            {
                    m00 = pose.m0,
                    m01 = pose.m1,
                    m02 = pose.m2,
                    m03 = pose.m3,
                    m10 = pose.m4,
                    m11 = pose.m5,
                    m12 = pose.m6,
                    m13 = pose.m7,
                    m20 = pose.m8,
                    m21 = pose.m9,
                    m22 = pose.m10,
                    m23 = pose.m11,
                    m30 = 0f,
                    m31 = 0f,
                    m32 = 0f,
                    m33 = 1f,
            };

            return matrix;
        }
    }
}