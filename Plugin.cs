using System.Globalization;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace MonkeRealism
{
    [BepInPlugin(Constants.Guid, Constants.Name, Constants.Version)]
    public class Plugin : BaseUnityPlugin
    {
        private const float  BorderSpeed = 0.2f;
        private const float  BorderWidth = 3f;
        private const int    BorderSteps = 24;
        public static Plugin Instance;

        private static readonly Color ColBackground  = new Color(0.07f, 0.07f, 0.09f, 0.97f);
        private static readonly Color ColSurface     = new Color(0.12f, 0.12f, 0.16f, 1f);
        private static readonly Color ColSelected    = new Color(0.20f, 0.55f, 1.00f, 1f);
        private static readonly Color ColText        = new Color(0.92f, 0.92f, 0.96f, 1f);
        private static readonly Color ColSubtext     = new Color(0.55f, 0.55f, 0.65f, 1f);
        private static readonly Color ColAccent1     = new Color(0.20f, 0.70f, 1.00f, 1f);
        private static readonly Color ColAccent2     = new Color(0.70f, 0.30f, 1.00f, 1f);
        private static readonly Color ColAccentHover = new Color(0.30f, 0.80f, 1.00f, 1f);

        public GameObject TrackerObject;
        public GameObject TrackerFollower;
        public GameObject TrackerParent;

        public  Quaternion TrackerOffset = Quaternion.identity;
        private Color[]    borderColors;

        private float  borderPhase;
        private Rect[] borderRects;

        private Texture2D[] borderTextures;
        private GUIStyle    buttonStyle;

        private bool               calibrating;
        private float              calibrationTimer;
        private int                displayedCountdown = 3;
        private GUIStyle           labelSmallStyle;
        private GUIStyle           labelStyle;
        private ConfigEntry<float> offsetW;
        private ConfigEntry<float> offsetX;
        private ConfigEntry<float> offsetY;
        private ConfigEntry<float> offsetZ;

        private bool playedCalibrationSound;

        private AudioClip         pressSound, calibrateSound;
        private GUIStyle          scrollViewStyle;
        private GUIStyle          selectedButtonStyle;
        public  ConfigEntry<bool> ShouldUseTracker;
        // --- Elbow tracking config ---
        public ConfigEntry<string> LeftElbowTrackerName;
        public ConfigEntry<string> RightElbowTrackerName;
        public ConfigEntry<bool>   ShouldUseElbowTracking;

        private bool showUi;

        private AudioSource source;
        private bool        stylesInitialized;
        private Texture2D   texActive;

        private Texture2D texBackground;
        private Texture2D texDivider;
        private Texture2D texHover;
        private Texture2D texSelected;
        private Texture2D texSurface;
        private Texture2D texTransparent;

        private Font     titleFont, mainFont;
        private GUIStyle titleStyle;

        public  ConfigEntry<string> TrackerName;
        private Vector2             trackerScroll;
        private Rect                windowRect = new Rect(15, 15, 340, 760);
        private GUIStyle            windowStyle;

        private void Awake()
        {
            Instance = this;

            TrackerName      = Config.Bind("Tracker Settings", "Tracker",      "WAIST");
            ShouldUseTracker = Config.Bind("Tracker Settings", "Use Tracking", true);
            LeftElbowTrackerName   = Config.Bind("Elbow Tracking", "Left Elbow Tracker",  "LEFT_ELBOW");
            RightElbowTrackerName  = Config.Bind("Elbow Tracking", "Right Elbow Tracker", "RIGHT_ELBOW");
            ShouldUseElbowTracking = Config.Bind("Elbow Tracking", "Use Elbow Tracking",  true);
            // In the ShouldUseElbowTracking toggle handler:
            ShouldUseElbowTracking.SettingChanged += (_, _) =>
            {
                var gorillaIK = VRRig.LocalRig?.GetComponent<GorillaIK>();
                if (gorillaIK != null)
                    gorillaIK.enabled = !ShouldUseElbowTracking.Value;
            };

            offsetX = Config.Bind("Offsets", "X", 0f);
            offsetY = Config.Bind("Offsets", "Y", 0f);
            offsetZ = Config.Bind("Offsets", "Z", 0f);
            offsetW = Config.Bind("Offsets", "W", 1f);

            TrackerOffset = new Quaternion(
                    offsetX.Value,
                    offsetY.Value,
                    offsetZ.Value,
                    offsetW.Value
            );

            int total = BorderSteps * 3;
            borderTextures = new Texture2D[total];
            borderColors   = new Color[total];
            borderRects    = new Rect[total];
        }

        private void Start()
        {
            HarmonyPatches.ApplyHarmonyPatches();

            GorillaTagger.OnPlayerSpawned(TrackerManager.Initialize);

            GorillaTagger.OnPlayerSpawned(() =>
            {
                Transform rigRoot = GorillaTagger.Instance.offlineVRRig.transform;
                ElbowTrackingManager.Initialize(rigRoot);
                var gorillaIK = VRRig.LocalRig?.GetComponent<GorillaIK>();
                if (gorillaIK != null)
                    gorillaIK.enabled = !ShouldUseElbowTracking.Value;
            });

            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable
            {
                    { Constants.HashKey, Constants.Version },
            });

            Stream bundleStream = Assembly.GetExecutingAssembly()
                                          .GetManifestResourceStream("MonkeRealism.Assets.monkerealism");

            AssetBundle bundle = AssetBundle.LoadFromStream(bundleStream);

            pressSound     = bundle.LoadAsset<AudioClip>("MonkeRealismPress");
            calibrateSound = bundle.LoadAsset<AudioClip>("MonkeRealismCalibrate");

            titleFont = bundle.LoadAsset<Font>("Coolvetica");
            mainFont  = bundle.LoadAsset<Font>("Jersey");
        }

        private void Update()
        {
            if (Keyboard.current.f3Key.wasPressedThisFrame)
                showUi = !showUi;

            borderPhase += Time.deltaTime * BorderSpeed;
            if (borderPhase > 1f) borderPhase -= 1f;

            if (calibrating)
            {
                if (!playedCalibrationSound)
                {
                    PlaySound(calibrateSound);
                    playedCalibrationSound = true;
                }

                calibrationTimer -= Time.deltaTime;

                displayedCountdown = Mathf.CeilToInt(calibrationTimer);

                if (calibrationTimer <= 0f)
                {
                    Quaternion? rot = TrackerManager.GetTrackerRotation(TrackerName.Value);

                    if (rot.HasValue)
                    {
                        //TODO Fix calibration so it's correct
                        
                        Transform head = GorillaTagger.Instance.mainCamera.transform;

                        Quaternion trackerRotation = rot.Value;

                        Vector3 headForward = head.forward;
                        headForward.y = 0f;
                        headForward.Normalize();

                        Vector3 trackerForward = trackerRotation * Vector3.forward;
                        trackerForward.y = 0f;
                        trackerForward.Normalize();

                        Quaternion headYaw    = Quaternion.LookRotation(headForward,    Vector3.up);
                        Quaternion trackerYaw = Quaternion.LookRotation(trackerForward, Vector3.up);

                        TrackerOffset = Quaternion.Inverse(trackerYaw) * headYaw;

                        SaveOffset();
                    }

                    calibrating            = false;
                    playedCalibrationSound = false;
                }
            }

            if (ShouldUseElbowTracking.Value)
            {
                ElbowTrackingManager.ApplyRotations(
                    LeftElbowTrackerName.Value,
                    RightElbowTrackerName.Value,
                    TrackerOffset   // reuses the same offset quaternion as the waist; give elbows their own if needed
                );
            }

            Quaternion? trackerRot = TrackerManager.GetTrackerRotation(TrackerName.Value);

            if (!trackerRot.HasValue)
                return;

            TrackerObject.transform.localRotation = trackerRot.Value;
        }

        private void OnGUI()
        {
            if (!showUi)
                return;

            InitializeStyles();
            DrawAnimatedBorder();

            windowRect = GUI.Window(7373, windowRect, DrawWindow, GUIContent.none, windowStyle);
        }

        private void DrawWindow(int id)
        {
            GUILayout.Space(12);

            GUILayout.Label("MONKE REALISM", titleStyle);
            GUILayout.Space(2);
            GUILayout.Label("TRACKER CONFIG", labelSmallStyle);

            DrawDivider();

            GUILayout.Space(6);
            GUILayout.Label("ACTIVE TRACKER", labelSmallStyle);
            GUILayout.Space(4);
            GUILayout.Label(TrackerName.Value, labelStyle);

            GUILayout.Space(10);
            GUILayout.Label("SELECT TRACKER", labelSmallStyle);
            GUILayout.Space(4);

            trackerScroll = GUILayout.BeginScrollView(
                    trackerScroll,
                    false,
                    false,
                    GUIStyle.none,
                    GUIStyle.none,
                    scrollViewStyle,
                    GUILayout.Height(160)
            );

            foreach (string tracker in TrackerManager.GetTrackers())
            {
                GUIStyle style = tracker == TrackerName.Value ? selectedButtonStyle : buttonStyle;
                if (Button(tracker, style, GUILayout.Height(36)))
                    TrackerName.Value = tracker;

                GUILayout.Space(3);
            }

            GUILayout.EndScrollView();

            DrawDivider();

            GUILayout.Space(6);
            GUILayout.Label("ROTATION OFFSET", labelSmallStyle);
            GUILayout.Space(6);

            DrawAxisButtons(Vector3.right, "PITCH  X");
            GUILayout.Space(4);
            DrawAxisButtons(Vector3.up, "YAW    Y");
            GUILayout.Space(4);
            DrawAxisButtons(Vector3.forward, "ROLL   Z");

            DrawDivider();

            GUILayout.Space(6);

            if (!calibrating)
            {
                if (Button("START T-POSE CALIBRATION", buttonStyle, GUILayout.Height(44)))
                {
                    calibrationTimer   = 3f;
                    displayedCountdown = 3;
                    calibrating        = true;
                }
            }
            else
            {
                GUILayout.Label($"HOLD T-POSE  {displayedCountdown}", titleStyle);
            }

            DrawDivider();

            GUILayout.Space(6);

            Vector3 euler = TrackerObject.transform.eulerAngles;

            GUILayout.Label("LIVE ROTATION", labelSmallStyle);
            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            DrawRotationBadge("X", euler.x);
            GUILayout.Space(4);
            DrawRotationBadge("Y", euler.y);
            GUILayout.Space(4);
            DrawRotationBadge("Z", euler.z);
            GUILayout.EndHorizontal();
            
            GUILayout.Space(8);

            string trackingText =
                    ShouldUseTracker.Value
                            ? "TRACKING ENABLED"
                            : "TRACKING DISABLED";

            GUIStyle toggleStyle =
                    ShouldUseTracker.Value
                            ? selectedButtonStyle
                            : buttonStyle;

            if (Button(
                        trackingText,
                        toggleStyle,
                        GUILayout.Height(42)))
            {
                ShouldUseTracker.Value = !ShouldUseTracker.Value;
                Config.Save();
            }

            DrawDivider();
            GUILayout.Space(6);
            GUILayout.Label("ELBOW TRACKING", labelSmallStyle);
            GUILayout.Space(4);

            string elbowText  = ShouldUseElbowTracking.Value ? "ELBOWS ENABLED" : "ELBOWS DISABLED";
            GUIStyle elbowStyle = ShouldUseElbowTracking.Value ? selectedButtonStyle : buttonStyle;

            if (Button(elbowText, elbowStyle, GUILayout.Height(42)))
            {
                ShouldUseElbowTracking.Value = !ShouldUseElbowTracking.Value;
                Config.Save();
            }

            GUILayout.Space(6);
            GUILayout.Label("LEFT ELBOW", labelSmallStyle);
            GUILayout.Space(4);
            GUILayout.Label(LeftElbowTrackerName.Value, labelStyle);
            GUILayout.Space(6);
            GUILayout.Label("RIGHT ELBOW", labelSmallStyle);
            GUILayout.Space(4);
            GUILayout.Label(RightElbowTrackerName.Value, labelStyle);
            
            GUILayout.Space(10);
            GUILayout.Label($"TRACKERS ONLINE  {TrackerManager.GetTrackers().Count}", labelSmallStyle);
            GUILayout.Space(10);

            GUI.DragWindow(new Rect(0, 0, 9999, 9999));
        }

        private void DrawAxisButtons(Vector3 axis, string label)
        {
            GUILayout.Label(label, labelSmallStyle);
            GUILayout.Space(3);
            GUILayout.BeginHorizontal();

            if (Button("− 10°", buttonStyle, GUILayout.Height(32)))
            {
                TrackerOffset = Quaternion.AngleAxis(-10f, axis) * TrackerOffset;
                SaveOffset();
            }

            GUILayout.Space(4);

            if (Button("+ 10°", buttonStyle, GUILayout.Height(32)))
            {
                TrackerOffset = Quaternion.AngleAxis(10f, axis) * TrackerOffset;
                SaveOffset();
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(2);
        }

        private void DrawRotationBadge(string axis, float value)
        {
            GUILayout.BeginVertical(GUILayout.Width(82));
            GUILayout.Label(axis,                                                      labelSmallStyle);
            GUILayout.Label(Mathf.Round(value).ToString(CultureInfo.InvariantCulture), labelStyle);
            GUILayout.EndVertical();
        }

        private void DrawDivider()
        {
            GUILayout.Space(8);
            Rect r = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            r.x     += 10;
            r.width -= 20;
            GUI.DrawTexture(r, texDivider);
            GUILayout.Space(8);
        }

        private bool Button(string text, GUIStyle style, params GUILayoutOption[] options)
        {
            bool pressed = GUILayout.Button(text, style, options);

            if (pressed && !text.ToLower().Contains("calibration"))
                PlaySound(pressSound);

            return pressed;
        }

        private void DrawAnimatedBorder()
        {
            float x         = windowRect.x;
            float y         = windowRect.y;
            float w         = windowRect.width;
            float h         = windowRect.height;
            float perimeter = 2f          * (w + h);
            float dashLen   = perimeter   * 0.18f;
            float head      = borderPhase * perimeter;

            for (int dash = 0; dash < 3; dash++)
            {
                float from = (head - dash * (perimeter * 0.33f) + perimeter) % perimeter;
                float t    = dash                                            / 3f;
                Color colA = Color.Lerp(ColAccent1, ColAccent2, t);
                Color colB = Color.Lerp(ColAccent2, ColAccent1, t);

                for (int i = 0; i < BorderSteps; i++)
                {
                    int   idx   = dash * BorderSteps + i;
                    float tLerp = (float)i                                          / BorderSteps;
                    float d0    = (from + dashLen * tLerp)                          % perimeter;
                    float d1    = (from + dashLen * ((float)(i + 1) / BorderSteps)) % perimeter;

                    Color col = Color.Lerp(colA, colB, tLerp);
                    col.a = Mathf.Sin(tLerp * Mathf.PI) * 0.9f + 0.1f;

                    if (col != borderColors[idx])
                    {
                        borderColors[idx] = col;
                        if (borderTextures[idx] == null)
                            borderTextures[idx] = new Texture2D(1, 1) { wrapMode = TextureWrapMode.Clamp, };

                        borderTextures[idx].SetPixel(0, 0, col);
                        borderTextures[idx].Apply();
                    }

                    Rect seg = SegmentRect(x, y, w, h, perimeter, d0, d1);
                    if (seg.width > 0 && seg.height > 0)
                        GUI.DrawTexture(seg, borderTextures[idx]);
                }
            }
        }

        private Rect SegmentRect(float x, float y, float w, float h, float perimeter, float d0, float d1)
        {
            Vector2 p0 = PerimeterPoint(x, y, w, h, perimeter, d0);
            Vector2 p1 = PerimeterPoint(x, y, w, h, perimeter, d1);

            float minX = Mathf.Min(p0.x, p1.x) - BorderWidth * 0.5f;
            float minY = Mathf.Min(p0.y, p1.y) - BorderWidth * 0.5f;
            float maxX = Mathf.Max(p0.x, p1.x) + BorderWidth * 0.5f;
            float maxY = Mathf.Max(p0.y, p1.y) + BorderWidth * 0.5f;

            if (maxX - minX < BorderWidth)
            {
                minX = p0.x - BorderWidth * 0.5f;
                maxX = minX + BorderWidth;
            }

            if (maxY - minY < BorderWidth)
            {
                minY = p0.y - BorderWidth * 0.5f;
                maxY = minY + BorderWidth;
            }

            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        private Vector2 PerimeterPoint(float x, float y, float w, float h, float perimeter, float d)
        {
            d = (d % perimeter + perimeter) % perimeter;

            if (d < w) return new Vector2(x + d, y);
            d -= w;

            if (d < h) return new Vector2(x + w, y + d);
            d -= h;

            if (d < w) return new Vector2(x + w - d, y + h);
            d -= w;

            return new Vector2(x, y + h - d);
        }

        private void InitializeStyles()
        {
            if (stylesInitialized) return;
            stylesInitialized = true;

            texBackground  = MakeTex(ColBackground);
            texSurface     = MakeTex(ColSurface);
            texSelected    = MakeTex(ColSelected);
            texHover       = MakeTex(new Color(0.22f, 0.22f, 0.28f, 1f));
            texActive      = MakeTex(new Color(0.16f, 0.45f, 0.90f, 1f));
            texTransparent = MakeTex(new Color(0f,    0f,    0f,    0f));
            texDivider     = MakeTex(new Color(0.25f, 0.25f, 0.35f, 0.6f));

            windowStyle = new GUIStyle
            {
                    normal =
                    {
                            background = texBackground,
                    },
                    padding = new RectOffset(14, 14, 10, 14),
                    border  = new RectOffset(0,  0,  0,  0),
            };

            titleStyle = new GUIStyle
            {
                    font      = titleFont,
                    fontSize  = 22,
                    fontStyle = FontStyle.Bold,
                    normal    = { textColor = ColText, },
                    alignment = TextAnchor.MiddleCenter,
                    richText  = true,
            };

            labelSmallStyle = new GUIStyle
            {
                    font      = mainFont,
                    fontSize  = 11,
                    normal    = { textColor = ColSubtext, },
                    alignment = TextAnchor.MiddleLeft,
                    richText  = true,
            };

            labelStyle = new GUIStyle
            {
                    font      = mainFont,
                    fontSize  = 18,
                    fontStyle = FontStyle.Bold,
                    normal    = { textColor = ColText, },
                    alignment = TextAnchor.MiddleLeft,
                    richText  = true,
            };

            buttonStyle = new GUIStyle
            {
                    font      = mainFont,
                    fontSize  = 14,
                    fontStyle = FontStyle.Bold,
                    normal =
                    {
                            background = texSurface,
                            textColor  = ColText,
                    },
                    hover =
                    {
                            background = texHover,
                            textColor  = ColAccentHover,
                    },
                    active =
                    {
                            background = texActive,
                            textColor  = Color.white,
                    },
                    focused =
                    {
                            background = texSurface,
                            textColor  = ColText,
                    },
                    onNormal =
                    {
                            background = texSurface,
                            textColor  = ColText,
                    },
                    onHover =
                    {
                            background = texHover,
                            textColor  = ColAccentHover,
                    },
                    onActive =
                    {
                            background = texActive,
                            textColor  = Color.white,
                    },
                    onFocused =
                    {
                            background = texSurface,
                            textColor  = ColText,
                    },
                    alignment = TextAnchor.MiddleCenter,
                    border    = new RectOffset(0,  0,  0, 0),
                    padding   = new RectOffset(10, 10, 6, 6),
            };

            selectedButtonStyle = new GUIStyle(buttonStyle)
            {
                    normal =
                    {
                            background = texSelected,
                            textColor  = Color.white,
                    },
                    hover =
                    {
                            background = texSelected,
                            textColor  = Color.white,
                    },
                    active =
                    {
                            background = texActive,
                            textColor  = Color.white,
                    },
                    onNormal =
                    {
                            background = texSelected,
                            textColor  = Color.white,
                    },
                    onHover =
                    {
                            background = texSelected,
                            textColor  = Color.white,
                    },
                    onActive =
                    {
                            background = texActive,
                            textColor  = Color.white,
                    },
            };

            scrollViewStyle = new GUIStyle
            {
                    normal =
                    {
                            background = texTransparent,
                    },
            };
        }

        private Texture2D MakeTex(Color col)
        {
            Texture2D tex = new Texture2D(1, 1) { wrapMode = TextureWrapMode.Clamp, };
            tex.SetPixel(0, 0, col);
            tex.Apply();

            return tex;
        }

        private void SaveOffset()
        {
            offsetX.Value = TrackerOffset.x;
            offsetY.Value = TrackerOffset.y;
            offsetZ.Value = TrackerOffset.z;
            offsetW.Value = TrackerOffset.w;
            Config.Save();

            TrackerFollower.transform.localRotation = TrackerOffset;
        }

        private void PlaySound(AudioClip clip)
        {
            if (source == null)
            {
                source             = new GameObject("MonkeRealismAudioSource").AddComponent<AudioSource>();
                source.playOnAwake = false;
            }

            source.PlayOneShot(clip);
        }
    }
}