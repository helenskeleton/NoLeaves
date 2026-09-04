using System;
using System.Collections;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NoLeaves
{
    public class UpdateChecks : MonoBehaviour
    {
        private const string latestreleaseURL = "https://api.github.com/repos/helenskeleton/NoLeaves/releases/latest";
        private const string latestreleasepageURL = "https://github.com/helenskeleton/NoLeaves/releases/latest";
        private static readonly HttpClient httpClient = CreateHttpClient();

        private Coroutine updateCheckCoroutine;
        private bool updateCheckStarted;
        private bool openedReleasePage;
        private bool outdatedMessageShown;

        private void Start()
        {
            StartUpdateCheck();
        }

        private static HttpClient CreateHttpClient()
        {
            HttpClient client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"NoLeaves/{PluginInfo.PLUGIN_VERSION}");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return client;
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
                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, latestreleaseURL);
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
                    FileName = latestreleasepageURL,
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

        private void OnDestroy()
        {
            if (updateCheckCoroutine != null)
            {
                StopCoroutine(updateCheckCoroutine);
                updateCheckCoroutine = null;
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
    }
}
