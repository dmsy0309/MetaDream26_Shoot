using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Metadream.Shoot.Network;

namespace Metadream.Shoot.Replay
{
    /// <summary>
    /// Sends finished replays to the main hosting server (POST /replays). Tries the tailnet address first, then the
    /// public HTTPS address (for PCs outside the tailnet, e.g. school computers). Failed uploads leave a ".pending"
    /// marker next to the file and are retried on the next launch.
    /// </summary>
    public static class ReplayUploader
    {
        public const string Key = "metadream-shoot-replay-v1";
        public const int HttpPort = 47801;
        /// <summary>Public (outside tailnet) base URL of the main hosting server HTTP service.</summary>
        /// <summary>Tailscale Funnel on the hosting server (HTTPS 10000 -> 127.0.0.1:47801).</summary>
        public const string PublicUrl = "https://replays.example.invalid:10000";
        public const string PendingSuffix = ".pending";

        public static int Uploaded { get; private set; }
        public static int Failed { get; private set; }
        public static string LastResult { get; private set; }

        public static List<string> Endpoints()
        {
            var list = new List<string> { "http://" + MasterServerEndpoint.Host + ":" + HttpPort };
            if (!string.IsNullOrEmpty(PublicUrl) && MasterServerEndpoint.Host == MasterServerEndpoint.DefaultHost) list.Add(PublicUrl);
            return list;
        }

        public static void Upload(string path, ReplayMeta meta)
        {
            try { File.WriteAllText(path + PendingSuffix, JsonUtility.ToJson(meta)); } catch (Exception) { }
            Send(path, meta, Endpoints(), 0);
        }

        private static void Send(string path, ReplayMeta meta, List<string> endpoints, int attempt)
        {
            byte[] body;
            try { body = File.ReadAllBytes(path); }
            catch (Exception e) { LastResult = "읽기 실패: " + e.Message; return; }
            string url = endpoints[attempt] + "/replays";
            var req = new UnityWebRequest(url, "POST")
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
                string answer = req.downloadHandler != null ? req.downloadHandler.text : "";
                string error = req.error;
                req.Dispose();
                if (ok)
                {
                    Uploaded++;
                    LastResult = "업로드 완료 " + url;
                    try { File.Delete(path + PendingSuffix); } catch (Exception) { }
                    Debug.Log("[Replay] uploaded " + Path.GetFileName(path) + " -> " + url + " " + answer);
                }
                else if (attempt + 1 < endpoints.Count)
                {
                    Debug.LogWarning("[Replay] upload to " + url + " failed (" + error + "), trying " + endpoints[attempt + 1]);
                    Send(path, meta, endpoints, attempt + 1);
                }
                else
                {
                    Failed++;
                    LastResult = "업로드 실패: " + error;
                    Debug.LogWarning("[Replay] upload failed (" + error + "); will retry on next launch: " + Path.GetFileName(path));
                }
            };
        }

        /// <summary>Retries uploads that failed in an earlier session.</summary>
        public static void RetryPending()
        {
            try
            {
                if (!Directory.Exists(ReplayStore.Folder)) return;
                foreach (var marker in Directory.GetFiles(ReplayStore.Folder, "*" + PendingSuffix))
                {
                    string path = marker.Substring(0, marker.Length - PendingSuffix.Length);
                    if (!File.Exists(path)) { File.Delete(marker); continue; }
                    var meta = JsonUtility.FromJson<ReplayMeta>(File.ReadAllText(marker));
                    Debug.Log("[Replay] retrying upload " + Path.GetFileName(path));
                    Send(path, meta, Endpoints(), 0);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Replay] retry failed: " + e.Message);
            }
        }
    }
}
