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

// TODO: TEST THIS HEAVILY. NOT SURE IF IT WORKS AT ALL

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

            StartCoroutine(WaitForFriendDefLoader());
            StartCoroutine(WaitForTurretDefLoader());
        }

        public IEnumerator WaitForFriendDefLoader()
        {
            FriendDefLoader._init();
            while (FriendDefLoader._map.Count == 0)
            {
                yield return (object)new WaitForSeconds(0.1f);
            }
            Upgrade.OnFriendDefLoaderInitialized();
        }

        public IEnumerator WaitForTurretDefLoader()
        {
            var turretLoader = SingletonScriptableObject<TurretDefLoader>.I;
            while (turretLoader.Defs.Count == 0)
            {
                yield return (object)new WaitForSeconds(0.1f);
            }
            Upgrade.OnTurretDefLoaderInitialized(turretLoader);
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

    public class Upgrade
    {
        private static List<Upgrade> _allUpgrades = new List<Upgrade>();

        public static List<Upgrade> AllUpgrades { get => _allUpgrades; }
        public static List<UpgradeDef> AllUpgradeDefs = new List<UpgradeDef>();
        private static List<LootCollectionIdsEnum> _lootPools = new List<LootCollectionIdsEnum>();
        public static List<LootCollectionIdsEnum> LootPools { get => _lootPools; }

        public UpgradeDef _upgradeDef;
        private List<UpgradeParam> _upgradeParams = new List<UpgradeParam>();
        private LootDefProperties _lootDefProperties;
        public Sprite myCustomSprite;
        private static Dictionary<string, FriendDef> FriendDefMap;
        public bool UpgradeAddsFriends = false;
        public List<FriendDef> FriendsToAdd = new List<FriendDef>();
        private static Dictionary<string, TurretDef> TurretDict = new Dictionary<string, TurretDef>();
        public bool UpgradeAddsTurrets = false;
        public List<TurretDef> TurretsToAdd = new List<TurretDef>();

        public string Name;
        public string Description;
        public string ImageFilePath;

        public bool DoDebug = false;


        // before they are able to do anything, items require names, descriptions, and an image file path
        public Upgrade(string name, string description, string imageFilePath)
        {
            this.Name = name;
            this.Description = description;
            this.ImageFilePath = imageFilePath;

        }
        
        public UpgradeDef CreateUpgradeDef() {
            // creating new instance of UpgradeDef
            _upgradeDef = ScriptableObject.CreateInstance<UpgradeDef>();
            _upgradeDef.name = this.Name;
            _upgradeDef.UpgradeType = UpgradeDef.UpgradeTypeEnum.Upgrade;
            _upgradeDef.Disabled = false;
            _upgradeDef.MaxStacks = 1;
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
            _lootDefProperties.Rarity = Atomicrops.Core.Loot.Rarity_.Common; // important
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

            // create new upgrade param
            UpgradeParam myUpgradeParam = new UpgradeParam();

            myUpgradeParam.Path = $"#{this.Name}";
            myUpgradeParam.Value = 1f;
            myUpgradeParam.Action = UpgradeParam.Operation.Set;

            // use reflection to set valuemin valuemax
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

            // add custom param to GlobalActions for later use
            GlobalActions.AddAction($"#{this.Name}", actionMethod, cleanupMethod);
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

            Upgrade._allUpgrades.Add(this);
            Upgrade._lootPools.Add(id);

        }

        public void AddFriends(Dictionary<string, int> friends)
        {

            this.UpgradeAddsFriends = true;

            foreach (var item in friends)
            {
                try
                {
                    for (int i = 0; i < item.Value; i++)
                    {
                        this.FriendsToAdd.Add(FriendDefMap[item.Key]);
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"{e}\nFailed to add friend {item.Key}.\nOnly accepted values are bee, chickenweed, cow, pig, and dronerifler");
                    return;
                }
            }
        }

        public void AddTurrets(Dictionary<string, int> turrets)
        {
            // TODO: FINISH THIS. MAKE THE TURRETDEFLIST A DICTIONARY ONCE YOU FIGURE OUT WHAT THE TURRETDEFS IN THE LIST ARE
            // 1 is turret, 2 is curret, 3 is scarecrow

            this.UpgradeAddsTurrets = true;

            foreach (var item in turrets)
            {
                try
                {
                    for (int i = 0; i < item.Value; i++)
                    {
                        this.TurretsToAdd.Add(TurretDict[item.Key]);
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"{e}\nFailed to add turret {item.Key}.\nOnly accepted values are turret, curret, and scarecrow");
                    return;
                }
            }


        }

        public static void OnFriendDefLoaderInitialized()
        {
            FriendDefMap = FriendDefLoader._map;

            FriendDefMap.Add("AlienPet", FriendDefLoader.I.AlienPet);

            // TODO: ADD SPOUSES
        }

        public static void OnTurretDefLoaderInitialized(TurretDefLoader turretDefLoader)
        {
            TurretDict.Add("turret", turretDefLoader.Defs[0]);
            TurretDict.Add("curret", turretDefLoader.Defs[1]);
            TurretDict.Add("scarecrow", turretDefLoader.Defs[2]);
        }

        public void ToggleDebug()
        {
            this.DoDebug = true;
            Plugin.Log.LogInfo($"Debug for {this.Name} is on!");
        }


    }

    [HarmonyPatch(typeof(LootCollection), MethodType.Constructor)]
    [HarmonyPatch(new Type[] { typeof(LootCollectionDef), typeof(LootCollectionIdsEnum), typeof(int), typeof(bool), typeof(bool), typeof(bool) })]
    class LootCollection_Constructor_Patch
    {
        static void Postfix(LootCollection __instance, LootCollectionDef collectionDef, LootCollectionIdsEnum id, int seed, bool doDlcCheck, bool isCrow, bool isClassic)
        {


            if (Upgrade.AllUpgradeDefs.Count == 0)
            {
                foreach (var def in Upgrade.AllUpgrades)
                {
                    Upgrade.AllUpgradeDefs.Add(def.CreateUpgradeDef());
                }
            }

            for (int i = 0; i < Upgrade.AllUpgrades.Count; i++)
        {
            if (id == Upgrade.LootPools[i])
            {
                // get the private field_ loots using reflection
                FieldInfo field = typeof(LootCollection).GetField("_loots", BindingFlags.NonPublic | BindingFlags.Instance);

                if (field != null)
                {
                    // Get the current value of _loots
                    List<ILootDef> loots = field.GetValue(__instance) as List<ILootDef>;

                    if (loots != null)
                    {
                        // Use current millisecond combined with the hash code of the item's name as the seed for the Random object
                        int randomSeed = DateTime.Now.Millisecond + Upgrade.AllUpgrades[i].Name.GetHashCode();
                        System.Random rand = new System.Random(randomSeed);

                        // calculate a random index between 0 and the count of items in the list
                        int randomIndex = rand.Next(loots.Count);

                        if (Upgrade.AllUpgrades[i].DoDebug == true)
                        {
                            // insert loots and beginning of loot table
                            loots.Insert(0, Upgrade.AllUpgrades[i]._upgradeDef);
                        }
                        else
                        {
                            // insert myUpgrade at a random position if debugging is not turned on
                            loots.Insert(randomIndex, Upgrade.AllUpgrades[i]._upgradeDef);
                        }


                        // Set the field's value to the new list
                        field.SetValue(__instance, loots);
                    }
                }
            }
        }
        }
    }

}