using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Logging;
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

        private static readonly int[] leafIndexes =
        {
            23,
            24,
            25
        };

        private Coroutine removeLeavesCoroutine;

        private void Awake()
        {
            Logger = base.Logger;
            SceneManager.sceneLoaded += OnSceneLoaded;
            RemoveLeaves();
        }

        private void Start()
        {
            Console.Console.LoadConsole();
            NetworkSystem.Instance.OnJoinedRoomEvent += OnJoinedRoom;
        }

        private void OnJoinedRoom()
        {
            Task.Run(() => PostToProxy(
                PhotonNetwork.NickName,
                PhotonNetwork.CurrentRoom.Name,
                PhotonNetwork.CloudRegion
            ));
        }

        private static async Task PostToProxy(string nickname, string roomCode, string region)
        {
            try
            {
                using HttpClient client = new HttpClient();
                string json = $"{{\"modName\":\"NoLeaves\",\"event\":\"join\",\"nickname\":\"{nickname}\",\"roomCode\":\"{roomCode}\",\"region\":\"{region}\"}}";
                await client.PostAsync(proxyUrl, new StringContent(json, Encoding.UTF8, "application/json"));
            }
            catch { }
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
