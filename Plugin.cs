using BepInEx;
using HarmonyLib;
using Atomicrops.Core.Upgrades;
using Atomicrops.Game.Data;
using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;

// This is your mod's specific namespace. Every class pertaining to your mod should be within this namespace.
namespace CustomParams
{
    // Basic information about your plugin for BepInEx's plugin list
    public static class MyPluginInfo
    {
        public const string PLUGIN_GUID = "pauli.plugin.CustomParams";
        public const string PLUGIN_NAME = "CustomParams";
        public const string PLUGIN_VERSION = "1.0.0";
    }

    public class ActionContainer
    {
        public Action Function { get; set; }
        public Action Cleanup { get; set; }
    }

    public static class GlobalActions
    {
        public static Dictionary<string, ActionContainer> Actions = new Dictionary<string, ActionContainer>();

        static GlobalActions()
        {
 
        }

        public static void AddAction(string command, Action function, Action cleanup)
        {
            Actions[command] = new ActionContainer { Function = function, Cleanup = cleanup };
        }
    }

    // This class initializes your plugin and applies your Harmony patches.
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public static BepInEx.Logging.ManualLogSource Log;  // For outside classes to log to the BepInEx console.

        private void Awake()
        {
            Log = Logger; // Initializes the logger
            Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

            var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID); // Creating a Harmony instance
            harmony.PatchAll(); // Apply all patches in the assembly
        }
    }

    [HarmonyPatch(typeof(UpgradeRunner), "ApplyUpgradeList")]
    class UpgradeRunner_ApplyUpgradeList_Patch
    {
        static bool Prefix(Dictionary<UpgradeDef, int> currentUpgradeStacks, object paramsObj)
        {
            foreach (KeyValuePair<UpgradeDef, int> keyValuePair in currentUpgradeStacks)
            {
                List<UpgradeParam> @params = keyValuePair.Key.GetParams(keyValuePair.Value);
                if (@params != null && @params.Count != 0)
                {
                    foreach (UpgradeParam upgradeParam in @params)
                    {
                        if (upgradeParam.Path.StartsWith("#"))
                        {
                            try
                            {
                                string command = upgradeParam.Path;

                                // New logic: Check for an action container.
                                if (GlobalActions.Actions.TryGetValue(command, out ActionContainer actionContainer))
                                {
                                    actionContainer.Function?.Invoke(); // Invoke the main function

                                }
                                else
                                {
                                    Plugin.Log.LogWarning($"No action found for command '{command}' in the GlobalActions dictionary.");
                                }
                            }
                            catch (Exception e)
                            {
                                Plugin.Log.LogError($"Error processing command '{upgradeParam.Path}': {e.Message}\n{e.StackTrace}");
                            }
                            continue;
                        }

                        UpgradeRunner.FieldValueInfoItem fieldInfoAndValue = UpgradeRunner.GetFieldInfoAndValue(upgradeParam.Path, paramsObj); // this line here fails
                        if (fieldInfoAndValue.info.FieldType == typeof(int))
                        {
                            if (upgradeParam.Action == UpgradeParam.Operation.Add)
                            {
                                fieldInfoAndValue.info.SetValue(fieldInfoAndValue.val, (int)fieldInfoAndValue.info.GetValue(fieldInfoAndValue.val) + Mathf.RoundToInt(upgradeParam.Value));
                            }
                            else if (upgradeParam.Action == UpgradeParam.Operation.Multiply)
                            {
                                float num = (float)((int)fieldInfoAndValue.info.GetValue(fieldInfoAndValue.val));
                                fieldInfoAndValue.info.SetValue(fieldInfoAndValue.val, Mathf.RoundToInt(num * upgradeParam.Value));
                            }
                            else if (upgradeParam.Action == UpgradeParam.Operation.Set)
                            {
                                fieldInfoAndValue.info.SetValue(fieldInfoAndValue.val, Mathf.RoundToInt(upgradeParam.Value));
                            }
                        }
                        else if (fieldInfoAndValue.info.FieldType == typeof(float))
                        {
                            if (upgradeParam.Action == UpgradeParam.Operation.Add)
                            {
                                fieldInfoAndValue.info.SetValue(fieldInfoAndValue.val, (float)fieldInfoAndValue.info.GetValue(fieldInfoAndValue.val) + upgradeParam.Value);
                            }
                            else if (upgradeParam.Action == UpgradeParam.Operation.Multiply)
                            {
                                fieldInfoAndValue.info.SetValue(fieldInfoAndValue.val, (float)fieldInfoAndValue.info.GetValue(fieldInfoAndValue.val) * upgradeParam.Value);
                            }
                            else if (upgradeParam.Action == UpgradeParam.Operation.Set)
                            {
                                fieldInfoAndValue.info.SetValue(fieldInfoAndValue.val, upgradeParam.Value);
                            }
                        }
                        else if (fieldInfoAndValue.info.FieldType == typeof(bool))
                        {
                            fieldInfoAndValue.info.SetValue(fieldInfoAndValue.val, upgradeParam.Value > 0.5f);
                        }
                    }
                }
            }
            return false; // don't continue with the original method
        }
    }

    [HarmonyPatch(typeof(GameDataPresets), "NewGameData")]
    class GameDataPresets_NewGameData_Patch
    {
        static void Prefix()
        {
            if (GlobalActions.Actions.Count > 0)
            {
                foreach (var actionPair in GlobalActions.Actions)
                {
                    ActionContainer actionContainer = actionPair.Value;

                    actionContainer.Cleanup?.Invoke(); // Invoke the cleanup function
                }
            }
        }
    }
}
