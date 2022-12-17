﻿using HarmonyLib;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using static QuickStackStore.QSSConfig;

namespace QuickStackStore
{
    // base implementation originally from 'Trash Items' mod, as allowed in their permission settings on nexus
    // https://www.nexusmods.com/valheim/mods/441
    // https://github.com/virtuaCode/valheim-mods/tree/main/TrashItems
    internal class TrashModule
    {
        private static ClickState clickState = 0;

        public static Sprite trashSprite;
        public static Sprite bgSprite;
        public static GameObject dialog;
        public static Transform trashRoot;
        public static TrashButton trashButton;

        public static void DoQuickTrash()
        {
            if (TrashConfig.ShowConfirmDialogForQuickTrash.Value)
            {
                ShowBaseConfirmDialog(null, LocalizationConfig.QuickTrashConfirmation.Value, string.Empty, QuickTrash);
            }
            else
            {
                QuickTrash();
            }
        }

        public static void QuickTrash()
        {
            var player = Player.m_localPlayer;
            UserConfig playerConfig = UserConfig.GetPlayerConfig(player.GetPlayerID());

            int num = 0;

            var list = player.GetInventory().GetAllItems();

            for (int i = list.Count - 1; i >= 0; i--)
            {
                var item = list[i];

                if (!playerConfig.IsSlotFavorited(item.m_gridPos) && playerConfig.IsItemNameConsideredTrashFlagged(item.m_shared))
                {
                    num++;
                    player.RemoveEquipAction(item);
                    player.UnequipItem(item, false);
                    player.GetInventory().RemoveItem(item);
                }
            }

            InventoryGui.instance.SetupDragItem(null, null, 0);
            InventoryGui.instance.UpdateCraftingPanel(false);

            Debug.Log($"Quick trashed {num} item/s from player inventory");

            player.GetInventory().Changed();
        }

        [HarmonyPatch(typeof(InventoryGui))]
        internal static class TrashItemsPatches
        {
            [HarmonyPatch(nameof(InventoryGui.Show))]
            [HarmonyPostfix]
            public static void Show_Postfix(InventoryGui __instance)
            {
                if (__instance != InventoryGui.instance)
                {
                    return;
                }

                if (trashRoot != null || GeneralConfig.DisableAllNewButtons.Value || !TrashConfig.DisplayTrashCanUI.Value)
                {
                    return;
                }

                Transform playerInventory = __instance.m_player.transform;

                trashRoot = Object.Instantiate(playerInventory.Find("Armor"), playerInventory);
                trashButton = trashRoot.gameObject.AddComponent<TrashButton>();
            }

            [HarmonyPatch(nameof(InventoryGui.Hide))]
            [HarmonyPostfix]
            public static void Postfix()
            {
                OnChoice();
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(InventoryGui.UpdateItemDrag))]
            public static void UpdateItemDrag_Postfix(InventoryGui __instance, ItemDrop.ItemData ___m_dragItem, Inventory ___m_dragInventory, int ___m_dragAmount)
            {
                if (dialog != null || clickState == 0)
                {
                    return;
                }

                if (___m_dragItem == null)
                {
                    if (clickState == ClickState.ClickedQuickTrash)
                    {
                        DoQuickTrash();
                    }

                    clickState = 0;
                    return;
                }

                if (___m_dragInventory == null || !___m_dragInventory.ContainsItem(___m_dragItem))
                {
                    clickState = 0;
                    return;
                }

                var player = Player.m_localPlayer;
                var playerConfig = UserConfig.GetPlayerConfig(player.GetPlayerID());

                if (clickState == ClickState.ClickedTrashFlagging)
                {
                    var didFlagSuccessfully = playerConfig.ToggleItemNameTrashFlagging(InventoryGui.instance.m_dragItem.m_shared);

                    if (!didFlagSuccessfully)
                    {
                        player.Message(MessageHud.MessageType.Center, LocalizationConfig.CantTrashFlagFavoritedItemWarning.Value, 0, null);
                    }

                    clickState = 0;
                    return;
                }

                if (clickState == ClickState.ClickedTrash)
                {
                    if ((player.m_inventory == ___m_dragInventory && playerConfig.IsSlotFavorited(___m_dragItem.m_gridPos)) || playerConfig.IsItemNameFavorited(___m_dragItem.m_shared))
                    {
                        player.Message(MessageHud.MessageType.Center, LocalizationConfig.CantTrashFavoritedItemWarning.Value, 0, null);
                        clickState = 0;
                        return;
                    }

                    var conf = TrashConfig.ShowConfirmDialogForNormalItem.Value;

                    if (conf == ShowConfirmDialogOption.Always
                        || (conf == ShowConfirmDialogOption.WhenNotTrashFlagged && !playerConfig.IsItemNameConsideredTrashFlagged(___m_dragItem.m_shared)))
                    {
                        ShowConfirmDialog(___m_dragItem, ___m_dragAmount, () => TrashItem(__instance, ___m_dragInventory, ___m_dragItem, ___m_dragAmount));
                    }
                    else
                    {
                        TrashItem(__instance, ___m_dragInventory, ___m_dragItem, ___m_dragAmount);
                    }

                    clickState = 0;
                    return;
                }
            }

            public static void TrashItem(InventoryGui __instance, Inventory ___m_dragInventory, ItemDrop.ItemData ___m_dragItem, int ___m_dragAmount)
            {
                if (___m_dragAmount == ___m_dragItem.m_stack)
                {
                    Player.m_localPlayer.RemoveEquipAction(___m_dragItem);
                    Player.m_localPlayer.UnequipItem(___m_dragItem, false);
                    ___m_dragInventory.RemoveItem(___m_dragItem);
                }
                else
                {
                    ___m_dragInventory.RemoveItem(___m_dragItem, ___m_dragAmount);
                }

                __instance.SetupDragItem(null, null, 0);
                __instance.UpdateCraftingPanel(false);
            }
        }

        public class TrashButton : MonoBehaviour
        {
            private Canvas canvas;
            private RectTransform rectTransform;
            private GameObject buttonGo;

            protected void Awake()
            {
                if (InventoryGui.instance == null)
                {
                    return;
                }

                var playerInventory = InventoryGui.instance.m_player.transform;
                RectTransform rect = GetComponent<RectTransform>();
                rect.anchoredPosition -= new Vector2(0, 78);

                SetText(LocalizationConfig.TrashLabel.Value);
                SetColor(TrashConfig.TrashLabelColor.Value);

                // Replace armor with trash icon
                Transform tArmor = transform.Find("armor_icon");

                if (!tArmor)
                {
                    Debug.LogError("armor_icon not found!");
                }

                tArmor.GetComponent<Image>().sprite = trashSprite;

                transform.SetSiblingIndex(0);
                transform.gameObject.name = "Trash";

                buttonGo = new GameObject("ButtonCanvas");
                rectTransform = buttonGo.AddComponent<RectTransform>();
                rectTransform.transform.SetParent(transform.transform, true);
                rectTransform.anchoredPosition = Vector2.zero;
                rectTransform.sizeDelta = new Vector2(70, 74);
                canvas = buttonGo.AddComponent<Canvas>();
                buttonGo.AddComponent<GraphicRaycaster>();

                // Add trash ui button
                Button button = buttonGo.AddComponent<Button>();
                button.onClick.AddListener(() => TrashOrTrashFlagItem());
                var image = buttonGo.AddComponent<Image>();
                image.color = new Color(0, 0, 0, 0);

                // Add border background
                Transform frames = playerInventory.Find("selected_frame");
                GameObject newFrame = Instantiate(frames.GetChild(0).gameObject, transform);
                newFrame.GetComponent<Image>().sprite = bgSprite;
                newFrame.transform.SetAsFirstSibling();
                newFrame.GetComponent<RectTransform>().sizeDelta = new Vector2(-8, 22);
                newFrame.GetComponent<RectTransform>().anchoredPosition = new Vector2(6, 7.5f);

                // Add inventory screen tab
                UIGroupHandler handler = gameObject.AddComponent<UIGroupHandler>();
                handler.m_groupPriority = 1;
                handler.m_enableWhenActiveAndGamepad = newFrame;
                InventoryGui.instance.m_uiGroups = InventoryGui.instance.m_uiGroups.AddToArray(handler);

                gameObject.AddComponent<TrashHandler>();
            }

            protected void Start()
            {
                StartCoroutine(DelayedOverrideSorting());
            }

            private IEnumerator DelayedOverrideSorting()
            {
                yield return null;

                if (canvas == null)
                {
                    yield break;
                }

                canvas.overrideSorting = true;
                canvas.sortingOrder = 1;
            }

            public void SetText(string text)
            {
                Transform tText = transform.Find("ac_text");

                if (!tText)
                {
                    Debug.LogError("ac_text not found!");
                    return;
                }

                tText.GetComponent<Text>().text = text;
            }

            public void SetColor(Color color)
            {
                Transform tText = transform.Find("ac_text");

                if (!tText)
                {
                    Debug.LogError("ac_text not found!");
                    return;
                }

                tText.GetComponent<Text>().color = color;
            }
        }

        public static void ShowConfirmDialog(ItemDrop.ItemData item, int itemAmount, UnityAction onConfirm)
        {
            ShowBaseConfirmDialog(item.GetIcon(),
                Localization.instance.Localize(item.m_shared.m_name),
                $"{itemAmount}/{item.m_shared.m_maxStackSize}",
                onConfirm);
        }

        public static void ShowBaseConfirmDialog(Sprite potentialIcon, string name, string amountText, UnityAction onConfirm)
        {
            if (InventoryGui.instance == null || dialog != null)
            {
                return;
            }

            dialog = Object.Instantiate(InventoryGui.instance.m_splitPanel.gameObject, InventoryGui.instance.transform);

            var okButton = dialog.transform.Find("win_bkg/Button_ok").GetComponent<Button>();
            okButton.onClick.RemoveAllListeners();
            okButton.onClick.AddListener(new UnityAction(OnChoice));
            okButton.onClick.AddListener(onConfirm);
            okButton.GetComponentInChildren<Text>().text = "Trash";
            okButton.GetComponentInChildren<Text>().color = new Color(1, 0.2f, 0.1f);

            var cancelButton = dialog.transform.Find("win_bkg/Button_cancel").GetComponent<Button>();
            cancelButton.onClick.RemoveAllListeners();
            cancelButton.onClick.AddListener(new UnityAction(OnChoice));

            dialog.transform.Find("win_bkg/Slider").gameObject.SetActive(false);

            var text = dialog.transform.Find("win_bkg/Text").GetComponent<Text>();
            text.text = name;

            var iconComp = dialog.transform.Find("win_bkg/Icon_bkg/Icon").GetComponent<Image>();

            if (potentialIcon)
            {
                iconComp.sprite = potentialIcon;
            }
            else
            {
                iconComp.sprite = trashSprite;
            }

            var amountComp = dialog.transform.Find("win_bkg/amount").GetComponent<Text>();

            amountComp.text = amountText;

            dialog.gameObject.SetActive(true);
        }

        public static void OnChoice()
        {
            clickState = 0;

            if (dialog != null)
            {
                Object.Destroy(dialog);
                dialog = null;
            }
        }

        public static void TrashOrTrashFlagItem(bool usedFromHotkey = false)
        {
            //Debug.Log("Trash Item clicked!");

            if (clickState != ClickState.None || InventoryGui.instance == null)
            {
                return;
            }

            if (InventoryGui.instance.m_dragGo != null)
            {
                if (Helper.IsPressingFavoriteKey())
                {
                    clickState = ClickState.ClickedTrashFlagging;
                }
                else
                {
                    clickState = ClickState.ClickedTrash;
                }
            }
            else
            {
                if (!usedFromHotkey && TrashConfig.EnableQuickTrash.Value && !Helper.IsPressingFavoriteKey())
                {
                    clickState = ClickState.ClickedQuickTrash;
                }
            }
        }

        private enum ClickState
        {
            None = 0,
            ClickedTrash = 1,
            ClickedTrashFlagging = 2,
            ClickedQuickTrash = 3
        }
    }

    public class TrashHandler : MonoBehaviour
    {
        private UIGroupHandler handler;

        protected void Awake()
        {
            handler = this.GetComponent<UIGroupHandler>();
        }

        protected void Update()
        {
            if (ZInput.GetButtonDown("JoyButtonA") && handler.IsActive())
            {
                TrashModule.TrashOrTrashFlagItem();
                // Switch back to inventory tab
                InventoryGui.instance.SetActiveGroup(1);
            }
        }
    }
}