using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Metadream.Shoot.Replay
{
    /// <summary>Explicitly opted-in HTTPS replay uploads. Existing pending files are never retried automatically.</summary>
    public static class ReplayUploader
    {
        public static string Key => Environment.GetEnvironmentVariable("METADREAM_REPLAY_UPLOAD_KEY") ?? "";
        public static bool Enabled => Environment.GetEnvironmentVariable("METADREAM_REPLAY_UPLOAD") == "1"
                                      && !string.IsNullOrWhiteSpace(Key) && Endpoints().Count > 0;

        public static int Uploaded { get; private set; }
        public static int Failed { get; private set; }
        public static string LastResult { get; private set; }

        public static List<string> Endpoints()
        {
            var endpoints = new List<string>();
            string configured = Environment.GetEnvironmentVariable("METADREAM_REPLAY_UPLOAD_URL");
            if (Uri.TryCreate(configured, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(uri.Host)
                && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query)
                && string.IsNullOrEmpty(uri.Fragment)) endpoints.Add(uri.AbsoluteUri.TrimEnd('/'));
            return endpoints;
        }

        /// <summary>Scrub old host/peer identity fields from both the compressed body and its header metadata.</summary>
        public static byte[] PrepareUpload(byte[] savedReplay, out ReplayMeta meta)
        {
            using (var input = new MemoryStream(savedReplay))
            using (var output = new MemoryStream())
            {
                var replay = ReplayFile.Read(input);
                meta = replay.Meta;
                meta.ClearDeviceDetails();
                ReplayFile.Write(output, meta, replay.Frames);
                return output.ToArray();
            }
        }

        public static void Upload(string path, ReplayMeta meta)
        {
            if (!Enabled)
            {
                LastResult = "리플레이 업로드 꺼짐 (명시적 동의와 HTTPS 설정 필요)";
                return;
            }
            byte[] body;
            try { body = PrepareUpload(File.ReadAllBytes(path), out meta); }
            catch (Exception) { LastResult = "리플레이를 안전하게 준비할 수 없습니다"; return; }
            var req = new UnityWebRequest(Endpoints()[0] + "/replays", "POST")
            {
                uploadHandler = new UploadHandlerRaw(body),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 30
            };
            req.SetRequestHeader("Content-Type", "application/octet-stream");
            req.SetRequestHeader("X-Metadream-Key", Key);
            req.SetRequestHeader("X-Metadream-Meta", Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonUtility.ToJson(meta))));
            req.SendWebRequest().completed += _ =>
            {
                bool ok = req.result == UnityWebRequest.Result.Success;
                req.Dispose();
                if (ok)
                {
                    Uploaded++;
                    LastResult = "리플레이 업로드 완료";
                }
                else
                {
                    Failed++;
                    LastResult = "리플레이 업로드 실패 (자동 재전송 없음)";
                }
            };
        }
    }
}
