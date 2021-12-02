namespace CryoFall.Automaton
{
    using AtomicTorch.CBND.CoreMod.Bootstrappers;
    using AtomicTorch.CBND.CoreMod.ClientComponents.Input;
    using AtomicTorch.CBND.CoreMod.Systems.Notifications;
    using AtomicTorch.CBND.GameApi.Data.Characters;
    using AtomicTorch.CBND.GameApi.Scripting;
    using AtomicTorch.GameEngine.Common.Primitives;
    using CryoFall.Automaton.Features;
    using System.Collections.Generic;

    public class BootstrapperMiningRoutes : BaseBootstrapper
    {
        private static ClientInputContext gameplayInputContext;

        public override void ClientInitialize()
        {
            ClientInputManager.RegisterButtonsEnum<MiningRoutesButton>();
            AutomatonManager.AddFeature(FeatureMiningRoutes.Instance);
            BootstrapperClientGame.InitCallback += GameInitHandler;
            BootstrapperClientGame.ResetCallback += ResetHandler;
        }

        private static void GameInitHandler(ICharacter currentCharacter)
        {
            gameplayInputContext = ClientInputContext
                .Start("MiningRoutes StartRecordMode")
                .HandleButtonDown(MiningRoutesButton.StartRecordMode, () =>
                {
                    if (FeatureMiningRoutes.IsEnabled)
                    {
                        FeatureMiningRoutes.recordMode = !FeatureMiningRoutes.recordMode;
                        if (FeatureMiningRoutes.recordMode)
                        {
                            FeatureMiningRoutes.routeList[FeatureMiningRoutes.editingRouteListString] = new List<Vector2D>();
                        }
                        FeatureMiningRoutes.inputAllowed = (!(AutomatonManager.IsEnabled)) || FeatureMiningRoutes.recordMode;
                        NotificationSystem.ClientShowNotification("Record mode: " + FeatureMiningRoutes.recordMode);
                    }
                });
        }

        private static void ResetHandler()
        {
            gameplayInputContext?.Stop();
            gameplayInputContext = null;
        }
    }
}