using RoR2;
using BepInEx;
using MonoMod.Cil;
using UnityEngine;
using Mono.Cecil.Cil;
using System;
using BepInEx.Configuration;
using UnityEngine.Networking;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using TILER2;
using TMPro;
using R2API;
using RoR2.ExpansionManagement;

namespace ThinkInvisible.RoR2TTCG {
    
    [BepInDependency(R2API.R2API.PluginGUID, R2API.R2API.PluginVersion)]
    [BepInDependency(TILER2Plugin.ModGuid, TILER2Plugin.ModVer)]
    [BepInPlugin(ModGuid, ModName, ModVer)]
    public class RoR2TTCGPlugin:BaseUnityPlugin {
        public const string ModVer = "1.0.0";
        public const string ModName = "RoR2TTCG";
        public const string ModGuid = "com.ThinkInvisible.RoR2TTCG";

        //todo: prep github, debug cam track

        internal static BepInEx.Logging.ManualLogSource _logger;

        private static ConfigFile cfgFile;
        internal static AssetBundle resources;

        public class GlobalConfig : AutoConfigContainer {
            [AutoConfig("If true, hides the dynamic description text on trading card pickup models. Enabling this may slightly improve performance.",
                AutoConfigFlags.DeferForever)]
            public bool hideDesc { get; private set; } = false;

            [AutoConfig("If true, descriptions on trading card pickup models will be the (typically longer) description text of the item. If false, pickup text will be used instead.",
                AutoConfigFlags.DeferForever)]
            public bool longDesc { get; private set; } = true;

            [AutoConfig("If true, trading card pickup models will have customized spin behavior which makes descriptions more readable. Disabling this may slightly improve compatibility and performance.",
                AutoConfigFlags.DeferForever)]
            public bool spinMod { get; private set; } = true;
        }

        public static readonly GlobalConfig globalConfig = new GlobalConfig();

        public void Awake() {
            _logger = Logger;
            cfgFile = new ConfigFile(Paths.ConfigPath + "\\" + ModGuid + ".cfg", true);
            using(var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RoR2TTCG.ror2ttcg_assets")) {
                resources = AssetBundle.LoadFromStream(stream);
            }

            globalConfig.BindAll(cfgFile, "RoR2TTCG", "Global");

            On.RoR2.PickupCatalog.Init += On_PickupCatalogInit;
            On.RoR2.UI.LogBook.LogBookController.BuildPickupEntries += On_LogbookBuildPickupEntries;
            Language.onCurrentLanguageChanged += Language_onCurrentLanguageChanged;
            On.RoR2.PickupDisplay.RebuildModel += PickupDisplay_RebuildModel;

            if(globalConfig.spinMod)
                IL.RoR2.PickupDisplay.Update += IL_PickupDisplayUpdate;

            T2Module.InitModules(new T2Module.ModInfo {
                displayName = "Risk of Rain 2: The Trading Card Game",
                longIdentifier = "RoR2TTCG",
                shortIdentifier = "TCG",
                mainConfigFile = cfgFile
            });
        }

        private void PickupDisplay_RebuildModel(On.RoR2.PickupDisplay.orig_RebuildModel orig, PickupDisplay self) {
            orig(self);
            if(self.modelObject?.transform.Find("cardfront")) {
                self.modelScale = 12f;
                self.modelObject.transform.localScale = Vector3.one * 12f;
            }
        }

        public void Start() {
            pluginIsStarted = true;
        }

        private void On_PickupCatalogInit(On.RoR2.PickupCatalog.orig_Init orig) {
            orig();

            Logger.LogDebug("Processing pickup models...");

            var eqpCardPrefab = RoR2TTCGPlugin.resources.LoadAsset<GameObject>("Assets/RoR2TTCG/models/EqpCard.prefab");
            var lunarCardPrefab = RoR2TTCGPlugin.resources.LoadAsset<GameObject>("Assets/RoR2TTCG/models/LunarCard.prefab");
            var lunEqpCardPrefab = RoR2TTCGPlugin.resources.LoadAsset<GameObject>("Assets/RoR2TTCG/models/LqpCard.prefab");
            var t1CardPrefab = RoR2TTCGPlugin.resources.LoadAsset<GameObject>("Assets/RoR2TTCG/models/CommonCard.prefab");
            var t2CardPrefab = RoR2TTCGPlugin.resources.LoadAsset<GameObject>("Assets/RoR2TTCG/models/UncommonCard.prefab");
            var t3CardPrefab = RoR2TTCGPlugin.resources.LoadAsset<GameObject>("Assets/RoR2TTCG/models/RareCard.prefab");
            var bossCardPrefab = RoR2TTCGPlugin.resources.LoadAsset<GameObject>("Assets/RoR2TTCG/models/BossCard.prefab");

            int replacedItems = 0;
            int replacedEqps = 0;

            foreach(var pickup in PickupCatalog.allPickups) {
                GameObject npfb;
                if(pickup.interactContextToken == "EQUIPMENT_PICKUP_CONTEXT") {
                    if(pickup.equipmentIndex == EquipmentIndex.None)
                        continue;
                    var eqp = EquipmentCatalog.GetEquipmentDef(pickup.equipmentIndex);
                    if(!eqp || !eqp.canDrop) continue;
                    npfb = eqp.isLunar ? lunEqpCardPrefab : eqpCardPrefab;
                    replacedEqps++;
                } else if(pickup.interactContextToken == "ITEM_PICKUP_CONTEXT") {
                    if(pickup.itemIndex == ItemIndex.None)
                        continue;

                    var item = ItemCatalog.GetItemDef(pickup.itemIndex);
                    if(!item) continue;
                    switch(item.tier) {
                        case ItemTier.Tier1:
                            npfb = t1CardPrefab; break;
                        case ItemTier.Tier2:
                            npfb = t2CardPrefab; break;
                        case ItemTier.Tier3:
                            npfb = t3CardPrefab; break;
                        case ItemTier.Lunar:
                            npfb = lunarCardPrefab; break;
                        case ItemTier.Boss:
                            npfb = bossCardPrefab; break;
                        default:
                            continue;
                    }
                    if(npfb != null)
                        replacedItems++;
                } else continue;
                if(npfb != null)
                    pickup.displayPrefab = npfb;
            }
            Logger.LogDebug("Replaced " + replacedItems + " item models and " + replacedEqps + " equipment models.");

            int replacedDescs = 0;

            var tmpfont = LegacyResourcesAPI.Load<TMP_FontAsset>("tmpfonts/misc/tmpRiskOfRainFont Bold OutlineSDF");
            var tmpmtl = LegacyResourcesAPI.Load<Material>("tmpfonts/misc/tmpRiskOfRainFont Bold OutlineSDF");

            foreach(var pickup in PickupCatalog.allPickups) {
                //pattern-match for CI card prefabs
                var ctsf = pickup.displayPrefab?.transform;
                if(!ctsf) continue;

                var cfront = ctsf.Find("cardfront");
                if(cfront == null) continue;
                var croot = cfront.Find("carddesc");
                var cnroot = cfront.Find("cardname");
                var csprite = ctsf.Find("ovrsprite");

                if(croot == null || cnroot == null || csprite == null) continue;

                //instantiate and update references
                pickup.displayPrefab = pickup.displayPrefab.InstantiateClone($"TCGPickupCardPrefab{pickup.pickupIndex}", false);
                ctsf = pickup.displayPrefab.transform;
                cfront = ctsf.Find("cardfront");
                croot = cfront.Find("carddesc");
                cnroot = cfront.Find("cardname");
                csprite = ctsf.Find("ovrsprite");

                csprite.GetComponent<MeshRenderer>().material.mainTexture = pickup.iconTexture;

                if(globalConfig.spinMod)
                    pickup.displayPrefab.AddComponent<SpinModFlag>();

                string pname;
                string pdesc;
                Color prar = new Color(1f, 0f, 1f);
                if(pickup.interactContextToken == "EQUIPMENT_PICKUP_CONTEXT") {
                    var eqp = EquipmentCatalog.GetEquipmentDef(pickup.equipmentIndex);
                    if(eqp == null) continue;
                    pname = Language.GetString(eqp.nameToken);
                    pdesc = Language.GetString(globalConfig.longDesc ? eqp.descriptionToken : eqp.pickupToken);
                    prar = new Color(1f, 0.7f, 0.4f);
                } else if(pickup.interactContextToken == "ITEM_PICKUP_CONTEXT") {
                    var item = ItemCatalog.GetItemDef(pickup.itemIndex);
                    if(item == null) continue;
                    pname = Language.GetString(item.nameToken);
                    pdesc = Language.GetString(globalConfig.longDesc ? item.descriptionToken : item.pickupToken);
                    switch(item.tier) {
                        case ItemTier.Boss: prar = new Color(1f, 1f, 0f); break;
                        case ItemTier.Lunar: prar = new Color(0f, 0.6f, 1f); break;
                        case ItemTier.Tier1: prar = new Color(0.8f, 0.8f, 0.8f); break;
                        case ItemTier.Tier2: prar = new Color(0.2f, 1f, 0.2f); break;
                        case ItemTier.Tier3: prar = new Color(1f, 0.2f, 0.2f); break;
                    }
                } else continue;

                if(globalConfig.hideDesc) {
                    Destroy(croot.gameObject);
                    Destroy(cnroot.gameObject);
                } else {
                    var cdsc = croot.gameObject.AddComponent<TextMeshPro>();
                    cdsc.richText = true;
                    cdsc.enableWordWrapping = true;
                    cdsc.alignment = TextAlignmentOptions.Center;
                    cdsc.margin = new Vector4(4f, 1.874178f, 4f, 1.015695f);
                    cdsc.enableAutoSizing = true;
                    cdsc.overrideColorTags = false;
                    cdsc.fontSizeMin = 1f;
                    cdsc.fontSizeMax = 8f;
                    cdsc.fontSize = 1f;
                    _ = cdsc.renderer;
                    cdsc.font = tmpfont;
                    cdsc.material = tmpmtl;
                    cdsc.color = Color.black;
                    cdsc.text = pdesc;

                    var cname = cnroot.gameObject.AddComponent<TextMeshPro>();
                    cname.richText = true;
                    cname.enableWordWrapping = false;
                    cname.alignment = TextAlignmentOptions.Center;
                    cname.margin = new Vector4(6.0f, 1.2f, 6.0f, 1.4f);
                    cname.enableAutoSizing = true;
                    cname.overrideColorTags = true;
                    cname.fontSizeMin = 1f;
                    cname.fontSizeMax = 10f;
                    cname.fontSize = 1f;
                    _ = cname.renderer;
                    cname.font = tmpfont;
                    cname.material = tmpmtl;
                    cname.outlineColor = Color.black;
                    cname.outlineWidth = 1f;
                    cname.color = prar;
                    cname.fontStyle = FontStyles.Bold;
                    cname.text = pname;
                }
                replacedDescs++;
            }
            Logger.LogDebug((globalConfig.hideDesc ? "Destroyed " : "Inserted ") + replacedDescs + " pickup model descriptions.");
        }

        private void IL_PickupDisplayUpdate(ILContext il) {
            ILCursor c = new ILCursor(il);

            bool ILFound = c.TryGotoNext(MoveType.After,
                x => x.MatchLdfld<PickupDisplay>("modelObject"));
            GameObject puo = null;
            if(ILFound) {
                c.Emit(OpCodes.Dup);
                c.EmitDelegate<Action<GameObject>>(x => {
                    puo = x;
                });
            } else {
                Logger.LogError("Failed to apply IL patch (pickup model spin modifier 1)");
                return;
            }

            ILFound = c.TryGotoNext(MoveType.After,
                x => x.MatchLdarg(0),
                x => x.MatchLdfld<PickupDisplay>("spinSpeed"),
                x => x.MatchLdarg(0),
                x => x.MatchLdfld<PickupDisplay>("localTime"),
                x => x.MatchMul());
            if(ILFound) {
                c.EmitDelegate<Func<float, float>>((origAngle) => {
                    if(!puo || !puo.GetComponent<SpinModFlag>() || !NetworkClient.active || PlayerCharacterMasterController.instances.Count == 0) return origAngle;
                    var body = PlayerCharacterMasterController.instances[0].master.GetBody();
                    if(!body) return origAngle;
                    CameraRigController rig = null;
                    foreach(var rigTest in CameraRigController.readOnlyInstancesList) {
                        if(rigTest.target == body.gameObject && rigTest._localUserViewer.cachedBodyObject == body.gameObject && !rigTest.hasOverride) {
                            rig = rigTest;
                            break;
                        }
                    }
                    Transform btsf;
                    if(rig != null)
                        btsf = rig.sceneCam.transform;
                    else btsf = body.coreTransform;
                    if(!btsf) btsf = body.transform;
                    return RoR2.Util.QuaternionSafeLookRotation(btsf.position - puo.transform.position).eulerAngles.y
                        + (float)Math.Tanh(((origAngle / 100.0f) % 6.2832f - 3.1416f) * 2f) * 180f
                        + 180f
                        - (puo.transform.parent?.eulerAngles.y ?? 0f);
                });
            } else {
                Logger.LogError("Failed to apply IL patch (pickup model spin modifier 2)");
            }

        }

        private RoR2.UI.LogBook.Entry[] On_LogbookBuildPickupEntries(On.RoR2.UI.LogBook.LogBookController.orig_BuildPickupEntries orig, Dictionary<ExpansionDef, bool> expansionAvailability) {
            var retv = orig(expansionAvailability);
            Logger.LogDebug("Processing logbook models...");
            int replacedModels = 0;
            foreach(RoR2.UI.LogBook.Entry e in retv) {
                if(!(e.extraData is PickupIndex pind)) continue;
                var npfb = PickupCatalog.GetPickupDef(pind).displayPrefab;
                if(npfb.transform.Find("cardfront")) {
                    e.modelPrefab = npfb;
                    replacedModels++;
                }
            }
            Logger.LogDebug("Modified " + replacedModels + " logbook models.");
            return retv;
        }

        bool pluginIsStarted = false; //set to true in Start
        private void Language_onCurrentLanguageChanged() {
            if(!pluginIsStarted) return;
            foreach(var item in ItemCatalog.allItemDefs) {
                if(item == null) continue;
                var pind = PickupCatalog.FindPickupIndex(item.itemIndex);
                if(pind == PickupIndex.none) continue;
                var pdef = PickupCatalog.GetPickupDef(pind);
                var cobj = pdef.displayPrefab;
                if(cobj == null) continue;
                var ctsf = pdef.displayPrefab.transform;
                if(ctsf == null) continue;
                var cfront = ctsf.Find("cardfront");
                if(cfront == null) continue;

                cfront.Find("carddesc").GetComponent<TextMeshPro>().text = Language.GetString(globalConfig.longDesc ? item.descriptionToken : item.pickupToken);
                cfront.Find("cardname").GetComponent<TextMeshPro>().text = Language.GetString(item.nameToken);
            }
            foreach(var eqpind in EquipmentCatalog.allEquipment) {
                var eqp = EquipmentCatalog.GetEquipmentDef(eqpind);
                if(eqp == null) continue;
                var pind = PickupCatalog.FindPickupIndex(eqp.equipmentIndex);
                if(pind == PickupIndex.none) continue;
                var pdef = PickupCatalog.GetPickupDef(pind);
                var cobj = pdef.displayPrefab;
                if(cobj == null) continue;
                var ctsf = pdef.displayPrefab.transform;
                if(ctsf == null) continue;
                var cfront = ctsf.Find("cardfront");
                if(cfront == null) continue;

                cfront.Find("carddesc").GetComponent<TextMeshPro>().text = Language.GetString(globalConfig.longDesc ? eqp.descriptionToken : eqp.pickupToken);
                cfront.Find("cardname").GetComponent<TextMeshPro>().text = Language.GetString(eqp.nameToken);
            }
            if(LocalUserManager.readOnlyLocalUsersList.Count > 0) {
                var prof = LocalUserManager.readOnlyLocalUsersList[0].userProfile;
                var itemLogs = RoR2.UI.LogBook.LogBookController.categories.First(x => x.nameToken == "LOGBOOK_CATEGORY_ITEMANDEQUIPMENT");
                foreach(var entry in itemLogs.buildEntries(prof)) {
                    if(entry == null || !(entry.extraData is PickupIndex ind)) continue;
                    var pdef = PickupCatalog.GetPickupDef(ind);
                    if(pdef == null) continue;
                    entry.modelPrefab = pdef.displayPrefab;
                }
            }
        }
    }

    public class SpinModFlag : MonoBehaviour { }
}