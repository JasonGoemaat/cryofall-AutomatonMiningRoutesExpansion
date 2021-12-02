namespace CryoFall.Automaton
{
    using System.ComponentModel;
    using AtomicTorch.CBND.CoreMod.ClientComponents.Input;
    using AtomicTorch.CBND.GameApi;
    using AtomicTorch.CBND.GameApi.ServicesClient;

    [NotPersistent]
    public enum MiningRoutesButton
    {
        [Description("Start record mode")]
        [ButtonInfo(InputKey.F10, Category = "AutomatonMiningRoutesExpansion")]
        StartRecordMode,
    }
}
