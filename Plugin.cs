using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Logging;
using GorillaNetworking;
using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NoLeaves
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger;

        private const string forestPath = "Environment Objects/LocalObjects_Prefab/Forest";
        private const string proxyUrl = "https://poopoomods-proxy.geforce9614.workers.dev/";
        private const string API_SECRET = "NoLeaves48927NoPeeking";
        private const int joinReportCooldownSeconds = 15;
        private static readonly int[] leafIndexes =
        {
            23,
            24,
            25
        };
        private static readonly (GTZone Zone, string MapName)[] mapDetectionZones =
        {
            (GTZone.cave, "Caves"),
            (GTZone.skyJungle, "Clouds"),
            (GTZone.canyon, "Canyons"),
            (GTZone.beach, "Beach"),
            (GTZone.mountain, "Mountains"),
            (GTZone.basement, "Basement"),
            (GTZone.Metropolis, "Metropolis"),
            (GTZone.arcade, "Arcade"),
            (GTZone.critters, "Critters"),
            (GTZone.rotating, "Rotating"),
            (GTZone.bayou, "Bayou"),
            (GTZone.monkeBlocks, "Monke Blocks"),
            (GTZone.hoverboard, "Skate Park"),
            (GTZone.VIMExperience1, "Lava Forest"),
            (GTZone.city, "City"),
            (GTZone.cityNoBuildings, "City"),
            (GTZone.cityWithSkyJungle, "City"),
            (GTZone.forest, "Forest")
        };
        private static readonly HttpClient httpClient = CreateHttpClient();
        private static readonly object joinReportLock = new object();

        private static DateTime lastJoinReportUtc = DateTime.MinValue;
        private static string lastJoinReportKey = string.Empty;
        private static bool missingAPI_SECRETLogged;
        private static bool joinReportingDisabled;

        private Coroutine removeLeavesCoroutine;
        private void Awake()
        {
            Logger = base.Logger;
            SceneManager.sceneLoaded += OnSceneLoaded;
            RemoveLeaves();
        }

        private void Start()
        {
            NetworkSystem.Instance.OnJoinedRoomEvent += OnJoinedRoom;
        }

        private void OnJoinedRoom()
        {
            if (joinReportingDisabled)
            {
                return;
            }

            string nickname = PhotonNetwork.NickName ?? string.Empty;
            string roomCode = PhotonNetwork.CurrentRoom?.Name ?? string.Empty;
            string region = NormalizeRegion(PhotonNetwork.CloudRegion);
            string userId = PhotonNetwork.LocalPlayer?.UserId ?? string.Empty;
            int playerCount = PhotonNetwork.PlayerList?.Length ?? 0;
            string rawGameMode = NetworkSystem.Instance?.GameModeString ?? string.Empty;
            (string queue, string gameMode) = ParseQueueAndGamemode(rawGameMode);
            bool isPrivate = !(PhotonNetwork.CurrentRoom?.IsVisible ?? true);
            string mapName = GetCurrentMapName();

            if (!ShouldSendJoinReport(nickname, roomCode, region))
            {
                return;
            }

            Task.Run(() => PostToProxy(nickname, roomCode, region, userId, isPrivate, playerCount, queue, gameMode, mapName));
        }

        private static async Task PostToProxy(string nickname, string roomCode, string region, string userId, bool isPrivate, int playerCount, string queue, string gameMode, string mapName)
        {
            if (joinReportingDisabled)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(API_SECRET))
            {
                if (!missingAPI_SECRETLogged)
                {
                    missingAPI_SECRETLogged = true;
                    Logger.LogWarning("Join reporting is disabled until Plugin.API_SECRET is configured to match the Cloudflare Worker API_SECRET.");
                }

                return;
            }

            try
            {
                string json = BuildJoinReportJson(nickname, roomCode, region, userId, isPrivate, playerCount, queue, gameMode, mapName);

                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, proxyUrl);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", API_SECRET);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using HttpResponseMessage response = await httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    string responseBody = await ReadResponseBodySafe(response);
                    joinReportingDisabled = true;
                    Logger.LogWarning($"Join report rejected with status code {(int)response.StatusCode} ({response.ReasonPhrase}). Body: {responseBody}");
                    Logger.LogWarning("Join reporting has been disabled for this session after a backend failure.");
                }
                else
                {
                    Logger.LogInfo($"Join report sent for room '{roomCode}' in region '{region}'.");
                }
            }
            catch (Exception ex)
            {
                joinReportingDisabled = true;
                Logger.LogError($"Join report failed: {ex}");
                Logger.LogWarning("Join reporting has been disabled for this session after a backend failure.");
            }
        }

        private static HttpClient CreateHttpClient()
        {
            HttpClient client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            return client;
        }

        private static bool ShouldSendJoinReport(string nickname, string roomCode, string region)
        {
            string joinKey = $"{nickname}|{roomCode}|{region}";
            DateTime nowUtc = DateTime.UtcNow;

            lock (joinReportLock)
            {
                if (joinKey == lastJoinReportKey &&
                    (nowUtc - lastJoinReportUtc).TotalSeconds < joinReportCooldownSeconds)
                {
                    return false;
                }

                lastJoinReportKey = joinKey;
                lastJoinReportUtc = nowUtc;
                return true;
            }
        }

        private static string BuildJoinReportJson(string nickname, string roomCode, string region, string userId, bool isPrivate, int playerCount, string queue, string gameMode, string mapName)
        {
            StringBuilder builder = new StringBuilder(256);
            builder.Append('{');
            builder.Append("\"modName\":\"").Append(EscapeJson("NoLeaves")).Append("\",");
            builder.Append("\"event\":\"").Append(EscapeJson("join")).Append("\",");
            builder.Append("\"nickname\":\"").Append(EscapeJson(nickname)).Append("\",");
            builder.Append("\"roomCode\":\"").Append(EscapeJson(roomCode)).Append("\",");
            builder.Append("\"region\":\"").Append(EscapeJson(region)).Append("\",");
            builder.Append("\"userId\":\"").Append(EscapeJson(userId)).Append("\",");
            builder.Append("\"playerCount\":").Append(playerCount).Append(',');
            builder.Append("\"queue\":\"").Append(EscapeJson(queue)).Append("\",");
            builder.Append("\"gameMode\":\"").Append(EscapeJson(gameMode)).Append("\",");
            builder.Append("\"gamemode\":\"").Append(EscapeJson(gameMode)).Append("\",");
            builder.Append("\"map\":\"").Append(EscapeJson(mapName)).Append("\",");
            builder.Append("\"privacy\":\"").Append(isPrivate ? "Private" : "Public").Append("\",");
            builder.Append("\"isPrivate\":").Append(isPrivate ? "true" : "false");
            builder.Append('}');
            return builder.ToString();
        }

        private static (string Queue, string GameMode) ParseQueueAndGamemode(string rawGameMode)
        {
            if (string.IsNullOrWhiteSpace(rawGameMode))
            {
                return ("UNKNOWN", "UNKNOWN");
            }

            string[] parts = rawGameMode.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            string queue = parts.Length > 1 ? NormalizeModeValue(parts[1]) : "UNKNOWN";
            string gameMode = parts.Length > 2 ? NormalizeModeValue(parts[2]) : "UNKNOWN";
            return (queue, gameMode);
        }

        private static string NormalizeModeValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "UNKNOWN";
            }

            string normalized = value.Trim().ToUpperInvariant();
            return normalized switch
            {
                "COMPETITIVE" => "COMP",
                _ => normalized
            };
        }

        private static string NormalizeRegion(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string normalized = value.Trim();
            int slashIndex = normalized.IndexOf('/');
            if (slashIndex >= 0)
            {
                normalized = normalized.Substring(0, slashIndex);
            }

            return normalized;
        }

        private static async Task<string> ReadResponseBodySafe(HttpResponseMessage response)
        {
            try
            {
                string body = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(body))
                {
                    return "<empty>";
                }

                return body.Length > 256 ? body.Substring(0, 256) : body;
            }
            catch (Exception ex)
            {
                return $"<failed to read body: {ex.Message}>";
            }
        }

        private static string GetCurrentMapName()
        {
            if (GorillaComputer.instance != null && GorillaComputer.instance.IsPlayerInVirtualStump())
            {
                return "Virtual Stump";
            }

            if (ZoneManagement.instance != null)
            {
                foreach ((GTZone zone, string mapName) in mapDetectionZones)
                {
                    if (ZoneManagement.instance.IsZoneActive(zone))
                    {
                        return mapName;
                    }
                }
            }

            string sceneName = SceneManager.GetActiveScene().name;
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                return "Unknown";
            }

            return sceneName;
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length + 8);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (char.IsControl(c))
                        {
                            builder.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            return builder.ToString();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;

            if (removeLeavesCoroutine != null)
            {
                StopCoroutine(removeLeavesCoroutine);
                removeLeavesCoroutine = null;
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RemoveLeaves();
        }

        private void RemoveLeaves()
        {
            if (removeLeavesCoroutine != null)
            {
                StopCoroutine(removeLeavesCoroutine);
            }

            removeLeavesCoroutine = StartCoroutine(RemoveLeavesLater());
        }

        private IEnumerator RemoveLeavesLater()
        {
            const int attempts = 12;
            const float delaySeconds = 0.5f;
            int totalDisabled = 0;

            for (int attempt = 0; attempt < attempts; attempt++)
            {
                int disabledThisPass = RemoveLeavesPass();
                totalDisabled += disabledThisPass;

                if (attempt < attempts - 1)
                {
                    yield return new WaitForSeconds(delaySeconds);
                }
            }

            string sceneName = SceneManager.GetActiveScene().name;

            if (totalDisabled > 0)
            {
                Logger.LogInfo($"Disabled {totalDisabled} leaf renderer(s) in scene '{sceneName}'.");
            }
            else
            {
                Logger.LogWarning($"No leaf renderers were found in scene '{sceneName}'.");
            }

            removeLeavesCoroutine = null;
        }

        private int RemoveLeavesPass()
        {
            int count = 0;

            foreach (GameObject obj in GetLeaves())
            {
                obj.SetActive(false);
                count++;
            }

            return count;
        }

        private static IEnumerable<GameObject> GetLeaves()
        {
            HashSet<GameObject> foundObjs = new HashSet<GameObject>();
            GameObject forest = GameObject.Find(forestPath);
            if (forest == null)
            {
                return foundObjs;
            }

            foreach (int leafIndex in leafIndexes)
            {
                if (leafIndex < 0 || leafIndex >= forest.transform.childCount)
                {
                    continue;
                }

                GameObject obj = forest.transform.GetChild(leafIndex).gameObject;
                if (obj == null || !obj.scene.IsValid())
                {
                    continue;
                }

                foundObjs.Add(obj);
            }

            return foundObjs;
        }

    }
}
