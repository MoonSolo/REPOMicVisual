using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace MicVisual
{
    /// <summary>
    /// Entry point. Binds the configuration and patches the game's own update loop so the
    /// indicator is ticked from inside a live frame rather than from a MonoBehaviour created
    /// during the BepInEx preloader.
    /// </summary>
    [BepInPlugin(PluginGuid, "MicVisual", PluginVersion)]
    [BepInProcess("REPO.exe")]
    public class MicVisualPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.repo.micvisual";
        public const string PluginVersion = "1.0.0";

        public static ManualLogSource Log;
        public static MicSettings Settings;

        private void Awake()
        {
            Log = Logger;
            Settings = new MicSettings(Config);

            Log.LogInfo($"MicVisual {PluginVersion} loaded");

            try
            {
                ApplyPatches(new Harmony(PluginGuid));
            }
            catch (Exception ex)
            {
                Log.LogError($"Failed to patch the game update loop: {ex}");
            }
        }

        /// <summary>
        /// Hooks whichever per-frame game method can be found. RoundDirector.Update is the one
        /// other REPO overlays patch and is therefore the primary target; PlayerVoiceChat.Update is
        /// the fallback because it only exists once voice chat is set up.
        ///
        /// The types are resolved by name so this assembly never takes a compile-time dependency
        /// on Assembly-CSharp and cannot fail to load after a game update.
        /// </summary>
        private static void ApplyPatches(Harmony harmony)
        {
            string[] targets = { "RoundDirector", "PlayerVoiceChat" };
            bool patched = false;

            foreach (string typeName in targets)
            {
                var type = AccessTools.TypeByName(typeName);
                if (type == null)
                {
                    Log.LogWarning($"{typeName} not found, skipping");
                    continue;
                }

                var update = AccessTools.Method(type, "Update", Type.EmptyTypes);
                if (update == null)
                {
                    Log.LogWarning($"{typeName}.Update not found, skipping");
                    continue;
                }

                harmony.Patch(update, postfix: new HarmonyMethod(
                    typeof(MicIndicatorPatch), nameof(MicIndicatorPatch.Postfix)));

                Log.LogInfo($"Patched {typeName}.Update");
                patched = true;
                break;
            }

            if (!patched)
                Log.LogError("No patch target found; the indicator will not update");
        }
    }

    /// <summary>Postfix that advances the indicator by one frame.</summary>
    public static class MicIndicatorPatch
    {
        public static void Postfix()
        {
            MicIndicator.Tick();
        }
    }

    /// <summary>Live configuration, bound to the BepInEx config file on startup.</summary>
    public class MicSettings
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<bool> Verbose;
        public readonly ConfigEntry<float> Sensitivity;
        public readonly ConfigEntry<float> NoiseFloor;
        public readonly ConfigEntry<float> AttackSpeed;
        public readonly ConfigEntry<float> ReleaseSpeed;
        public readonly ConfigEntry<float> PeakDecay;
        public readonly ConfigEntry<bool> ShowPeakMarker;
        public readonly ConfigEntry<bool> HideWhenSilent;
        public readonly ConfigEntry<float> SilentHideDelay;
        public readonly ConfigEntry<float> FadeSpeed;
        public readonly ConfigEntry<float> Scale;
        public readonly ConfigEntry<float> MarginRight;
        public readonly ConfigEntry<float> MarginBottom;

        public MicSettings(ConfigFile config)
        {
            Enabled = config.Bind("Indicator", "Enabled", true,
                "Show the microphone indicator.");

            Verbose = config.Bind("Indicator", "Verbose", false,
                "Log the raw microphone level and indicator state every two seconds. Useful for tuning Sensitivity.");

            Sensitivity = config.Bind("Indicator", "Sensitivity", 4f,
                "How strongly the microphone level fills the bar. Raise it if the bar barely moves.");

            NoiseFloor = config.Bind("Indicator", "NoiseFloor", 0.02f,
                "Levels below this fraction count as silence, so the bar does not flicker on room noise.");

            AttackSpeed = config.Bind("Indicator", "AttackSpeed", 18f,
                "How fast the bar rises, in units per second.");

            ReleaseSpeed = config.Bind("Indicator", "ReleaseSpeed", 6f,
                "How fast the bar falls back, in units per second.");

            ShowPeakMarker = config.Bind("Indicator", "ShowPeakMarker", true,
                "Show a thin marker at the highest recent level.");

            PeakDecay = config.Bind("Indicator", "PeakDecay", 0.6f,
                "How quickly the peak marker falls back, in units per second.");

            HideWhenSilent = config.Bind("Indicator", "HideWhenSilent", false,
                "Fade the whole indicator out during silence instead of showing an empty bar.");

            SilentHideDelay = config.Bind("Indicator", "SilentHideDelay", 0.6f,
                "Seconds of silence before the indicator fades out.");

            FadeSpeed = config.Bind("Indicator", "FadeSpeed", 6f,
                "Fade speed of the whole indicator, in units per second.");

            Scale = config.Bind("Layout", "Scale", 1f,
                "Size multiplier for the indicator.");

            MarginRight = config.Bind("Layout", "MarginRight", 20f,
                "Distance from the right edge of the screen, in 1080p reference pixels.");

            MarginBottom = config.Bind("Layout", "MarginBottom", 40f,
                "Distance from the bottom edge of the screen, in 1080p reference pixels.");
        }
    }
}
