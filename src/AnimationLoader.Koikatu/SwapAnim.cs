using System;
using BepInEx;
using HarmonyLib;
using IllusionUtility.GetUtility;
using SceneAssist;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Xml.Linq;
using System.Xml.Serialization;
using Illusion.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static HFlag;

[assembly: System.Reflection.AssemblyFileVersion(AnimationLoader.Koikatu.SwapAnim.Version)]

namespace AnimationLoader.Koikatu
{
    [BepInPlugin(GUID, "SwapAnim", Version)]
    public class SwapAnim : BaseUnityPlugin
    {
        public const string GUID = "SwapAnim";
        public const string Version = "1.0.2";

        private const string ManifestRootElement = "AnimationLoader";
        private const string ManifestArrayItem = "Animation";
        private static readonly XmlSerializer xmlSerializer = new XmlSerializer(typeof(SwapAnimationInfo));

        private static Dictionary<EMode, List<SwapAnimationInfo>> animationDict;
        private static SwapAnimationInfo swapAnimationInfo;
        private static List<HSceneProc.AnimationListInfo>[] lstAnimInfo;
        private static PositionCategory category;
        private static readonly Type vrType = Type.GetType("VRHScene, Assembly-CSharp");

        private void Awake()
        {
            var harmony = Harmony.CreateAndPatchAll(typeof(SwapAnim), nameof(SwapAnim));

            if(vrType != null)
            {
                harmony.Patch(AccessTools.Method(vrType, "ChangeAnimator"), postfix: new HarmonyMethod(AccessTools.Method(typeof(SwapAnim), nameof(post_HSceneProc_ChangeAnimator))));
                harmony.Patch(AccessTools.Method(vrType, "ChangeCategory"), prefix: new HarmonyMethod(AccessTools.Method(typeof(SwapAnim), nameof(pre_HSceneProc_ChangeCategory))));
                harmony.Patch(AccessTools.Method(vrType, "CreateAllAnimationList"), postfix: new HarmonyMethod(AccessTools.Method(typeof(SwapAnim), nameof(post_HSceneProc_CreateAllAnimationList))));
            }
        }

        private void Start()
        {
            var animationElements = Sideloader.Sideloader.Manifests.Values
                .Select(x => x.manifestDocument?.Root?.Element(ManifestRootElement))
                .Where(x => x != null)
                .SelectMany(x => x.Elements(ManifestArrayItem));
            
            LoadXmlAnimations(animationElements);
        }

#if DEBUG
        private void Update()
        {
            if(Input.GetKeyDown(KeyCode.RightControl))
            {
                var path = Path.Combine(Paths.ConfigPath, "AnimationLoader.xml");
                if(File.Exists(path))
                {
                    Logger.LogMessage("Loading test animations");
                    var doc = XDocument.Load(path);
                    var animationElements = doc.Root?.Element(ManifestRootElement).Elements(ManifestArrayItem);
                    LoadXmlAnimations(animationElements);
                }
                else
                {
                    Logger.LogMessage("Place AnimationLoader.xml in the config folder to test animations");
                }
            }
        }
#endif

        private void LoadXmlAnimations(IEnumerable<XElement> animationElements)
        {
            animationDict = new Dictionary<EMode, List<SwapAnimationInfo>>();
            
            foreach(var animElem in animationElements)
            {
                var reader = animElem.CreateReader();
                var data = (SwapAnimationInfo)xmlSerializer.Deserialize(reader);
                reader.Close();
                            
                if(!animationDict.TryGetValue(data.Mode, out var list))
                    animationDict[data.Mode] = list = new List<SwapAnimationInfo>();
                list.Add(data);
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(HSceneProc), "ChangeAnimator")]
        private static void post_HSceneProc_ChangeAnimator(HSceneProc.AnimationListInfo _nextAinmInfo)
        {
            SwapAnimation();
        }

        [HarmonyPrefix, HarmonyPatch(typeof(HSceneProc), "ChangeCategory")]
        private static void pre_HSceneProc_ChangeCategory(int _category)
        {
            category = (PositionCategory)_category;
        }

        [HarmonyTranspiler, HarmonyPatch(typeof(HSprite), "OnChangePlaySelect")]
        private static IEnumerable<CodeInstruction> tpl_HSprite_OnChangePlaySelect(IEnumerable<CodeInstruction> instructions)
        {
            //force position change even if position appears to match. Prevents clicks from being eaten.
            return new CodeMatcher(instructions)
                .MatchForward(false, 
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(HSprite), "flags")),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(HFlag), "nowAnimationInfo")),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(HSceneProc.AnimationListInfo), "id"))
                )
                .MatchForward(false,
                    new CodeMatch(OpCodes.Ret)
                )
                .SetAndAdvance(OpCodes.Nop, null)
                .InstructionEnumeration();
        }

        [HarmonyPostfix, HarmonyPatch(typeof(HSprite), "LoadMotionList")]
        private static void post_HSprite_LoadMotionList(HSprite __instance, List<HSceneProc.AnimationListInfo> _lstAnimInfo, GameObject _objParent)
        {
            if(_lstAnimInfo == null || _lstAnimInfo.Count == 0) return;
            var first = _lstAnimInfo[0];

            //TODO: scrollable list?
            DestroyImmediate(_objParent.GetComponent<VerticalLayoutGroup>());
            DestroyImmediate(_objParent.GetComponent<GridLayoutGroup>());
            DestroyImmediate(_objParent.GetComponent<ContentSizeFitter>());
            var glg = _objParent.AddComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(200, 35);
            glg.startAxis = GridLayoutGroup.Axis.Vertical;
            glg.startCorner = GridLayoutGroup.Corner.UpperRight;
            glg.constraint = GridLayoutGroup.Constraint.FixedRowCount;
            glg.constraintCount = 15;
            glg.childAlignment = TextAnchor.UpperRight;

            if(!animationDict.TryGetValue(first.mode, out var swapAnimations)) return;

            foreach(var anim in swapAnimations.Where(x => (int)x.kindHoushi == first.kindHoushi && (!x.categories.Any() || x.categories.Contains(category))))
            {
                var tg = _objParent.GetComponent<ToggleGroup>();
                var btn = Instantiate(__instance.objMotionListNode, _objParent.transform, false);
                var aic = btn.AddComponent<HSprite.AnimationInfoComponent>();

                aic.info = lstAnimInfo[(int)first.mode].First(x => x.id == anim.DonorPoseId).DeepCopy();

                var label = btn.GetComponentInChildren<TextMeshProUGUI>();

                var image = btn.transform.FindLoop("Background").gameObject.GetComponent<Image>();
                image.color = new Color(0.96f, 1f, 0.9f);

                label.text = anim.AnimationName;
                label.color = Color.black;

                //TODO: wat
                var tgl = btn.GetComponent<Toggle>();
                tgl.group = tg;
                tgl.enabled = false;
                tgl.enabled = true;

                //todo: hide all new indicators?
                var newIndicator = btn.transform.FindLoop("New");
                if(newIndicator != null)
                    newIndicator.SetActive(false);

                btn.GetComponent<PointerAction>().listClickAction.Add(() =>
                {
                    //var nai = __instance.flags.nowAnimationInfo;
                    //force position change even if position appears to match. Prevents clicks from being eaten.
                    //if (nai.mode != aic.info.mode || nai.id != aic.info.id)
                    {
                        swapAnimationInfo = anim;
                        __instance.OnChangePlaySelect(btn);
                    }
                });
                btn.SetActive(true);
                if(__instance.flags.nowAnimationInfo == aic.info)
                    btn.GetComponent<Toggle>().isOn = true;
            }
        }

        private static void SwapAnimation()
        {
            if(swapAnimationInfo == null) return;

            var racF = AssetBundleManager.LoadAllAsset(swapAnimationInfo.PathFemale, typeof(RuntimeAnimatorController)).GetAllAssets<RuntimeAnimatorController>()
                .FirstOrDefault(x => x.animationClips.Length > 0 && x.animationClips[0] != null && !string.IsNullOrEmpty(x.animationClips[0].name));
            
            var racM = AssetBundleManager.LoadAllAsset(swapAnimationInfo.PathMale, typeof(RuntimeAnimatorController)).GetAllAssets<RuntimeAnimatorController>()
                .FirstOrDefault(x => x.animationClips.Length > 0 && x.animationClips[0] != null && !string.IsNullOrEmpty(x.animationClips[0].name));
            
            var instance = vrType == null ? Singleton<HSceneProc>.Instance : FindObjectOfType(vrType);
            var t_hsp = Traverse.Create(instance);

            var female = t_hsp.Field<List<ChaControl>>("lstFemale").Value[0];
            var male = t_hsp.Field<ChaControl>("male").Value;
            ////TODO: lstFemale[1], male1

            female.animBody.runtimeAnimatorController = SetupAnimatorOverrideController(female.animBody.runtimeAnimatorController, racF);
            
            if(racM != null)
                male.animBody.runtimeAnimatorController = SetupAnimatorOverrideController(male.animBody.runtimeAnimatorController, racM);

            var mi = t_hsp.Field<List<MotionIK>>("lstMotionIK").Value;
            mi.ForEach(mik => mik.Release());
            mi.Clear();

            //TODO: MotionIKData.
            mi.Add(new MotionIK(female));
            mi.Add(new MotionIK(male));
            mi.ForEach(mik =>
            {
                mik.SetPartners(mi);
                mik.Reset();
            });

            swapAnimationInfo = null;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(HSceneProc), "CreateAllAnimationList")]
        private static void post_HSceneProc_CreateAllAnimationList(HSceneProc __instance)
        {
            var instance = vrType == null ? Singleton<HSceneProc>.Instance : FindObjectOfType(vrType);
            lstAnimInfo = Traverse.Create(instance).Field<List<HSceneProc.AnimationListInfo>[]>("lstAnimInfo").Value;
        }

        private static AnimatorOverrideController SetupAnimatorOverrideController(RuntimeAnimatorController src, RuntimeAnimatorController over)
        {
            if(src == null || over == null) return null;
            var aoc = new AnimatorOverrideController(src);
            var target = new AnimatorOverrideController(over);
            foreach(var ac in src.animationClips.Where(x => x != null)) //thanks omega/katarsys
                aoc[ac.name] = ac;
            foreach(var ac in target.animationClips.Where(x => x != null)) //thanks omega/katarsys
                aoc[ac.name] = ac;
            aoc.name = over.name;
            return aoc;
        }
        
        [XmlRoot("Animation")]
        [Serializable]
        public class SwapAnimationInfo
        {
            [XmlElement]
            public string PathFemale;
            [XmlElement]
            public string PathMale;

            [XmlElement]
            public string AnimationName;

            [XmlElement]
            public EMode Mode;
            [XmlElement]
            public KindHoushi kindHoushi;
            [XmlArray]
            [XmlArrayItem("category", Type = typeof(PositionCategory))]
            public PositionCategory[] categories;
            [XmlElement]
            public int DonorPoseId;
        }

        public enum KindHoushi
        {
            Hand = 0,
            Mouth = 1,
            Breasts = 2
        }

        public enum PositionCategory
        {
            LieDown = 0,
            Stand = 1,
            SitChair = 2,
            Stool = 3,
            SofaBench = 4,
            BacklessBench = 5,
            SchoolDesk = 6,
            Desk = 7,
            Wall = 8,
            StandPool = 9,
            SitDesk = 10,
            SquadDesk = 11,
            Ground3P = 1100,
        }
    }
}
