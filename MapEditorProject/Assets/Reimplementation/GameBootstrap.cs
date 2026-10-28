using System;
using System.Collections;
using UnityEngine;
using Metadream.Shoot.Core;
using Metadream.Shoot.Network;
using Metadream.Shoot.Online;
using Metadream.Shoot.Presentation;

namespace Metadream.Shoot
{
    /// <summary>
    /// Scene entry point. Command line:
    ///   -demo / -demo2v2 (CPU match, auto rematch), -quick (2 rounds to win), -seed n, -timescale x, -autoquit seconds
    ///   -host [-teamsize 1|2] [-autostart seconds] : open a room
    ///   -join &lt;code|ip[:port]&gt; : join a room
    ///   -autoplay : the local player is driven by the CPU (network smoke tests)
    ///   -capture &lt;directory&gt; : save screenshots after 2, 5 and 9 seconds (rendering checks on other machines)
    ///   -server [-config server.json] : headless dedicated server (always on in Dedicated Server builds)
    ///   -tutorial : start the tutorial directly · -settings : open the settings page
    ///   -debug : developer debug mode for this run · -content &lt;dir&gt; : extra cards/maps folder
    ///   -master &lt;host[:port]&gt; : main hosting server override (testing) · -nomaster : do not register rooms
    ///   -nickname &lt;name&gt; : set the nickname · -replay &lt;file.mdreplay&gt; : play a replay directly
    ///   -replaysmenu : open the replay list
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private Material spriteMaterial;

        /// <summary>True in Dedicated Server builds (UNITY_SERVER) or when started with -server.</summary>
        public static bool IsServer { get; private set; }

        private void Awake()
        {
#if UNITY_SERVER
            IsServer = true;
#else
            IsServer = Array.IndexOf(Environment.GetCommandLineArgs(), "-server") >= 0;
#endif
            var args = Environment.GetCommandLineArgs();
            string content = StringArg(args, "-content");
            if (content != null) Content.ContentDatabase.ExternalDirectory = content;
            string master = StringArg(args, "-master") ?? Environment.GetEnvironmentVariable("METADREAM_MASTER_HOST");
            if (master != null)
            {
                int colon = master.LastIndexOf(':');
                if (colon > 0 && int.TryParse(master.Substring(colon + 1), out int mp)) { MasterServerEndpoint.Host = master.Substring(0, colon); MasterServerEndpoint.Port = mp; }
                else MasterServerEndpoint.Host = master;
            }
            if (!IsServer) DevSettings.ApplyCommandLine(args);
            string nickname = StringArg(args, "-nickname");
            if (nickname != null) Online.PlayerProfile.SessionOverride = Online.PlayerProfile.Sanitize(nickname);
            string lookArg = StringArg(args, "-look"); // -look HAT,EXPRESSION,ACCESSORY[,MOUTH,ACCESSORY2] (this run only)
            if (lookArg != null)
            {
                var p = lookArg.Split(',');
                int Part(int i) => i < p.Length && int.TryParse(p[i], out int v) ? v : 0;
                Online.PlayerProfile.SessionLook = new Player.CharacterLook(Part(0), Part(1), Part(2)) { Mouth = (byte)Mathf.Clamp(Part(3),0,255), Accessory2 = (byte)Mathf.Clamp(Part(4),0,255) }.Sanitized;
            }
            if (spriteMaterial != null) SpriteFactory.Material = spriteMaterial;
            Application.targetFrameRate = 120;
            Application.runInBackground = true;
            Time.fixedDeltaTime = 0.02f; // matches the 0.02 constant in PlayerVelocity.AddForce
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            if (cam.GetComponent<CameraRig>() == null) cam.gameObject.AddComponent<CameraRig>();
            if (GetComponent<GameFlow>() == null) gameObject.AddComponent<GameFlow>();
            if (IsServer) return; // no menus or HUD on a headless server
            if (GetComponent<FrontEnd>() == null) gameObject.AddComponent<FrontEnd>();
            if (GetComponent<GameHud>() == null) gameObject.AddComponent<GameHud>();
            if (GetComponent<DebugOverlay>() == null) gameObject.AddComponent<DebugOverlay>();
            if (GetComponent<CheatPanel>() == null) gameObject.AddComponent<CheatPanel>();
            if (GetComponent<ChatPanel>() == null) gameObject.AddComponent<ChatPanel>();
            if (GetComponent<QuitConfirm>() == null) gameObject.AddComponent<QuitConfirm>();
        }

        private void Start()
        {
            var args = Environment.GetCommandLineArgs();
            if (IsServer)
            {
                DedicatedServer.Launch(args);
                float serverQuit = FloatArg(args, "-autoquit", 0f);
                if (serverQuit > 0f) StartCoroutine(QuitServerAfter(serverQuit));
                return;
            }
            float autoQuit = FloatArg(args, "-autoquit", 0f);
            float timeScale = FloatArg(args, "-timescale", 1f);
            int seed = (int)FloatArg(args, "-seed", Environment.TickCount);
            bool autoplay = HasArg(args, "-autoplay");
            var settings = new MatchSettings();
            if (HasArg(args, "-quick")) settings.roundsToWinGame = 2;
            float pickTime = FloatArg(args, "-picktime", -1f);
            if (pickTime >= 0f) settings.pickTimeLimit = (int)pickTime;
            string onlyMap = StringArg(args, "-map");
            if (!string.IsNullOrEmpty(onlyMap)) settings.mapPool = new[] { onlyMap }; // e.g. -cheat 1 -map script_rising_lava
            var flow = GetComponent<GameFlow>();

            if (HasArg(args, "-demo") || HasArg(args, "-demo2v2"))
            {
                flow.AutoRematch = !HasArg(args, "-norematch");
                flow.StartMatch(HasArg(args, "-demo2v2") ? GameMode.CpuDemoTeams : GameMode.CpuDemo, settings, seed);
            }
            else if (HasArg(args, "-versuscpu"))
            {
                flow.StartMatch(GameMode.VersusCpu, settings, seed);
            }
            else if (HasArg(args, "-host"))
            {
                int teamSize = (int)FloatArg(args, "-teamsize", 1f);
                var host = NetHostSession.Open(teamSize, autoplay, out string error);
                if (host == null) GetComponent<FrontEnd>().ShowNotice(error);
                else
                {
                    flow.AutoRematch = autoplay;
                    float autoStart = FloatArg(args, "-autostart", -1f);
                    if (autoStart >= 0f) StartCoroutine(AutoStart(host, autoStart, settings, seed));
                }
            }
            else if (HasArg(args, "-tutorial"))
            {
                Tutorial.TutorialController.Launch();
            }
            else if (HasArg(args, "-settings"))
            {
                GetComponent<FrontEnd>().OpenSettingsPage();
            }
            else if (HasArg(args, "-lookpage"))
            {
                GetComponent<FrontEnd>().OpenLook();
            }
            else if (HasArg(args, "-browse"))
            {
                GetComponent<FrontEnd>().OpenJoinPage();
            }
            else if (StringArg(args, "-replay") != null)
            {
                try
                {
                    var player = Replay.ReplayPlayer.Play(Replay.ReplayFile.Load(StringArg(args, "-replay")));
                    // Test hooks for screenshots: -replayseek s -replayspeed index -replayselect slot -replayzoom z
                    float seek = FloatArg(args, "-replayseek", -1f);
                    if (seek >= 0f) player.Seek(seek);
                    int speed = (int)FloatArg(args, "-replayspeed", -1f);
                    if (speed >= 0) player.SetSpeedIndex(speed);
                    int select = (int)FloatArg(args, "-replayselect", -1f);
                    if (select >= 0) player.Select(select);
                    float zoom = FloatArg(args, "-replayzoom", 1f);
                    if (zoom > 1f) player.SetZoom(zoom, null);
                }
                catch (Exception e) { GetComponent<FrontEnd>().ShowNotice("리플레이를 열 수 없습니다: " + e.Message); }
            }
            else if (HasArg(args, "-replaysmenu"))
            {
                GetComponent<FrontEnd>().OpenReplaysPage();
            }
            else if (HasArg(args, "-codex"))
            {
                DevSettings.ApplyCommandLine(new[] { "-debug" });
                GetComponent<FrontEnd>().OpenCodexPage(StringArg(args, "-codex"));
            }
            else if (StringArg(args, "-cheat") != null)
            {
                DevSettings.ApplyCommandLine(new[] { "-debug" });
                flow.StartMatch(StringArg(args, "-cheat") == "2" ? GameMode.CheatTeams : GameMode.CheatDuel, settings);
                string query = StringArg(args, "-cheatsearch");
                if (query != null) StartCoroutine(OpenCheatSearch(query));
            }
            else
            {
                string target = StringArg(args, "-join");
                if (target != null)
                {
                    if (RoomCode.TryParseTarget(target, out string h, out int p)) GetComponent<FrontEnd>().JoinTarget(h, p, autoplay);
                    else GetComponent<FrontEnd>().ShowNotice("잘못된 참가 주소: " + target);
                }
            }

            if (autoQuit > 0f) StartCoroutine(QuitAfter(autoQuit, timeScale));
            else if (timeScale != 1f) StartCoroutine(HoldTimeScale(timeScale));
            float quitPrompt = FloatArg(args, "-quitprompt", -1f);
            if (quitPrompt >= 0f) StartCoroutine(OpenQuitPrompt(quitPrompt));
            string captureDir = StringArg(args, "-capture");
            if (captureDir != null) StartCoroutine(Capture(captureDir, StringArg(args, "-captureat")));
        }

        /// <summary>-quitprompt N: open the "정말 나가시겠습니까?" dialog after N seconds (screenshots).</summary>
        private static IEnumerator OpenQuitPrompt(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            var flow = GameFlow.Instance;
            if (flow != null && flow.HasMatch) QuitConfirm.Open(flow.EscapeOverride ?? flow.ReturnToTitle);
        }

        private static IEnumerator Capture(string directory, string times)
        {
            System.IO.Directory.CreateDirectory(directory);
            float start = Time.realtimeSinceStartup;
            var list = new System.Collections.Generic.List<float> { 2f, 5f, 9f };
            if (!string.IsNullOrEmpty(times))
            {
                list.Clear();
                foreach (var part in times.Split(','))
                    if (float.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t)) list.Add(t);
            }
            foreach (float at in list)
            {
                while (Time.realtimeSinceStartup - start < at) yield return null;
                yield return new WaitForEndOfFrame();
                string path = System.IO.Path.Combine(directory, "capture-" + at.ToString("0") + "s.png");
                ScreenCapture.CaptureScreenshot(path);
                Debug.Log("[UI] capture " + path);
            }
        }

        private static IEnumerator AutoStart(NetHostSession host, float seconds, MatchSettings settings, int seed)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (host != null && !host.InMatch) host.StartMatch(settings, seed);
        }

        /// <summary>-cheatsearch: open the cheat card search with a query once the battle runs (screenshots).</summary>
        private static IEnumerator OpenCheatSearch(string query)
        {
            yield return new WaitForSecondsRealtime(4f);
            var panel = FindAnyObjectByType<CheatPanel>();
            if (panel == null) yield break;
            panel.Open();
            panel.SetQuery(query);
        }

        private static IEnumerator QuitServerAfter(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (DedicatedServer.Instance != null) DedicatedServer.Instance.Shutdown(0);
        }

        private static IEnumerator QuitAfter(float seconds, float timeScale)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                if (timeScale != 1f && Time.timeScale >= 0.99f && NetClientSession.Active == null) Time.timeScale = timeScale;
                yield return null;
            }
            var flow = GameFlow.Instance;
            if (flow != null)
            {
                var shots = new System.Text.StringBuilder();
                foreach (var f in flow.Fighters) shots.Append(f.DisplayName).Append(':').Append(f.ShotsFired).Append(' ');
                Debug.Log("[Match] autoquit role=" + flow.Role + " matches=" + flow.MatchesCompleted + " points=" + flow.PointsPlayed + " picks=" + flow.PicksCompleted + " shots=" + shots + flow.CardSummary());
            }
            if (NetHostSession.Active != null) NetHostSession.Active.Close();
            if (NetClientSession.Active != null) NetClientSession.Active.Leave(null);
            Application.Quit(0);
        }

        private static IEnumerator HoldTimeScale(float timeScale)
        {
            while (true)
            {
                if (Time.timeScale >= 0.99f && NetClientSession.Active == null) Time.timeScale = timeScale;
                yield return null;
            }
        }

        private static bool HasArg(string[] args, string name) => Array.IndexOf(args, name) >= 0;

        private static string StringArg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private static float FloatArg(string[] args, string name, float fallback)
        {
            int i = Array.IndexOf(args, name);
            if (i >= 0 && i + 1 < args.Length && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) return v;
            return fallback;
        }
    }
}

