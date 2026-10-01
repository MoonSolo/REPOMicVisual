using UnityEngine;

namespace MicVisual
{
    /// <summary>
    /// Owns the indicator state and draws it.
    ///
    /// <see cref="Tick"/> is called from a Harmony patch on the game's own update loop rather
    /// than from a MonoBehaviour created in the plugin's Awake: BepInEx creates plugin objects
    /// during the preloader, before the Unity player loop starts, so Start/Update on such an
    /// object never run. Driving everything from the patch matches how the other REPO overlays
    /// work.
    ///
    /// Drawing goes through IMGUI, which needs no canvas, no RectTransform hierarchy and no
    /// sprites on disk, and renders on top of the game's own UI.
    /// </summary>
    public static class MicIndicator
    {
        /// <summary>Layout in reference pixels, scaled to the actual screen height.</summary>
        private const float ReferenceHeight = 1080f;
        private const float PanelWidth = 132f;
        private const float PanelHeight = 36f;
        private const float IconSize = 28f;
        private const float BarHeight = 12f;
        private const float BarWidth = 92f;
        private const float Padding = 4f;
        private const float PeakWidth = 2f;

        private static readonly Color IconColor = new Color(0.92f, 0.95f, 0.98f, 1f);
        private static readonly Color BarColor = new Color(0.30f, 0.90f, 0.35f, 1f);
        private static readonly Color TrackColor = new Color(0f, 0f, 0f, 0.45f);
        private static readonly Color PeakColor = new Color(1f, 1f, 1f, 0.85f);
        private static readonly Color MutedColor = new Color(0.65f, 0.65f, 0.68f, 1f);

        private static readonly MicLevelSource Source = new MicLevelSource();

        private static GameObject _host;
        private static Texture2D _iconTexture;
        private static Texture2D _barTexture;
        private static int _lastFrame = -1;
        private static float _nextHeartbeat;

        private static float _display;
        private static float _peak;
        private static float _alpha;
        private static float _silentTime;

        /// <summary>Advances the indicator by one frame. Safe to call more than once per frame.</summary>
        public static void Tick()
        {
            // Several RoundDirector instances can exist; only step once per frame.
            if (_lastFrame == Time.frameCount)
                return;
            _lastFrame = Time.frameCount;


            EnsureHost();

            var cfg = MicVisualPlugin.Settings;
            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);

            Source.TryResolve();
            bool available = Source.Sample();
            bool muted = Source.Muted;

            float raw = 0f;
            if (available && !muted)
            {
                raw = Mathf.Clamp01(Source.Level * cfg.Sensitivity.Value);
                float floor = cfg.NoiseFloor.Value;
                raw = raw <= floor ? 0f : (raw - floor) / Mathf.Max(1f - floor, 0.0001f);
            }

            // Fast attack, slower release: the bar snaps up on speech and eases back down.
            float speed = raw > _display ? cfg.AttackSpeed.Value : cfg.ReleaseSpeed.Value;
            _display = Mathf.Lerp(_display, raw, 1f - Mathf.Exp(-speed * dt));

            _peak = cfg.ShowPeakMarker.Value
                ? Mathf.Max(_display, _peak - cfg.PeakDecay.Value * dt)
                : 0f;

            if (!muted && _display > 0.001f)
                _silentTime = 0f;
            else
                _silentTime += dt;

            bool wantVisible = cfg.Enabled.Value && available;
            if (cfg.HideWhenSilent.Value && _silentTime > cfg.SilentHideDelay.Value)
                wantVisible = false;

            _alpha = Mathf.MoveTowards(_alpha, wantVisible ? 1f : 0f, dt * cfg.FadeSpeed.Value);

            if (cfg.Verbose.Value && Time.unscaledTime >= _nextHeartbeat)
            {
                _nextHeartbeat = Time.unscaledTime + 2f;
                MicVisualPlugin.Log?.LogInfo(
                    $"mic: raw={Source.Level:F4} bar={raw:F3} display={_display:F3} " +
                    $"muted={muted} available={available} resolved={Source.Resolved}");
            }
        }

        /// <summary>Renders the indicator. Called from <see cref="MicIndicatorBehaviour"/>.</summary>
        public static void Draw()
        {
            var cfg = MicVisualPlugin.Settings;
            if (_iconTexture == null || cfg == null || _alpha <= 0.001f)
                return;

            // IMGUI draws in screen pixels with the origin at the top left, and draws in
            // increasing GUI.depth order, so a very low depth puts us on top of other overlays.
            int previousDepth = GUI.depth;
            GUI.depth = -1000;

            try
            {
                float scale = Screen.height / ReferenceHeight * Mathf.Max(cfg.Scale.Value, 0.01f);

                float w = PanelWidth * scale;
                float h = PanelHeight * scale;
                float mr = cfg.MarginRight.Value * scale;
                float mb = cfg.MarginBottom.Value * scale;

                var panel = new Rect(Screen.width - w - mr, Screen.height - h - mb, w, h);
                bool muted = Source.Muted;

                float iconSize = IconSize * scale;
                var iconRect = new Rect(
                    panel.x + Padding * scale,
                    panel.y + (panel.height - iconSize) * 0.5f,
                    iconSize, iconSize);
                Draw(iconRect, _iconTexture, muted ? MutedColor : IconColor);

                float barWidth = BarWidth * scale;
                float barHeight = BarHeight * scale;
                var barRect = new Rect(
                    panel.xMax - Padding * scale - barWidth,
                    panel.y + (panel.height - barHeight) * 0.5f,
                    barWidth, barHeight);

                DrawPill(barRect, TrackColor, 1f);
                DrawPill(barRect, muted ? MutedColor : BarColor, Mathf.Clamp01(_display));

                if (_peak > 0.01f)
                {
                    float peakWidth = PeakWidth * scale;
                    var peakRect = new Rect(
                        barRect.x + barRect.width * _peak - peakWidth * 0.5f,
                        barRect.y - 2f * scale,
                        peakWidth,
                        barRect.height + 4f * scale);
                    GUI.DrawTexture(peakRect, Texture2D.whiteTexture, ScaleMode.StretchToFill,
                                    true, 0f, Fade(PeakColor), 0f, 0f);
                }
            }
            finally
            {
                GUI.depth = previousDepth;
            }
        }

        private static void Draw(Rect rect, Texture2D texture, Color color)
        {
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true, 0f, Fade(color), 0f, 0f);
        }

        /// <summary>
        /// Draws a horizontal capsule, assembling it from a rounded cap, a flat middle and a
        /// rounded cap so the ends keep their radius at any fill width.
        /// </summary>
        private static void DrawPill(Rect rect, Color color, float fill)
        {
            float diameter = rect.height;
            float width = rect.width * Mathf.Clamp01(fill);
            if (width <= 0.5f)
                return;

            float leftCap = Mathf.Min(diameter, width);
            float rightCap = Mathf.Min(diameter, Mathf.Max(0f, width - diameter));
            float middle = Mathf.Max(0f, width - leftCap - rightCap);

            if (middle > 0f)
            {
                GUI.DrawTexture(new Rect(rect.x + leftCap, rect.y, middle, rect.height),
                                Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                                Fade(color), 0f, 0f);
            }

            Draw(new Rect(rect.x, rect.y, leftCap, rect.height), _barTexture, color);
            Draw(new Rect(rect.x + width - rightCap, rect.y, rightCap, rect.height), _barTexture, color);
        }

        private static Color Fade(Color color)
        {
            color.a *= _alpha;
            return color;
        }

        /// <summary>
        /// Creates the object that owns the OnGUI callback and the textures. Both are scene bound
        /// and rebuilt after every scene change, which keeps them tied to a live player loop.
        /// </summary>
        private static void EnsureHost()
        {
            if (_host != null && _iconTexture != null)
                return;

            if (_host == null)
            {
                _host = new GameObject("MicVisualGui");
                _host.AddComponent<MicIndicatorBehaviour>();
            }

            if (_iconTexture == null)
            {
                _iconTexture = UiSprites.CreateMicIcon(128);
                _barTexture = UiSprites.CreateRoundedRect(64, 64, 32);
            }
        }
    }

    /// <summary>Hosts the OnGUI callback for <see cref="MicIndicator"/>.</summary>
    public sealed class MicIndicatorBehaviour : MonoBehaviour
    {
        private void OnGUI()
        {
            MicIndicator.Draw();
        }
    }
}
