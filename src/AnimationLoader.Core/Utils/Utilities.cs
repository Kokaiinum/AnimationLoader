﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using UnityEngine;

using HarmonyLib;
using Illusion.Extensions;

using KKAPI.Utilities;


namespace AnimationLoader
{
    public partial class SwapAnim
    {
        internal class Utilities
        {
            internal static void SaveAnimInfo()
            {
                if (_hprocInstance == null)
                {
#if DEBUG
                    Logger.LogWarning($"0016: [ShowAnimInfo] Instance? " +
                        $"{_hprocInstance is not null}");
#endif
                    return;
                }

                var total = 0;
                var lstAnimInfo = Traverse
                    .Create(_hprocInstance)
                    .Field<List<HSceneProc.AnimationListInfo>[]>("lstAnimInfo").Value;

                // id, mode,
                // nameAnimation (Japanese name), posture,
                // numCtrl, kindHoshi,
                // hoshiLoopActionS, isFemaleInitiative,
                // {category list},
                // fileSiruPaste
                for (var i = 0; i < lstAnimInfo.Length; i++)
                {
                    var lines = lstAnimInfo[i].Select(x => $"{x.id}, {x.mode}," +
                         $" {TranslateName(x.nameAnimation)}, {x.posture}," +
                         $" {x.numCtrl}, {x.kindHoushi}," +
                         $" {x.houshiLoopActionS}, {x.isFemaleInitiative}," +
                         $"{CategoryList(x.lstCategory)}," +
                         $" {x.paramFemale.fileSiruPaste}");

                    File.WriteAllLines($"lst{i}.csv", lines.ToArray());
                    total += lines.ToArray().Length;
                }
#if DEBUG
                Logger.LogWarning($"0017: Total animations {total}");
#endif
            }

            internal static string Translate(string name)
            {
                if (!TranslationHelper.TryTranslate(name, out var tmp))
                {
                    return name;
                }

                return tmp;
            }

            internal static string TranslateName(string animationName, bool original = false)
            {
                var tmp = Translate(animationName);
                if ((tmp == animationName) || !original)
                {
                    return tmp;
                }
                return $"{tmp} ({animationName})";
            }

            // TODO: Tried a few ways to make it work with/without casting
            // did not work check why later
            internal static string CategoryList(List<HSceneProc.Category> categories)
            {
                var tmp = "";
                var first = true;

                foreach (var c in categories)
                {
                    if (first)
                    {
                        tmp += c.category.ToString();
                        first = false;
                    }
                    else
                    {
                        tmp += ", " + c.category.ToString();
                    }
                }
                return "\" { " + tmp + " }\"";
            }

            internal static int CountAnimations(List<HSceneProc.AnimationListInfo>[] lstAnimInfo)
            {
                var count = 0;

                foreach (var c in lstAnimInfo)
                {
                    count += c.Count;
                }
                return count;
            }

            internal static void SaveHProcInstance(object instance)
            {
                // TODO: This makes it VR incompatible
                if (_hprocInstance == null)
                {
                    _hprocInstance = (HSceneProc)instance;
                    if (_hprocInstance == null)
                    {
                        Logger.LogDebug($"0009: Failed to save _hprocInstance");
                    }
                    else
                    {
                        Logger.LogDebug($"0010: _hprocInstance saved.");
                    }
                }
            }

            internal static bool HasMovement(AnimationInfo anim)
            {
                if (anim.SwapAnim != null)
                {
                    if (anim.SwapAnim.PositionHeroine != Vector3.zero)
                    {
                        return true;
                    }
                    if (anim.SwapAnim.PositionPlayer != Vector3.zero)
                    {
                        return true;
                    }
                }
                return false;
            }

            /// <summary>
            /// Set new original position for characters if there is a move 
            /// from original position saved
            /// </summary>
            /// <param name="message"></param>
            internal static void SetOriginalPositionAll()
            {
                if (_hprocInstance == null)
                {
                    return;
                }

                var heroines = _hprocInstance.flags.lstHeroine;
                for (var i = 0; i < heroines.Count; i++)
                {
                    if (IsNewPosition(heroines[i].chaCtrl))
                    {
                        GetMoveController(heroines[i].chaCtrl).SetOriginalPosition();
                    }
                }
                if (IsNewPosition(_hprocInstance.flags.player.chaCtrl))
                {
                    GetMoveController(_hprocInstance.flags.player.chaCtrl).SetOriginalPosition();
                }
            }


            /// <summary>
            /// Determine if there is a change in original position
            /// </summary>
            /// <param name="chaControl"></param>
            /// <returns></returns>
            internal static bool IsNewPosition(ChaControl chaControl)
            {
                var controller = GetMoveController(chaControl);
                var newPosition = chaControl.transform.position;
                var originalPosition = controller._originalPosition;
                var lastMovePosition = controller._lastMovePosition;
                if (newPosition != originalPosition && newPosition != lastMovePosition)
                {
                    return true;
                }
                return false;
            }

        }
    }
}
