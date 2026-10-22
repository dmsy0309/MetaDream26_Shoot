using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using Metadream.Shoot.Content;

namespace Metadream.MapEditor
{
    /// <summary>
    /// Command line (verification, screenshots, batch processing):
    ///   -open &lt;file.json&gt; · -builtin &lt;id&gt; · -sample (every object type) · -select movers:0 · -modal export|open|playtest|gamerun|help|settings
    ///   -gamepath &lt;MetadreamShoot.app|exe&gt; · -installto &lt;Content dir&gt; · -package &lt;zip&gt; · -savejson &lt;file&gt;
    ///   -playtest sandbox|dummy|cpu1|teams|cpu|demo|demo2v2 [-playtestdelay s] · -launchgame cpu|cheat|demo [-gameargs "..."]
    ///   -selftest · -capture &lt;dir&gt; [-captureat 1,3,5] · -autoquit &lt;seconds&gt;
    /// </summary>
    public sealed partial class MapEditorApp
    {
        private void RunCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            string gp = Arg(args, "-gamepath");
            if (gp != null) { GamePath = gp; SaveSettings(); }
            string open = Arg(args, "-open");
            if (open != null) OpenFile(open);
            string builtin = Arg(args, "-builtin");
            if (builtin != null) OpenBuiltin(builtin);
            if (Has(args, "-sample")) { SetDocument(SampleMap(), false); FitView(); }
            if (Has(args, "-scriptsample")) { SetDocument(ScriptSampleMap(), false); FitView(); }
            string team = Arg(args, "-teamspawns");
            if (team != null)
            {
                var v = team.Split(',');
                SetTeamSpawns(true);
                for (int i = 0; i + 1 < v.Length && 2 + i / 2 < Doc.Count(ObjKind.Spawn); i += 2)
                    if (float.TryParse(v[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float sx) && float.TryParse(v[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float sy))
                    {
                        var s = Doc.Data.spawns[2 + i / 2];
                        s.x = sx; s.y = sy;
                    }
                Changed();
            }
            string tool = Arg(args, "-tool");
            if (tool != null && Enum.TryParse(tool, true, out Tool t)) SetTool(t);
            string marquee = Arg(args, "-marquee");
            if (marquee != null)
            {
                var v = marquee.Split(',');
                var f = new float[4];
                if (v.Length == 4 && float.TryParse(v[0], NumberStyles.Float, CultureInfo.InvariantCulture, out f[0]) && float.TryParse(v[1], NumberStyles.Float, CultureInfo.InvariantCulture, out f[1])
                    && float.TryParse(v[2], NumberStyles.Float, CultureInfo.InvariantCulture, out f[2]) && float.TryParse(v[3], NumberStyles.Float, CultureInfo.InvariantCulture, out f[3]))
                    FinishMarquee(new Vector2(f[0], f[1]), new Vector2(f[2], f[3]), false);
            }
            string sel = Arg(args, "-select");
            if (sel != null && sel.Contains(":"))
            {
                var parts = sel.Split(':');
                if (int.TryParse(parts[1], out int index)) Select(new ObjRef(MapDocument.KindFromJsonList(parts[0]), index));
            }
            if (Has(args, "-selftest")) RunSelfTest();
            string install = Arg(args, "-installto");
            if (install != null) BatchInstall(install);
            string package = Arg(args, "-package");
            if (package != null)
            {
                try { GameContent.BuildPackage(Doc, package, true); Debug.Log("[MapEditor] batch package ok " + package); }
                catch (Exception e) { Debug.LogError("[MapEditor] batch package failed: " + e.Message); }
            }
            string saveJson = Arg(args, "-savejson");
            if (saveJson != null) SaveTo(saveJson);
            string modal = Arg(args, "-modal");
            if (modal != null && Enum.TryParse(modal, true, out Modal m)) OpenModal(m);
            if (Has(args, "-behaviortab")) _scriptTab = 1;
            string pt = Arg(args, "-playtest");
            if (pt != null) StartCoroutine(After(Float(args, "-playtestdelay", 1f), () => StartPlaytestArg(pt)));
            string lg = Arg(args, "-launchgame");
            if (lg != null)
            {
                var mode = lg == "cheat" ? GameLauncher.Mode.CheatDuel : lg == "demo" ? GameLauncher.Mode.CpuDemo : GameLauncher.Mode.VersusCpu;
                string err = LaunchRealGame(mode, Arg(args, "-gameargs"));
                Debug.Log("[MapEditor] launchgame " + (err ?? "ok") + " content=" + GameContent.GameTestContentDir);
            }
            string capture = Arg(args, "-capture");
            if (capture != null) StartCoroutine(Capture(capture, Arg(args, "-captureat")));
            float quit = Float(args, "-autoquit", 0f);
            if (quit > 0f) StartCoroutine(After(quit, () => { _quitConfirmed = true; Debug.Log("[MapEditor] autoquit"); Application.Quit(0); }));
        }

        private void StartPlaytestArg(string pt)
        {
            switch (pt)
            {
                case "cpu": StartPlaytest(Playtest.Kind.VersusCpu, Playtest.Opponent.None); break;
                case "demo": StartPlaytest(Playtest.Kind.CpuDemo, Playtest.Opponent.None); break;
                case "demo2v2": StartPlaytest(Playtest.Kind.CpuDemoTeams, Playtest.Opponent.None); break;
                case "dummy": StartPlaytest(Playtest.Kind.Sandbox, Playtest.Opponent.Dummy); break;
                case "cpu1": StartPlaytest(Playtest.Kind.Sandbox, Playtest.Opponent.Cpu); break;
                case "teams": StartPlaytest(Playtest.Kind.Sandbox, Playtest.Opponent.Teams); break;
                default: StartPlaytest(Playtest.Kind.Sandbox, Playtest.Opponent.None); break;
            }
        }

        private void BatchInstall(string contentDir)
        {
            Revalidate();
            if (!GameAccepts) { Debug.LogError("[MapEditor] batch install refused: validation errors " + GameContent.ErrorCount(_issues)); return; }
            string path = GameContent.InstallToContentDir(Doc, contentDir, true, out _);
            string hash = GameContent.ContentHashWith(contentDir, out int playable, out _);
            Debug.Log("[MapEditor] batch install ok " + path + " hash=" + hash + " playable=" + playable);
        }

        /// <summary>Lossless round trip of every built-in map, install + reload, package and exclusive game content.</summary>
        public void RunSelfTest()
        {
            var failures = MapCompare.RoundTripBuiltins(out int count);
            Debug.Log("[SelfTest] builtin round trip " + (count - failures.Count) + "/" + count + (failures.Count > 0 ? " failures: " + string.Join("; ", failures) : ""));
            string tmp = Path.Combine(Application.temporaryCachePath, "selftest");
            try
            {
                var doc = SampleMap();
                var issues = GameContent.Validate(doc, true, out var def);
                Debug.Log("[SelfTest] sample validate errors=" + GameContent.ErrorCount(issues) + " loaded=" + (def != null));
                string content = Path.Combine(tmp, "Content");
                string path = GameContent.InstallToContentDir(doc, content, true, out _);
                string hash = GameContent.ContentHashWith(content, out int playable, out var loadIssues);
                int fileErrors = 0;
                foreach (var i in loadIssues) if (i.Severity == ContentSeverity.Error && i.Source == path) fileErrors++;
                Debug.Log("[SelfTest] install " + path + " hash=" + hash + " playable=" + playable + " fileErrors=" + fileErrors);
                string exclusive = GameContent.PrepareExclusiveGameContent(doc, null);
                GameContent.ContentHashWith(exclusive, out int only, out _);
                Debug.Log("[SelfTest] exclusive game content playable=" + only + " (expected 1)");
                string zip = GameContent.BuildPackage(doc, Path.Combine(tmp, "sample.zip"), true);
                Debug.Log("[SelfTest] package " + zip + " bytes=" + new FileInfo(zip).Length);
                Debug.Log("[SelfTest] result " + (failures.Count == 0 && def != null && fileErrors == 0 && only == 1 ? "PASS" : "FAIL"));
            }
            catch (Exception e) { Debug.LogError("[SelfTest] FAIL " + e); }
        }

        /// <summary>Showcase map using every object type the game supports.</summary>
        /// <summary>Map script showcase: named lava rising every 6 s, a trigger area that launches players, a spinning blade (C# behavior).</summary>
        public static MapDocument ScriptSampleMap()
        {
            var doc = new MapDocument();
            var d = doc.Data;
            d.id = "editor_script_sample";
            d.name = "에디터 예제 — 스크립트 용암";
            d.origin = "Metadream Shoot Map Maker script sample";
            d.solids = new[]
            {
                new RectData { x = -22f, y = -11f, w = 22f, h = 3f },
                new RectData { x = 22f, y = -11f, w = 22f, h = 3f },
                new RectData { name = "blade", x = 0f, y = 4f, w = 9f, h = 0.8f },
                new RectData { x = -20f, y = 3f, w = 8f, h = 1.2f },
                new RectData { x = 20f, y = 3f, w = 8f, h = 1.2f }
            };
            d.spawns = new[] { new PointData { x = -22f, y = -7f }, new PointData { x = 22f, y = -7f } };
            d.hazards = new[] { new ZoneData { name = "lava", x = 0f, y = -19f, w = 72f, h = 3f, dps = 45f } };
            d.areas = new[] { new AreaData { name = "launch", x = -30f, y = -9f, w = 4f, h = 1f, show = true, color = "#40ff9060" } };
            d.behaviors = new[] { new Metadream.Shoot.Maps.Scripting.MapBehaviorSpec { type = "spin", target = "blade", speed = 60f } };
            d.script = new[]
            {
                "# 6초마다 용암이 1.5씩 차오르고, 왼쪽 초록 영역을 밟으면 위로 튕겨 오른다",
                "var level = 0",
                "on start",
                "  hud(\"초록 발판: 점프 · 용암이 6초마다 차오름\")",
                "end",
                "every 6",
                "  if level < 5",
                "    level = level + 1",
                "    move_by(lava, 0, 1.5, 1.5)",
                "    banner(\"용암 \" + level + \"단계\", 1.2, \"#ff7040\")",
                "  end",
                "end",
                "on enter launch",
                "  push(player, 0, 38)",
                "  effect(\"ring\", player.x, player.y, 2, \"#40ff90\")",
                "end"
            };
            doc.Normalize();
            return doc;
        }

        /// <summary>Showcase map using every object type the game supports.</summary>
        public static MapDocument SampleMap()
        {
            var doc = new MapDocument();
            var d = doc.Data;
            d.id = "editor_sample";
            d.name = "에디터 예제 — 용암 협곡";
            d.origin = "Metadream Shoot Map Maker sample";
            d.solids = new[]
            {
                new RectData { x = -24f, y = -12f, w = 18f, h = 4f },
                new RectData { x = 24f, y = -12f, w = 18f, h = 4f },
                new RectData { x = 0f, y = 3f, w = 12f, h = 1.2f },
                new RectData { x = -14f, y = 8f, w = 7f, h = 1f, r = 12f },
                new RectData { x = 14f, y = 8f, w = 7f, h = 1f, r = -12f }
            };
            d.boxes = new[] { new RectData { x = -24f, y = -8f, w = 2f, h = 2f }, new RectData { x = 0f, y = 5f, w = 1.6f, h = 1.6f } };
            d.spawns = new[] { new PointData { x = -26f, y = -7f }, new PointData { x = 26f, y = -7f } };
            d.movers = new[]
            {
                new MoverData { x = -8f, y = -10f, w = 6f, h = 1.2f, path = new[] { new PointData { x = 0f, y = 0f }, new PointData { x = 16f, y = 0f } }, speed = 5f, wait = 1f }
            };
            d.pads = new[] { new ZoneData { x = -17f, y = -9.7f, w = 3f, h = 0.6f, power = 34f } };
            d.hazards = new[] { new ZoneData { x = 0f, y = -18.5f, w = 71f, h = 3f, dps = 60f } };
            d.winds = new[] { new ZoneData { x = 24f, y = 4f, w = 8f, h = 22f, fx = 0f, fy = 20f } };
            doc.Background = new MapBackground { enabled = true, top = "#2A0D08", bottom = "#0E0503" };
            doc.Normalize();
            return doc;
        }

        private static IEnumerator After(float seconds, Action action)
        {
            yield return new WaitForSecondsRealtime(seconds);
            action();
        }

        private static IEnumerator Capture(string directory, string times)
        {
            Directory.CreateDirectory(directory);
            float start = Time.realtimeSinceStartup;
            var list = new List<float> { 2f };
            if (!string.IsNullOrEmpty(times))
            {
                list.Clear();
                foreach (var part in times.Split(','))
                    if (float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var t)) list.Add(t);
            }
            foreach (float at in list)
            {
                while (Time.realtimeSinceStartup - start < at) yield return null;
                yield return new WaitForEndOfFrame();
                string path = Path.Combine(directory, "editor-" + at.ToString("0.#", CultureInfo.InvariantCulture) + "s.png");
                ScreenCapture.CaptureScreenshot(path);
                Debug.Log("[MapEditor] capture " + path);
            }
        }

        private static bool Has(string[] args, string name) => Array.IndexOf(args, name) >= 0;

        private static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private static float Float(string[] args, string name, float fallback)
        {
            string s = Arg(args, name);
            return s != null && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }
    }
}
