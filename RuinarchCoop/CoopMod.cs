using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using HarmonyLib;
using Ruinarch.Modding;
using UnityEngine;
using UnityEngine.SceneManagement;
using UtilityScripts;

namespace RuinarchCoop
{
    public sealed class CoopMod : IRuinarchMod
    {
        public void OnLoad(ModContext context)
        {
            new Harmony("kaal31.ruinarch.coop").PatchAll(typeof(CoopMod).Assembly);
            var go = new GameObject("RuinarchCoop");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var menu = go.AddComponent<CoopSession>();
            menu.ModDirectory = context.ModDirectory;
            menu.Log = context.Logger;
        }
    }

    public sealed class CoopSession : MonoBehaviour
    {
        internal static CoopSession Instance;
        internal string ModDirectory;
        internal ModLogger Log;
        internal static bool Frozen { get { return Instance != null && Instance.frozen; } }
        internal static bool PermitSave;
        private bool frozen, host, busy, open = true, awaitingLoad;
        private bool networkActive;
        private Rect window = new Rect(16, 78, 440, 340);
        private string address = "127.0.0.1", port = "29471", code = "", status = "Offline";
        private string snapshotPath;
        private float deadline;
        private int generation;
        private TcpListener listener;
        private TcpClient connection;
        private volatile bool guestLoaded;
        private readonly Queue<Action> actions = new Queue<Action>();
        private readonly Dictionary<CanvasGroup, bool> groups = new Dictionary<CanvasGroup, bool>();
        private bool InWorld { get { return GameManager.Instance != null && GameManager.Instance.gameHasStarted; } }
        private void Awake() { Instance = this; }
        private void OnDestroy() { StopNetwork(); RestoreUI(); if (Instance == this) Instance = null; }
        private void OnApplicationQuit() { StopNetwork(); }
        private void Post(int id, Action action)
        {
            lock (actions) actions.Enqueue(() => { if (id == generation) action(); });
        }
        private void Update()
        {
            lock (actions)
            {
                while (actions.Count > 0)
                {
                    try { actions.Dequeue()(); }
                    catch (Exception ex) { Fail(ex); }
                }
            }
            if (!frozen) return;
            if (SceneManager.GetActiveScene().name == "MainMenu" && !busy && !awaitingLoad)
            {
                StopNetwork(); frozen = false; RestoreUI(); status = "Offline"; return;
            }
            if (InWorld)
            {
                if (!GameManager.Instance.isPaused) GameManager.Instance.SetPausedState(true);
                // Keep camera controls active; disable gameplay buttons for this read-only milestone.
                LockCanvas(UIManager.Instance == null ? null : UIManager.Instance.canvas);
                LockCanvas(UIManager.Instance == null ? null : UIManager.Instance.smallInfoCanvas);
            }
            if (awaitingLoad && InWorld && !LevelLoaderManager.Instance.isLoadingNewScene &&
                !LevelLoaderManager.Instance.IsLoadingScreenActive())
            {
                awaitingLoad = false; guestLoaded = true; busy = false;
                status = networkActive ? "World loaded. Independent camera test ready. Both worlds are paused." : "World loaded after disconnect. Remains paused; return to main menu.";
                Log.Info("COOP_GUEST_LOADED archive=" + Path.GetFileName(snapshotPath));
            }
            else if (awaitingLoad && Time.realtimeSinceStartup > deadline)
            {
                awaitingLoad = false; busy = false;
                Fail(new TimeoutException("World loading exceeded 180 seconds. Check the game log."));
            }
        }
        private void LockCanvas(Canvas canvas)
        {
            if (canvas == null) return;
            var g = canvas.GetComponent<CanvasGroup>();
            if (g == null) g = canvas.gameObject.AddComponent<CanvasGroup>();
            if (!groups.ContainsKey(g)) groups.Add(g, g.interactable);
            g.interactable = false;
        }
        private void RestoreUI()
        {
            foreach (var p in groups) if (p.Key != null) p.Key.interactable = p.Value;
            groups.Clear();
        }
        internal static bool PointerOverUI()
        {
            var i = Instance;
            if (i == null) return false;
            var p = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return new Rect(108, 4, 110, 28).Contains(p) || (i.open && i.window.Contains(p));
        }
        private void OnGUI()
        {
            if (GUI.Button(new Rect(108, 4, 110, 28), "CO-OP TEST")) open = !open;
            if (open) window = GUILayout.Window(0xC001, window, Draw, "Co-op 0.1 — paused world-transfer test");
        }
        private void Draw(int id)
        {
            GUILayout.Label("First milestone: same saved world, separate cameras.\nLive world updates and ability casting are not available.");
            GUILayout.Label("Host address (guest only)"); address = GUILayout.TextField(address, 255);
            GUILayout.Label("TCP port"); port = GUILayout.TextField(port, 5);
            GUILayout.Label("Session code (host generates one)"); code = GUILayout.TextField(code, 32);
            GUILayout.Label(status);
            GUI.enabled = !busy && !frozen;
            if (GUILayout.Button("Host current world (pauses and saves)")) Run(Host);
            if (GUILayout.Button("Join from main menu")) Run(Join);
            GUI.enabled = frozen || busy;
            if (GUILayout.Button(host ? "End test — keep host paused" : "Disconnect / cancel"))
            {
                StopNetwork(); busy = false;
                if (host || !InWorld && !awaitingLoad) { frozen = false; RestoreUI(); }
                status = frozen ? "Disconnected. Guest stays paused; return to the main menu." : "Test ended. You may resume your game.";
            }
            GUI.enabled = frozen && !host && InWorld && !awaitingLoad;
            if (GUILayout.Button("Return guest to main menu"))
            {
                StopNetwork(); awaitingLoad = false; busy = false;
                LevelLoaderManager.Instance.LoadLevel("MainMenu");
            }
            GUI.enabled = true;
            GUI.DragWindow(new Rect(0, 0, 440, 22));
        }
        private void Run(Action action) { try { action(); } catch (Exception ex) { Fail(ex); } }
        private int Port()
        {
            int n;
            if (!int.TryParse(port, out n) || n < 1024 || n > 65535) throw new Exception("Use a port from 1024 to 65535.");
            return n;
        }
        private string Fingerprint()
        {
            // Exact binaries and mod configuration must match on both PCs.
            var lines = new List<string> { "protocol=" + Wire.Protocol, "game=" + Application.version,
                "assembly=" + Wire.Hash(File.ReadAllBytes(typeof(GameManager).Assembly.Location)) };
            string root = Path.GetDirectoryName(ModDirectory);
            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
            {
                if (!file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                lines.Add(file.Substring(root.Length).Replace('\\', '/') + "=" + Wire.Hash(File.ReadAllBytes(file)));
            }
            return Wire.Hash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", lines.ToArray())));
        }
        private void Host()
        {
            if (!InWorld) throw new Exception("Create/load a small world and place your portal first.");
            var save = SaveManager.Instance.saveCurrentProgressManager;
            if (save.isSaving || save.isWritingToDisk || !save.CanSaveCurrentProgress()) throw new Exception("Wait until the game can save.");
            int n = Port(); string fingerprint = Fingerprint();
            code = Guid.NewGuid().ToString("N").Substring(0, 12);
            host = true; frozen = true; busy = true; networkActive = true;
            GameManager.Instance.SetPausedState(true);
            status = "Creating complete world snapshot…";
            StartCoroutine(Capture(n, fingerprint, code, generation));
        }
        private IEnumerator Capture(int n, string fingerprint, string sessionCode, int id)
        {
            string name = "CoopSnapshot-" + Guid.NewGuid().ToString("N");
            snapshotPath = Path.Combine(Utilities.gameSavePath, name + ".zip");
            var save = SaveManager.Instance.saveCurrentProgressManager;
            // The save callback fires BEFORE the zip is written. Wait for both save stages.
            PermitSave = true;
            try { save.DoManualSave(name); }
            finally { PermitSave = false; }
            float end = Time.realtimeSinceStartup + 180;
            yield return null;
            while (!File.Exists(snapshotPath) || save.isSaving || save.isWritingToDisk)
            {
                if (id != generation) yield break;
                if (Time.realtimeSinceStartup > end) { Fail(new TimeoutException("Snapshot save timed out.")); yield break; }
                yield return null;
            }
            if (id != generation) yield break;
            try
            {
                var bytes = File.ReadAllBytes(snapshotPath);
                Wire.ValidateArchive(bytes);
                listener = new TcpListener(IPAddress.Any, n); listener.Start(1);
                var server = listener;
                busy = false; status = "Listening on port " + n + ". Share your LAN address and session code.";
                Log.Info("COOP_HOST_SNAPSHOT sha256=" + Wire.Hash(bytes));
                StartWorker(id, () => HostWorker(server, bytes, fingerprint, sessionCode, id));
            }
            catch (Exception ex) { Fail(ex); }
        }
        private void HostWorker(TcpListener server, byte[] bytes, string fingerprint, string sessionCode, int id)
        {
            using (var peer = server.AcceptTcpClient())
            {
                if (id != generation) return;
                connection = peer; Configure(peer);
                var stream = peer.GetStream();
                if (Wire.ReadText(stream) != "RUINARCH-COOP-1" || Wire.ReadText(stream) != sessionCode || Wire.ReadText(stream) != fingerprint)
                    throw new InvalidDataException("Session code or game/mod versions do not match.");
                Wire.WriteText(stream, "OK"); Wire.SendArchive(stream, bytes);
                Post(id, () => status = "Snapshot sent. Waiting for the guest to load…");
                bool reported = false;
                while (id == generation)
                {
                    Wire.WriteText(stream, "PING");
                    string response = Wire.ReadText(stream);
                    if (response != "LOADING" && response != "READY") throw new InvalidDataException("Invalid peer response");
                    if (response == "READY" && !reported)
                    {
                        reported = true;
                        Post(id, () => { status = "Guest loaded the snapshot. Test panning/zooming independently."; Log.Info("COOP_PEER_READY"); });
                    }
                    Thread.Sleep(1000);
                }
            }
        }
        private void Join()
        {
            if (SceneManager.GetActiveScene().name != "MainMenu") throw new Exception("Join from the main menu to protect your current world.");
            if (string.IsNullOrWhiteSpace(code)) throw new Exception("Enter the host's session code.");
            int n = Port(); string fingerprint = Fingerprint(), target = address.Trim(), sessionCode = code.Trim();
            string cache = Path.Combine(ModDirectory, "SessionCache"); Directory.CreateDirectory(cache);
            snapshotPath = Path.Combine(cache, "received-" + Guid.NewGuid().ToString("N") + ".zip");
            string path = snapshotPath;
            host = false; frozen = true; busy = true; guestLoaded = false; networkActive = true;
            status = "Connecting…";
            int id = generation;
            StartWorker(id, () =>
            {
                using (var peer = new TcpClient())
                {
                    if (id != generation) return;
                    connection = peer;
                    var connect = peer.BeginConnect(target, n, null, null);
                    using (connect.AsyncWaitHandle)
                    {
                        if (!connect.AsyncWaitHandle.WaitOne(10000)) throw new TimeoutException("Connection timed out.");
                        peer.EndConnect(connect);
                    }
                    Configure(peer); var stream = peer.GetStream();
                    Wire.WriteText(stream, "RUINARCH-COOP-1"); Wire.WriteText(stream, sessionCode); Wire.WriteText(stream, fingerprint);
                    if (Wire.ReadText(stream) != "OK") throw new Exception("Host rejected this connection.");
                    var bytes = Wire.ReceiveArchive(stream);
                    if (id != generation) return;
                    File.WriteAllBytes(path + ".part", bytes); File.Move(path + ".part", path);
                    Post(id, () =>
                    {
                        if (SceneManager.GetActiveScene().name != "MainMenu") throw new Exception("Leave the guest at the main menu while connecting.");
                        status = "Loading received world…"; awaitingLoad = true; deadline = Time.realtimeSinceStartup + 180;
                        Log.Info("COOP_RECEIVED sha256=" + Wire.Hash(bytes));
                        SaveManager.Instance.saveCurrentProgressManager.SetCurrentSaveDataPath(path);
                        MainMenuManager.Instance.StartGame();
                    });
                    while (id == generation)
                    {
                        if (Wire.ReadText(stream) != "PING") throw new InvalidDataException("Invalid host message");
                        Wire.WriteText(stream, guestLoaded ? "READY" : "LOADING");
                    }
                }
            });
        }
        private static void Configure(TcpClient peer)
        {
            peer.NoDelay = true; peer.ReceiveTimeout = 30000; peer.SendTimeout = 30000;
        }
        private void StartWorker(int id, Action action)
        {
            new Thread(() => { try { action(); } catch (Exception ex) { Post(id, () => Fail(ex)); } }) { IsBackground = true, Name = "RuinarchCoop" }.Start();
        }
        private void Fail(Exception ex)
        {
            Log.Error("Co-op: " + ex);
            StopNetwork(); busy = false;
            if (!host && !InWorld && !awaitingLoad) frozen = false;
            status = "Error: " + ex.Message + (frozen ? " World remains paused." : "");
        }
        private void StopNetwork()
        {
            networkActive = false;
            Interlocked.Increment(ref generation);
            if (listener != null) { listener.Stop(); listener = null; }
            if (connection != null) { connection.Close(); connection = null; }
            guestLoaded = false;
        }
    }

    [HarmonyPatch(typeof(GameManager), "SetPausedState")]
    internal static class PauseGuard
    {
        private static void Prefix(ref bool isPaused) { if (CoopSession.Frozen) isPaused = true; }
    }
    [HarmonyPatch(typeof(GameManager), "Update")]
    internal static class TickGuard { private static bool Prefix() { return !CoopSession.Frozen; } }
    [HarmonyPatch(typeof(CharacterTickManager), "Update")]
    internal static class CharacterGuard { private static bool Prefix() { return !CoopSession.Frozen; } }
    [HarmonyPatch(typeof(SaveCurrentProgressManager), "DoManualSave")]
    internal static class SaveGuard { private static bool Prefix() { return !CoopSession.Frozen || CoopSession.PermitSave; } }
    [HarmonyPatch(typeof(UIManager), "IsMouseOnUI")]
    internal static class OverlayGuard
    {
        private static void Postfix(ref bool __result) { if (CoopSession.PointerOverUI()) __result = true; }
    }
    [HarmonyPatch(typeof(Player_Input.SpellInputModule), "OnUpdate")]
    internal static class SpellInputModuleGuard { private static bool Prefix() { return !CoopSession.Frozen; } }
    [HarmonyPatch(typeof(Player_Input.IntelInputModule), "OnUpdate")]
    internal static class IntelInputModuleGuard { private static bool Prefix() { return !CoopSession.Frozen; } }
    [HarmonyPatch(typeof(Player_Input.SeizeInputModule), "OnUpdate")]
    internal static class SeizeInputModuleGuard { private static bool Prefix() { return !CoopSession.Frozen; } }
    [HarmonyPatch(typeof(Player_Input.PickPortalInputModule), "OnUpdate")]
    internal static class PickPortalInputModuleGuard { private static bool Prefix() { return !CoopSession.Frozen; } }
}
