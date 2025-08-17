using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using CustomParams;

using Atomicrops.Game.EnemySystem;
using Atomicrops.Core.DamageSystem;

namespace NecrootmancyMod
{
    public static class MyPluginInfo
    {
        public const string PLUGIN_GUID = "pauli.plugin.Necrootmancy";
        public const string PLUGIN_NAME = "Necrootmancy";
        public const string PLUGIN_VERSION = "2.0.0";
    }

    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    [BepInDependency("pauli.plugin.CustomParams")]
    public class Plugin : BaseUnityPlugin
    {
        public static BepInEx.Logging.ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID}, v {MyPluginInfo.PLUGIN_VERSION} is loaded!");

            // Register upgrade
            RegisterUpgrade();

            // Apply patches (if necessary)
            var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            harmony.PatchAll();

        }

        private void RegisterUpgrade()
        {
            Upgrade necrootmancy = new Upgrade("Necrootmancy", "Summon a root fighter on enemy kill.", "Necrootmancy.png", Assembly.GetExecutingAssembly());

            // Add various param types to ensure compatibility
            // see wiki for valid vanilla params
            necrootmancy.AddVanillaParam("Player.Speed", 2f, "Multiply");

            // Add 2 of each friend
            Dictionary<string, int> friends = new Dictionary<string, int>();
            friends.Add("bee", 2);
            friends.Add("bee2", 2);
            friends.Add("cow", 2);
            friends.Add("cow2", 2);
            friends.Add("pig", 2);
            friends.Add("pig2", 2);
            friends.Add("chickenweed", 2);
            friends.Add("chickenweed2", 2);
            friends.Add("alienpet", 2);
            friends.Add("rue", 2);
            friends.Add("borage", 2);
            friends.Add("norman", 2);
            friends.Add("waterchris", 2);
            friends.Add("furryosa", 2);
            friends.Add("drone", 2);

            necrootmancy.AddFriends(friends);

            // add turrets, currets, and scarecrows
            Dictionary<string, int> turrets = new Dictionary<string, int>();
            turrets.Add("turret", 10);
            turrets.Add("curret", 10);
            turrets.Add("scarecrow", 10);
            necrootmancy.AddTurrets(turrets);

            // add loot
            // each item can only have one type of loot added
            // see wiki for valid loot strings
            necrootmancy.AddLoot("AddTime", 100);

            // Add custom param logic
            necrootmancy.AddCustomParam(NecrootmancyState.EnableDoNecrootmancy, NecrootmancyState.Cleanup);

            // Optional: enable debug mode to always spawn at the top of the list
            // Turn this off before uploading mod
            necrootmancy.ToggleDebug();


            // Add to a loot pool (e.g., Main)
            // MUST BE CALLED LAST
            necrootmancy.AddUpgradeToLootPool("Main");
        }
    }

    public static class NecrootmancyState
    {
        public static bool DoNecrootmancy { get; set; } = false;
        public static GameObject RootFighter { get; set; } = null;

        public static void EnableDoNecrootmancy()
        {
            if (RootFighter == null)
            {
                GameObject upgradesController = GameObject.Find("UpgradesController");

                FarmTaskUpgradeProcs farmTaskUpgradeProcs = upgradesController.GetComponent<FarmTaskUpgradeProcs>();

                RootFighter = farmTaskUpgradeProcs.RootFighter;
            }
            DoNecrootmancy = true;
        }

        public static void Cleanup()
        {
            DoNecrootmancy = false;
        }
    }

    [HarmonyPatch(typeof(UpgradeProcs_1_6), "OnOnEnemyKilled")]
    class UpgradeProcs_1_6_OnOnEnemyKilled_Patch
    {
        static void Postfix(UpgradeProcs_1_6 __instance, object arg1, EnemyController.EnemyControllerEventArgs arg2)
        {

            if (NecrootmancyState.DoNecrootmancy && arg2.Source == DamageSource.PlayerGun)
            {
                Vector2 anchor = arg2.Enemy.GetAgent().GetAnchor(AgentAnchors.Top);

                Vector3 anchor3D = new Vector3(anchor.x, anchor.y, 0);

                SimplePool.Spawn(NecrootmancyState.RootFighter, anchor3D, Quaternion.identity);

            }
        }
    }
}
