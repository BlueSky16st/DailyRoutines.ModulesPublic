using System.Collections.Generic;
using System.Threading.Tasks;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace DailyRoutines.ModulesPublic;

public class AutoTankStanceEnhanced : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = "切换坦克时自动盾姿",
        Description = "",
        Category = ModuleCategories.Action,
        Author = ["Hsin"]
    };

    private static readonly HashSet<uint> InvalidContentTypes = [16, 17, 18, 19, 31, 32, 34, 35];

    private static readonly Dictionary<uint, (uint Action, uint Status)> TankStanceActions = new()
    {
        // 剑术师 / 骑士
        [1] = (28, 79),
        [19] = (28, 79),
        // 斧术师 / 战士
        [3] = (48, 91),
        [21] = (48, 91),
        // 暗黑骑士
        [32] = (3629, 743),
        // 绝枪战士
        [37] = (16142, 1833),
    };

    protected override void Init()
    {
        TaskHelper ??= new TaskHelper { TimeoutMS = 30_000 };

        DService.Instance().ClientState.ClassJobChanged += OnClassJobChanged;
        DService.Instance().ClientState.LevelChanged += LevelChange;
        DService.Instance().DutyState.DutyRecommenced += OnDutyRecommenced;
    }

    protected override void Uninit()
    {
        DService.Instance().ClientState.ClassJobChanged -= OnClassJobChanged;
        DService.Instance().ClientState.LevelChanged -= LevelChange;
        DService.Instance().DutyState.DutyRecommenced -= OnDutyRecommenced;

        base.Uninit();
    }

    private void OnDutyRecommenced(object? sender, ushort e)
    {
        TaskHelper.Abort();
        TaskHelper.Enqueue(CheckCurrentJob);
    }

    private void OnClassJobChanged(uint classJobId)
    {
        TaskHelper.DelayNext(1000);
        TaskHelper.Enqueue(CheckCurrentJob);
    }

    private void LevelChange(uint classJobId, uint level)
    {
        TaskHelper.DelayNext(1000);
        TaskHelper.Enqueue(CheckCurrentJob);
    }

    private static bool? CheckCurrentJob()
    {
        // if (BetweenAreas || OccupiedInEvent || !IsScreenReady()) return false;
        // if (OccupiedInEvent || !IsScreenReady()) return false;

        if (DService.Instance().ObjectTable.LocalPlayer is not { ClassJob.RowId: var job, IsTargetable: true } || job == 0)
            return false;

        if (!TankStanceActions.TryGetValue(job, out var info)) return true;

        if (LocalPlayerState.HasStatus(info.Status, out _)) return true;

        return UseActionManager.Instance().UseAction(ActionType.Action, info.Action);
    }
}
