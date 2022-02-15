﻿using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace VFECore
{
    public class VFEGlobal : Mod
    {
        public static VFEGlobalSettings settings;
        private Vector2 scrollPosition = Vector2.zero;
        protected readonly Vector2 ButtonSize = new Vector2(120f, 40f);

        public VFEGlobal(ModContentPack content) : base(content)
        {
            settings = GetSettings<VFEGlobalSettings>();
            // Toggable patches
            foreach (ModContentPack mod in LoadedModManager.RunningMods)
            {
                if (mod.Patches != null)
                {
                    int modPatchesCount = mod.Patches.ToList().FindAll(p => p is PatchOperationToggableSequence pt && pt.ModsFound()).Count;
                    if (modPatchesCount > 0)
                    {
                        ModUsingToggablePatchCount++;
                        ToggablePatchCount += modPatchesCount;
                    }
                }
            }
        }

        public override string SettingsCategory() => "Vanilla Framework Expanded";

        private int PageIndex = 0;

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Rect tabRect = new Rect(inRect)
            {
                y = inRect.y + 40f
            };
            Rect mainRect = new Rect(inRect)
            {
                height = inRect.height - 40f,
                y = inRect.y + 40f
            };

            Widgets.DrawMenuSection(mainRect);
            List<TabRecord> tabs = new List<TabRecord>
            {
                new TabRecord("GeneralTitle".Translate(), () =>
                {
                    PageIndex = 0;
                    WriteSettings();

                }, PageIndex == 0),
                new TabRecord("TPTitle".Translate(), () =>
                {
                    PageIndex = 1;
                    WriteSettings();

                }, PageIndex == 1)
            };
            TabDrawer.DrawTabs(tabRect, tabs);

            switch (PageIndex)
            {
                case 0:
                    GeneralSettings(mainRect.ContractedBy(15f));
                    break;
                case 1:
                    ToggablePatchesSettings(mainRect.ContractedBy(15f));
                    break;
                default:
                    break;
            }
        }

        // General settings

        private int FactionCanBeAddedCount;

        private void GeneralSettings(Rect rect)
        {
            Listing_Standard list = new Listing_Standard();
            list.Begin(rect);

            Text.Font = GameFont.Small;
            list.Label("Faction Discovery");
            if (Current.Game != null)
            {
                FactionCanBeAddedCount = DefDatabase<FactionDef>.AllDefs.Where(ValidatorAnyFactionLeft).Count();
                list.Label("CanAddXFaction".Translate(FactionCanBeAddedCount));
                if (FactionCanBeAddedCount > 0 && list.ButtonText("AskForPopUp".Translate(), "AskForPopUpExplained".Translate()))
                {
                    Current.Game.World.GetComponent<NewFactionSpawningState>().ignoredFactions.Clear();
                    IEnumerator<FactionDef> factionEnumerator = DefDatabase<FactionDef>.AllDefs.Where(Patch_GameComponentUtility.LoadedGame.Validator).GetEnumerator();
                    if (factionEnumerator.MoveNext())
                    {
                        // Only one dialog can be stacked at a time, so give it the list of all factions
                        Dialog_NewFactionSpawning.OpenDialog(factionEnumerator);
                    }
                }
            }
            else
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                list.Label("NeedToBeInGame".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
            }
            list.GapLine(12);

            // KCSG
            list.Gap(12);
            list.Label("Custom Structure Generation :");
            list.Gap(5);
            list.CheckboxLabeled("Verbose logging", ref settings.enableVerboseLogging);
            list.GapLine(12);

            // Texture Variations
            list.Gap(12);
            list.Label("Texture Variations:");
            list.Gap(5);
            list.CheckboxLabeled("VFE_RandomOrSequentially".Translate(), ref settings.isRandomGraphic, null);
            list.Gap(5);
            list.CheckboxLabeled("VFE_HideRandomizeButton".Translate(), ref settings.hideRandomizeButton, null);
            list.GapLine(12);

            // General
            list.Gap(12);
            list.CheckboxLabeled("Disable Texture Caching", ref settings.disableCaching, "Warning: Enabling this might cause performance issues.");

            list.End();
        }

        private bool ValidatorAnyFactionLeft(FactionDef faction)
        {
            if (faction == null) return false;
            if (faction.isPlayer) return false;
            if (!faction.canMakeRandomly && faction.hidden && faction.maxCountAtGameStart <= 0) return false;
            if (Find.FactionManager.AllFactions.Count(f => f.def == faction) > 0) return false;
            if (NewFactionSpawningUtility.NeverSpawn(faction)) return false;
            return true;
        }

        // Toggable patches settings

        private readonly int ToggablePatchCount;
        private readonly int ModUsingToggablePatchCount = 0;

        private void ToggablePatchesSettings(Rect rect)
        {
            Rect viewRect = new Rect(rect)
            {
                height = 110f + (ToggablePatchCount + ModUsingToggablePatchCount) * 32f,
                width = rect.width - 20f,
            };

            Listing_Standard list = new Listing_Standard();
            Widgets.BeginScrollView(rect, ref scrollPosition, viewRect, true);
            list.Begin(viewRect);

            Text.Anchor = TextAnchor.MiddleCenter;
            list.Label("NeedRestart".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            list.Gap();

            foreach (ModContentPack modContentPack in (from m in LoadedModManager.RunningMods orderby m.OverwritePriority select m).ThenBy((ModContentPack x) => LoadedModManager.RunningModsListForReading.IndexOf(x)))
            {
                if (modContentPack?.Patches != null && modContentPack.Patches.Any(p => p is PatchOperationToggableSequence pt && pt.ModsFound()))
                {
                    Text.Anchor = TextAnchor.MiddleCenter;
                    list.Label(modContentPack.Name);
                    Text.Anchor = TextAnchor.UpperLeft;
                    AddButtons(list, modContentPack);
                }
            }

            list.End();
            Widgets.EndScrollView();
        }

        private void AddButtons(Listing_Standard list, ModContentPack modContentPack)
        {
            foreach (PatchOperation patchOperation in modContentPack.Patches)
            {
                if (patchOperation is PatchOperationToggableSequence p && p.ModsFound())
                {
                    string pLabelSmall = p.label.Replace(" ", "");
                    string bLabel = !settings.toggablePatch.NullOrEmpty() && settings.toggablePatch.ContainsKey(pLabelSmall) ? settings.toggablePatch[pLabelSmall].ToString() : p.enabled.ToString();
                    if (list.ButtonTextLabeled(p.label, bLabel))
                    {
                        if (!settings.toggablePatch.NullOrEmpty() && settings.toggablePatch.ContainsKey(pLabelSmall)) // Already in, we remove it
                        {
                            settings.toggablePatch.Remove(pLabelSmall);
                        }
                        else // Add to toggablePatch with the inverse value
                        {
                            if (settings.toggablePatch.NullOrEmpty()) settings.toggablePatch = new Dictionary<string, bool>();
                            settings.toggablePatch.Add(pLabelSmall, !p.enabled);
                        }
                    }
                }
            }
        }
    }

    public class VFEGlobalSettings : ModSettings
    {
        public Dictionary<string, bool> toggablePatch = new Dictionary<string, bool>();
        public bool enableVerboseLogging;
        public bool disableCaching;
        public bool isRandomGraphic = true;
        public bool hideRandomizeButton = false;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref toggablePatch, "toggablePatch", LookMode.Value);
            Scribe_Values.Look(ref enableVerboseLogging, "enableVerboseLogging", false);
            Scribe_Values.Look(ref disableCaching, "disableCaching", true);
            Scribe_Values.Look(ref isRandomGraphic, "isRandomGraphic", true, true);
            Scribe_Values.Look(ref hideRandomizeButton, "hideRandomizeButton", false, true);
        }
    }
}