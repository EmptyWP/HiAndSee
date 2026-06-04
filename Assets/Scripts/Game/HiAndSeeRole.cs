namespace HiAndSee.Game
{
    public enum HiAndSeeRole : byte
    {
        Unassigned = 0,
        Ghost = 1,
        Impostor = 2,
        Sheriff = 3,
        Civilian = 4
    }

    public enum HiAndSeeRoundResult : byte
    {
        None = 0,
        HumansWin = 1,
        GhostsWin = 2
    }
}
