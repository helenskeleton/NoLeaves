using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BepInEx;
using GorillaNetworking;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NoLeaves
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        internal static new CompatibilityLogger Logger { get; } = new CompatibilityLogger();

        private const string forestPath = "Environment Objects/LocalObjects_Prefab/Forest";
        private const string proxyUrl = "https://poopoomods-proxy.geforce9614.workers.dev/";
        private const string latestReleaseApiUrl = "https://api.github.com/repos/helenskeleton/NoLeaves/releases/latest";
        private const string latestReleasePageUrl = "https://github.com/helenskeleton/NoLeaves/releases/latest";
        private const string API_SECRET = "NoLeaves48927NoPeeking";
        private const int joinReportCooldownSeconds = 15;
        private const float presenceHeartbeatIntervalSeconds = 30f;
        private const string combinedLeafObjectName = "UnityTempFile-5642b89260ac826449cafb7fdeb899e4 (combined by EdMeshCombiner)";
        private static readonly int[] leafSiblingIndexes =
        {
            22,
            23,
            24
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
        private static bool joinReportingDisabled;

        private Coroutine removeLeavesCoroutine;
        private Coroutine presenceHeartbeatCoroutine;
        private Coroutine updateCheckCoroutine;
        private bool updateCheckStarted;
        private bool openedReleasePage;
        private bool outdatedMessageShown;
        private readonly string sessionId = Guid.NewGuid().ToString("N");

        private void Awake()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            RemoveLeaves();
            StartUpdateCheck();
        }

        private void Start()
        {
            NetworkSystem.Instance.OnJoinedRoomEvent += OnJoinedRoom;
            NetworkSystem.Instance.OnReturnedToSinglePlayer += OnReturnedToSinglePlayer;
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
                StartPresenceHeartbeat();
                return;
            }

            StartPresenceHeartbeat();
            Task.Run(() => PostToProxy("join", nickname, roomCode, region, userId, isPrivate, playerCount, queue, gameMode, mapName, sessionId));
        }

        private void OnReturnedToSinglePlayer()
        {
            StopPresenceHeartbeat();

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

            Task.Run(() => PostToProxy("leave", nickname, roomCode, region, userId, isPrivate, playerCount, queue, gameMode, mapName, sessionId));
        }

        private static async Task PostToProxy(string eventName, string nickname, string roomCode, string region, string userId, bool isPrivate, int playerCount, string queue, string gameMode, string mapName, string sessionId)
        {
            if (joinReportingDisabled)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(API_SECRET))
            {
                return;
            }

            try
            {
                string json = BuildProxyEventJson(eventName, nickname, roomCode, region, userId, isPrivate, playerCount, queue, gameMode, mapName, sessionId);

                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, proxyUrl);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", API_SECRET);
                request.Headers.UserAgent.ParseAdd($"NoLeaves/{PluginInfo.PLUGIN_VERSION}");
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using HttpResponseMessage response = await httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    joinReportingDisabled = true;
                }
            }
            catch
            {
                joinReportingDisabled = true;
            }
        }

        private static HttpClient CreateHttpClient()
        {
            HttpClient client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"NoLeaves/{PluginInfo.PLUGIN_VERSION}");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
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

        private static string BuildProxyEventJson(string eventName, string nickname, string roomCode, string region, string userId, bool isPrivate, int playerCount, string queue, string gameMode, string mapName, string sessionId)
        {
            StringBuilder builder = new StringBuilder(320);
            builder.Append('{');
            builder.Append("\"modName\":\"").Append(EscapeJson("NoLeaves")).Append("\",");
            builder.Append("\"event\":\"").Append(EscapeJson(eventName)).Append("\",");
            builder.Append("\"nickname\":\"").Append(EscapeJson(nickname)).Append("\",");
            builder.Append("\"roomCode\":\"").Append(EscapeJson(roomCode)).Append("\",");
            builder.Append("\"region\":\"").Append(EscapeJson(region)).Append("\",");
            builder.Append("\"userId\":\"").Append(EscapeJson(userId)).Append("\",");
            builder.Append("\"sessionId\":\"").Append(EscapeJson(sessionId)).Append("\",");
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

        private void StartPresenceHeartbeat()
        {
            if (presenceHeartbeatCoroutine != null)
            {
                return;
            }

            presenceHeartbeatCoroutine = StartCoroutine(SendPresenceHeartbeat());
        }

        private void StopPresenceHeartbeat()
        {
            if (presenceHeartbeatCoroutine == null)
            {
                return;
            }

            StopCoroutine(presenceHeartbeatCoroutine);
            presenceHeartbeatCoroutine = null;
        }

        private IEnumerator SendPresenceHeartbeat()
        {
            while (true)
            {
                yield return new WaitForSeconds(presenceHeartbeatIntervalSeconds);

                if (joinReportingDisabled || PhotonNetwork.CurrentRoom == null)
                {
                    continue;
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

                Task.Run(() => PostToProxy("heartbeat", nickname, roomCode, region, userId, isPrivate, playerCount, queue, gameMode, mapName, sessionId));
            }
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
            if (NetworkSystem.Instance != null)
            {
                NetworkSystem.Instance.OnJoinedRoomEvent -= OnJoinedRoom;
                NetworkSystem.Instance.OnReturnedToSinglePlayer -= OnReturnedToSinglePlayer;
            }

            if (removeLeavesCoroutine != null)
            {
                StopCoroutine(removeLeavesCoroutine);
                removeLeavesCoroutine = null;
            }

            StopPresenceHeartbeat();

            if (updateCheckCoroutine != null)
            {
                StopCoroutine(updateCheckCoroutine);
                updateCheckCoroutine = null;
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

        private void StartUpdateCheck()
        {
            if (updateCheckStarted)
            {
                return;
            }

            updateCheckStarted = true;
            updateCheckCoroutine = StartCoroutine(CheckForUpdatesLater());
        }

        private IEnumerator CheckForUpdatesLater()
        {
            yield return new WaitForSeconds(3f);

            Task<UpdateCheckResult> updateTask = CheckForUpdatesAsync();
            while (!updateTask.IsCompleted)
            {
                yield return null;
            }

            if (updateTask.IsFaulted)
            {
                updateCheckCoroutine = null;
                yield break;
            }

            HandleUpdateResult(updateTask.Result);
            updateCheckCoroutine = null;
        }

        private void HandleUpdateResult(UpdateCheckResult result)
        {
            switch (result.Status)
            {
                case UpdateStatus.UpToDate:
                    break;
                case UpdateStatus.Outdated:
                    if (!outdatedMessageShown)
                    {
                        outdatedMessageShown = true;
                        StartCoroutine(ShowOutdatedVersionMessage(result.LatestVersion));
                    }
                    break;
                case UpdateStatus.Failed:
                    break;
            }
        }

        private static async Task<UpdateCheckResult> CheckForUpdatesAsync()
        {
            try
            {
                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, latestReleaseApiUrl);
                using HttpResponseMessage response = await httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    string body = await ReadResponseBodySafe(response);
                    return UpdateCheckResult.Fail($"GitHub returned {(int)response.StatusCode} ({response.ReasonPhrase}). Body: {body}");
                }

                string responseJson = await response.Content.ReadAsStringAsync();
                string latestVersionText = ExtractLatestReleaseVersion(responseJson);
                if (!TryParseVersion(latestVersionText, out Version latestVersion))
                {
                    return UpdateCheckResult.Fail($"Could not parse latest release version '{latestVersionText}'.");
                }

                if (!TryParseVersion(PluginInfo.PLUGIN_VERSION, out Version currentVersion))
                {
                    return UpdateCheckResult.Fail($"Current plugin version '{PluginInfo.PLUGIN_VERSION}' is invalid.");
                }

                if (latestVersion <= currentVersion)
                {
                    return UpdateCheckResult.UpToDate(latestVersion.ToString());
                }

                return UpdateCheckResult.Outdated(latestVersion.ToString());
            }
            catch (Exception ex)
            {
                return UpdateCheckResult.Fail(ex.Message);
            }
        }

        private static string ExtractJsonStringValue(string json, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(propertyName))
            {
                return string.Empty;
            }

            Match match = Regex.Match(json, $"\"{Regex.Escape(propertyName)}\"\\s*:\\s*\"(?<value>[^\"\\\\]*(?:\\\\.[^\"\\\\]*)*)\"");
            if (!match.Success)
            {
                return string.Empty;
            }

            return Regex.Unescape(match.Groups["value"].Value);
        }

        private static string ExtractLatestReleaseVersion(string json)
        {
            string releaseName = ExtractJsonStringValue(json, "name");
            if (TryParseVersion(releaseName, out Version namedVersion))
            {
                return namedVersion.ToString();
            }

            string tagName = ExtractJsonStringValue(json, "tag_name");
            if (TryParseVersion(tagName, out Version taggedVersion))
            {
                return taggedVersion.ToString();
            }

            return string.Empty;
        }

        private static bool TryParseVersion(string rawVersion, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(rawVersion))
            {
                return false;
            }

            string trimmed = rawVersion.Trim();
            int digitIndex = -1;
            for (int i = 0; i < trimmed.Length; i++)
            {
                if (char.IsDigit(trimmed[i]))
                {
                    digitIndex = i;
                    break;
                }
            }

            if (digitIndex < 0)
            {
                return false;
            }

            StringBuilder normalized = new StringBuilder();
            for (int i = digitIndex; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (char.IsDigit(c) || c == '.')
                {
                    normalized.Append(c);
                }
                else
                {
                    break;
                }
            }

            return Version.TryParse(normalized.ToString(), out version);
        }

        private IEnumerator ShowOutdatedVersionMessage(string latestVersion)
        {
            if (!openedReleasePage)
            {
                openedReleasePage = true;
                Process.Start(new ProcessStartInfo
                {
                    FileName = latestReleasePageUrl,
                    UseShellExecute = true
                });
            }

            GameObject stumpObj = new GameObject("NoLeavesOutdatedMessageObject");
            Canvas canvas = stumpObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            CanvasScaler scaler = stumpObj.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;
            stumpObj.AddComponent<GraphicRaycaster>();

            RectTransform canvasRect = stumpObj.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(9f, 9f);
            stumpObj.transform.position = new Vector3(-66.9419f, 12.35f, -82.6273f);
            stumpObj.transform.localScale = Vector3.one * 0.003f;
            stumpObj.transform.Rotate(0f, 180f, 0f);

            TextMeshProUGUI textObj = new GameObject("OutdatedText").AddComponent<TextMeshProUGUI>();
            textObj.transform.SetParent(stumpObj.transform, false);
            textObj.fontSize = 30f;
            textObj.alignment = TextAlignmentOptions.Center;
            textObj.color = Color.white;

            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchoredPosition = new Vector2(0f, -50f);
            textRect.sizeDelta = new Vector2(900f, 700f);

            textObj.text = $"<color=yellow>NoLeaves is outdated.</color>\nInstalled: {PluginInfo.PLUGIN_VERSION}\nLatest: {latestVersion}\nDownload the latest release from GitHub.";

            while (stumpObj != null)
            {
                if (Camera.main != null)
                {
                    stumpObj.transform.LookAt(Camera.main.transform.position);
                    stumpObj.transform.Rotate(0f, 180f, 0f);
                }

                yield return null;
            }
        }

        private IEnumerator RemoveLeavesLater()
        {
            const int attempts = 12;
            const float delaySeconds = 0.5f;

            for (int attempt = 0; attempt < attempts; attempt++)
            {
                RemoveLeavesPass();

                if (attempt < attempts - 1)
                {
                    yield return new WaitForSeconds(delaySeconds);
                }
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

            CollectLeafObjectsByName(forest.transform, foundObjs);
            CollectLeafObjectsBySiblingIndex(forest.transform, foundObjs);

            return foundObjs;
        }

        private static void CollectLeafObjectsByName(Transform parent, ISet<GameObject> foundObjs)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                GameObject obj = child.gameObject;
                if (obj != null &&
                    obj.scene.IsValid() &&
                    string.Equals(obj.name, combinedLeafObjectName, StringComparison.Ordinal))
                {
                    foundObjs.Add(obj);
                }

                CollectLeafObjectsByName(child, foundObjs);
            }
        }

        private static void CollectLeafObjectsBySiblingIndex(Transform forestRoot, ISet<GameObject> foundObjs)
        {
            foreach (int siblingIndex in leafSiblingIndexes)
            {
                if (siblingIndex < 0 || siblingIndex >= forestRoot.childCount)
                {
                    continue;
                }

                GameObject obj = forestRoot.GetChild(siblingIndex).gameObject;
                if (obj != null && obj.scene.IsValid())
                {
                    foundObjs.Add(obj);
                }
            }
        }

        private enum UpdateStatus
        {
            UpToDate,
            Outdated,
            Failed
        }

        private sealed class UpdateCheckResult
        {
            public UpdateStatus Status { get; private set; }
            public string LatestVersion { get; private set; }
            public string Message { get; private set; }

            public static UpdateCheckResult UpToDate(string latestVersion)
            {
                return new UpdateCheckResult
                {
                    Status = UpdateStatus.UpToDate,
                    LatestVersion = latestVersion
                };
            }

            public static UpdateCheckResult Outdated(string latestVersion)
            {
                return new UpdateCheckResult
                {
                    Status = UpdateStatus.Outdated,
                    LatestVersion = latestVersion
                };
            }

            public static UpdateCheckResult Fail(string message)
            {
                return new UpdateCheckResult
                {
                    Status = UpdateStatus.Failed,
                    Message = message
                };
            }
        }

        internal sealed class CompatibilityLogger
        {
            public void LogInfo(string message) { }
        }

    }
}
