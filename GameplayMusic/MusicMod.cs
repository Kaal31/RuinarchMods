using System;
using System.Collections;
using System.IO;
using HarmonyLib;
using Ruinarch.Modding;
using Settings;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace GameplayMusic
{
    public sealed class MusicMod : IRuinarchMod
    {
        public void OnLoad(ModContext context)
        {
            var harmony = new Harmony("local.gameplaymusic");
            var go = new GameObject("GameplayMusic");
            UnityEngine.Object.DontDestroyOnLoad(go);
            try
            {
                go.AddComponent<MusicPlayer>().Initialize(context);
                harmony.PatchAll(typeof(MusicMod).Assembly);
            }
            catch
            {
                harmony.UnpatchAll("local.gameplaymusic");
                UnityEngine.Object.Destroy(go);
                throw;
            }
        }
    }

    [Serializable]
    public sealed class MusicConfig
    {
        public bool enabled = true;
        public bool shuffle = true;
        public float volume = 1f;
        public bool dynamicMusic = true;
        public float crossfadeSeconds = 3f;
        public float minimumMoodSeconds = 12f;
        public float disasterSeconds = 25f;
        public float summonSeconds = 12f;
        public float threatReleaseSeconds = 8f;
    }

    public sealed class MusicPlayer : MonoBehaviour
    {
        internal static MusicPlayer Instance;
        private ModLogger log;
        private MusicConfig config = new MusicConfig();
        private MusicLibrary library;
        private readonly MusicDirector director = new MusicDirector();
        private AudioSource source, outgoing;
        private AudioListener fallbackListener;
        private UnityWebRequest request;
        private Mood current;
        private bool loading, muted, wasInGame, fading;
        private float retryAt, scanAt, listenerAt, startedAt, fadeAt, fadeDuration;
        private float baseVolume, disasterLogAt;

        public void Initialize(ModContext context)
        {
            log = context.Logger;
            string folder = Path.Combine(context.ModDirectory, "Music");
            Directory.CreateDirectory(folder);
            foreach (Mood mood in Enum.GetValues(typeof(Mood))) Directory.CreateDirectory(Path.Combine(folder, mood.ToString()));
            try
            {
                string path = Path.Combine(context.ModDirectory, "config.json");
                // Preserve initialized defaults for settings absent in older configs.
                if (File.Exists(path)) JsonUtility.FromJsonOverwrite(File.ReadAllText(path), config);
            }
            catch (Exception ex) { config = new MusicConfig(); log.Warning("Invalid config; using defaults: " + ex.Message); }
            config.volume = Valid(config.volume, 1f, 0f, 1f);
            config.crossfadeSeconds = Valid(config.crossfadeSeconds, 3f, 0f, 15f);
            config.minimumMoodSeconds = Valid(config.minimumMoodSeconds, 12f, 0f, 120f);
            config.disasterSeconds = Valid(config.disasterSeconds, 25f, 1f, 300f);
            config.summonSeconds = Valid(config.summonSeconds, 12f, 1f, 120f);
            config.threatReleaseSeconds = Valid(config.threatReleaseSeconds, 8f, 0f, 120f);
            library = new MusicLibrary(folder, config.shuffle);
            source = MakeSource(); outgoing = MakeSource();
            Instance = this;
            log.Info("Dynamic music ready: Ambient, Threat, Disaster, Summon. Root Music tracks remain ambient. Priority: Threat > Disaster > Summon > Ambient.");
        }

        private static float Valid(float value, float fallback, float min, float max)
        { return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max); }

        private AudioSource MakeSource()
        {
            AudioSource result = gameObject.AddComponent<AudioSource>();
            result.playOnAwake = false; result.spatialBlend = 0f; result.ignoreListenerPause = true;
            result.volume = 0f;
            return result;
        }

        private bool InGame()
        {
            return config.enabled && SceneManager.GetActiveScene().name == "Game" &&
                GameManager.Instance != null && GameManager.Instance.gameHasStarted &&
                (LevelLoaderManager.Instance == null || (!LevelLoaderManager.Instance.isLoadingNewScene &&
                !LevelLoaderManager.Instance.IsLoadingScreenActive()));
        }

        internal void OnSummon()
        {
            if (!config.dynamicMusic || !InGame()) return;
            director.Summon(Time.realtimeSinceStartup, config.summonSeconds);
        }

        internal void OnDisaster(string reason)
        {
            if (!config.dynamicMusic || !InGame()) return;
            float now = Time.realtimeSinceStartup;
            director.Disaster(now, config.disasterSeconds);
            if (now >= disasterLogAt) { log.Info("Disaster activity: " + reason); disasterLogAt = now + 10f; }
        }

        private Mood Wanted(float now)
        {
            bool threat = PlayerManager.Instance != null && PlayerManager.Instance.player != null &&
                PlayerManager.Instance.player.retaliationComponent != null && PlayerManager.Instance.player.retaliationComponent.isRetaliating;
            // This also detects games loaded during an existing retaliation.
            Mood mood = config.dynamicMusic ? director.Evaluate(now, threat, config.threatReleaseSeconds) : Mood.Ambient;
            return library.Resolve(mood);
        }

        private float MusicVolume()
        { return SettingsManager.Instance == null ? 100f : SettingsManager.Instance.settings.musicVolume; }

        private void LateUpdate()
        {
            if (source == null || library == null) return;
            if (!InGame())
            {
                if (wasInGame) StopPlayback();
                wasInGame = false;
                return;
            }
            wasInGame = true;
            float now = Time.realtimeSinceStartup;
            if (now >= scanAt)
            {
                try { library.Refresh(); }
                catch (Exception ex) { log.Warning("Cannot scan music: " + ex.Message); }
                scanAt = now + 10f;
            }
            if (muted && now >= listenerAt) { EnsureListener(); listenerAt = now + 2f; }
            float master = SettingsManager.Instance == null ? 100f : SettingsManager.Instance.settings.masterVolume;
            baseVolume = config.volume * Mathf.Clamp01(master / 100f) * Mathf.Clamp01(MusicVolume() / 100f);
            UpdateFade(now);
            Mood wanted = Wanted(now);
            if (!loading && !fading && now >= retryAt)
            {
                bool change = MusicDirector.CanSwitch(current, wanted, now - startedAt, config.minimumMoodSeconds, source.isPlaying);
                bool ending = !source.isPlaying || (source.clip != null && source.clip.length - source.time <= Mathf.Min(config.crossfadeSeconds, source.clip.length * 0.5f));
                if (change || ending) StartCoroutine(PlayNext(change || !source.isPlaying ? wanted : current));
            }
            if (muted) AkSoundEngine.SetRTPCValue("Music_Volume", 0f);
        }

        private void UpdateFade(float now)
        {
            float amount = fading ? (fadeDuration <= 0f ? 1f : Mathf.Clamp01((now - fadeAt) / fadeDuration)) : 1f;
            source.volume = baseVolume * amount;
            outgoing.volume = baseVolume * (1f - amount);
            if (fading && amount >= 1f) { Release(outgoing); fading = false; }
            if (!source.isPlaying && !outgoing.isPlaying && !loading) RestoreMusic();
        }

        private IEnumerator PlayNext(Mood mood)
        {
            loading = true;
            try
            {
                string path = library.Next(mood);
                if (path == null)
                {
                    // An expired event fades out even when no ambient tracks were supplied.
                    if (source.isPlaying)
                    {
                        Release(outgoing);
                        AudioSource swap = outgoing; outgoing = source; source = swap;
                        BeginFade(config.crossfadeSeconds);
                    }
                    else RestoreMusic();
                    current = mood;
                    retryAt = Time.realtimeSinceStartup + 2f;
                    yield break;
                }
                string ext = Path.GetExtension(path).ToLowerInvariant();
                AudioType type = ext == ".ogg" ? AudioType.OGGVORBIS : ext == ".wav" ? AudioType.WAV : AudioType.MPEG;
                AudioClip clip = null;
                string error = null;
                try
                {
                    request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, type);
                    request.timeout = 30;
                    request.SendWebRequest();
                }
                catch (Exception ex) { error = ex.Message; }
                if (error == null)
                {
                    while (!request.isDone)
                    {
                        Mood pending = Wanted(Time.realtimeSinceStartup);
                        if (!InGame() || (pending != mood && (pending > mood || mood != current)))
                        { request.Abort(); yield break; }
                        yield return null;
                    }
                    try
                    {
                        if (request.result != UnityWebRequest.Result.Success) error = request.error;
                        else clip = DownloadHandlerAudioClip.GetContent(request);
                    }
                    catch (Exception ex) { error = ex.Message; }
                }
                if (clip == null || clip.length <= 0f || clip.loadState == AudioDataLoadState.Failed)
                {
                    if (clip != null) Destroy(clip);
                    library.Reject(path);
                    log.Warning("Skipping " + Path.GetFileName(path) + ": " + (error ?? "audio could not be decoded"));
                    retryAt = Time.realtimeSinceStartup + 0.5f;
                    yield break;
                }
                float now = Time.realtimeSinceStartup;
                Mood wanted = Wanted(now);
                if (!InGame() || (wanted != mood && (wanted > mood || mood != current)))
                { Destroy(clip); yield break; }
                EnsureListener();
                Release(outgoing);
                AudioSource old = source; source = outgoing; outgoing = old;
                source.clip = clip; source.volume = 0f; source.Play();
                muted = true;
                AkSoundEngine.SetRTPCValue("Music_Volume", 0f);
                if (current != mood || !outgoing.isPlaying) startedAt = now;
                current = mood;
                BeginFade(Mathf.Min(config.crossfadeSeconds, clip.length * 0.5f));
                log.Info("Playing [" + mood + "] " + Path.GetFileName(path));
            }
            finally
            {
                if (request != null) { request.Dispose(); request = null; }
                loading = false;
            }
        }

        private void BeginFade(float seconds)
        { fading = true; fadeAt = Time.realtimeSinceStartup; fadeDuration = seconds; }

        private void EnsureListener()
        {
            bool hasOther = false;
            foreach (AudioListener listener in UnityEngine.Object.FindObjectsOfType<AudioListener>())
                if (listener != fallbackListener && listener.isActiveAndEnabled) { hasOther = true; break; }
            if (!hasOther && fallbackListener == null) fallbackListener = gameObject.AddComponent<AudioListener>();
            if (fallbackListener != null) fallbackListener.enabled = !hasOther;
        }

        private void Release(AudioSource audio)
        {
            if (audio == null) return;
            audio.Stop();
            AudioClip old = audio.clip; audio.clip = null;
            if (old != null) Destroy(old);
        }

        private void RestoreMusic()
        {
            if (muted) { AkSoundEngine.SetRTPCValue("Music_Volume", MusicVolume()); muted = false; }
            if (fallbackListener != null) fallbackListener.enabled = false;
        }

        private void StopPlayback()
        {
            StopAllCoroutines();
            if (request != null) { request.Abort(); request.Dispose(); request = null; }
            loading = fading = false;
            Release(source); Release(outgoing); RestoreMusic();
            director.Reset();
            if (library != null) library.Reset();
            current = Mood.Ambient;
            retryAt = scanAt = listenerAt = startedAt = disasterLogAt = 0f;
        }

        private void OnDisable() { StopPlayback(); if (Instance == this) Instance = null; }
    }
}
