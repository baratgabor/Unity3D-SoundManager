using System;
using System.Text;
using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using Random = UnityEngine.Random;

namespace LeakyAbstraction
{
    public class SoundManager : SingletonMonoBehaviour<SoundManager>
    {
        /// <summary>
        /// This nested class holds all data related to the playback of a single AudioClip. Instances of this class are exposed to the Inspector through the SoundManager class.
        /// </summary>
        [Serializable]
        public class SoundEntity
        {
#if UNITY_EDITOR
            /// <summary>
            /// These members are part of the compilation only in the Unity Editor; i.e. they won't compile into your game.
            /// The string replaces the default 'Element' string displayed as the name of the entities inside the array.
            /// But, at the same time the field itself is hidden from editing.
            /// </summary>
            [HideInInspector]
            public string Name;
            public void SetName()
                => Name = soundType.ToString();
#endif

            public GameSound soundType;
            public AudioClip audioClip;

            [Range(0, 1)]
            public float volumeLow = 1f;
            [Range(0, 1)]
            public float volumeHigh = 1f;
            [Range(0, 2)]
            public float pitchLow = 1f;
            [Range(0, 2)]
            public float pitchHigh = 1f;
        }

        [Header("Number of simultaneous sounds supported at startup:")]
        [SerializeField]
        [Tooltip("Defines the number of AudioSources to create during initialization.")]
        private int _initialPoolSize = 10;

        [Header("Increase the number of simultaneous sounds on-demand:")]
        [SerializeField]
        [Tooltip("If set to TRUE, a new AudioSource will be created if all others are busy. If set to FALSE, the sound simply won't play.")]
        private bool _canGrowPool = true;

        [Header("Report sound types which don't have sounds assigned:")]
        [SerializeField]
        [Tooltip("If set to TRUE, all sound types defined in the Enum will be checked to see if there is at least one sound associated to them, and the unassigned ones will be reported in a warning.")]
        private bool _checkUnassignedSounds = true;

        [SerializeField]
        private SoundEntity[] _soundList = default;

        private Dictionary<GameSound, List<SoundEntity>> _soundMap = new Dictionary<GameSound, List<SoundEntity>>();
        private Stack<AudioSource> _availableAudioSources;

        private bool _initialized = false;
        private int _numOfAudioSources = 0;
        private readonly Vector3 _zeroVector = Vector3.zero;

        private const float RELEASE_MARGIN = 0.05f;
        private const float RETRYRELEASE_WAIT = 0.1f;
        private const string SOUNDPLAYER_GO_NAMEBASE = "SoundPlayer";

#if UNITY_EDITOR
        private void OnValidate()
        {
            // We're only interested in changes made in the Inspector
            if (!GUI.changed)
                return;

            // Set/update the names of entities to replace the generic 'element' name in Unity Inspector's array view
            foreach (var s in _soundList)
                s.SetName();

            // If changes were made in the Inspector while the game is running, and we're past initialization,
            // process again the list of sounds to make sure our runtime representation is up to date. 
            if (EditorApplication.isPlaying && _initialized)
                PopulateSoundMap();
        }
#endif

        /// <summary>
        /// Initialization.
        /// </summary>
        protected override void Awake()
        {
            base.Awake();

            if (_initialized == true)
                return;

            PopulateSoundMap();
            CreateAudioSources();
            _initialized = true;
        }

        /// <summary>
        /// Plays the specified sound.
        /// If multiple sounds are registed for the given type, selects one randomly.
        /// </summary>
        /// <param name="soundType">The pre-defined identifier of the sound to play.</param>
        /// <param name="playFinishedCallback">Delegate to be invoked when playback completed.</param>
        /// <returns>Returns the AudioSource that is playing the requested sound. Don't mess with it if you want the soundmanager to work dependably.</returns>
        public AudioSource PlaySound(GameSound soundType, Action playFinishedCallback = null)
            => PlaySound(soundType, 1, 1, playFinishedCallback);

        /// <summary>
        /// Plays the specified sound at the specified world position.
        /// If multiple sounds are registed for the given type, selects one randomly.
        /// </summary>
        /// <param name="soundType">The pre-defined identifier of the sound to play.</param>
        /// <param name="soundPosition">The world position of the sound playback.</param>
        /// <param name="playFinishedCallback">Delegate to be invoked when playback completed.</param>
        /// <returns>Returns the AudioSource that is playing the requested sound. Don't mess with it if you want the soundmanager to work dependably.</returns>
        public AudioSource PlaySound(GameSound soundType, Vector3 soundPosition, Action playFinishedCallback = null)
            => PlaySound(soundType, soundPosition, 1, 1, playFinishedCallback);

        /// <summary>
        /// Plays the specified sound with the specified volume and pitch overrides.
        /// If multiple sounds are registed for the given type, selects one randomly.
        /// </summary>
        /// <param name="soundType">The pre-defined identifier of the sound to play.</param>
        /// <param name="volumeMultiplier">The multiplier to apply to the volume. Applies on top of the random range value defined in Inspector.</param>
        /// <param name="pitchMultiplier">The multiplier to apply to the pitch. Applies on top of the random range value defined in Inspector.</param>
        /// <param name="playFinishedCallback">Delegate to be invoked when playback completed.</param>
        /// <returns>Returns the AudioSource that is playing the requested sound. Don't mess with it if you want the soundmanager to work dependably.</returns>
        public AudioSource PlaySound(GameSound soundType, float volumeMultiplier, float pitchMultiplier, Action playFinishedCallback = null)
        {
            var (canPlay, soundList) = SoundPlayPreChecks(soundType);

            if (!canPlay)
                return null;

            return PlaySound_Internal(
                GetRandomSound(soundList),
                volumeMultiplier: volumeMultiplier,
                pitchMultiplier: pitchMultiplier,
                positionalSound: false,
                soundPosition: _zeroVector,
                playFinishedCallback
            );
        }

        /// <summary>
        /// Plays the specified sound at the specified world position, with the specified volume and pitch overrides.
        /// If multiple sounds are registed for the given type, selects one randomly.
        /// </summary>
        /// <param name="soundType">The pre-defined identifier of the sound to play.</param>
        /// <param name="soundPosition">The world position of the sound playback.</param>
        /// <param name="volumeMultiplier">The multiplier to apply to the volume. Applies on top of the random range value defined in Inspector.</param>
        /// <param name="pitchMultiplier">The multiplier to apply to the pitch. Applies on top of the random range value defined in Inspector.</param>
        /// <param name="playFinishedCallback">Delegate to be invoked when playback completed.</param>
        /// <returns>Returns the AudioSource that is playing the requested sound. Don't mess with it if you want the soundmanager to work dependably.</returns>
        public AudioSource PlaySound(GameSound soundType, Vector3 soundPosition, float volumeMultiplier, float pitchMultiplier, Action playFinishedCallback = null)
        {
            var (canPlay, soundList) = SoundPlayPreChecks(soundType);

            if (!canPlay)
                return null;

            return PlaySound_Internal(
                GetRandomSound(soundList),
                volumeMultiplier,
                pitchMultiplier,
                positionalSound: true,
                soundPosition: soundPosition,
                playFinishedCallback
            );
        }

        /// <summary>
        /// Converts the Editor-compatible array into a fast-lookup dictionary map.
        /// Makes a list of sounds that has the same sound type.
        /// </summary>
        private void PopulateSoundMap()
        {
            if (_soundList == null || _soundList.Length == 0)
            {
                Debug.LogWarning($"No sounds are assigned to {nameof(SoundManager)}. It will be unable to play any sounds.");
                return;
            }

            foreach (var s in _soundList)
            {
                // Silently skip entries where 'None' is selected as soundtype
                if (s.soundType == GameSound.None)
                    continue;

                if (s.audioClip == null)
                {
                    Debug.LogWarning($"There is an entry for the soundtype '{s.soundType}' that is missing its {nameof(AudioClip)}. This entry won't be used.");
                    continue;
                }

                if (_soundMap.TryGetValue(s.soundType, out var list))
                    // If a list already exists for the given sound type, simply add an additional entry to it.
                    list.Add(s);
                else
                    // If the list doesn't exist yet, instantiate and add a new list, and initialize it to contain the first entry.
                    _soundMap.Add(s.soundType, new List<SoundEntity>() { s });
            }

            // If requested, check and report which soundtypes don't have any sound entry associated in Inspector (i.e. can't play).
            if (_checkUnassignedSounds)
                CheckAndLogUnassignedSoundTypes();
        }

        /// <summary>
        /// Checks all GameSound enum values to see if there is at least one sound assigned to it.
        /// If it finds enum values without any sound assigned, logs a warning to the console with the list of missing sound types.
        /// </summary>
        private void CheckAndLogUnassignedSoundTypes()
        {
            StringBuilder missingSoundsBuilder = null;
            foreach (GameSound soundType in Enum.GetValues(typeof(GameSound)))
            {
                if (soundType == GameSound.None || _soundMap.ContainsKey(soundType))
                    continue;

                if (missingSoundsBuilder == null)
                    // Included in this scope to avoid allocation of StringBuilder if we don't have anything to report.
                    missingSoundsBuilder = new StringBuilder(
                        $"{nameof(SoundManager)} initialization found that there is no sound set for the following sound types:");

                missingSoundsBuilder.Append($"{Environment.NewLine} - {soundType.ToString()}");
            }

            if (missingSoundsBuilder != null)
                Debug.LogWarning(missingSoundsBuilder.ToString());
        }

        /// <summary>
        /// Creates the specified number of AudioSource instances.
        /// </summary>
        private void CreateAudioSources()
        {
            _availableAudioSources = new Stack<AudioSource>(capacity: _canGrowPool ? _initialPoolSize * 2 : _initialPoolSize);

            for (int i = 0; i < _initialPoolSize; i++)
                CreateAudioSource();
        }

        /// <summary>
        /// Creates a new child GameObject with an AudioSource component, and registers the component in the list of available AudioSources.
        /// </summary>
        /// <returns>Returns the added component.</returns>
        private AudioSource CreateAudioSource()
        {
            _numOfAudioSources++;
            var go = new GameObject(SOUNDPLAYER_GO_NAMEBASE + _numOfAudioSources);
            go.transform.parent = this.transform;

            var audioSource = go.AddComponent<AudioSource>();
            _availableAudioSources.Push(audioSource);

            return audioSource;
        }

        /// <summary>
        /// Returns whether playback of a given soundtype is possible, and if so, returns all sound variations available for the given soundtype (or null).
        /// </summary>
        private (bool canPlay, List<SoundEntity> availableSounds) SoundPlayPreChecks(GameSound soundType)
        {
            if (!_initialized)
            {
                Debug.LogWarning("Sound playback was requested before SoundManager was initialized. Initializing now.");
                Awake();
            }

            if (soundType == GameSound.None)
            {
                Debug.LogWarning("Sound playback failed. Soundtype 'None' was requested to play. Specify a valid soundtype.");
                return (canPlay: false, null);
            }

            var soundListExists = _soundMap.TryGetValue(soundType,
                out var soundList); // Note out var

            if (!soundListExists)
            {
                Debug.LogWarning($"Sound playback failed. No {nameof(AudioClip)} is set for the sound type '{soundType}'.");
                return (canPlay: false, null);
            }

            if (_availableAudioSources.Count == 0)
            {
                if (!_canGrowPool)
                {
                    Debug.LogWarning($"Sound playback failed, because no {nameof(AudioSource)} was available. " +
                        $"Increase the initial pool of {nameof(AudioSource)}s, or enable the on-demand creation of {nameof(AudioSource)}s.");
                    return (canPlay: false, null);
                }

                Debug.LogWarning($"All {nameof(AudioSource)}s were busy. New {nameof(AudioSource)} had to be instantiated for playback. " +
                    $"If you see this often, it's advisable to increase the initial pool of {nameof(AudioSource)}s.");
                CreateAudioSource();
            }

            return (canPlay: true, soundList);
        }

        /// <summary>
        /// Returns a random sound from a list of sounds.
        /// </summary>
        private SoundEntity GetRandomSound(List<SoundEntity> list)
            => list[Random.Range(0, list.Count)];

        /// <summary>
        /// Reserves and preps an AudioSource, plays the specified sound, and schedules the release of the AudioSource after playback is complete.
        /// </summary>
        private AudioSource PlaySound_Internal(
            SoundEntity sound,
            float volumeMultiplier,
            float pitchMultiplier,
            bool positionalSound,   // Separate bool param to avoid having to do Vector3 equality comparisons.
            Vector3 soundPosition,  // I don't like having this Vector3 here, but this seemed to be the most straighforward implementation.
            Action playFinishedCallback = null)
        {
            // Pop and prepare audio source
            var audioSource = _availableAudioSources.Pop();
            var pitch = Random.Range(sound.pitchLow, sound.pitchHigh) * pitchMultiplier;
            audioSource.volume = Random.Range(sound.volumeLow, sound.volumeHigh) * volumeMultiplier;
            audioSource.pitch = pitch;
            audioSource.clip = sound.audioClip;

            // Set AudioSource position if needed
            if (positionalSound)
                audioSource.transform.position = soundPosition;

            // Schedule audio source release
            var playtime = Mathf.Abs(sound.audioClip.length / pitch); // Abs() is to support negative pitch
            StartCoroutine(
                ReleaseAudioSourceDelayed(audioSource, playtime, positionalSound, playFinishedCallback)
            );

            // Do actual playback
            audioSource.Play();
            return audioSource;
        }

        /// <summary>
        /// Coroutine that holds an AudioSource reference, waits for playback completion, then restores AudioSource for next playback.
        /// </summary>
        private IEnumerator ReleaseAudioSourceDelayed(AudioSource audioSource, float releaseAfterSeconds, bool transformResetRequired, Action playFinishedCallback = null)
        {
            //TODO: Double check if this is supposed to be Realtime
            yield return new WaitForSecondsRealtime(releaseAfterSeconds + RELEASE_MARGIN);

            // Make sure it's actually finished
            int extraWaits = 0;
            while (audioSource.isPlaying)
            {
                Debug.LogWarning($"{nameof(AudioSource)} wasn't ready for release at the expected time. Waiting cycle: {++extraWaits}. " +
                    $"\nIf you see this often, consider increasing the {nameof(RELEASE_MARGIN)} constant.");
                yield return new WaitForSeconds(RETRYRELEASE_WAIT);
            }

            // Put soundplayer gameobject back to our own position, if requested
            if (transformResetRequired)
                audioSource.transform.localPosition = Vector3.zero;

            // Push back AudioSource to list of available
            _availableAudioSources.Push(audioSource);

            // Notify of completion
            playFinishedCallback?.Invoke();
        }
    }
}
