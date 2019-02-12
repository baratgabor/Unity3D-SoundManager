using System;
using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using Random = UnityEngine.Random;

namespace LeakyAbstraction
{
    public class SoundManager : SingletonMonoBehaviour<SoundManager>, ICoroutineControl
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
        public enum LoggingType
        {
            None,
            LogOnlyInEditor,
            LogAlways
        }

        [Header("Number of simultaneous sounds supported at startup:")]
        [SerializeField]
        [Tooltip("Defines the number of AudioSources to create during initialization.")]
        private int _initialPoolSize = 10;

        [Header("Increase the number of simultaneous sounds on-demand:")]
        [SerializeField]
        [Tooltip("If set to TRUE, a new AudioSource will be created if all others are busy. If set to FALSE, the sound simply won't play.")]
        private bool _canGrowPool = true;

        [Header("Logging behavior:")]
        [SerializeField]
        [Tooltip("Note that selecting 'None' disables logging completely, so the next setting will have no effect.")]
        private LoggingType _loggingType = LoggingType.LogOnlyInEditor;

        [Header("Report sound types which don't have entries defines:")]
        [SerializeField]
        [Tooltip("If set to TRUE, all sound types defined in the Enum will be checked to see if there is at least one sound associated to them, and the unassigned ones will be reported in a warning.")]
        private bool _checkUnassignedSounds = true;

        [SerializeField]
        private SoundEntity[] _soundList = default;

        private Dictionary<GameSound, List<SoundEntity>> _soundMap = new Dictionary<GameSound, List<SoundEntity>>();
        private Stack<SoundPlayer> _soundPlayerPool;

        private bool _initialized = false;
        private readonly Vector3 _zeroVector = Vector3.zero;

        private const float RELEASE_MARGIN = 0.05f;
        private const float RETRYRELEASE_WAIT = 0.1f;
        private const string SOUNDPLAYER_GO_NAMEBASE = "SoundPlayer";
        private int _soundPlayerNameIndex = 0;

        private SoundManagerDebugLogger _log;

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

            SetupLogging();
            PopulateSoundMap();
            GrowPool(_initialPoolSize);
            _initialized = true;
        }

        private void SetupLogging()
        {
            switch (_loggingType)
            {
                case LoggingType.None:
                    break;
                case LoggingType.LogOnlyInEditor:
                    if (Application.isEditor)
                        _log = new SoundManagerDebugLogger();
                    break;
                case LoggingType.LogAlways:
                    _log = new SoundManagerDebugLogger();
                    break;
                default:
                    Debug.LogException(
                        new InvalidOperationException($"Unknown logging type '{_loggingType}' encountered. Cannot set up logging behavior."));
                    break;
            }
        }

        /// <summary>
        /// Plays the specified sound.
        /// If multiple sounds are registed for the given type, selects one randomly.
        /// </summary>
        /// <param name="soundType">The pre-defined identifier of the sound to play.</param>
        /// <param name="playFinishedCallback">Delegate to be invoked when playback completed.</param>
        /// <returns>Returns the AudioSource that is playing the requested sound. Don't mess with it if you want the soundmanager to work dependably.</returns>
        public AudioSource PlaySound(GameSound soundType, Action<GameSound> playFinishedCallback = null)
            => Play_Internal(soundType, 1, 1, PlayMode.Simple, null, _zeroVector, playFinishedCallback);

        /// <summary>
        /// Plays the specified sound at the specified world position.
        /// If multiple sounds are registed for the given type, selects one randomly.
        /// </summary>
        /// <param name="soundType">The pre-defined identifier of the sound to play.</param>
        /// <param name="soundPosition">The world position of the sound playback.</param>
        /// <param name="playFinishedCallback">Delegate to be invoked when playback completed.</param>
        /// <returns>Returns the AudioSource that is playing the requested sound. Don't mess with it if you want the soundmanager to work dependably.</returns>
        public AudioSource PlaySoundPositioned(GameSound soundType, Vector3 soundPosition, Action<GameSound> playFinishedCallback = null)
            => Play_Internal(soundType, 1, 1, PlayMode.Positioned, null, soundPosition, playFinishedCallback);

        /// <summary>
        /// Plays the specified sound while following the specified transform's position.
        /// If multiple sounds are registed for the given type, selects one randomly.
        /// </summary>
        /// <param name="soundType">The pre-defined identifier of the sound to play.</param>
        /// <param name="targetTransform">The transform to be followed during the playback.</param>
        /// <param name="playFinishedCallback">Delegate to be invoked when playback completed.</param>
        /// <returns>Returns the AudioSource that is playing the requested sound. Don't mess with it if you want the soundmanager to work dependably.</returns>
        public AudioSource PlaySoundPositioned(GameSound soundType, Transform targetTransform, Action<GameSound> playFinishedCallback = null)
            => Play_Internal(soundType, 1, 1, PlayMode.Tracking, targetTransform, _zeroVector, playFinishedCallback);

        /// <summary>
        /// Plays the specified sound with volume and pitch overrides.
        /// If multiple sounds are registed for the given type, selects one randomly.
        /// </summary>
        /// <param name="soundType">The pre-defined identifier of the sound to play.</param>
        /// <param name="volumeMultiplier">The multiplier to apply to the volume. Applies on top of the random range value defined in Inspector.</param>
        /// <param name="pitchMultiplier">The multiplier to apply to the pitch. Applies on top of the random range value defined in Inspector.</param>
        /// <param name="playFinishedCallback">Delegate to be invoked when playback completed.</param>
        /// <returns>Returns the AudioSource that is playing the requested sound. Don't mess with it if you want the soundmanager to work dependably.</returns>
        public AudioSource PlaySound(GameSound soundType, float volumeMultiplier, float pitchMultiplier, Action<GameSound> playFinishedCallback = null)
            => Play_Internal(soundType, volumeMultiplier, pitchMultiplier, PlayMode.Simple, null, _zeroVector, playFinishedCallback);

        /// <summary>
        /// Plays the specified sound with volume and pitch overrides, at the specified world position.
        /// If multiple sounds are registed for the given type, selects one randomly.
        /// </summary>
        /// <param name="soundType">The pre-defined identifier of the sound to play.</param>
        /// <param name="soundPosition">The world position of the sound playback.</param>
        /// <param name="volumeMultiplier">The multiplier to apply to the volume. Applies on top of the random range value defined in Inspector.</param>
        /// <param name="pitchMultiplier">The multiplier to apply to the pitch. Applies on top of the random range value defined in Inspector.</param>
        /// <param name="playFinishedCallback">Delegate to be invoked when playback completed.</param>
        /// <returns>Returns the AudioSource that is playing the requested sound. Don't mess with it if you want the soundmanager to work dependably.</returns>
        public AudioSource PlaySoundPositioned(GameSound soundType, float volumeMultiplier, float pitchMultiplier, Vector3 soundPosition, Action<GameSound> playFinishedCallback = null)
            => Play_Internal(soundType, volumeMultiplier, pitchMultiplier, PlayMode.Positioned, null, soundPosition, playFinishedCallback);

        /// <summary>
        /// Plays the specified sound with volume and pitch overrides, while following the specified transform's position.
        /// If multiple sounds are registed for the given type, selects one randomly.
        /// </summary>
        /// <param name="soundType">The pre-defined identifier of the sound to play.</param>
        /// <param name="targetTransform">The transform to be followed during the playback.</param>
        /// <param name="volumeMultiplier">The multiplier to apply to the volume. Applies on top of the random range value defined in Inspector.</param>
        /// <param name="pitchMultiplier">The multiplier to apply to the pitch. Applies on top of the random range value defined in Inspector.</param>
        /// <param name="playFinishedCallback">Delegate to be invoked when playback completed.</param>
        /// <returns>Returns the AudioSource that is playing the requested sound. Don't mess with it if you want the soundmanager to work dependably.</returns>
        public AudioSource PlaySoundPositioned(GameSound soundType, float volumeMultiplier, float pitchMultiplier, Transform targetTransform, Action<GameSound> playFinishedCallback = null)
            => Play_Internal(soundType, volumeMultiplier, pitchMultiplier, PlayMode.Tracking, targetTransform, _zeroVector, playFinishedCallback);

        private AudioSource Play_Internal(GameSound soundType, float volumeMultiplier, float pitchMultiplier, PlayMode playMode, Transform targetTransform, Vector3 targetPosition, Action<GameSound> playFinishedCallback = null)
        {
            var (canPlay, soundList) = SoundPlayPreChecks(soundType);

            if (!canPlay)
                return null;

            return _soundPlayerPool.Pop()
                .Play(GetRandomSound(soundList), volumeMultiplier, pitchMultiplier, playMode, targetTransform, targetPosition, playFinishedCallback);
        }

        /// <summary>
        /// Converts the Editor-compatible array into a fast-lookup dictionary map.
        /// Creates a list for each sound type, to support multiple sounds of the same type.
        /// </summary>
        private void PopulateSoundMap()
        {
            // No sounds entries defined at all
            if (_soundList == null || _soundList.Length == 0)
            {
                _log?.SoundEntries_NoneDefined();
                return;
            }

            foreach (var s in _soundList)
            {
                // Silently skip entries where 'None' is selected as soundtype
                if (s.soundType == GameSound.None)
                    continue;

                // Skip entries where audioclip is missing
                if (s.audioClip == null)
                {
                    _log?.SoundEntries_FaultyEntry_NoAudioClip(s.soundType);
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
            if (_checkUnassignedSounds && _log != null)
                LogUnassignedSoundTypes();
        }

        /// <summary>
        /// Checks all GameSound enum values to see if there is at least one sound assigned to it.
        /// If it finds enum values without any sound assigned, logs a warning to the console with the list of missing sound types.
        /// </summary>
        private void LogUnassignedSoundTypes()
        {
            List<GameSound> missingSoundsList = null;
            foreach (GameSound soundType in Enum.GetValues(typeof(GameSound)))
            {
                if (soundType == GameSound.None || _soundMap.ContainsKey(soundType))
                    continue;

                // Instantiation in this scope to avoid allocation if no reporting is needed.
                if (missingSoundsList == null)
                    missingSoundsList = new List<GameSound>();

                missingSoundsList.Add(soundType);
            }

            if (missingSoundsList != null)
                _log?.SoundEntries_SomeNotDefined(missingSoundsList);
        }

        /// <summary>
        /// Grows pool by the specified number. Creates pool if it doesn't yet exist.
        /// </summary>
        private void GrowPool(int num)
        {
            if (_soundPlayerPool == null)
                CreatePool(num);

            for (int i = 0; i < num; i++)
                _soundPlayerPool.Push(CreateSoundPlayer());

            void CreatePool(int capacity)
            {
                // If initial pool size is greater, use that instead
                if (_initialPoolSize > capacity)
                    capacity = _initialPoolSize;
                // If pool can grow, reserve double
                if (_canGrowPool)
                    capacity *= 2;

                _soundPlayerPool = new Stack<SoundPlayer>(capacity);
            }

            SoundPlayer CreateSoundPlayer()
            {
                _soundPlayerNameIndex++;
                var go = new GameObject(SOUNDPLAYER_GO_NAMEBASE + _soundPlayerNameIndex);
                go.transform.parent = this.transform;

                var audioSource = go.AddComponent<AudioSource>();

                var soundPlayer = new SoundPlayer(audioSource, this, _log);
                soundPlayer.PlaybackComplete += OnPlaybackFinished;

                return soundPlayer;
            }
        }

        /// <summary>
        /// Returns SoundPlayer to the pool after it finished playing a sound.
        /// </summary>
        private void OnPlaybackFinished(SoundPlayer player)
            => _soundPlayerPool.Push(player);

        /// <summary>
        /// Returns whether playback of a given soundtype is possible, and if so, returns all sound variations available for the given soundtype (or null).
        /// </summary>
        private (bool canPlay, List<SoundEntity> availableSounds) SoundPlayPreChecks(GameSound soundType)
        {
            // Initialization is expected to happen earlier
            if (!_initialized)
            {
                _log?.Initialization_HadToExpedite();
                Awake();
            }

            // Sound type 'None' is not valid for playback
            if (soundType == GameSound.None)
            {
                _log?.PlaybackFail_NoneSoundRequested();
                return (canPlay: false, null);
            }

            var soundListExists = _soundMap.TryGetValue(soundType,
                out var soundList); // Note out var

            // No valid sound entries are defined for the requested soundtype - i.e. nothing to play
            if (!soundListExists)
            {
                _log?.PlaybackFail_NoEntryDefined(soundType);
                return (canPlay: false, null);
            }

            if (_soundPlayerPool.Count == 0)
            {
                // Playback fails if pool is exhausted, and we can't grow
                if (!_canGrowPool)
                {
                    _log?.PlaybackFail_PoolExhausted();
                    return (canPlay: false, null);
                }

                // If pool can grow, grow pool, and proceed with playback
                _log?.PoolHadToGrow();
                GrowPool(1);
            }

            return (canPlay: true, soundList);
        }

        /// <summary>
        /// Returns a random sound from a list of sounds.
        /// </summary>
        private SoundEntity GetRandomSound(List<SoundEntity> list)
            => list[Random.Range(0, list.Count)];

        /// <summary>
        /// Encapsulates message logging. Bit messy, but helps to totally avoid string operations and allocations if logging is disabled.
        /// </summary>
        private class SoundManagerDebugLogger
        {
            public void SoundEntries_NoneDefined()
                => Debug.LogWarning($"No sound entries are defined for the {nameof(SoundManager)}. Won't be able to play any sounds.");

            public void SoundEntries_SomeNotDefined(List<GameSound> typesWithoutEntry)
                => Debug.LogWarning($"{nameof(SoundManager)} initialization didn't find any valid sound entries for the following sound types (these sounds cannot play): " +
                    string.Join(", ", typesWithoutEntry.ToArray()));

            public void SoundEntries_FaultyEntry_NoAudioClip(GameSound missingClipType)
                => Debug.LogWarning($"An entry for soundtype '{missingClipType}' missing its {nameof(AudioClip)}. This entry will be ignored.");

            public void Initialization_HadToExpedite()
                => Debug.LogWarning("Sound playback was requested before SoundManager was initialized. Initializing now.");

            public void PlaybackFail_NoneSoundRequested()
                => Debug.LogWarning("Sound playback failed. Soundtype 'None' was requested to play. Specify a valid soundtype.");

            public void PlaybackFail_NoEntryDefined(GameSound soundType)
                => Debug.LogWarning($"Sound playback failed. Soundtype '{soundType}' has no sounds assigned to it.");

            public void PlaybackFail_PoolExhausted()
                => Debug.LogWarning($"Sound playback failed, because no {nameof(AudioSource)} was available. " +
                    $"Increase the initial pool of {nameof(AudioSource)}s, or enable the on-demand creation of {nameof(AudioSource)}s.");

            public void PoolHadToGrow()
                => Debug.LogWarning($"All {nameof(AudioSource)}s were busy. New {nameof(AudioSource)} had to be instantiated for playback. " +
                    $"If you see this often, it's advisable to increase the initial pool of {nameof(AudioSource)}s.");

            public void AudioSourceNeededExtraWait(int extraWaitNum)
                => Debug.LogWarning($"{nameof(AudioSource)} wasn't ready for release at the expected time. Waiting cycle: {extraWaitNum}. " +
                    $"\nIf you see this often, consider increasing the {nameof(RELEASE_MARGIN)} constant.");
        }

        /// <summary>
        /// Encapsulates sound playback and AudioSource handling responsibilities.
        /// Provides notification of playback completion.
        /// </summary>
        private class SoundPlayer
        {
            public event Action<SoundPlayer> PlaybackComplete;
            public bool IsPlaying => _isWaiting;

            private readonly SoundManagerDebugLogger _log;
            private readonly AudioSource _audioSource;
            private readonly Transform _audioTransform;
            private readonly ICoroutineControl _coroutines;

            private GameSound _currentSound;
            private Action<GameSound> _currentCallback;
            private Coroutine _currentCoroutine;
            private bool _isWaiting;

            public SoundPlayer(AudioSource audioSource, ICoroutineControl coroutineControl, SoundManagerDebugLogger log)
            {
                _log = log;
                _audioSource = audioSource;
                _coroutines = coroutineControl;

                // Cache
                _audioTransform = _audioSource.transform;
            }

            // Not used currently
            public void StopImmediate(Vector3 restorePosition)
            {
                if (!_isWaiting)
                    throw new InvalidOperationException("Cannot stop, no active playback is registered.");

                _audioSource.Stop();
                _audioTransform.position = restorePosition;
                _coroutines.StopCoroutine(_currentCoroutine);
                _isWaiting = false;
                DoStopped();
            }

            public AudioSource Play(SoundEntity soundEntity, float volumeMultiplier, float pitchMultiplier, 
                PlayMode playMode, Transform targetTransform, Vector3 targetPosition, Action<GameSound> callback = null)
            {
                _currentSound = soundEntity.soundType;
                _currentCallback = callback;
                var waitingTime = Play_Internal(soundEntity, volumeMultiplier, pitchMultiplier);

                switch (playMode)
                {
                    case PlayMode.Simple:
                        // Vanilla waiter
                        _currentCoroutine = _coroutines.StartCoroutine(
                            PlaybackWaiter(waitingTime));
                        break;
                    case PlayMode.Positioned:
                        // Positioned waiter
                        _currentCoroutine = _coroutines.StartCoroutine(
                            PlaybackWaiter_Positioned(waitingTime, targetPosition));
                        break;
                    case PlayMode.Tracking:
                        // Tracking waiter
                        _currentCoroutine = _coroutines.StartCoroutine(
                            PlaybackWaiter_Tracking(waitingTime, targetTransform));
                        break;
                    default:
                        throw new InvalidOperationException($"Cannot process unknown {nameof(PlayMode)} '{playMode}'.");
                }

                return _audioSource;
            }

            /// <summary>
            /// Preps the AudioSource and plays the specified sound.
            /// </summary>
            private float Play_Internal(SoundEntity sound, float volumeMultiplier, float pitchMultiplier)
            {
                // Prepare audio source
                var pitch = Random.Range(sound.pitchLow, sound.pitchHigh) * pitchMultiplier;
                _audioSource.volume = Random.Range(sound.volumeLow, sound.volumeHigh) * volumeMultiplier;
                _audioSource.pitch = pitch;
                _audioSource.clip = sound.audioClip;

                // Calculate actual time length of sound playback 
                var playTime = Mathf.Abs(sound.audioClip.length / pitch); // Abs() is to support negative pitch

                // Start actual playback
                _audioSource.Play();

                return playTime;
            }

            /// <summary>
            /// Waits for audio playback to finish, then executes notifications
            /// </summary>
            private IEnumerator PlaybackWaiter(float releaseAfterSeconds)
            {
                _isWaiting = true;

                // Actual wait
                yield return new WaitForSecondsRealtime(releaseAfterSeconds + RELEASE_MARGIN);

                // Make sure it's actually finished
                int extraWaits = 0;
                while (_audioSource.isPlaying)
                {
                    // Report extra wait
                    _log?.AudioSourceNeededExtraWait(++extraWaits);
                    yield return new WaitForSeconds(RETRYRELEASE_WAIT);
                }

                _isWaiting = false;
                DoStopped();
            }

            /// <summary>
            /// Extends normal waiter with a feature: positions the AudioSource's transform to follow another transform during playback
            /// </summary>
            private IEnumerator PlaybackWaiter_Tracking(float releaseAfterSeconds, Transform target)
            {
                var origin = _audioTransform.position;

                // Execute wait, as separately running coroutine
                _coroutines.StartCoroutine(PlaybackWaiter(releaseAfterSeconds));

                while (_isWaiting)
                {
                    _audioTransform.position = target.position;
                    yield return null;
                }

                _audioTransform.position = origin;
            }

            /// <summary>
            /// Extends normal waiter with a feature: positions the AudioSource's transform during playback
            /// </summary>
            private IEnumerator PlaybackWaiter_Positioned(float releaseAfterSeconds, Vector3 soundPosition)
            {
                var origin = _audioTransform.position;
                _audioTransform.position = soundPosition;

                // Execute wait, waiting for its completion
                yield return PlaybackWaiter(releaseAfterSeconds);

                _audioTransform.transform.localPosition = origin;
            }

            /// <summary>
            /// Executes responsibilities due at playback completion
            /// </summary>
            private void DoStopped()
            {
                if (_isWaiting)
                    throw new InvalidOperationException("Playback completion handling cannot execute. Active playback still registered.");

                PlaybackComplete?.Invoke(this);
                _currentCallback?.Invoke(_currentSound);

                _currentCallback = null;
                _currentCoroutine = null;
                _currentSound = GameSound.None;
            }
        }

        private enum PlayMode
        {
            Simple,
            Positioned,
            Tracking
        }
    }

    public interface ICoroutineControl
    {
        Coroutine StartCoroutine(IEnumerator routine);
        void StopCoroutine(Coroutine routine);
    }
}
