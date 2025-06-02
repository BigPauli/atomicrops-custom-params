using BepInEx;
using HarmonyLib;
using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SharedLib;

using Atomicrops.Core;
using Atomicrops.Core.Upgrades;
using Atomicrops.Core.Loot;
using Atomicrops.Core.SoDb2;
using Atomicrops.Game.Data;
using Atomicrops.Game.Loot;
using Atomicrops.Game.ParamsSystem;
using Atomicrops.Crops;
using CustomParams;

// TODO: FIX ITEM NOT SHOWING UP PROBLEM IS LIKELY DUE TO ALLUPGRADES ATTRIBUTE

namespace CustomParams
{

    public static class MyPluginInfo
    {
        public const string PLUGIN_GUID = "pauli.plugin.CustomParams";
        public const string PLUGIN_NAME = "CustomParams";
        public const string PLUGIN_VERSION = "1.1.0";
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
            return false;
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

        static void Postfix(GameData data, int seed, bool isDaily, int year, ProfileModel profile)
        {
            Upgrade.OnFriendDefLoaderInitialized();
            Upgrade.OnSpouseDefLoaderInitialized();
            Upgrade.OnTurretDefLoaderInitialized();

            foreach (var item in Upgrade.FriendDefMap)
            {
                Plugin.Log.LogInfo($"{item.Key}: {item.Value}");
            }
            

            Upgrade.AllUpgradeDefs.Clear();
            foreach (var upgrade in Upgrade.AllUpgrades)
            {
                var upgradeDef = upgrade.GetOrCreateUpgradeDef();
                if (upgradeDef != null)
                {
                    Upgrade.AllUpgradeDefs.Add(upgradeDef);
                }
            }

            for (int i = 0; i < Upgrade.AllUpgrades.Count; i++)
            {
                if (Upgrade.AllUpgradeDefs[i] == null) continue;

                LootCollection targetCollection = null;
                LootCollectionIdsEnum pool = Upgrade.LootPools[i];

                switch (pool)
                {
                    case LootCollectionIdsEnum.Main:
                        targetCollection = data.MainLootCollection;
                        break;
                    case LootCollectionIdsEnum.Special:
                        targetCollection = data.SpecialLootCollection;
                        break;
                    case LootCollectionIdsEnum.GoldenChest:
                        targetCollection = data.GoldenChestLootCollection;
                        break;
                    case LootCollectionIdsEnum.DeerShop:
                        targetCollection = data.DeerShopLootCollection;
                        break;
                    case LootCollectionIdsEnum.Crow:
                        targetCollection = data.CrowLootCollection;
                        break;
                    case LootCollectionIdsEnum.CrowOregano:
                        targetCollection = data.CrowOreganoLootCollection;
                        break;
                    case LootCollectionIdsEnum.CrowCropLeveling:
                        targetCollection = data.CrowCropLevelingLootCollection;
                        break;
                    case LootCollectionIdsEnum.PowerSow:
                        targetCollection = data.PowerSowLootCollection;
                        break;
                    case LootCollectionIdsEnum.GardenBed:
                        targetCollection = data.GardenBedLootCollection;
                        break;
                    case LootCollectionIdsEnum.TreeUpgrades:
                        targetCollection = data.TreeUpgradeCollection;
                        break;
                }

                if (targetCollection != null)
                {
                    var lootsField = typeof(LootCollection).GetField("_loots", BindingFlags.NonPublic | BindingFlags.Instance);
                    var loots = (List<ILootDef>)lootsField.GetValue(targetCollection);

                    if (Upgrade.AllUpgrades[i].DoDebug)
                    {
                        loots.Insert(0, Upgrade.AllUpgradeDefs[i]);
                    }
                    else
                    {
                        System.Random rand = new System.Random(seed + Upgrade.AllUpgrades[i].Name.GetHashCode());
                        int index = rand.Next(loots.Count + 1);
                        loots.Insert(index, Upgrade.AllUpgradeDefs[i]);
                    }

                    lootsField.SetValue(targetCollection, loots);
                }
            }
        }
    }

    [HarmonyPatch(typeof(MetaPrevRunRewardUtils), "GenerateRewardModelAndClearPorfileFlag")]
    class MetaPrevRunRewardUtils_GenerateRewardModelAndClearPorfileFlag_Patch
    {
        static bool Prefix(ref MetaPrevRunRewardsModel __result)
        {
            if (Upgrade.DebugPresent == true) {
                MetaPrevRunRewardsModel metaPrevRunRewardsModel = new MetaPrevRunRewardsModel();
                metaPrevRunRewardsModel.Upgrades = new List<UpgradeDef>();

                for (int i = 0; i < Upgrade.AllUpgrades.Count; i++)
                {
                    if (Upgrade.AllUpgrades[i].DoDebug == true)
                    {
                        metaPrevRunRewardsModel.Upgrades.Add(Upgrade.AllUpgradeDefs[i]);
                    }
                }
                __result = metaPrevRunRewardsModel;
                return false;
            }

            return true;
        }
    }

    public class Upgrade
    {

        public static List<Upgrade> AllUpgrades = new List<Upgrade>();
        public static List<UpgradeDef> AllUpgradeDefs = new List<UpgradeDef>();
        private static List<LootCollectionIdsEnum> _lootPools = new List<LootCollectionIdsEnum>();
        public static List<LootCollectionIdsEnum> LootPools { get => _lootPools; }

        public UpgradeDef _upgradeDef;
        private List<UpgradeParam> _upgradeParams = new List<UpgradeParam>();
        private LootDefProperties _lootDefProperties;
        public Sprite myCustomSprite;
        public static Dictionary<string, FriendDef> FriendDefMap = new Dictionary<string, FriendDef>();
        public bool UpgradeAddsFriends = false;
        public List<string> FriendsToAddStr = new List<string>();
        public List<FriendDef> FriendsToAdd = new List<FriendDef>();
        public static Dictionary<string, TurretDef> TurretDefMap = new Dictionary<string, TurretDef>();
        public bool UpgradeAddsTurrets = false;
        public List<string> TurretsToAddStr = new List<string>();
        public List<TurretDef> TurretsToAdd = new List<TurretDef>();

        public string Name;
        public string Description;
        public string ImageFilePath;

        public bool DoDebug = false;
        public static bool DebugPresent = false;
        private List<(Action actionMethod, Action cleanupMethod)> _pendingCustomParams = new();



        // before they are able to do anything, items require names, descriptions, and an image file path
        public Upgrade(string name, string description, string imageFilePath)
        {
            this.Name = name;
            this.Description = description;
            this.ImageFilePath = imageFilePath;

        }

        public UpgradeDef GetOrCreateUpgradeDef()
        {
            if (_upgradeDef != null)
            {
                return _upgradeDef;
            }

            // Safety checks for FriendDefs and TurretDefs being loaded
            if (UpgradeAddsFriends && !FriendDefMap.ContainsKey("pig") && !FriendDefMap.ContainsKey("rue"))
            {
                Plugin.Log.LogWarning($"FriendDefMap not yet initialized for upgrade {Name}. Skipping creation.");
                return null;
            }

            if (UpgradeAddsTurrets && TurretDefMap.Count == 0)
            {
                Plugin.Log.LogWarning($"TurretDefMap not yet initialized for upgrade {Name}. Skipping creation.");
                return null;
            }

            this._addFriends();
            this._addTurrets();


            // creating new instance of UpgradeDef
            _upgradeDef = ScriptableObject.CreateInstance<UpgradeDef>();
            _upgradeDef.name = this.Name;
            _upgradeDef.UpgradeType = UpgradeDef.UpgradeTypeEnum.Upgrade;
            _upgradeDef.Disabled = false;
            _upgradeDef.MaxStacks = 100;
            _upgradeDef.RemoveUpgradesWhenPickedUp = new List<UpgradeDef>();
            _upgradeDef.DoAddDependents = false;
            _upgradeDef.DependentCollection = LootCollectionIdsEnum.Main;
            _upgradeDef.Dependents = new List<UpgradeDef>();
            _upgradeDef.DependentsILoot = new List<SoDb2Item>();
            _upgradeDef.DoInstantApply = false;
            _upgradeDef.InstantApply = null;
            _upgradeDef.InstantApplyAmount = 1;
            _upgradeDef.DoRandomSelectInstantApply = false;
            _upgradeDef.InstantApplyRandomSelect = new List<InstantApplyLootDef>();
            _upgradeDef.DoAddSeeds = false;
            _upgradeDef.AddSeeds = null;
            _upgradeDef.AddSeedsList = new List<CropDef>();
            _upgradeDef.AddSeedsCount = 0;
            _upgradeDef.AddAloeVeraHeals = 0;
            _upgradeDef.DropOnDamage = false;
            _upgradeDef.DropFx = null;
            _upgradeDef.DropSound = null;
            _upgradeDef.IsTomorrowLongerUpgrade = false;
            _upgradeDef.DoAddFriends = this.UpgradeAddsFriends;
            _upgradeDef.AddFriendAroundPlayer = this.UpgradeAddsFriends;
            _upgradeDef.AddFriends = this.FriendsToAdd;
            _upgradeDef.DoAddTurrets = this.UpgradeAddsTurrets;
            _upgradeDef.Turrets = this.TurretsToAdd;
            _upgradeDef.RunFunction = UpgradeDef.FunctionEnum.None;
            _upgradeDef.DoAddGardenBed = false;
            _upgradeDef.GardenBeds = new List<GardenBedDef>();
            _upgradeDef.AddPowerSowableSeeds = 0;

            // use reflection to set params list to just an empty list for now
            var field = typeof(UpgradeDef).GetField("Params", BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(_upgradeDef, _upgradeParams);

            // Creating LootProperties
            _lootDefProperties = new LootDefProperties();
            _lootDefProperties.Tag = LootDefProperties.TagEnum.None;
            _lootDefProperties.Dlc = AtomicropsDlcManager.Dlcs.None;
            _lootDefProperties.Build = "";
            _lootDefProperties.DisplayName = this.Name; // important
            _lootDefProperties.Description = this.Description; // important
            _lootDefProperties.DisplayNameLocalizationKey = "";
            _lootDefProperties.DoNameFormatter = false;
            _lootDefProperties.NameFormatterArg1 = "";
            _lootDefProperties.DoLocNameFormatterArg1 = false;
            _lootDefProperties.DescriptionLocalizationKey = "";
            _lootDefProperties.DoDescFormatter = false;
            _lootDefProperties.DescFormatterArg1 = "";
            _lootDefProperties.DoLocDescFormatterArg1 = false;
            _lootDefProperties.DescFormatterArg2 = "";
            _lootDefProperties.DoLocDescFormatterArg2 = false;
            _lootDefProperties.DescFormatterArg3 = "";
            _lootDefProperties.DoLocDescFormatterArg3 = false;
            _lootDefProperties.DoAltDescFormattersForCrow = false;
            _lootDefProperties.AltDescFormattersForCrow = new LootDefProperties.DescFormatters();
            _lootDefProperties.DoDescComposition = false;
            _lootDefProperties.AppendDescComposition = false;
            _lootDefProperties.DescCompJoinUseComma = false;
            _lootDefProperties.LocElementsForDescComposition = new List<LocElement>();
            _lootDefProperties.Rarity = Atomicrops.Core.Loot.Rarity_.Common;
            _lootDefProperties.PrimaryBiome = 0;
            _lootDefProperties.LuckMult = 0f;
            _lootDefProperties.UseCustomCost = false;
            _lootDefProperties.CustomCost = 100;
            _lootDefProperties.DontSpawnOnLastDay = false;
            _lootDefProperties.DoMutuallyExclusive = false;
            _lootDefProperties.MutuallyExclusive = null;
            _lootDefProperties.InventoryIconAnim = null;
            _lootDefProperties.InventoryIconSelected = null;
            _lootDefProperties.InventoryIconSelectedAnim = null;
            _lootDefProperties.InGameLootSprite = null;
            _lootDefProperties.InGameLootClip = null;
            _lootDefProperties.DoAltIconsIfCrow = false;
            _lootDefProperties.IconsIfCrow = new LootDefProperties.Icons();
            _lootDefProperties.RevealClip = null;
            _lootDefProperties.LootSpriteColorMult = UnityEngine.Color.white;
            _lootDefProperties.InGameLootShadowHeightOffset = 0f;
            _lootDefProperties.SetSortOffset = false;
            _lootDefProperties.SortOffset = 0f;
            _lootDefProperties.SizeForShadow = 1f;
            _lootDefProperties.ShowTooltip = true;
            _lootDefProperties.Stack = false;
            _lootDefProperties.DoHop = true;
            _lootDefProperties.Flash = true;
            _lootDefProperties.NoToolTipRegion = false;
            _lootDefProperties.ToolTipOffset = new Vector2(0, 0.3f);

            string assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string directory = System.IO.Path.GetDirectoryName(assemblyLocation);
            string filePath = System.IO.Path.Combine(directory, this.ImageFilePath);

            byte[] imageBytes = System.IO.File.ReadAllBytes(filePath);
            Texture2D texture = new Texture2D(2, 2);
            if (UnityEngine.ImageConversion.LoadImage(texture, imageBytes))
            {

                texture.filterMode = FilterMode.Point;
                Rect rect = new Rect(0, 0, texture.width, texture.height);
                Vector2 pivot = new Vector2(0.5f, 0.5f);
                float pixelsPerUnit = 32.0f;
                myCustomSprite = Sprite.Create(texture, rect, pivot, pixelsPerUnit);
            }
            else
            {
                Debug.LogError("Failed to load image.");
            }

            // manually setting InventoryIcon and InGameLootSprite
            _lootDefProperties.InventoryIcon = myCustomSprite;
            _lootDefProperties.InGameLootSprite = myCustomSprite;

            // manually setting pickup sound event from another item
            _lootDefProperties.PickupSoundEvent = SoDb2Utils.GetItem<UpgradeDef>(10).LootProperties.PickupSoundEvent;
            _lootDefProperties.DropSoundEvent = SoDb2Utils.GetItem<UpgradeDef>(10).LootProperties.DropSoundEvent;

            // setting our new LootDefProperties to _upgradeDef
            _upgradeDef.LootProperties = _lootDefProperties;

            // add any custom params to list of params
            foreach (var (actionMethod, cleanupMethod) in _pendingCustomParams)
            {
                UpgradeParam myUpgradeParam = new UpgradeParam
                {
                    Path = $"#{this.Name}",
                    Value = 1f,
                    Action = UpgradeParam.Operation.Set
                };

                var fieldInfoMin = typeof(UpgradeParam).GetField("ValueMin", BindingFlags.NonPublic | BindingFlags.Instance);
                fieldInfoMin?.SetValue(myUpgradeParam, 1f);
                var fieldInfoMax = typeof(UpgradeParam).GetField("ValueMax", BindingFlags.NonPublic | BindingFlags.Instance);
                fieldInfoMax?.SetValue(myUpgradeParam, 1f);

                _upgradeParams.Add(myUpgradeParam);

                GlobalActions.AddAction($"#{this.Name}", actionMethod, cleanupMethod);
            }

            // All custom params now fully registered
            _pendingCustomParams.Clear();

            return _upgradeDef;
        }

        public void AddVanillaParam(string param, float value, string operation)
        {
            if (operation != "Add" && operation != "Set" && operation != "Multiply")
            {
                Plugin.Log.LogError($"{_upgradeDef.name} Incorrect operation: must be Add, Set, or Multiply");
                return;
            }

            // create new upgrade param
            UpgradeParam myUpgradeParam = new UpgradeParam();

            myUpgradeParam.Path = param;
            myUpgradeParam.Value = value;

            switch (operation)
            {
                case "Add":
                    myUpgradeParam.Action = UpgradeParam.Operation.Add;
                    break;
                case "Set":
                    myUpgradeParam.Action = UpgradeParam.Operation.Set;
                    break;
                case "Multiply":
                    myUpgradeParam.Action = UpgradeParam.Operation.Multiply;
                    break;
                default:
                    break;
            }

            // use reflection to set ValueMin and ValueMax
            var fieldInfoMin = typeof(UpgradeParam).GetField("ValueMin", BindingFlags.NonPublic | BindingFlags.Instance);
            if (fieldInfoMin != null)
            {
                fieldInfoMin.SetValue(myUpgradeParam, value);
            }

            var fieldInfoMax = typeof(UpgradeParam).GetField("ValueMax", BindingFlags.NonPublic | BindingFlags.Instance);
            if (fieldInfoMax != null)
            {
                fieldInfoMax.SetValue(myUpgradeParam, value);
            }

            // append new param to list of params
            this._upgradeParams.Add(myUpgradeParam);

            return;

        }

        public void AddVanillaParam(string param, bool value)
        {
            // create new upgrade param
            UpgradeParam myUpgradeParam = new UpgradeParam();

            myUpgradeParam.Path = param;
            myUpgradeParam.Value = value ? 1f : 0f;
            myUpgradeParam.Action = UpgradeParam.Operation.Set;

            // use reflection to set ValueMin and ValueMax
            var fieldInfoMin = typeof(UpgradeParam).GetField("ValueMin", BindingFlags.NonPublic | BindingFlags.Instance);
            if (fieldInfoMin != null)
            {
                fieldInfoMin.SetValue(myUpgradeParam, 1f);
            }

            var fieldInfoMax = typeof(UpgradeParam).GetField("ValueMax", BindingFlags.NonPublic | BindingFlags.Instance);
            if (fieldInfoMax != null)
            {
                fieldInfoMax.SetValue(myUpgradeParam, 1f);
            }

            // append new param to list of params
            this._upgradeParams.Add(myUpgradeParam);

            return;
        }

        public void AddCustomParam(Action actionMethod, Action cleanupMethod)
        {
            _pendingCustomParams.Add((actionMethod, cleanupMethod));
        }

        public void AddUpgradeToLootPool(string pool)
        {
            LootCollectionIdsEnum id;

            switch (pool)
            {
                case "Main":
                    id = LootCollectionIdsEnum.Main;
                    break;
                case "Special":
                    id = LootCollectionIdsEnum.Special;
                    break;
                case "GoldenChest":
                    id = LootCollectionIdsEnum.GoldenChest;
                    break;
                case "DeerShop":
                    id = LootCollectionIdsEnum.DeerShop;
                    break;
                case "Crow":
                    id = LootCollectionIdsEnum.Crow;
                    break;
                case "CrowOregano":
                    id = LootCollectionIdsEnum.CrowOregano;
                    break;
                case "PowerSow":
                    id = LootCollectionIdsEnum.PowerSow;
                    break;
                case "GardenBed":
                    id = LootCollectionIdsEnum.GardenBed;
                    break;
                case "TreeUpgrades":
                    id = LootCollectionIdsEnum.TreeUpgrades;
                    break;
                case "CrowCropLeveling":
                    id = LootCollectionIdsEnum.CrowCropLeveling;
                    break;
                default:
                    Plugin.Log.LogError($"{_upgradeDef.name} pool error: pool must be Main, Special, GoldenChest, DeerShop, Crow, CrowOregano, PowerSow, GardenBed, TreeUpgrades, or CrowCropLeveling");
                    return;
            }

            Upgrade.AllUpgrades.Add(this);
            Upgrade._lootPools.Add(id);

        }

        public void AddFriends(Dictionary<string, int> friends)
        {

            this.UpgradeAddsFriends = true;

            foreach (var item in friends)
            {

                for (int i = 0; i < item.Value; i++)
                {
                    this.FriendsToAddStr.Add(item.Key);
                }
            }
        }

        public void _addFriends()
        {
            foreach (var friend in this.FriendsToAddStr)
            {
                try
                {
                    this.FriendsToAdd.Add(FriendDefMap[friend]);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"{e}\nFailed to add friend {friend}.\nOnly accepted values are bee, bee2, cow, cow2, pig, pig2, chickenweed, chickenweed2, dronerifler, alienpet, rue, borage, norman, waterchris, and furryosa");
                    return;
                }
            }
        }

        public void AddTurrets(Dictionary<string, int> turrets)
        {
            // 1 is turret, 2 is curret, 3 is scarecrow

            this.UpgradeAddsTurrets = true;

            foreach (var item in turrets)
            {

                for (int i = 0; i < item.Value; i++)
                {
                    this.TurretsToAddStr.Add(item.Key);
                }
            }


        }

        public void _addTurrets()
        {
            foreach (var turret in this.TurretsToAddStr)
            {
                try
                {
                    this.TurretsToAdd.Add(TurretDefMap[turret]);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"{e}\nFailed to add turret {turret}.\nOnly accepted values are turret, scarecrow, and curret");
                    return;
                }
            }
        }

        public static void OnFriendDefLoaderInitialized()
        {
            var configFriends = SingletonScriptableObject<ConfigGame>.I.Friends;

            FriendDefMap["bee"] = configFriends.FarmAnimalsLevel1[0];
            FriendDefMap["chickenweed"] = configFriends.FarmAnimalsLevel1[1];
            FriendDefMap["cow"] = configFriends.FarmAnimalsLevel1[2];
            FriendDefMap["pig"] = configFriends.FarmAnimalsLevel1[3];

            FriendDefMap["bee2"] = configFriends.FarmAnimalsLevel2[0];
            FriendDefMap["chickenweed2"] = configFriends.FarmAnimalsLevel2[1];
            FriendDefMap["cow2"] = configFriends.FarmAnimalsLevel2[2];
            FriendDefMap["pig2"] = configFriends.FarmAnimalsLevel2[3];

            FriendDefMap["alienpet"] = configFriends.AlienPet;

            FriendDefMap["drone"] = SingletonScriptableObject<ConfigGame>.I.Player.Robusta.DefaultFriends[0];
        }









        public static void OnTurretDefLoaderInitialized()
        {
            var turretDefLoader = SingletonScriptableObject<TurretDefLoader>.I;

            if (!TurretDefMap.ContainsKey("turret"))
                TurretDefMap.Add("turret", turretDefLoader.Defs[0]);

            if (!TurretDefMap.ContainsKey("curret"))
                TurretDefMap.Add("curret", turretDefLoader.Defs[1]);

            if (!TurretDefMap.ContainsKey("scarecrow"))
                TurretDefMap.Add("scarecrow", turretDefLoader.Defs[2]);
        }

        public static void OnSpouseDefLoaderInitialized()
        {
            var eligibles = SingletonScriptableObject<GameData>.I.Eligibles;
            foreach (FriendDef spouse in eligibles._getall().Keys)
            {
                string key = spouse.Name.ToLower();
                if (!FriendDefMap.ContainsKey(key))
                {
                    FriendDefMap[key] = spouse;
                }
            }
        }



        public void ToggleDebug()
        {
            this.DoDebug = true;
            Upgrade.DebugPresent = true;
            Plugin.Log.LogInfo($"Debug for {this.Name} is on!");
        }


    }

}