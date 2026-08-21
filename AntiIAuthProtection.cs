using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace AntiIAuth
{
    /// <summary>
    /// A Malware protector for Developers
    /// Drag and drop this into your project(s) and call "AntiIAuthProtection.Initialize(this);" in your Awake() Method.
    /// Made by yours truly, poopoovr.
    /// </summary>
    public static class AntiIAuthProtection
    {
        public static ConfigEntry<string> BlockedURLsConfig;
        public static HashSet<string> BlockedURLs = new HashSet<string>();
        public static HashSet<string> FetchedURLs = new HashSet<string>();
        private static bool _initialized = false;
        
        public static void Initialize(BaseUnityPlugin plugin)
        {
            if (_initialized) return;
            _initialized = true;

            ScanPluginsFolder();

            BlockedURLsConfig = plugin.Config.Bind(
                "AntiIAuth Protection", 
                "BlockedURLs", 
                "anotheraxiom.site,anotheraxiem.site,wadawdawdaw.click,seralyth.lol,faggot.click,sentinelhook.lol,95.217.1.57,israelauth.site",
                "Add to list if there is a website related to dangerous stuff you want to avoid and block");
            
            UpdateBlockedURLs();
            BlockedURLsConfig.SettingChanged += (sender, args) => UpdateBlockedURLs();

            var harmony = new Harmony($"antiiauth.protection.{Assembly.GetExecutingAssembly().GetName().Name}");
            harmony.PatchAll(typeof(Patch_WebRequest_Create_String));
            harmony.PatchAll(typeof(Patch_WebRequest_Create_Uri));
            harmony.PatchAll(typeof(Patch_UnityWebRequest_SendWebRequest));

            plugin.StartCoroutine(FetchBannedURLsRoutine());
        }

        private static void UpdateBlockedURLs()
        {
            BlockedURLs = new HashSet<string>(
                BlockedURLsConfig.Value.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
            );
        }

        private static IEnumerator FetchBannedURLsRoutine()
        {
            using (UnityWebRequest webRequest = UnityWebRequest.Get("https://menu.seralyth.software/bannedurls"))
            {
                yield return webRequest.SendWebRequest();

                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    string json = webRequest.downloadHandler.text;
                    ParseAndAddFetchedURLs(json);
                }
            }
        }

        private static void ParseAndAddFetchedURLs(string json)
        {
            MatchCollection matches = Regex.Matches(json, "\"([^\"]+)\"\\s*:\\s*\"([^\"]+)\"");
            foreach (Match match in matches)
            {
                string domain = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(domain) && domain != "banned")
                {
                    FetchedURLs.Add(domain.ToLowerInvariant());
                }
            }
        }

        public static bool IsUrlBlocked(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            
            string lowerUrl = url.ToLowerInvariant();
            
            foreach (var blocked in BlockedURLs)
            {
                if (lowerUrl.Contains(blocked.ToLowerInvariant()))
                {
                    return true;
                }
            }

            foreach (var fetched in FetchedURLs)
            {
                if (lowerUrl.Contains(fetched))
                {
                    return true;
                }
            }
            return false;
        }

        private static void ScanPluginsFolder()
        {
            try
            {
                string pluginDir = Paths.PluginPath;
                string[] allDlls = Directory.GetFiles(pluginDir, "*.dll", SearchOption.AllDirectories);

                string ourAssemblyPath = Assembly.GetExecutingAssembly().Location;
                bool malwareFound = false;

                foreach (string dllPath in allDlls)
                {
                    if (string.Equals(dllPath, ourAssemblyPath, StringComparison.OrdinalIgnoreCase)) continue;

                    if (ScanFile(dllPath))
                    {
                        malwareFound = true;
                    }
                }

                if (malwareFound)
                {
                    Application.Quit();
                    Environment.Exit(0);
                }
            }
            catch
            {
            
            }
        }

        private static readonly string[] BadKeywords = new string[]
        {
            "harmony.patchinfo.bin",
            "harmonypatchinfo.bin",
            ".graze",
            "israelauth",
            "pastebin"
        };

        private static bool ScanFile(string filePath)
        {
            try
            {
                byte[] fileBytes = File.ReadAllBytes(filePath);
                string fileContent = Encoding.ASCII.GetString(fileBytes).ToLowerInvariant();

                foreach (string keyword in BadKeywords)
                {
                    if (fileContent.Contains(keyword.ToLowerInvariant()))
                    {
                        NeutralizeFile(filePath);
                        return true;
                    }
                }
            }
            catch
            {

            }
            return false;
        }

        private static void NeutralizeFile(string filePath)
        {
            try
            {
                string newPath = filePath + "israelauth";
                if (File.Exists(newPath))
                {
                    File.Delete(newPath);
                }
                File.Move(filePath, newPath);
            }
            catch
            {

            }
        }

        [HarmonyPatch(typeof(WebRequest), nameof(WebRequest.Create), new Type[] { typeof(string) })]
        internal class Patch_WebRequest_Create_String
        {
            static bool Prefix(string requestUriString, ref WebRequest __result)
            {
                if (IsUrlBlocked(requestUriString))
                {
                    __result = null;
                    return false;    
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(WebRequest), nameof(WebRequest.Create), new Type[] { typeof(Uri) })]
        internal class Patch_WebRequest_Create_Uri
        {
            static bool Prefix(Uri requestUri, ref WebRequest __result)
            {
                if (requestUri != null && IsUrlBlocked(requestUri.ToString()))
                {
                    __result = null;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(UnityWebRequest), nameof(UnityWebRequest.SendWebRequest))]
        internal class Patch_UnityWebRequest_SendWebRequest
        {
            static bool Prefix(UnityWebRequest __instance)
            {
                if (__instance != null && IsUrlBlocked(__instance.url))
                {
                    __instance.Abort();
                    return false;
                }
                return true;
            }
        }
    }
}
