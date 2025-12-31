using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DailyRoutines.Abstracts;
using DailyRoutines.Widgets;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace DailyRoutines.ModulesPublic;

public unsafe class ActionMessenger : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = "使用技能时发言",
        Description = "你可以配置在使用技能时发送消息到小队频道, 可以配置不同的消息, 我会在其中随机挑选一条进行发送",
        Category = ModuleCategories.Action,
        Author = ["Hsin"]
    };

    private static ModuleStorage ModuleConfig = null!;

    private static string GetStr(string msg, string? id = null) => GetLoc(msg) + (id != null ? $"##am-{id}" : "");

    #region Init

    protected override void Init()
    {
        ModuleConfig = LoadConfig<ModuleStorage>() ?? new ModuleStorage();

        // 初始化配置名称列表
        ModuleConfig.Configurations = ModuleConfig.Configurations.Where(p => !string.IsNullOrWhiteSpace(p.Key))
                                                  .ToDictionary(p => p.Key, p => p.Value);
        configNames = ModuleConfig.Configurations.Select(p => p.Key).ToList();
        SaveConfig(ModuleConfig);

        ActionSelect ??= new ActionSelectCombo("##ActionSelect", LuminaGetter.Get<Lumina.Excel.Sheets.Action>()
                                                                             .Where(p => !p.Name.IsEmpty
                                                                                         && p.IsPlayerAction
                                                                                         && p.IsPvP == false));

        UseActionManager.RegCharacterStartCast(PostCharacterStartCast);
        // UseActionManager.RegPreCharacterCompleteCast(PreCharacterCompleteCast);
        // UseActionManager.RegCharacterCompleteCast(PostCharacterCompleteCast);
        UseActionManager.RegUseAction(PostUseAction);
        // UseActionManager.RegUseActionLocation(PostUseActionLocation);
    }

    protected override void Uninit()
    {
        UseActionManager.Unreg(PostCharacterStartCast);
        // UseActionManager.UnregPreCharacterCompleteCast(PreCharacterCompleteCast);
        // UseActionManager.UnregCharacterCompleteCast(PostCharacterCompleteCast);
        UseActionManager.Unreg(PostUseAction);
        // UseActionManager.UnregUseActionLocation(PostUseActionLocation);

        base.Uninit();
    }

    #endregion

    #region UI

    private string json = "";
    private static ActionSelectCombo? ActionSelect;
    private int selectedConfigIndex = -1;
    private string newConfigName = "";
    private bool showAddDialog = false;
    private List<string> configNames = [];
    private string newMessageInput = "";
    private bool showRenameDialog = false;
    private int renameConfigIndex = -1;
    private string renameConfigName = "";

    protected override void ConfigUI() => ConfigureActionUI();

    private void ConfigureActionUI()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 4));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 6));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.85f, 0.95f, 0.95f, 1.0f));

        // 发送频道
        ImGui.TextColored(new Vector4(0.7f, 0.8f, 1.0f, 1.0f), GetStr("发送频道") + ":");
        ImGui.SameLine();
        ImGui.TextDisabled("(选择消息发送的频道)");
        var chatType = ModuleConfig.ChatTypeConfig;
        if (ImGui.BeginCombo("##ChatType", GetChatTypePreview(chatType)))
        {
            if (ImGui.Selectable(GetStr("说话"), chatType == ChatType.Say))
                ModuleConfig.ChatTypeConfig = ChatType.Say;

            if (ImGui.Selectable(GetStr("小队"), chatType == ChatType.Party))
                ModuleConfig.ChatTypeConfig = ChatType.Party;

            if (ImGui.Selectable(GetStr("默语"), chatType == ChatType.Echo))
                ModuleConfig.ChatTypeConfig = ChatType.Echo;

            ImGui.EndCombo();
        }

        ImGui.PopStyleColor();
        ImGui.PopStyleVar(2);

        ImGui.Separator();
        ImGui.Spacing();

        // 创建两列布局
        ImGui.Columns(2, "SkillConfig", true);

        // 左列 - 技能列表
        ConfigureLeftColumn();

        ImGui.NextColumn();

        // 右列 - 详细配置
        ConfigureRightColumn();

        // 重置为单列
        ImGui.Columns(1);
    }

    private void ConfigureLeftColumn()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 4));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 6));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.8f, 1.0f, 1.0f));
        ImGui.Text(GetStr("配置列表"));
        ImGui.PopStyleColor();
        ImGui.Spacing();

        // 添加按钮
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.18f, 0.32f, 0.45f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.22f, 0.38f, 0.55f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.13f, 0.22f, 0.35f, 1.0f));
        if (ImGui.Button(GetStr("添加配置", "AddConfig"), new Vector2(120, 28)))
        {
            showAddDialog = true;
            newConfigName = "";
        }

        ImGui.PopStyleColor(3);
        ImGui.Spacing();

        // 添加配置对话框
        if (showAddDialog)
        {
            ImGui.SetNextWindowSize(new Vector2(320, 140));
            if (ImGui.Begin(GetStr("新建配置", "AddConfigDialog"), ref showAddDialog))
            {
                ImGui.Text(GetStr("配置名称") + ":");
                ImGui.InputText("##NewConfigName", ref newConfigName, 256);
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                if (ImGui.Button(GetStr("确认", "ConfirmAdd"), new Vector2(80, 0)))
                {
                    if (!string.IsNullOrWhiteSpace(newConfigName) && !configNames.Contains(newConfigName))
                    {
                        configNames.Add(newConfigName);
                        showAddDialog = false;
                        newConfigName = "";
                        ModuleConfig.Configurations[newConfigName] = new ConfigData();
                        SaveConfig(ModuleConfig);
                    }
                }

                ImGui.SameLine();
                if (ImGui.Button(GetStr("取消", "CancelAdd"), new Vector2(80, 0)))
                {
                    showAddDialog = false;
                    newConfigName = "";
                }
            }

            ImGui.End();
        }

        ImGui.Separator();
        ImGui.Spacing();

        // 配置名称列表
        for (var i = 0; i < configNames.Count; i++)
        {
            var configName = configNames[i];
            ImGui.PushID(i);
            ImGui.PushStyleColor(ImGuiCol.Header, selectedConfigIndex == i
                                                      ? new Vector4(0.18f, 0.28f, 0.45f, 0.85f)
                                                      : new Vector4(0.7f, 0.8f, 0.9f, 0.7f));
            ImGui.PushStyleColor(ImGuiCol.Text, selectedConfigIndex == i
                                                    ? new Vector4(0.95f, 0.98f, 1.0f, 1.0f)
                                                    : new Vector4(0.7f, 0.8f, 0.9f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.22f, 0.32f, 0.55f, 0.7f));
            if (ImGui.Selectable(GetStr(configName, $"Config{i}"), selectedConfigIndex == i))
                selectedConfigIndex = i;
            ImGui.PopStyleColor(3);

            // 右键菜单
            if (ImGui.BeginPopupContextItem($"ConfigContext{i}"))
            {
                if (ImGui.MenuItem(GetStr("重命名", "RenameConfig")))
                {
                    renameConfigIndex = i;
                    renameConfigName = configName;
                    showRenameDialog = true;
                    ImGui.CloseCurrentPopup();
                }

                if (ImGui.MenuItem(GetStr("删除", "DeleteConfig")))
                {
                    ModuleConfig.Configurations.Remove(configName);
                    configNames.RemoveAt(i);
                    if (selectedConfigIndex == i)
                        selectedConfigIndex = -1;
                    else if (selectedConfigIndex > i)
                        selectedConfigIndex--;
                    SaveConfig(ModuleConfig);
                }

                ImGui.EndPopup();
            }

            ImGui.PopID();
            ImGui.Spacing();
        }

        ImGui.PopStyleVar(2);

        // 重命名配置对话框
        if (showRenameDialog)
        {
            ImGui.SetNextWindowSize(new Vector2(320, 140));
            if (ImGui.Begin(GetStr("重命名配置", "RenameConfigDialog"), ref showRenameDialog))
            {
                var originalName = configNames[renameConfigIndex];
                ImGui.Text(GetStr("新名称") + ":");
                ImGui.InputText("##RenameConfigName", ref renameConfigName, 256);
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                if (ImGui.Button(GetStr("确认", "ConfirmRename"), new Vector2(80, 0)))
                {
                    if (!string.IsNullOrWhiteSpace(renameConfigName) && renameConfigName != originalName && !configNames.Contains(renameConfigName))
                    {
                        configNames[renameConfigIndex] = renameConfigName;
                        ModuleConfig.Configurations[renameConfigName] = ModuleConfig.Configurations[originalName];
                        ModuleConfig.Configurations.Remove(originalName);

                        SaveConfig(ModuleConfig);
                        showRenameDialog = false;
                    }
                }

                ImGui.SameLine();
                if (ImGui.Button(GetStr("取消", "CancelRename"), new Vector2(80, 0)))
                    showRenameDialog = false;

                ImGui.End();
            }
        }
    }

    private void ConfigureRightColumn()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 4));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(10, 8));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.9f, 0.8f, 1.0f));
        ImGui.Text(GetStr("技能配置"));
        ImGui.PopStyleColor();
        ImGui.Spacing();

        if (selectedConfigIndex >= 0 && selectedConfigIndex < configNames.Count)
        {
            var selectedConfigName = configNames[selectedConfigIndex];
            ImGui.TextColored(new Vector4(0.8f, 0.85f, 1.0f, 1.0f), GetStr("当前配置") + $": {selectedConfigName}");
            ImGui.Spacing();

            // 确保配置数据存在
            if (!ModuleConfig.Configurations.ContainsKey(selectedConfigName))
                ModuleConfig.Configurations[selectedConfigName] = new ConfigData();

            var configData = ModuleConfig.Configurations[selectedConfigName];

            // 启用
            ImGui.PushStyleColor(ImGuiCol.CheckMark, new Vector4(0.3f, 0.8f, 0.5f, 1.0f));
            var isEnabled = configData.IsEnabled;
            if (ImGui.Checkbox(GetStr("启用", $"Enabled{selectedConfigIndex}"), ref isEnabled))
            {
                configData.IsEnabled = isEnabled;
                SaveConfig(ModuleConfig);
            }

            ImGui.PopStyleColor();
            ImGui.Spacing();

            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 4));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 6));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.85f, 0.95f, 0.95f, 1.0f));

            // 消息概率
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 0.7f, 1.0f), GetStr("发送消息概率") + ":");
            ImGui.SameLine();
            ImGui.TextDisabled("(0-100, 概率越高越频繁)");
            var moduleConfigMessageProbability = configData.MessageProbability;
            if (ImGui.SliderInt("##MessageProbability", ref moduleConfigMessageProbability, 0, 100))
            {
                configData.MessageProbability = moduleConfigMessageProbability;
                SaveConfig(ModuleConfig);
            }

            ImGui.PopStyleColor();
            ImGui.PopStyleVar(2);

            // 技能选择下拉多选框
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 0.7f, 1.0f), GetStr("选择技能") + ":");
            ActionSelect.SelectedActionIDs = configData.SelectedActionIDs.ToHashSet();
            if (ActionSelect.DrawCheckbox())
            {
                configData.SelectedActionIDs = ActionSelect.SelectedActionIDs.ToList();
                SaveConfig(ModuleConfig);
            }

            ImGui.Spacing();

            // 消息配置
            ImGui.TextColored(new Vector4(0.7f, 0.8f, 1.0f, 1.0f), GetStr("消息列表(会随机挑选一条消息发送)") + ":");
            ImGui.Spacing();

            // 显示现有消息
            for (var i = 0; i < configData.Messages.Count; i++)
            {
                ImGui.PushID($"Message{i}");
                var message = configData.Messages[i];
                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.15f, 0.18f, 0.22f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.25f, 0.28f, 0.35f, 0.7f));
                if (ImGui.InputTextMultiline($"##MessageInput{i}", ref message, 1024, new Vector2(0, 50)))
                {
                    configData.Messages[i] = message;
                    SaveConfig(ModuleConfig);
                }

                ImGui.PopStyleColor(2);
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.7f, 0.2f, 0.2f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.8f, 0.3f, 0.3f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.6f, 0.15f, 0.15f, 1.0f));
                if (ImGui.Button(GetStr("删除", $"DeleteMessage{i}"), new Vector2(60, 0)))
                {
                    configData.Messages.RemoveAt(i);
                    SaveConfig(ModuleConfig);
                    i--;
                }

                ImGui.PopStyleColor(3);
                ImGui.PopID();
                ImGui.Spacing();
            }

            // 新消息输入
            ImGui.TextColored(new Vector4(0.8f, 0.95f, 0.9f, 1.0f), GetStr("添加新消息") + ":");
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.15f, 0.18f, 0.22f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.25f, 0.28f, 0.35f, 0.7f));
            if (ImGui.InputTextMultiline("##NewMessage", ref newMessageInput, 1024, new Vector2(0, 50)))
            {
                // 输入时不需要特殊处理
            }

            ImGui.PopStyleColor(2);
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.18f, 0.45f, 0.32f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.22f, 0.55f, 0.38f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.13f, 0.35f, 0.22f, 1.0f));
            if (ImGui.Button(GetStr("添加", "AddMessage"), new Vector2(60, 0)))
            {
                if (!string.IsNullOrWhiteSpace(newMessageInput))
                {
                    configData.Messages.Add(newMessageInput);
                    newMessageInput = "";
                    SaveConfig(ModuleConfig);
                }
            }

            ImGui.PopStyleColor(3);
        }
        else
            ImGui.TextColored(new Vector4(1.0f, 0.7f, 0.7f, 1.0f), GetStr("请从左侧选择一个配置进行编辑"));

        ImGui.PopStyleVar(2);
    }

    #endregion

    #region Hooks

    private static DateTime lastMessageTime = DateTime.MinValue;
    private const int CooldownMs = 2000;

    public static void PostCharacterStartCast(
        nint result,
        IBattleChara player,
        ActionType type,
        uint actionId,
        nint a4,
        float rotation,
        float a6) => ProcessAction(true, type, actionId, player);

    private static void PreCharacterCompleteCast(
        ref bool isPrevented,
        ref IBattleChara player,
        ref ActionType type,
        ref uint actionId,
        ref uint spellId,
        ref GameObjectId animationTargetId,
        ref Vector3 location,
        ref float rotation,
        ref short lastUsedActionSequence,
        ref int animationVariation,
        ref int ballistaEntityId) => ProcessAction(true, type, actionId);

    private static void PostCharacterCompleteCast(
        nint result,
        IBattleChara player,
        ActionType type,
        uint actionId,
        uint spellId,
        GameObjectId animationTargetId,
        Vector3 location,
        float rotation,
        short lastUsedActionSequence,
        int animationVariation,
        int ballistaEntityId) => ProcessAction(true, type, actionId);

    private static void PostUseAction(
        bool result,
        ActionType actionType,
        uint actionId,
        ulong targetId,
        uint extraParam,
        ActionManager.UseActionMode queueState,
        uint comboRouteId) => ProcessAction(result, actionType, actionId);

    private static void PostUseActionLocation(
        bool result,
        ActionType actionType,
        uint actionId,
        ulong targetId,
        Vector3 location,
        uint extraParam) => ProcessAction(result, actionType, actionId);

    private static void ProcessAction(bool result, ActionType actionType, uint actionId, IBattleChara? player = null)
    {
        if (actionType != ActionType.Action || !result || !ModuleConfig.IsEnabled)
            return;

        // 冷却判断
        var now = DateTime.Now;
        if ((now - lastMessageTime).TotalMilliseconds < CooldownMs)
            return;

        var random = new Random();

        if (player != null)
        {
            var localPlayer = Control.GetLocalPlayer();
            if (localPlayer == null || player.AccountID != localPlayer->AccountId)
                return;
        }

        // 查找包含此技能ID的配置
        var matchingMessages = new List<string>();
        foreach (var config in ModuleConfig.Configurations.Values)
        {
            if (config.IsEnabled && config.SelectedActionIDs.Contains(actionId) && config.Messages.Count > 0)
            {
                // 检查概率
                if (random.Next(1, 101) > config.MessageProbability)
                    continue;

                matchingMessages.AddRange(config.Messages);
            }
        }

        if (matchingMessages.Count == 0)
            return;

        // 随机选择一条消息
        var selectedMessage = matchingMessages[random.Next(matchingMessages.Count)];

        // 发送消息
        SendChatMessage(selectedMessage, ModuleConfig.ChatTypeConfig);

        // 记录发送时间
        lastMessageTime = now;
    }

    #endregion

    /// <summary>
    /// 获取聊天类型的预览文本
    /// </summary>
    /// <param name="chatType"></param>
    /// <returns></returns>
    private static string GetChatTypePreview(string chatType)
    {
        return chatType switch
        {
            ChatType.Say => GetStr("说话"),
            ChatType.Party => GetStr("小队"),
            ChatType.Echo => GetStr("默语"),
            _ => GetStr("Unknown")
        };
    }

    /// <summary>
    /// 发送聊天消息
    /// </summary>
    /// <param name="message"></param>
    /// <param name="chatType"></param>
    private static void SendChatMessage(string message, string chatType = ChatType.Echo)
    {
        var lines = message.Split(['\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
            ChatManager.SendMessage(chatType + line);
    }

    #region Config

    private static class ChatType
    {
        public const string Say = "/s ";

        public const string Party = "/p ";

        public const string Echo = "/e ";
    }

    private class ModuleStorage : ModuleConfiguration
    {
        // 是否启用功能
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 发送频道
        /// </summary>
        public string ChatTypeConfig { get; set; } = ChatType.Echo;

        // 配置名称 -> 配置数据的映射
        public Dictionary<string, ConfigData> Configurations { get; set; } = new();
    }

    private class ConfigData
    {
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 消息发送概率（0-100）
        /// </summary>
        public int MessageProbability { get; set; } = 100;

        public List<uint> SelectedActionIDs { get; set; } = [];
        public List<string> Messages { get; set; } = [];
    }

    #endregion
}
