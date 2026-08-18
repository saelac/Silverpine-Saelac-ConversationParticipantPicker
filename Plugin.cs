using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ConversationParticipantPicker;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid =
        "salt.silverpine.conversationparticipantpicker";
    public const string PluginName = "Conversation Participant Picker";
    public const string PluginVersion = "1.1.0";

    private Harmony _harmony;

    private void Awake()
    {
        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll();
    }
}

[HarmonyPatch(typeof(NeuralNPC), nameof(NeuralNPC.OnInteractInnerInvoke))]
[HarmonyPriority(Priority.First)]
internal static class ConversationParticipantPickerPatch
{
    private const string AcaciaUnlockedGuid =
        "salt.silverpine.acaciaunlocked";
    private const int ParticipantsPerPage = 4;

    private static bool Prefix(
        NeuralNPC __instance,
        string initialPlayerMessage)
    {
        string playerMessage = initialPlayerMessage;

        if (!InferenceServerSetupHandler.Instance.modelLoaded
            || SaveUI.Instance.IsSavingBlocked()
            || Player.Instance.inCombat)
        {
            return true;
        }

        var targets = new List<NeuralNPC>();
        AddTargets(Player.Instance.transform.GetVector2IntPosition());
        AddTargets(__instance.transform.GetVector2IntPosition());

        bool acaciaUnlocked =
            Chainloader.PluginInfos.ContainsKey(AcaciaUnlockedGuid);
        targets.RemoveAll(n =>
            (!acaciaUnlocked && n.npcName == NPCName.Acacia)
            || !DialogBox.Instance.IsEndDialogAllowed(n)
            || !__instance.CanSeeTransform(n.transform));

        if (__instance.npcName == NPCName.Acacia
            || !DialogBox.Instance.IsEndDialogAllowed(__instance))
        {
            playerMessage = null;
        }

        MethodInfo triggerDialog = AccessTools.Method(
            typeof(NeuralNPC),
            "TriggerDialog");
        MethodInfo triggerMultiDialog = AccessTools.Method(
            typeof(NeuralNPC),
            "TriggerMultiDialog");

        if ((__instance.npcName != NPCName.Acacia || acaciaUnlocked)
            && DialogBox.Instance.IsEndDialogAllowed(__instance)
            && targets.Count > 1)
        {
            ShowConversationOptions();
        }
        else
        {
            triggerDialog.Invoke(
                __instance,
                new object[] { playerMessage });
        }

        return false;

        void ShowConversationOptions()
        {
            DialogBox.Instance.DisplayTextNoDialog(
                "Who would you like to talk to?",
                new DialogOption(
                    string.Join(", ", targets.Select(n => n.GetFinalName())),
                    delegate { StartMultiDialog(targets); }),
                new DialogOption(
                    __instance.GetFinalName(),
                    delegate
                    {
                        triggerDialog.Invoke(
                            __instance,
                            new object[] { playerMessage });
                    }),
                new DialogOption(
                    "Choose participants...",
                    delegate
                    {
                        ShowParticipantPicker(
                            new HashSet<NeuralNPC> { __instance },
                            0);
                    }));
        }

        void ShowParticipantPicker(
            HashSet<NeuralNPC> selected,
            int pageIndex)
        {
            List<NeuralNPC> candidates =
                targets.Where(n => n != __instance).ToList();
            int pageCount =
                (candidates.Count + ParticipantsPerPage - 1)
                / ParticipantsPerPage;
            pageIndex = Mathf.Clamp(pageIndex, 0, pageCount - 1);

            var options = new List<DialogOption>();
            foreach (NeuralNPC target in candidates
                .Skip(pageIndex * ParticipantsPerPage)
                .Take(ParticipantsPerPage))
            {
                NeuralNPC capturedTarget = target;
                options.Add(new DialogOption(
                    (selected.Contains(capturedTarget) ? "[x] " : "[ ] ")
                    + capturedTarget.GetFinalName(),
                    delegate
                    {
                        if (!selected.Add(capturedTarget))
                        {
                            selected.Remove(capturedTarget);
                        }
                        ShowParticipantPicker(selected, pageIndex);
                    }));
            }

            if (pageCount > 1)
            {
                options.Add(new DialogOption(
                    "Next page ("
                    + (pageIndex + 1)
                    + "/"
                    + pageCount
                    + ")",
                    delegate
                    {
                        ShowParticipantPicker(
                            selected,
                            (pageIndex + 1) % pageCount);
                    }));
            }

            options.Add(new DialogOption(
                "Review (" + (selected.Count - 1) + " selected)",
                delegate { ShowSelectionReview(selected, pageIndex); }));

            DialogBox.Instance.DisplayTextNoDialog(
                "Choose who joins "
                + __instance.GetFinalName()
                + ". Select names to toggle them."
                + (pageCount > 1
                    ? "\nPage " + (pageIndex + 1) + " of " + pageCount + "."
                    : ""),
                options.ToArray());
        }

        void ShowSelectionReview(
            HashSet<NeuralNPC> selected,
            int returnPageIndex)
        {
            List<NeuralNPC> participants =
                targets.Where(selected.Contains).ToList();
            var options = new List<DialogOption>();

            if (participants.Count > 1)
            {
                options.Add(new DialogOption(
                    "Start conversation",
                    delegate { StartMultiDialog(participants); }));
            }

            options.Add(new DialogOption(
                "Continue selecting",
                delegate
                {
                    ShowParticipantPicker(selected, returnPageIndex);
                }));
            options.Add(new DialogOption(
                "Back",
                ShowConversationOptions));

            DialogBox.Instance.DisplayTextNoDialog(
                participants.Count > 1
                    ? "Conversation participants:\n"
                        + string.Join(
                            ", ",
                            participants.Select(n => n.GetFinalName()))
                    : "Select at least one NPC to join "
                        + __instance.GetFinalName()
                        + ".",
                options.ToArray());
        }

        void StartMultiDialog(List<NeuralNPC> participants)
        {
            triggerMultiDialog.Invoke(
                null,
                new object[]
                {
                    playerMessage,
                    __instance,
                    participants
                });
        }

        void AddTargets(Vector2Int position)
        {
            for (int x = position.x - 2; x < position.x + 3; x++)
            {
                for (int y = position.y - 2; y < position.y + 3; y++)
                {
                    foreach (NeuralNPC npc in NeuralNPC.neuralNPCs.Values)
                    {
                        if (npc.transform.GetVector2IntPosition()
                                == new Vector2Int(x, y)
                            && !targets.Contains(npc))
                        {
                            targets.Add(npc);
                        }
                    }
                }
            }
        }
    }
}
