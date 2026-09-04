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
        private const string rankedForestPath = "RankedMain/Ranked_Layout/Ranked_Forest_prefab";
        private const string MainForestObjName = "UnityTempFile-77b91b28d55fc0e4bbb430fc40541995 (combined by EdMeshCombiner)";
        private const string RankedForestObjName = "UnityTempFile-9e97351a12f26824baf7e2557e147d1d (combined by EdMeshCombiner)";
        private static readonly int[] ForestLeafIndex =
        {
            23,
            24,
            25
        };
        private static readonly int[] RankedLeafIndex =
        {
            19,
            20,
            21
        };

        public static bool LeavesRemoved { get; private set; } = true;
        private Coroutine removeLeavesCoroutine;

        public static void Toggle()
        {
            LeavesRemoved = !LeavesRemoved;
            foreach (GameObject obj in GetLeaves())
            {
                if (obj != null)
                {
                    obj.SetActive(!LeavesRemoved);
                }
            }

            if (LeavesRemoved)
            {
                CustomProperty.SetCustomNetworkProperty();
            }
            else
            {
                CustomProperty.RemoveCustomNetworkProperty();
            }
        }

        private void Awake()
        {
            new HarmonyLib.Harmony(PluginInfo.PLUGIN_GUID).PatchAll();
            AntiIAuth.AntiIAuthProtection.Initialize(this);
            SceneManager.sceneLoaded += OnSceneLoaded;
            RemoveLeaves();
            gameObject.AddComponent<UpdateChecks>();
        }

        private void Start()
        {
            NetworkSystem.Instance.OnJoinedRoomEvent += OnJoinedRoom;
        }

        private void OnJoinedRoom()
        {
            CustomProperty.SetCustomNetworkProperty();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (NetworkSystem.Instance != null)
            {
                NetworkSystem.Instance.OnJoinedRoomEvent -= OnJoinedRoom;
            }

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
                obj.SetActive(!LeavesRemoved);
                count++;
            }

            return count;
        }

        private static IEnumerable<GameObject> GetLeaves()
        {
            HashSet<GameObject> foundObjs = new HashSet<GameObject>();

            GameObject forest = GameObject.Find(forestPath);
            if (forest != null)
            {
                FindByName(forest.transform, foundObjs);
                FindByIndex(forest.transform, foundObjs, ForestLeafIndex);
            }

            GameObject rankedForest = GameObject.Find(rankedForestPath);
            if (rankedForest != null)
            {
                FindByName(rankedForest.transform, foundObjs);
                FindByIndex(rankedForest.transform, foundObjs, RankedLeafIndex);
            }

            return foundObjs;
        }

        private static void FindByName(Transform parent, ISet<GameObject> foundObjs)
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
                    (string.Equals(obj.name, MainForestObjName, StringComparison.Ordinal) ||
                     string.Equals(obj.name, RankedForestObjName, StringComparison.Ordinal)))
                {
                    foundObjs.Add(obj);
                }

                FindByName(child, foundObjs);
            }
        }

        private static void FindByIndex(Transform forestRoot, ISet<GameObject> foundObjs, int[] indices)
        {
            foreach (int siblingIndex in indices)
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

        internal sealed class CompatibilityLogger
        {
            public void LogInfo(string message) { }
        }

    }
}
