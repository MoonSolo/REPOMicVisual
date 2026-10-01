using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MicVisual
{
    /// <summary>
    /// Reads the microphone level straight out of REPO's own voice chat implementation.
    ///
    /// <see cref="PlayerVoiceChat"/> already samples the Photon Voice recorder's audio every
    /// frame and stores the result in the private <c>clipLoudness</c> field (mean absolute
    /// amplitude of <c>sampleDataLength</c> samples, scaled by the in-game microphone volume).
    /// Reusing that value means the bar reacts to exactly the same signal the game transmits,
    /// and we never open a second microphone stream of our own.
    ///
    /// Everything is resolved by reflection so a game update that renames or removes these
    /// members degrades to "no indicator" instead of a type load failure at startup.
    /// </summary>
    public sealed class MicLevelSource
    {
        private const string VoiceChatType = "PlayerVoiceChat";

        private Type _voiceChatType;
        private FieldInfo _instanceField;
        private FieldInfo _loudnessField;
        private FieldInfo _muteField;
        private FieldInfo _micEnabledField;
        private FieldInfo _isTalkingField;
        private bool _resolved;
        private bool _resolveFailed;

        /// <summary>True once the game's voice chat members have been located.</summary>
        public bool Resolved => _resolved;

        /// <summary>The game's own mic amplitude. Zero when the game is not capturing audio.</summary>
        public float Level { get; private set; }

        /// <summary>Player has muted voice chat in-game.</summary>
        public bool Muted { get; private set; }

        /// <summary>A capture device is selected and available.</summary>
        public bool MicrophoneAvailable { get; private set; }

        /// <summary>The game currently considers the player to be talking.</summary>
        public bool IsTalking { get; private set; }

        /// <summary>Resolves the game members. Safe to call repeatedly; only does work once.</summary>
        public bool TryResolve()
        {
            if (_resolved || _resolveFailed)
                return _resolved;

            _voiceChatType = AccessTools.TypeByName(VoiceChatType);
            if (_voiceChatType == null)
            {
                _resolveFailed = true;
                MicVisualPlugin.Log?.LogWarning(
                    $"'{VoiceChatType}' not found in Assembly-CSharp; mic indicator disabled.");
                return false;
            }

            _instanceField = AccessTools.Field(_voiceChatType, "instance");
            _loudnessField = AccessTools.Field(_voiceChatType, "clipLoudness");
            _muteField = AccessTools.Field(_voiceChatType, "toggleMute");
            _micEnabledField = AccessTools.Field(_voiceChatType, "microphoneEnabled");
            _isTalkingField = AccessTools.Field(_voiceChatType, "isTalking");

            if (_instanceField == null || _loudnessField == null)
            {
                _resolveFailed = true;
                MicVisualPlugin.Log?.LogError(
                    $"'{VoiceChatType}' found but expected fields are missing; mic indicator disabled.");
                return false;
            }

            _resolved = true;
            return true;
        }

        /// <summary>
        /// Pulls a fresh reading. Returns false while no voice chat instance exists
        /// (main menu, loading screens), letting the UI fade the indicator out.
        /// </summary>
        public bool Sample()
        {
            if (!TryResolve())
                return false;

            object instance = _instanceField.GetValue(null);
            if (instance is UnityEngine.Object unityInstance && unityInstance == null)
                instance = null; // destroyed by a scene change

            if (instance == null)
            {
                Level = 0f;
                Muted = false;
                MicrophoneAvailable = false;
                IsTalking = false;
                return false;
            }

            Level = (float)_loudnessField.GetValue(instance);
            Muted = ReadBool(_muteField, instance);
            MicrophoneAvailable = ReadBool(_micEnabledField, instance);
            IsTalking = ReadBool(_isTalkingField, instance);
            return true;
        }

        private static bool ReadBool(FieldInfo field, object instance)
        {
            if (field == null)
                return false;

            try
            {
                return field.GetValue(instance) is bool value && value;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
