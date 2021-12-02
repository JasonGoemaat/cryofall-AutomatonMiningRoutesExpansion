namespace CryoFall.Automaton.Features
{
    using System.Collections.Generic;
    using AtomicTorch.CBND.GameApi.Data;
    using AtomicTorch.CBND.GameApi.Scripting;
    using AtomicTorch.GameEngine.Common.Primitives;
    using AtomicTorch.CBND.CoreMod.Systems.WorldObjectClaim;
    using AtomicTorch.CBND.GameApi.Data.World;
    using AtomicTorch.CBND.GameApi.Extensions;
    using CryoFall.Automaton.ClientSettings.Options;
    using CryoFall.Automaton.ClientSettings;
    using AtomicTorch.CBND.CoreMod.Characters.Player;
    using AtomicTorch.CBND.CoreMod.Characters.Input;
    using AtomicTorch.CBND.GameApi.Scripting.ClientComponents;
    using AtomicTorch.CBND.CoreMod.Tiles;
    using AtomicTorch.CBND.GameApi.ServicesClient;
    using System;
    using AtomicTorch.CBND.CoreMod.Characters;
    using AtomicTorch.CBND.CoreMod.Systems.Physics;
    using AtomicTorch.CBND.CoreMod.Systems.Weapons;
    using AtomicTorch.CBND.CoreMod.Drones;
    using AtomicTorch.CBND.CoreMod.UI.Controls.Game.Items.Controls;
    using AtomicTorch.CBND.CoreMod.Items.Weapons;
    using AtomicTorch.CBND.CoreMod.StaticObjects.Vegetation;
    using AtomicTorch.CBND.GameApi.Data.Physics;
    using System.Linq;
    using AtomicTorch.CBND.CoreMod.Systems.CharacterDroneControl;
    using AtomicTorch.CBND.GameApi.Data.Items;
    using AtomicTorch.CBND.CoreMod.Characters.Mobs;

    class FeatureMiningRoutes : ProtoFeature<FeatureMiningRoutes>
    {
        public override string Name => "FeatureMiningRoutes";

        public override string Description => "This Mod allows the creation of Routes, which the Mod will walk if it is active. " +
            "While doing this it shoots mobs. " +
            "Note: Place a weapon in Hobar slot 1 and a drone controller in hotbar slot 2.";

        public static Dictionary<string, List<Vector2D>> routeList;
        private List<Vector2D> activeRouteList;
        private string activeRouteListString;
        private int currentWaypoint;

        public static string editingRouteListString;
        public static bool recordMode;
        public static new bool IsEnabled;

        public List<IProtoEntity> mobList = new List<IProtoEntity>();
        private FeatureDroneCommander droneCommander;
        
        private string optionsStorageLocalFilePath;
        private IClientStorage clientStorage;

        private Vector2D lastPosition;
        private double timeSpentInOnePlace = 0;

        private readonly byte weaponHotbarSlot = 0;
        private readonly byte droneControlHotbarSlot = 1;

        public static bool inputAllowed;
        private CharacterInput characterInput;


        protected override void PrepareFeature(List<IProtoEntity> entityList, List<IProtoEntity> requiredItemList)
        {
            mobList.AddRange(Api.FindProtoEntities<IProtoCharacterMob>());
            mobList.RemoveAll(t => (t is MobChicken || t is MobPangolin || t is MobStarfish || t is MobTurtle || t is MobRiverSnail));
            AutomatonManager.GetFeatures().ForEach(delegate (IProtoFeature feature)
            {
                if (feature is FeatureDroneCommander featureDroneCommander)
                {
                    droneCommander = featureDroneCommander;
                }
            });
            activeRouteList = new List<Vector2D>();
            editingRouteListString = "Default";
            routeList = new Dictionary<string, List<Vector2D>>();

            SetUpClientStorage();
            LoadRouteList();
        }
        private void SetUpClientStorage()
        {
            optionsStorageLocalFilePath = "Mods/" + "AutomatonMiningRoutesExpansion" + "/" + Id;
            clientStorage = Api.Client.Storage.GetStorage(optionsStorageLocalFilePath);
            clientStorage.RegisterType(typeof(Vector2D));
            clientStorage.RegisterType(typeof(List<Vector2D>));
            clientStorage.RegisterType(typeof(Dictionary<string, List<Vector2D>>));
        }
        private void LoadRouteList()
        {
            if (!clientStorage.TryLoad<Dictionary<string, object>>(out var snapshot))
            {
                return;
            }
            foreach (var pair in snapshot)
            {
                if (pair.Key == "routeList")
                {
                    if (pair.Value is Dictionary<string, List<Vector2D>> routeList)
                    {
                        FeatureMiningRoutes.routeList = routeList;
                    }
                }
                if (pair.Key == "editingRouteListString")
                {
                    if (pair.Value is string editingRouteListString)
                    {
                        FeatureMiningRoutes.editingRouteListString = editingRouteListString;
                    }
                }
            }
        }
        public override void PrepareOptions(SettingsFeature settingsFeature)
        {
            AddOptionIsMiningRoutesEnabled(settingsFeature);
            Options.Add(new OptionSeparator());
            AddOptionActiveRoute(settingsFeature);
            Options.Add(new OptionSeparator());
            AddOptionEditingRoute(settingsFeature);
        }

        protected void AddOptionIsMiningRoutesEnabled(SettingsFeature settingsFeature)
        {
            Options.Add(new OptionCheckBox(
                parentSettings: settingsFeature,
                id: "IsEnabled",
                label: IsEnabledText,
                defaultValue: false,
                valueChangedCallback: value =>
                {
                    settingsFeature.IsEnabled = IsEnabled = value;
                    settingsFeature.OnIsEnabledChanged(value);
                    inputAllowed = (!(value && AutomatonManager.IsEnabled)) || recordMode;
                }));
        }

        private void AddOptionActiveRoute(SettingsFeature settingsFeature)
        {
            Options.Add(new OptionTextBox<string>(
                parentSettings: settingsFeature,
                id: "ActiveRoute",
                label: "Active Route:",
                defaultValue: "Default",
                valueChangedCallback: (val) =>
                {
                    if (!routeList.ContainsKey(val))
                    {
                        routeList[val] = new List<Vector2D>();
                    }
                    activeRouteListString = val;

                },
                toolTip: "Defines which Route is used."));
        }

        private void AddOptionEditingRoute(SettingsFeature settingsFeature)
        {
            Options.Add(new OptionTextBox<string>(
                parentSettings: settingsFeature,
                id: "EditingRoute",
                label: "Editing Route:",
                defaultValue: "Default",
                valueChangedCallback: (val) =>
                {
                    editingRouteListString = val;
                },
                toolTip: "Defines which Route is being edited in record mode."));
        }

        public override void Update(double deltaTime)
        {
            if (recordMode)
            {
                return;
            }
            if (lastPosition.DistanceSquaredTo(CurrentCharacter.Position) < 0.1 * deltaTime)
            {
                timeSpentInOnePlace += deltaTime;
            }else
            {
                timeSpentInOnePlace = 0;
            }
            if (timeSpentInOnePlace > 10)
            {
                using var tempExceptDrones = Api.Shared.GetTempList<IItem>();
                var itemDrone = droneCommander.ClientSelectNextDrone(tempExceptDrones.AsList());
                if(!CharacterDroneControlSystem.ClientTryStartDrone(itemDrone,
                                                                 (Vector2Ushort)CurrentCharacter.Position,
                                                                 showErrorNotification: false))
                {
                    Vector2D headOfCharacter = new Vector2D(CurrentCharacter.Position.X, CurrentCharacter.Position.Y + 1);
                    CharacterDroneControlSystem.ClientTryStartDrone(itemDrone,
                                                                 (Vector2Ushort)headOfCharacter,
                                                                 showErrorNotification: false);
                }
                
                timeSpentInOnePlace = 0;
            }
            lastPosition = CurrentCharacter.Position;
            activeRouteList = routeList[activeRouteListString];
            if (!(IsEnabled && CheckPrecondition()))
            {
                return;
            }

            if (!FindAndAttackTarget())
            {
                if (MineArea())
                {
                    WalkToNextWaypoint();
                }
                else
                {
                    SetToClosestWaypoint();
                }
            }
        }

        public override void Execute()
        {
            if(recordMode)
            {
                routeList[editingRouteListString].Add(CurrentCharacter.Position);
                return;
            }
        }


        private bool WalkToNextWaypoint()
        {
            if (activeRouteList.Count == 0)
            {
                return true;
            }
            Vector2D targetPosition = activeRouteList[currentWaypoint];
            if (WalkToPosition(targetPosition, 0.2))
            {
                currentWaypoint += 1;
                if (currentWaypoint >= activeRouteList.Count)
                {
                    currentWaypoint = 0;
                }
                return true;
            }
            else
            {
                return false;
            }
        }

        private bool MineArea()
        {
            if (CurrentCharacter.Tile.ProtoTile == Api.GetProtoEntity<TileRocky>() 
                || CurrentCharacter.Tile.ProtoTile == Api.GetProtoEntity<TileClay>())
            {
                
                EnabledEntityList = droneCommander.EnabledEntityList;
                
                Vector2Ushort targetPosition;
                IStaticWorldObject target = GetClosestEntity();
                if (target == null)
                {
                    return true;
                }

                targetPosition = target.TilePosition;

                if (WalkToPosition((Vector2D)targetPosition, 5))
                {
                    if (target != null)
                    {
                        this.SetMoveInput(CharacterMoveModes.None);
                    }
                }
                return false;
            }
            return true;
        }

        private bool WalkToPosition(Vector2D targetPosition, double range)
        {
            Vector2D currentPosition = CurrentCharacter.Position;
            var move = CharacterMoveModes.None;
            move = SetMove(targetPosition, currentPosition, move);

            this.SetMoveInput(move);

            if (targetPosition.DistanceSquaredTo(currentPosition) < range)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private CharacterMoveModes SetMove(Vector2D targetPosition, Vector2D currentPosition, CharacterMoveModes move)
        {
            if (Math.Abs(targetPosition.X - currentPosition.X) > 0.1)
            {
                if (targetPosition.X > currentPosition.X)
                {
                    move |= CharacterMoveModes.Right;
                }
                if (targetPosition.X < currentPosition.X)
                {
                    move |= CharacterMoveModes.Left;
                }
            }
            if (Math.Abs(targetPosition.Y - currentPosition.Y) > 0.1)
            {
                if (targetPosition.Y < currentPosition.Y)
                {
                    move |= CharacterMoveModes.Down;
                }
                if (targetPosition.Y > currentPosition.Y)
                {
                    move |= CharacterMoveModes.Up;
                }
            }

            move |= CharacterMoveModes.ModifierRun;

            if ((move & CharacterMoveModes.Up) != 0
                && (move & CharacterMoveModes.Down) != 0)
            {
                // cannot move up and down simultaneously
                move &= ~(CharacterMoveModes.Up | CharacterMoveModes.Down);
            }

            if ((move & CharacterMoveModes.Left) != 0
                && (move & CharacterMoveModes.Right) != 0)
            {
                // cannot move left and right simultaneously
                move &= ~(CharacterMoveModes.Left | CharacterMoveModes.Right);
            }

            if ((move & (CharacterMoveModes.Left | CharacterMoveModes.Up | CharacterMoveModes.Right | CharacterMoveModes.Down))
                == CharacterMoveModes.None)
            {
                // cannot run when not moving
                move = CharacterMoveModes.None;
            }
            return move;
        }

        private bool FindAndAttackTarget()
        {
            var fromPos = CurrentCharacter.Position + GetWeaponOffset();
            using var objectsNearby = this.CurrentCharacter.PhysicsBody.PhysicsSpace
                                          .TestCircle(position: fromPos,
                                                      radius: this.GetCurrentWeaponRange(),
                                                      collisionGroup: CollisionGroups.HitboxRanged);
            var objectOfInterest = objectsNearby.AsList()
                                   ?.Where(t => this.mobList.Contains(t.PhysicsBody?.AssociatedWorldObject?.ProtoGameObject))
                                   .ToList();
            if (objectOfInterest == null || objectOfInterest.Count == 0)
            {
                ClientHotbarSelectedItemManager.SelectedSlotId = droneControlHotbarSlot;
                return false;
            }
            Vector2D closestTarget = new Vector2D(double.MaxValue, double.MaxValue);
            IWorldObject closestWorldObject = null;
            foreach (var obj in objectOfInterest)
            {
                var testWorldObject = obj.PhysicsBody.AssociatedWorldObject;
                var shape = obj.PhysicsBody.Shapes.FirstOrDefault(s =>
                                                                      s.CollisionGroup == CollisionGroups.HitboxRanged);
                if (shape == null)
                {
                    Api.Logger.Error("Target object has no HitBoxRanged shape" + testWorldObject);
                    continue;
                }
                var targetPoint = this.ShapeCenter(shape) + obj.PhysicsBody.Position;
                if (this.CheckForObstacles(testWorldObject, targetPoint))
                {
                    if (targetPoint.DistanceSquaredTo(CurrentCharacter.Position) < closestTarget.DistanceSquaredTo(CurrentCharacter.Position))
                    {
                        closestTarget = targetPoint;
                        closestWorldObject = testWorldObject;
                    }

                }

            }
            if (closestWorldObject != null)
            {
                ClientHotbarSelectedItemManager.SelectedSlotId = weaponHotbarSlot;
                this.AttackTarget(closestWorldObject, closestTarget);
                return true;
            }
            else
            {
                ClientHotbarSelectedItemManager.SelectedSlotId = droneControlHotbarSlot;
                return false;
            }
        }

        public void AttackTarget(IWorldObject targetObject, Vector2D intersectionPoint)
        {
            if (targetObject == null)
            {
                return;
            }

            var vectorToTargetPosition = CurrentCharacter.Position +
                                             GetWeaponOffset() -
                                             intersectionPoint;
            var runPoint = CurrentCharacter.Position + CurrentCharacter.Position - intersectionPoint;
            var rotationAngleRad =
                Math.Abs(Math.PI + Math.Atan2(vectorToTargetPosition.Y, vectorToTargetPosition.X));

            var move = CharacterMoveModes.None;
            if (vectorToTargetPosition.Length < GetCurrentWeaponRange()-3)
            {
                if (runPoint.X > CurrentCharacter.Position.X)
                {
                    move |= CharacterMoveModes.Right;
                }
                if (runPoint.X < CurrentCharacter.Position.X)
                {
                    move |= CharacterMoveModes.Left;
                }
                if (runPoint.Y < CurrentCharacter.Position.Y)
                {
                    move |= CharacterMoveModes.Down;
                }
                if (runPoint.Y > CurrentCharacter.Position.Y)
                {
                    move |= CharacterMoveModes.Up;
                }
            }
            else
            {
                if (intersectionPoint.X > CurrentCharacter.Position.X)
                {
                    move |= CharacterMoveModes.Right;
                }
                if (intersectionPoint.X < CurrentCharacter.Position.X)
                {
                    move |= CharacterMoveModes.Left;
                }
                if (intersectionPoint.Y < CurrentCharacter.Position.Y)
                {
                    move |= CharacterMoveModes.Down;
                }
                if (intersectionPoint.Y > CurrentCharacter.Position.Y)
                {
                    move |= CharacterMoveModes.Up;
                }
            }

            var command = new CharacterInputUpdate(move, (float)rotationAngleRad);
            ((PlayerCharacter)CurrentCharacter.ProtoCharacter).ClientSetInput(command);
            SelectedItem.ProtoItem.ClientItemUseStart(SelectedItem);
        }

        protected double GetCurrentWeaponRange()
        {
            var item = ClientHotbarSelectedItemManager.ContainerHotbar.GetItemAtSlot(weaponHotbarSlot);
            if (item == null)
            {
                return 0;
            }
            if (item.ProtoItem is ProtoItemWeaponRangedEnergy rangedWeaponEnergy)
            {
                return rangedWeaponEnergy.OverrideDamageDescription.RangeMax;
            }
            return 7;
        }

        protected Vector2D GetWeaponOffset()
        {
            return new Vector2D(0, CurrentCharacter.ProtoCharacter.CharacterWorldWeaponOffsetRanged);
        }

        protected double GetCurrentWeaponAttackDelay()
        {
            var weapon = SelectedItem.ProtoItem as IProtoItemWeaponRanged;
            return weapon?.FireInterval ?? 0d;
        }

        private bool CheckForObstacles(IWorldObject targetObject, Vector2D intersectionPoint)
        {
            var targetTile = Api.Client.World.GetTile((Vector2Ushort)intersectionPoint);
            if (targetTile.Height != CurrentCharacter.Tile.Height) { return false; }
            var fromPos = CurrentCharacter.Position + GetWeaponOffset();
            var toPos = (fromPos - intersectionPoint).Normalized * GetCurrentWeaponRange();

            bool canReachObject = false;
            using var obstaclesOnTheWay = this.CurrentCharacter.PhysicsBody.PhysicsSpace
                                              .TestLine(fromPosition: fromPos,
                                                        toPosition: fromPos - toPos,
                                                        collisionGroup: CollisionGroups.HitboxRanged);
            foreach (var testResult in obstaclesOnTheWay.AsList())
            {
                var testResultPhysicsBody = testResult.PhysicsBody;
                if (testResultPhysicsBody.AssociatedProtoTile != null)
                {
                    if (testResultPhysicsBody.AssociatedProtoTile.Kind != TileKind.Solid)
                    {
                        continue;
                    }
                    break;
                }

                var testWorldObject = testResultPhysicsBody.AssociatedWorldObject;
                if (testWorldObject == this.CurrentCharacter)
                {
                    continue;
                }

                if (!(testWorldObject.ProtoGameObject is IDamageableProtoWorldObject))
                {
                    continue;
                }

                if (testWorldObject == targetObject)
                {
                    canReachObject = true;
                    continue;
                }

                if (this.mobList.Contains(testWorldObject.ProtoWorldObject))
                {
                    continue;
                }
                if (testWorldObject.ProtoGameObject is IProtoDrone)
                {
                    continue;
                }
                if (testWorldObject.ProtoGameObject is IProtoObjectVegetation)
                {
                    continue;
                }
                return false;
            }

            return canReachObject;
        }

        private Vector2D ShapeCenter(IPhysicsShape shape)
        {
            if (shape != null)
            {
                switch (shape.ShapeType)
                {
                    case ShapeType.Rectangle:
                        var shapeRectangle = (RectangleShape)shape;
                        return shapeRectangle.Position + shapeRectangle.Size / 2d;
                    case ShapeType.Point:
                        var shapePoint = (PointShape)shape;
                        return shapePoint.Point;
                    case ShapeType.Circle:
                        var shapeCircle = (CircleShape)shape;
                        return shapeCircle.Center;
                    case ShapeType.Line:
                        break;
                    case ShapeType.LineSegment:
                        var lineSegmentShape = (LineSegmentShape)shape;
                        return new Vector2D((lineSegmentShape.Point1.X + lineSegmentShape.Point2.X) / 2d,
                                     (lineSegmentShape.Point1.Y + lineSegmentShape.Point2.Y) / 2d);
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            return new Vector2D(0, 0);
        }

        public override void Stop()
        {
            base.Stop();
            StopWeaponUse();
            inputAllowed = true;
            var snapshot = new Dictionary<string, object>();
            snapshot["routeList"] = routeList;
            snapshot["editingRouteListString"] = editingRouteListString;
            clientStorage.Save(snapshot);
        }

        private void StopWeaponUse()
        {
            if (SelectedItem != null)
            {
                if (SelectedItem.ProtoItem is ProtoItemWeaponRanged rangedWeapon
                    || SelectedItem.ProtoItem is ProtoItemWeaponRangedEnergy rangedWeaponEnergy)
                {
                    SelectedItem?.ProtoItem.ClientItemUseFinish(SelectedItem);
                }
            }
        }

        public override void Start(ClientComponent parentComponent)
        {
            base.Start(parentComponent);
            SetToClosestWaypoint();
            inputAllowed = !(IsEnabled) || recordMode;
        }

        private void SetToClosestWaypoint()
        {
            if (activeRouteList.Count == 0)
            {
                return;
            }
            var selectedDistanceSqr = double.MaxValue;
            Vector2D closestWaypoint = new Vector2D(0, 0);
            foreach (Vector2D waypoint in activeRouteList)
            {
                var distanceSqr = waypoint.DistanceSquaredTo(Api.Client.Characters.CurrentPlayerCharacter.Position);
                if (distanceSqr < selectedDistanceSqr)
                {
                    closestWaypoint = waypoint;
                    selectedDistanceSqr = distanceSqr;
                }
            }
            currentWaypoint = activeRouteList.IndexOf(closestWaypoint);
        }

        public IStaticWorldObject GetClosestEntity()
        {
            IStaticWorldObject selectedWorldObject = null;
            var selectedDistanceSqr = long.MaxValue;

            foreach (IProtoWorldObject entity in EnabledEntityList)
            {
                if (entity is null)
                {
                    return null;
                }
                var objectsNearby = Api.Client.World.GetStaticWorldObjectsOfProto(
                    (IProtoStaticWorldObject)entity);

                foreach (var worldObject in objectsNearby)
                {
                    var position = worldObject.TilePosition;
                    var distanceSqr = position.TileSqrDistanceTo(CurrentCharacter.TilePosition);
                    using var obstaclesOnTheWay = this.CurrentCharacter.PhysicsBody.PhysicsSpace
                                              .TestLine(fromPosition: CurrentCharacter.Position,
                                                        toPosition: (Vector2D)worldObject.TilePosition,
                                                        collisionGroup: CollisionGroups.HitboxRanged);
                    bool canReach = true;
                    foreach (var testResult in obstaclesOnTheWay.AsList())
                    {
                        var testResultPhysicsBody = testResult.PhysicsBody;
                        if (testResultPhysicsBody.AssociatedProtoTile != null)
                        {
                            if (testResultPhysicsBody.AssociatedProtoTile.Kind == TileKind.Solid)
                            {
                                canReach = false;
                                break;
                            }
                        }
                        var testWorldObject = testResultPhysicsBody.AssociatedWorldObject;
                        if (testWorldObject == this.CurrentCharacter)
                        {
                            continue;
                        }

                        if (!(testWorldObject.ProtoGameObject is IDamageableProtoWorldObject))
                        {
                            continue;
                        }

                        if (testWorldObject == worldObject)
                        {
                            continue;
                        }

                        if (testWorldObject.ProtoGameObject is IProtoDrone)
                        {
                            continue;
                        }
                        canReach = false;
                    }
                    if (!canReach)
                    {
                        continue;
                    }
                    var targetTile = Api.Client.World.GetTile(position);
                    if (targetTile.Height != CurrentCharacter.Tile.Height) { continue; }
                    if (distanceSqr >= selectedDistanceSqr)
                    {
                        continue;
                    }

                    if (!WorldObjectClaimSystem.SharedIsAllowInteraction(CurrentCharacter,
                                                                         worldObject,
                                                                         showClientNotification: false))
                    {
                        continue;
                    }
                    selectedWorldObject = worldObject;
                    selectedDistanceSqr = distanceSqr;
                }
            }
            return selectedWorldObject;
        }

        private void SetMoveInput(CharacterMoveModes moveModes)
        {
            this.characterInput.MoveModes = moveModes;
            this.characterInput.RotationAngleRad = PlayerCharacter.GetPrivateState(CurrentCharacter).Input.RotationAngleRad;

            var command = new CharacterInputUpdate(
                this.characterInput.MoveModes,
                this.characterInput.RotationAngleRad);

            ((PlayerCharacter)CurrentCharacter.ProtoCharacter)
                .ClientSetInput(command);
        }
    }

}
