using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using TanksRebirth.Enums;
using System.Linq;
using TanksRebirth.Internals.Common.GameInput;
using Microsoft.Xna.Framework.Input;
using TanksRebirth.Internals.Common.Utilities;
using TanksRebirth.Internals;
using Microsoft.Xna.Framework.Audio;
using TanksRebirth.Internals.Common;
using TanksRebirth.Internals.Core.Interfaces;
using TanksRebirth.GameContent.GameMechanics;
using TanksRebirth.GameContent.Systems;
using TanksRebirth.Internals.Common.Framework.Audio;
using TanksRebirth.Graphics;
using TanksRebirth.GameContent.Systems.Coordinates;
using TanksRebirth.Internals.Common.Framework;
using TanksRebirth.Internals.Common.IO;
using System.Reflection;

namespace TanksRebirth.GameContent
{
    public class AITank : Tank
    {
        public int TierHierarchy => (int)tier;

        public AiBehavior[] Behaviors { get; private set; } // each of these should keep track of an action the tank performs
        public AiBehavior[] SpecialBehaviors { get; private set; }

        public TankTier tier;

        /// <summary>Should always be -1 if this tank is not modded content.</summary>
        public int modTier = -1;

        private Texture2D _tankTexture, _shadowTexture;

        public Action enactBehavior;

        public int AITankId { get; }

        #region AiTankParams

        public record Params
        {
            /// <summary>The max angle of which this tank will "meander," or change its movement direction.</summary>
            public float MeanderAngle { get; set; }
            /// <summary>How often this tank will take a turn at <see cref="MeanderAngle"/> radians.</summary>
            public int MeanderFrequency { get; set; }

            /// <summary>Not implemented (yet). Determines how much this tank will move un attempt to get closer to its target.</summary>
            public float PursuitLevel { get; set; }
            /// <summary>Not implemented (yet). Determines how often this tank will try to move towards its target.</summary>
            public int PursuitFrequency { get; set; }

            /// <summary>How often this tank will move its turret in the target's direction. It will be inaccurate at the measure of <see cref="AimOffset"/>.</summary>
            public int TurretMeanderFrequency { get; set; }
            /// <summary>How fast this tank's turret rotates towards its target.</summary>
            public float TurretSpeed { get; set; }
            /// <summary>How inaccurate (in radians) this tank is trying to aim at its target.</summary>
            public float AimOffset { get; set; }

            /// <summary>The distance of which this tank is wary of projectiles and tries to move away from them.</summary>
            public float ProjectileWarinessRadius { get; set; }
            /// <summary>The distance of which this tank is wary of mines and tries to move away from them.</summary>
            public float MineWarinessRadius { get; set; }

            /// <summary>On a given tick, it has this chance out of 1 to lay a mine. <para>Do note that this value must be greater than 0 and less than or equal to 1.</para></summary>
            public float MinePlacementChance { get; set; } // 0.0f to 1.0f

            /// <summary>How long (in ticks) this tank moves away from a mine that it places.</summary>
            public int MoveFromMineTime { get; set; }

            /// <summary>The distance from the main shot calculation ray an enemy must be before this tank is allowed to fire.</summary>
            public float Inaccuracy { get; set; }

            /// <summary>How often this tank shoots when given the opportunity. 0 to 1 values only. Defaults to 1.</summary>
            public float ShootChance { get; set; } = 1f;

            /// <summary>How far ahead of this tank (in the direction the tank is going) that it is aware of obstacles and navigates around them.</summary>
            public int BlockWarinessDistance { get; set; } = 50;
            /// <summary>How often this tank reads the obstacles around it and navigates around them.</summary>
            public int BlockReadTime { get; set; } = 3;
            /// <summary>How far this tank must be from a teammate before it can lay a mine or fire a bullet.</summary>
            public float TeammateTankWariness { get; set; } = 30f;
            /// <summary>Whether or not this tank tries to find calculations all around it. This is not recommended for mobile tanks.</summary>
            public bool SmartRicochets { get; set; }
            /// <summary>Whether or not this tank attempts to lay mines near destructible obstacles rather than randomly. Useless for stationary tanks.</summary>
            public bool SmartMineLaying { get; set; }
            /// <summary>Whether or not this tank's shot raycast resets it's distance check per-bounce.</summary>
            public bool BounceReset { get; set; } = true;

            public float RedirectAngle { get; set; } = MathHelper.ToRadians(5);
        }
        /// <summary>The AI parameter collection of this AI Tank.</summary>
        public Params AiParams { get; } = new();

        public Vector3 aimTarget;

        /// <summary>Whether or not this tank sees its target. Generally should not be set, but the tank will shoot if able when this is true.</summary>
        public bool SeesTarget { get; set; }

        /// <summary>The target rotation for this tank's turret. <see cref="Tank.TurretRotation"/> will move towards this value at a rate of <see cref="TurretSpeed"/>.</summary>
        public float TargetTurretRotation;

        private Vector2 oldPosition;

        #endregion

        #region ModelBone & ModelMesh
        public Matrix[] boneTransforms;

        public ModelMesh CannonMesh;
        #endregion

        public static TankTier GetHighestTierActive()
        {
            var highest = TankTier.None;

            foreach (var tank in GameHandler.AllAITanks)
            {
                if (tank is not null && !tank.Dead)
                    if (tank.tier > highest)
                        highest = tank.tier;
            }
            return highest;
        }

        public static int CountAll()
            => GameHandler.AllAITanks.Count(tnk => tnk is not null && !tnk.Dead);

        public static int GetTankCountOfType(TankTier tier)
            => GameHandler.AllAITanks.Count(tnk => tnk is not null && tnk.tier == tier && !tnk.Dead);

        public void Swap(TankTier tier, bool setDefaults = true)
        {
            this.tier = tier;

            if ((int)tier <= (int)TankTier.Marble)
                _tankTexture = Assets[$"tank_" + tier.ToString().ToLower()];
            #region Special

            if (tier == TankTier.Commando)
            {
                Model = GameResources.GetGameResource<Model>("Assets/specialtanks/tank_commando");

                _tankTexture = GameResources.GetGameResource<Texture2D>("Assets/textures/tank/wee/tank_commando");

                foreach (var mesh in Model.Meshes)
                {
                    foreach (BasicEffect effect in mesh.Effects)
                    {
                        if (mesh.Name == "Laser_Beam")
                            effect.Texture = GameResources.GetGameResource<Texture2D>("Assets/textures/misc/laser");
                        if (mesh.Name == "Barrel_Laser")
                            effect.Texture = GameResources.GetGameResource<Texture2D>("Assets/textures/misc/armor");
                        if (mesh.Name == "Dish")
                            effect.Texture = GameResources.GetGameResource<Texture2D>("Assets/textures/tank/wee/tank_commando");
                    }
                }
                // fix?
            }
            else if (tier == TankTier.Assassin)
            {
                Model = GameResources.GetGameResource<Model>("Assets/specialtanks/tank_assassin");

                _tankTexture = GameResources.GetGameResource<Texture2D>("Assets/textures/tank/wee/tank_assassin");
            }
            else if (tier == TankTier.RocketDefender)
            {
                Model = GameResources.GetGameResource<Model>("Assets/specialtanks/tank_rocket");

                _tankTexture = GameResources.GetGameResource<Texture2D>("Assets/textures/tank/wee/tank_rocket");
            }
            else if (tier == TankTier.Electro)
            {
                Model = GameResources.GetGameResource<Model>("Assets/specialtanks/tank_electro");

                _tankTexture = GameResources.GetGameResource<Texture2D>("Assets/textures/tank/wee/tank_electro");
            }
            else if (tier == TankTier.Explosive)
            {
                Model = GameResources.GetGameResource<Model>("Assets/specialtanks/tank_explosive");

                _tankTexture = GameResources.GetGameResource<Texture2D>("Assets/textures/tank/wee/tank_explosive");
            }
            else
            {
                Model = GameResources.GetGameResource<Model>("Assets/tank_e");
            }

            #endregion

            if (setDefaults)
                ApplyDefaults();
        }

        /// <summary>
        /// Creates a new <see cref="AITank"/>.
        /// </summary>
        /// <param name="tier">The tier of this <see cref="AITank"/>. If '<see cref="TankTier.Random"/>', it will be randomly chosen.</param>
        /// <param name="setTankDefaults">Whether or not to give this <see cref="AITank"/> the default values.</param>
        /// /// <param name="isIngame">Whether or not this <see cref="AITank"/> is a gameplay tank or a cosmetic tank (i.e: display models on menus, etc).</param>
        public AITank(TankTier tier, Range<TankTier> tankRange = default, bool setTankDefaults = true, bool isIngame = true)
        {
            if (isIngame)
            {
                if (Difficulties.Types["BumpUp"])
                    tier += 1;
                if (Difficulties.Types["MeanGreens"])
                    tier = TankTier.Green;
                if (Difficulties.Types["MasterModBuff"] && !Difficulties.Types["MarbleModBuff"])
                    tier += 9;
                if (Difficulties.Types["MarbleModBuff"] && !Difficulties.Types["MasterModBuff"])
                    tier += 18;
                if (Difficulties.Types["RandomizedTanks"])
                {
                    tier = TankTier.Random;
                    tankRange = new Range<TankTier>(TankTier.Brown, TankTier.Marble); // set to commando when the time comes
                }
            }
            if (tier == TankTier.Random)
                tier = (TankTier)GameHandler.GameRand.Next((int)tankRange.Min, (int)tankRange.Max + 1);
            IsIngame = isIngame;
            if (isIngame)
            {
                Behaviors = new AiBehavior[10];
                SpecialBehaviors = new AiBehavior[3];

                for (int i = 0; i < Behaviors.Length; i++)
                    Behaviors[i] = new();

                for (int i = 0; i < SpecialBehaviors.Length; i++)
                    SpecialBehaviors[i] = new();

                Behaviors[0].Label = "TankBaseMovement";
                Behaviors[1].Label = "TankBarrelMovement";
                Behaviors[2].Label = "TankEnvReader";
                Behaviors[3].Label = "TankBulletFire";
                Behaviors[4].Label = "TankMinePlacement";
                Behaviors[5].Label = "TankMineAvoidance";
                Behaviors[6].Label = "TankBulletAvoidance";

                SpecialBehaviors[0].Label = "SpecialBehavior1"; // for special tanks (such as commando, etc)
                SpecialBehaviors[1].Label = "SpecialBehavior2";
                SpecialBehaviors[2].Label = "SpecialBehavior3";

                Dead = true;
            }

            /*if ((int)tier <= (int)TankTier.Black)
                _tankTexture = GameResources.GetGameResource<Texture2D>($"Assets/textures/tank/tank_{tier.ToString().ToLower()}");
            else if ((int)tier > (int)TankTier.Black && (int)tier <= (int)TankTier.Obsidian)
                _tankTexture = GameResources.GetGameResource<Texture2D>($"Assets/textures/tank/master/tank_{tier.ToString().ToLower()}");
            else if ((int)tier > (int)TankTier.Obsidian && (int)tier <= (int)TankTier.Marble)
                _tankTexture = GameResources.GetGameResource<Texture2D>($"Assets/textures/tank/marble/tank_{tier.ToString().ToLower()}");*/

            if ((int)tier <= (int)TankTier.Marble)
                _tankTexture = Assets[$"tank_" + tier.ToString().ToLower()];

            #region Special

            if (tier == TankTier.Commando)
            {
                Model = GameResources.GetGameResource<Model>("Assets/specialtanks/tank_commando");

                _tankTexture = GameResources.GetGameResource<Texture2D>("Assets/textures/tank/wee/tank_commando");
                // fix?
            }
            else if (tier == TankTier.Assassin)
            {
                Model = GameResources.GetGameResource<Model>("Assets/specialtanks/tank_assassin");

                _tankTexture = GameResources.GetGameResource<Texture2D>("Assets/textures/tank/wee/tank_assassin");
            }
            else if (tier == TankTier.RocketDefender)
            {
                Model = GameResources.GetGameResource<Model>("Assets/specialtanks/tank_rocket");

                _tankTexture = GameResources.GetGameResource<Texture2D>("Assets/textures/tank/wee/tank_rocket");
            }
            else if (tier == TankTier.Electro)
            {
                Model = GameResources.GetGameResource<Model>("Assets/specialtanks/tank_electro");

                _tankTexture = GameResources.GetGameResource<Texture2D>("Assets/textures/tank/wee/tank_electro");
            }
            else if (tier == TankTier.Explosive)
            {
                Model = GameResources.GetGameResource<Model>("Assets/specialtanks/tank_explosive");

                _tankTexture = GameResources.GetGameResource<Texture2D>("Assets/textures/tank/wee/tank_explosive");
            }
            else
            {
                Model = GameResources.GetGameResource<Model>("Assets/tank_e");
            }

            #endregion

            CannonMesh = Model.Meshes["Cannon"];

            boneTransforms = new Matrix[Model.Bones.Count];

            _shadowTexture = GameResources.GetGameResource<Texture2D>("Assets/textures/tank_shadow");

            this.tier = tier;
            if (isIngame)
            {

                if (setTankDefaults)
                    ApplyDefaults();

            }
            int index = Array.IndexOf(GameHandler.AllAITanks, GameHandler.AllAITanks.First(tank => tank is null));

            AITankId = index;

            GameHandler.AllAITanks[index] = this;

            int index2 = Array.IndexOf(GameHandler.AllTanks, GameHandler.AllTanks.First(tank => tank is null));

            WorldId = index2;

            GameHandler.AllTanks[index2] = this;
            base.Initialize();
        }
        public override void ApplyDefaults()
        {
            switch (tier)
            {
                #region VanillaTanks
                case TankTier.Brown:
                    Stationary = true;

                    TankDestructionColor = new(152, 96, 26);

                    AiParams.TurretMeanderFrequency = 30;
                    AiParams.TurretSpeed = 0.01f;
                    AiParams.AimOffset = MathHelper.ToRadians(170);
                    AiParams.Inaccuracy = 1.6f;

                    TurningSpeed = 0f;
                    MaximalTurn = 0;

                    ShootStun = 20;
                    ShellCooldown = 300;
                    ShellLimit = 1;
                    ShellSpeed = 3f;
                    ShellType = ShellTier.Standard;
                    RicochetCount = 1;

                    Invisible = false;
                    ShellHoming = new();

                    TreadPitch = 0;
                    MaxSpeed = 0f;

                    MineCooldown = 0;
                    MineLimit = 0;
                    MineStun = 0;

                    break;

                case TankTier.Ash:
                    AiParams.MeanderAngle = MathHelper.ToRadians(30);
                    AiParams.MeanderFrequency = 15;
                    AiParams.TurretMeanderFrequency = 40;
                    AiParams.TurretSpeed = 0.01f;
                    AiParams.AimOffset = MathHelper.ToRadians(40);

                    AiParams.Inaccuracy = 0.9f;

                    TankDestructionColor = Color.Gray;

                    AiParams.ProjectileWarinessRadius = 40;
                    AiParams.MineWarinessRadius = 40;

                    TurningSpeed = 0.08f;
                    MaximalTurn = MathHelper.ToRadians(10);

                    ShootStun = 3;
                    ShellCooldown = 180;
                    ShellLimit = 1;
                    ShellSpeed = 3f;
                    ShellType = ShellTier.Standard;
                    RicochetCount = 1;

                    Invisible = false;
                    Stationary = false;
                    ShellHoming = new();

                    TreadPitch = 0.085f;
                    MaxSpeed = 1.2f;

                    MineCooldown = 0;
                    MineLimit = 0;
                    MineStun = 0;

                    AiParams.BlockWarinessDistance = 25;
                    break;

                case TankTier.Marine:
                    AiParams.MeanderAngle = MathHelper.ToRadians(30);
                    AiParams.MeanderFrequency = 10;
                    AiParams.TurretMeanderFrequency = 10;
                    AiParams.TurretSpeed = 0.1f;
                    AiParams.AimOffset = MathHelper.ToRadians(0);

                    TankDestructionColor = Color.Teal;

                    AiParams.Inaccuracy = 0.15f;

                    AiParams.ProjectileWarinessRadius = 40;
                    AiParams.MineWarinessRadius = 80;

                    TurningSpeed = 0.2f;
                    MaximalTurn = MathHelper.ToRadians(10);

                    ShootStun = 20;
                    ShellCooldown = 180;
                    ShellLimit = 1;
                    ShellSpeed = 6f;
                    ShellType = ShellTier.Rocket;
                    RicochetCount = 0;

                    Invisible = false;
                    Stationary = false;
                    ShellHoming = new();

                    TreadPitch = 0.085f;
                    MaxSpeed = 1f;

                    MineCooldown = 0;
                    MineLimit = 0;
                    MineStun = 0;

                    AiParams.BlockWarinessDistance = 40;

                    break;

                case TankTier.Yellow:
                    AiParams.MeanderAngle = MathHelper.ToRadians(40);
                    AiParams.MeanderFrequency = 15;
                    AiParams.TurretMeanderFrequency = 20;
                    AiParams.TurretSpeed = 0.02f;
                    AiParams.AimOffset = 0.5f;

                    AiParams.Inaccuracy = 1.5f;

                    Acceleration = 0.3f;
                    Deceleration = 0.6f;

                    TankDestructionColor = Color.Yellow;

                    AiParams.ProjectileWarinessRadius = 40;
                    AiParams.MineWarinessRadius = 160;

                    TurningSpeed = 0.08f;
                    MaximalTurn = MathHelper.ToRadians(10);

                    ShootStun = 20;
                    ShellCooldown = 90;
                    ShellLimit = 1;
                    ShellSpeed = 3f;
                    ShellType = ShellTier.Standard;
                    RicochetCount = 1;

                    Invisible = false;
                    Stationary = false;
                    ShellHoming = new();

                    TreadPitch = 0.085f;
                    MaxSpeed = 1.8f;

                    MineCooldown = 600;
                    MineLimit = 4;
                    MineStun = 5;

                    AiParams.MinePlacementChance = 0.3f;
                    AiParams.MoveFromMineTime = 120;

                    if (Difficulties.Types["PieFactory"])
                    {
                        VulnerableToMines = false;
                        MineCooldown = 10;
                        MineLimit = 20;
                        MineStun = 0;
                        AiParams.MinePlacementChance = 1f;
                        AiParams.MineWarinessRadius = 0;
                    }
                    break;

                case TankTier.Pink:
                    AiParams.MeanderAngle = MathHelper.ToRadians(40);
                    AiParams.MeanderFrequency = 10;
                    AiParams.TurretMeanderFrequency = 40;
                    AiParams.TurretSpeed = 0.03f;
                    AiParams.AimOffset = 0.2f;

                    AiParams.Inaccuracy = 1.3f;

                    TankDestructionColor = Color.Pink;

                    AiParams.ProjectileWarinessRadius = 40;
                    AiParams.MineWarinessRadius = 160;

                    TurningSpeed = 0.08f;
                    MaximalTurn = MathHelper.ToRadians(10);

                    ShootStun = 5;
                    ShellCooldown = 30;
                    ShellLimit = 3;
                    ShellSpeed = 3f;
                    ShellType = ShellTier.Standard;
                    RicochetCount = 1;

                    Invisible = false;
                    Stationary = false;
                    ShellHoming = new();

                    TreadPitch = 0.1f;
                    MaxSpeed = 1.2f;

                    MineCooldown = 0;
                    MineLimit = 0;
                    MineStun = 0;

                    AiParams.BlockWarinessDistance = 35;
                    break;

                case TankTier.Purple:
                    AiParams.MeanderAngle = MathHelper.ToRadians(40);
                    AiParams.MeanderFrequency = 10;
                    AiParams.TurretMeanderFrequency = 25;

                    AiParams.Inaccuracy = 0.8f;

                    TankDestructionColor = Color.Purple;

                    AiParams.TurretSpeed = 0.03f;
                    AiParams.AimOffset = 0.18f;

                    AiParams.ProjectileWarinessRadius = 60;
                    AiParams.MineWarinessRadius = 160;

                    TurningSpeed = 0.06f;
                    MaximalTurn = MathHelper.ToRadians(10);

                    ShootStun = 5;
                    ShellCooldown = 30;
                    ShellLimit = 5;
                    ShellSpeed = 3f;
                    ShellType = ShellTier.Standard;
                    RicochetCount = 1;

                    Invisible = false;
                    Stationary = false;
                    ShellHoming = new();

                    TreadPitch = -0.2f;
                    MaxSpeed = 1.8f;
                    Acceleration = 0.3f;

                    MineCooldown = 700;
                    MineLimit = 2;
                    MineStun = 10;

                    AiParams.MoveFromMineTime = 60;
                    AiParams.MinePlacementChance = 0.05f;

                    AiParams.BlockWarinessDistance = 45;
                    break;

                case TankTier.Green:
                    Stationary = true;

                    AiParams.TurretMeanderFrequency = 30;
                    AiParams.TurretSpeed = 0.02f;
                    AiParams.AimOffset = MathHelper.ToRadians(80);
                    AiParams.Inaccuracy = MathHelper.ToRadians(25);

                    TankDestructionColor = Color.LimeGreen;

                    TurningSpeed = 0f;
                    MaximalTurn = 0;

                    ShootStun = 5;
                    ShellCooldown = 60;
                    ShellLimit = 2;
                    ShellSpeed = 6f; // 6f
                    ShellType = ShellTier.TrailedRocket;
                    RicochetCount = 2; // 2

                    Invisible = false;
                    ShellHoming = new();

                    TreadPitch = 0;
                    MaxSpeed = 0f;

                    MineCooldown = 0;
                    MineLimit = 0;
                    MineStun = 0;

                    break;

                case TankTier.White:
                    AiParams.MeanderAngle = MathHelper.ToRadians(30);
                    AiParams.MeanderFrequency = 10;
                    AiParams.TurretMeanderFrequency = 20;
                    AiParams.TurretSpeed = 0.03f;
                    AiParams.AimOffset = MathHelper.ToRadians(40);

                    AiParams.Inaccuracy = 0.8f;

                    TankDestructionColor = Color.White;

                    AiParams.ProjectileWarinessRadius = 40;
                    AiParams.MineWarinessRadius = 160;

                    TurningSpeed = 0.08f;
                    MaximalTurn = MathHelper.ToRadians(10);

                    ShootStun = 5;
                    ShellCooldown = 30;
                    ShellLimit = 5;
                    ShellSpeed = 3f;
                    ShellType = ShellTier.Standard;
                    RicochetCount = 1;

                    Stationary = false;
                    ShellHoming = new();

                    TreadPitch = -0.35f;
                    MaxSpeed = 1.2f;
                    Acceleration = 0.3f;

                    MineCooldown = 1000;
                    MineLimit = 2;
                    MineStun = 8;

                    AiParams.MoveFromMineTime = 40;
                    AiParams.MinePlacementChance = 0.08f;

                    AiParams.BlockWarinessDistance = 30;

                    Invisible = true;
                    break;

                case TankTier.Black:
                    AiParams.MeanderAngle = MathHelper.ToRadians(30);
                    AiParams.MeanderFrequency = 10;
                    AiParams.TurretMeanderFrequency = 20;
                    AiParams.TurretSpeed = 0.03f;
                    AiParams.AimOffset = MathHelper.ToRadians(5);

                    AiParams.Inaccuracy = 0.35f;

                    TankDestructionColor = Color.Black;

                    AiParams.ProjectileWarinessRadius = 100;
                    AiParams.MineWarinessRadius = 60;

                    TurningSpeed = 0.06f;
                    MaximalTurn = MathHelper.ToRadians(5);

                    ShootStun = 5;
                    ShellCooldown = 90;
                    ShellLimit = 3;
                    ShellSpeed = 6f;
                    ShellType = ShellTier.Rocket;
                    RicochetCount = 0;

                    Invisible = false;
                    Stationary = false;
                    ShellHoming = new();

                    TreadPitch = -0.26f;
                    MaxSpeed = 2.4f;
                    Acceleration = 0.3f;

                    ShootPitch = -0.2f;

                    MineCooldown = 850;
                    MineLimit = 2;
                    MineStun = 10;

                    AiParams.MoveFromMineTime = 100;
                    AiParams.MinePlacementChance = 0.05f;

                    AiParams.SmartMineLaying = true;

                    AiParams.BlockWarinessDistance = 60;
                    break;
                #endregion
                #region MasterMod
                case TankTier.Bronze:
                    AiParams.TurretMeanderFrequency = 15;
                    AiParams.TurretSpeed = 0.05f;
                    AiParams.AimOffset = 0.005f;

                    AiParams.Inaccuracy = 0.2f;

                    TankDestructionColor = new(152, 96, 26);

                    ShellCooldown = 50;
                    ShellLimit = 2;
                    ShellSpeed = 3f;
                    ShellType = ShellTier.Standard;
                    RicochetCount = 1;

                    Invisible = false;
                    Stationary = true;
                    ShellHoming = new();

                    AiParams.MoveFromMineTime = 100;
                    AiParams.MinePlacementChance = 0.05f;
                    break;
                case TankTier.Silver:
                    AiParams.MeanderAngle = MathHelper.ToRadians(30);
                    AiParams.MeanderFrequency = 10;
                    AiParams.TurretMeanderFrequency = 60;
                    AiParams.TurretSpeed = 0.045f;
                    AiParams.AimOffset = 0.9f;

                    AiParams.Inaccuracy = 0.4f;

                    TankDestructionColor = Color.Silver;

                    AiParams.ProjectileWarinessRadius = 70;
                    AiParams.MineWarinessRadius = 140;

                    TurningSpeed = 0.13f;
                    MaximalTurn = MathHelper.PiOver2;

                    ShootStun = 0;
                    ShellCooldown = 15;
                    ShellLimit = 8;
                    ShellSpeed = 4f;
                    ShellType = ShellTier.Standard;
                    RicochetCount = 1;

                    Invisible = false;
                    Stationary = false;
                    ShellHoming = new();

                    TreadPitch = 0.2f;
                    MaxSpeed = 1.6f;
                    Acceleration = 0.3f;
                    Deceleration = 0.6f;

                    MineCooldown = 60 * 20;
                    MineLimit = 1;
                    MineStun = 10;

                    AiParams.MoveFromMineTime = 100;
                    AiParams.MinePlacementChance = 0.05f;
                    break;
                case TankTier.Sapphire:
                    AiParams.MeanderAngle = MathHelper.ToRadians(30);
                    AiParams.MeanderFrequency = 15;
                    AiParams.TurretMeanderFrequency = 20;
                    AiParams.TurretSpeed = 0.025f;
                    AiParams.AimOffset = 0.01f;

                    AiParams.Inaccuracy = 0.4f;

                    TankDestructionColor = Color.DeepSkyBlue;

                    AiParams.ProjectileWarinessRadius = 40;
                    AiParams.MineWarinessRadius = 70;

                    TurningSpeed = 0.15f;
                    MaximalTurn = MathHelper.PiOver2;

                    ShootStun = 20;
                    ShellCooldown = 10;
                    ShellLimit = 3;
                    ShellSpeed = 5.5f;
                    ShellType = ShellTier.Rocket;
                    RicochetCount = 0;

                    Invisible = false;
                    Stationary = false;
                    ShellHoming = new();

                    TreadPitch = 0.08f;
                    MaxSpeed = 1.4f;
                    Acceleration = 0.3f;
                    Deceleration = 0.6f;

                    MineCooldown = 1000;
                    MineLimit = 1;
                    MineStun = 0;

                    AiParams.MoveFromMineTime = 90;
                    AiParams.MinePlacementChance = 0.05f;
                    break;
                case TankTier.Ruby:
                    AiParams.MeanderAngle = MathHelper.ToRadians(30);
                    AiParams.MeanderFrequency = 10;
                    AiParams.TurretMeanderFrequency = 20;
                    AiParams.TurretSpeed = 0.025f;
                    AiParams.AimOffset = 0.05f;

                    AiParams.Inaccuracy = 0.6f;

                    TankDestructionColor = Color.IndianRed;

                    //AiParams.PursuitLevel = 0.1f;
                    //AiParams.PursuitFrequency = 30;

                    AiParams.ProjectileWarinessRadius = 50;
                    AiParams.MineWarinessRadius = 0;

                    TurningSpeed = 0.5f;
                    MaximalTurn = MathHelper.PiOver2;

                    ShootStun = 0;
                    ShellCooldown = 8;
                    ShellLimit = 10;
                    ShellSpeed = 3f;
                    ShellType = ShellTier.Standard;
                    RicochetCount = 0;

                    Invisible = false;
                    Stationary = false;
                    ShellHoming = new();

                    TreadPitch = 0.08f;
                    MaxSpeed = 1.2f;
                    Acceleration = 0.4f;
                    Deceleration = 0.6f;

                    MineCooldown = 0;
                    MineLimit = 0;
                    MineStun = 0;

                    AiParams.MoveFromMineTime = 0;
                    AiParams.MinePlacementChance = 0;

                    AiParams.BlockWarinessDistance = 30;
                    break;
                case TankTier.Citrine:
                    AiParams.MeanderAngle = MathHelper.ToRadians(30);
                    AiParams.MeanderFrequency = 30;
                    AiParams.TurretMeanderFrequency = 20;
                    AiParams.TurretSpeed = 0.035f;
                    AiParams.AimOffset = 0.3f;

                    AiParams.Inaccuracy = 0.25f;

                    TankDestructionColor = Color.Yellow;

                    AiParams.ProjectileWarinessRadius = 80;
                    AiParams.MineWarinessRadius = 140;

                    TurningSpeed = 0.08f;
                    MaximalTurn = 1.4f;

                    ShootStun = 10;
                    ShellCooldown = 60;
                    ShellLimit = 3;
                    ShellSpeed = 6f;
                    ShellType = ShellTier.Standard;
                    RicochetCount = 0;

                    Invisible = false;
                    Stationary = false;
                    ShellHoming = new();

                    TreadPitch = -0.08f;
                    MaxSpeed = 3.2f;
                    Acceleration = 0.2f;
                    Deceleration = 0.4f;

                    MineCooldown = 360;
                    MineLimit = 4;
                    MineStun = 5;

                    AiParams.MoveFromMineTime = 40;
                    AiParams.MinePlacementChance = 0.15f;

                    AiParams.ShootChance = 0.95f;

                    AiParams.BlockWarinessDistance = 60;
                    break;
                case TankTier.Amethyst:
                    AiParams.MeanderAngle = MathHelper.ToRadians(30);
                    AiParams.MeanderFrequency = 5;
                    AiParams.TurretMeanderFrequency = 15;
                    AiParams.TurretSpeed = 0.05f;
                    AiParams.AimOffset = 0.3f;

                    AiParams.Inaccuracy = 0.65f;

                    TankDestructionColor = Color.Purple;

                    AiParams.ProjectileWarinessRadius = 70;
                    AiParams.MineWarinessRadius = 140;

                    TurningSpeed = 0.1f;
                    MaximalTurn = MathHelper.PiOver2 + 0.5f;

                    ShootStun = 5;
                    ShellCooldown = 25;
                    ShellLimit = 5;
                    ShellSpeed = 3.5f; // 3.5
                    ShellType = ShellTier.Standard;
                    RicochetCount = 1; // 1

                    Invisible = false;
                    Stationary = false;
                    ShellHoming = new();

                    TreadPitch = -0.2f;
                    MaxSpeed = 2f;
                    Acceleration = 0.6f;
                    Deceleration = 0.9f;

                    MineCooldown = 360;
                    MineLimit = 3;
                    MineStun = 10;

                    AiParams.MoveFromMineTime = 100;
                    AiParams.MinePlacementChance = 0.05f;
                    break;
                case TankTier.Emerald:
                    AiParams.TurretMeanderFrequency = 20;
                    AiParams.TurretSpeed = 0.04f;
                    AiParams.AimOffset = 1f;

                    AiParams.Inaccuracy = 0.35f;

                    TankDestructionColor = Color.Green;

                    ShellCooldown = 60;
                    ShellLimit = 3;
                    ShellSpeed = 8f;
                    ShellType = ShellTier.TrailedRocket;
                    RicochetCount = 2;

                    Stationary = true;
                    Invisible = true;
                    ShellHoming = new();

                    AiParams.SmartRicochets = true;
                    break;

                case TankTier.Gold:
                    AiParams.MeanderAngle = MathHelper.ToRadians(30);
                    AiParams.MeanderFrequency = 20;
                    AiParams.TurretMeanderFrequency = 20;
                    AiParams.TurretSpeed = 0.02f;
                    AiParams.AimOffset = 0.14f;

                    AiParams.Inaccuracy = 0.4f;

                    TankDestructionColor = Color.Gold;

                    AiParams.ShootChance = 0.7f;

                    AiParams.ProjectileWarinessRadius = 80;
                    AiParams.MineWarinessRadius = 120;

                    CanLayTread = false;

                    TurningSpeed = 0.06f;
                    MaximalTurn = 1.4f;

                    ShootStun = 5;
                    ShellCooldown = 30;
                    ShellLimit = 3;
                    ShellSpeed = 4f;
                    ShellType = ShellTier.Standard;
                    RicochetCount = 1;

                    Stationary = false;
                    ShellHoming = new();

                    TreadPitch = -0.1f;
                    MaxSpeed = 0.9f;
                    Acceleration = 0.8f;
                    Deceleration = 0.5f;

                    MineCooldown = 700;
                    MineLimit = 2;
                    MineStun = 10;

                    AiParams.MoveFromMineTime = 100;
                    AiParams.MinePlacementChance = 0.01f;

                    Invisible = true;
                    break;

                case TankTier.Obsidian:
                    AiParams.MeanderAngle = MathHelper.ToRadians(30);
                    AiParams.MeanderFrequency = 20;
                    AiParams.TurretMeanderFrequency = 20;
                    AiParams.TurretSpeed = 0.05f;
                    AiParams.AimOffset = 0.18f;

                    AiParams.Inaccuracy = 0.9f;

                    TankDestructionColor = Color.Black;

                    AiParams.ProjectileWarinessRadius = 70;
                    AiParams.MineWarinessRadius = 140;

                    TurningSpeed = 0.1f;
                    MaximalTurn = MathHelper.PiOver4;

                    ShootStun = 5;
                    ShellCooldown = 25;
                    ShellLimit = 3;
                    ShellSpeed = 6f;
                    ShellType = ShellTier.Rocket;
                    RicochetCount = 2;

                    Invisible = false;
                    Stationary = false;
                    ShellHoming = new();

                    TreadPitch = -0.26f;
                    MaxSpeed = 3f;
                    Acceleration = 0.6f;
                    Deceleration = 0.8f;

                    MineCooldown = 850;
                    MineLimit = 2;
                    MineStun = 10;

                    AiParams.MoveFromMineTime = 100;
                    AiParams.MinePlacementChance = 0.1f;

                    AiParams.BlockWarinessDistance = 50;
                    AiParams.BlockReadTime = 30;
                    break;
                #endregion
                #region AdvancedMod
                case TankTier.Granite:
                    AiParams.MeanderAngle = 0.8f;
                    AiParams.MeanderFrequency = 10;
                    AiParams.TurretMeanderFrequency = 20;
                    AiParams.TurretSpeed = 0.09f;
                    AiParams.AimOffset = 0f;

                    AiParams.Inaccuracy = 0.5f;

                    AiParams.ProjectileWarinessRadius = 150;
                    AiParams.MineWarinessRadius = 90;

                    TurningSpeed = 0.3f;
                    MaximalTurn = MathHelper.PiOver4;

                    ShootStun = 60;
                    ShellCooldown = 40;
                    ShellLimit = 2;
                    ShellSpeed = 5f;
                    ShellType = ShellTier.Standard;
                    RicochetCount = 1;

                    Invisible = false;
                    Stationary = false;
                    ShellHoming = new();

                    TreadPitch = 0.07f;
                    MaxSpeed = 0.9f;
                    Acceleration = 0.3f;
                    Deceleration = 0.4f;

                    AiParams.SmartRicochets = true;
                    break;
                case TankTier.Bubblegum:
                    AiParams.MeanderAngle = MathHelper.ToRadians(30);
                    AiParams.MeanderFrequency = 10;
                    AiParams.TurretMeanderFrequency = 60;
                    AiParams.TurretSpeed = 0.045f;
                    AiParams.AimOffset = 0.04f;

                    AiParams.Inaccuracy = 0.4f;

                    AiParams.ProjectileWarinessRadius = 140;
                    AiParams.MineWarinessRadius = 140;

                    TurningSpeed = 0.1f;
                    MaximalTurn = 0.5f;

                    ShootStun = 0;
                    ShellCooldown = 15;
                    ShellLimit = 8;
                    ShellSpeed = 4f;
                    ShellType = ShellTier.Standard;
                    RicochetCount = 1;

                    Invisible = false;
                    Stationary = false;
                    ShellHoming = new();

                    TreadPitch = 0.08f;
                    MaxSpeed = 1.3f;
                    Acceleration = 0.3f;
                    Deceleration = 0.6f;

                    MineCooldown = 940;
                    MineLimit = 1;
                    MineStun = 5;

                    AiParams.MoveFromMineTime = 100;
                    AiParams.MinePlacementChance = 0.02f;
                    AiParams.ShootChance = 0.2f;
                    break;
                case TankTier.Water:
                    AiParams.MeanderAngle = 0.25f;
                    AiParams.MeanderFrequency = 15;
                    AiParams.TurretMeanderFrequency = 10;
                    AiParams.TurretSpeed = 0.03f;
                    AiParams.AimOffset = 0.08f;

                    AiParams.Inaccuracy = 0.5f;

                    AiParams.ProjectileWarinessRadius = 90;
                    AiParams.MineWarinessRadius = 150;

                    TurningSpeed = 0.2f;
                    MaximalTurn = MathHelper.PiOver4;

                    ShootStun = 20;
                    ShellCooldown = 25;
                    ShellLimit = 2;
                    ShellSpeed = 5.5f;
                    ShellType = ShellTier.Rocket;
                    RicochetCount = 1;

                    Invisible = false;
                    Stationary = false;
                    ShellHoming = new();

                    TreadPitch = 0.14f;
                    MaxSpeed = 1.7f;
                    Acceleration = 0.4f;
                    Deceleration = 0.6f;
                    break;
                case TankTier.Crimson:
                    AiParams.MeanderAngle = 0.12f;
                    AiParams.MeanderFrequency = 8;
                    AiParams.TurretMeanderFrequency = 60;
                    AiParams.TurretSpeed = 0.07f;
                    AiParams.AimOffset = 0.04f;

                    AiParams.Inaccuracy = 0.2f;

                    AiParams.ProjectileWarinessRadius = 50;
                    AiParams.MineWarinessRadius = 50;

                    TurningSpeed = 0.1f;
                    MaximalTurn = 0.5f;

                    ShootStun = 1;
                    ShellCooldown = 5;
                    ShellLimit = 5;
                    ShellSpeed = 3f;
                    ShellType = ShellTier.Standard;
                    RicochetCount = 0;

                    Invisible = false;
                    Stationary = false;
                    ShellHoming = new();

                    TreadPitch = 0.08f;
                    MaxSpeed = 1.3f;
                    Acceleration = 0.6f;
                    Deceleration = 0.8f;

                    MineCooldown = 340;
                    MineLimit = 6;
                    MineStun = 3;

                    AiParams.MoveFromMineTime = 100;
                    AiParams.MinePlacementChance = 0.02f;
                    AiParams.ShootChance = 0.2f;
                    break;
                case TankTier.Tiger:
                    AiParams.MeanderAngle = 0.30f;
                    AiParams.MeanderFrequency = 2;
                    AiParams.TurretMeanderFrequency = 40;
                    AiParams.TurretSpeed = 0.1f;
                    AiParams.AimOffset = 0.12f;

                    AiParams.Inaccuracy = 0.7f;

                    AiParams.ProjectileWarinessRadius = 90;
                    AiParams.MineWarinessRadius = 120;

                    TurningSpeed = 0.1f;
                    MaximalTurn = MathHelper.PiOver2;

                    ShootStun = 0;
                    ShellCooldown = 20;
                    ShellLimit = 4;
                    ShellSpeed = 4f;
                    ShellType = ShellTier.Standard;
                    RicochetCount = 1;

                    Invisible = false;
                    Stationary = false;
                    ShellHoming = new();

                    TreadPitch = 0.14f;
                    MaxSpeed = 2f;
                    Acceleration = 0.6f;
                    Deceleration = 0.8f;

                    MineCooldown = 1;
                    MineLimit = 10;
                    MineStun = 0;

                    AiParams.MoveFromMineTime = 100;
                    AiParams.MinePlacementChance = 0.05f;
                    break;
                case TankTier.Fade:
                    AiParams.MeanderAngle = MathHelper.ToRadians(30);
                    AiParams.MeanderFrequency = 8;
                    AiParams.TurretMeanderFrequency = 40;
                    AiParams.TurretSpeed = 0.05f;
                    AiParams.AimOffset = 0.22f;

                    AiParams.Inaccuracy = 1.1f;

                    AiParams.ProjectileWarinessRadius = 100;
                    AiParams.MineWarinessRadius = 100;

                    TurningSpeed = 0.12f;
                    MaximalTurn = MathHelper.ToRadians(30);

                    ShootStun = 5;
                    ShellCooldown = 25;
                    ShellLimit = 5;
                    ShellSpeed = 3.5f;
                    ShellType = ShellTier.Standard;
                    RicochetCount = 1;

                    Invisible = false;
                    Stationary = false;
                    ShellHoming = new();

                    TreadPitch = -0.2f;
                    MaxSpeed = 1.9f;
                    Acceleration = 0.6f;
                    Deceleration = 0.9f;

                    MineCooldown = 680;
                    MineLimit = 3;
                    MineStun = 10;

                    AiParams.MoveFromMineTime = 100;
                    AiParams.MinePlacementChance = 0.05f;
                    break;
                case TankTier.Creeper:
                    AiParams.MeanderAngle = 0.2f;
                    AiParams.MeanderFrequency = 25;
                    AiParams.TurretMeanderFrequency = 40;
                    AiParams.TurretSpeed = 0.085f;
                    AiParams.AimOffset = 1f;

                    AiParams.SmartRicochets = true;

                    AiParams.Inaccuracy = 0.6f;

                    AiParams.ProjectileWarinessRadius = 150;
                    AiParams.MineWarinessRadius = 110;

                    TurningSpeed = 0.3f;
                    MaximalTurn = MathHelper.PiOver4;

                    ShootStun = 20;
                    ShellCooldown = 60;
                    ShellLimit = 2;
                    ShellSpeed = 8f;
                    ShellType = ShellTier.TrailedRocket;
                    RicochetCount = 3;

                    Invisible = false;
                    Stationary = false;
                    ShellHoming = new();

                    TreadPitch = -0.07f;
                    MaxSpeed = 1f;
                    Acceleration = 0.3f;
                    Deceleration = 0.4f;
                    break;
                case TankTier.Gamma:
                    AiParams.TurretMeanderFrequency = 20;
                    AiParams.TurretSpeed = 0.08f;
                    AiParams.AimOffset = 0.01f;

                    AiParams.Inaccuracy = 0.15f;

                    Invisible = false;
                    ShellHoming = new();

                    ShellCooldown = 40;
                    ShellLimit = 6;
                    ShellSpeed = 12.5f;
                    ShellType = ShellTier.Standard;
                    RicochetCount = 0;

                    Stationary = true;
                    break;
                case TankTier.Marble:
                    AiParams.MeanderAngle = MathHelper.PiOver2;
                    AiParams.MeanderFrequency = 10;
                    AiParams.TurretMeanderFrequency = 20;
                    AiParams.TurretSpeed = 0.08f;
                    AiParams.AimOffset = 0.11f;

                    AiParams.ProjectileWarinessRadius = 70;
                    AiParams.MineWarinessRadius = 140;

                    TurningSpeed = 0.1f;
                    MaximalTurn = MathHelper.PiOver4;

                    AiParams.Inaccuracy = 0.6f;

                    ShootStun = 5;
                    ShellCooldown = 25;
                    ShellLimit = 3;
                    ShellSpeed = 10f;
                    ShellType = ShellTier.Rocket;
                    RicochetCount = 1;

                    TreadPitch = -0.26f;
                    MaxSpeed = 2.6f;
                    Acceleration = 0.6f;
                    Deceleration = 0.8f;

                    Stationary = false;
                    Invisible = false;
                    ShellHoming = new();

                    MineCooldown = 850;
                    MineLimit = 2;
                    MineStun = 10;

                    AiParams.MoveFromMineTime = 100;
                    AiParams.MinePlacementChance = 0.05f;
                    break;
                #endregion

                #region Special
                case TankTier.Explosive:
                    Armor = new(this, 3);
                    AiParams.MeanderAngle = MathHelper.ToRadians(30);
                    AiParams.MeanderFrequency = 10;
                    AiParams.TurretMeanderFrequency = 60;
                    AiParams.TurretSpeed = 0.045f;
                    AiParams.AimOffset = 0.04f;

                    AiParams.Inaccuracy = 1.2f;

                    AiParams.ProjectileWarinessRadius = 140;
                    AiParams.MineWarinessRadius = 140;

                    TurningSpeed = 0.1f;
                    MaximalTurn = 0.4f;

                    ShootStun = 0;
                    ShellCooldown = 90;
                    ShellLimit = 2;
                    ShellSpeed = 2f;
                    ShellType = ShellTier.Explosive;
                    RicochetCount = 0;

                    ShootPitch = -0.1f;

                    Invisible = false;
                    Stationary = false;
                    ShellHoming = new();

                    TreadPitch = -0.8f;
                    MaxSpeed = 0.8f;
                    Acceleration = 0.3f;
                    Deceleration = 0.6f;

                    MineCooldown = 940;
                    MineLimit = 1;
                    MineStun = 5;

                    AiParams.MoveFromMineTime = 100;
                    AiParams.MinePlacementChance = 0.02f;
                    AiParams.ShootChance = 0.2f;
                    break;

                case TankTier.Electro:
                    AiParams.MeanderAngle = MathHelper.ToRadians(30);
                    AiParams.MeanderFrequency = 10;
                    AiParams.TurretMeanderFrequency = 60;
                    AiParams.TurretSpeed = 0.045f;
                    AiParams.AimOffset = 0.04f;

                    AiParams.ProjectileWarinessRadius = 140;
                    AiParams.MineWarinessRadius = 140;

                    TurningSpeed = 0.1f;
                    MaximalTurn = 0.5f;

                    ShootStun = 0;
                    ShellCooldown = 15;
                    ShellLimit = 8;
                    ShellSpeed = 4f;
                    ShellType = ShellTier.Standard;
                    RicochetCount = 1;

                    Invisible = false;
                    Stationary = false;
                    ShellHoming = new();

                    TreadPitch = 0.08f;
                    MaxSpeed = 1.3f;
                    Acceleration = 0.3f;
                    Deceleration = 0.6f;

                    MineCooldown = 940;
                    MineLimit = 1;
                    MineStun = 5;

                    AiParams.MoveFromMineTime = 100;
                    AiParams.MinePlacementChance = 0.02f;
                    AiParams.ShootChance = 0.2f;
                    break;

                case TankTier.RocketDefender:
                    AiParams.MeanderAngle = MathHelper.ToRadians(30);
                    AiParams.MeanderFrequency = 10;
                    AiParams.TurretMeanderFrequency = 60;
                    AiParams.TurretSpeed = 0.045f;
                    AiParams.AimOffset = 0.04f;

                    AiParams.ProjectileWarinessRadius = 140;
                    AiParams.MineWarinessRadius = 140;

                    TurningSpeed = 0.1f;
                    MaximalTurn = 0.5f;

                    ShootStun = 0;
                    ShellCooldown = 15;
                    ShellLimit = 8;
                    ShellSpeed = 4f;
                    ShellType = ShellTier.Standard;
                    RicochetCount = 1;

                    Invisible = false;
                    Stationary = false;
                    ShellHoming = new();

                    TreadPitch = 0.08f;
                    MaxSpeed = 1.3f;
                    Acceleration = 0.3f;
                    Deceleration = 0.6f;

                    MineCooldown = 940;
                    MineLimit = 1;
                    MineStun = 5;

                    AiParams.MoveFromMineTime = 100;
                    AiParams.MinePlacementChance = 0.02f;
                    AiParams.ShootChance = 0.2f;
                    break;

                case TankTier.Assassin:
                    AiParams.MeanderAngle = MathHelper.ToRadians(40);
                    AiParams.MeanderFrequency = 15;
                    AiParams.TurretMeanderFrequency = 1;
                    AiParams.TurretSpeed = 0.1f;
                    AiParams.AimOffset = 0f;

                    AiParams.Inaccuracy = 0.25f;
                    AiParams.BounceReset = false;

                    AiParams.ProjectileWarinessRadius = 140;
                    AiParams.MineWarinessRadius = 140;

                    TurningSpeed = 0.1f;
                    MaximalTurn = 0.2f;

                    ShootStun = 25;
                    ShellCooldown = 100;
                    ShellLimit = 1;
                    ShellSpeed = 9.5f;
                    ShellType = ShellTier.Supressed;
                    RicochetCount = 1;

                    /*ShootStun = 25;
                    ShellCooldown = 5;
                    ShellLimit = 50;
                    ShellSpeed = 9.5f;
                    ShellType = ShellTier.Supressed;
                    RicochetCount = 1;*/

                    Invisible = false;
                    Stationary = false;
                    ShellHoming = new();

                    TreadPitch = -0.4f;
                    MaxSpeed = 1.2f;
                    Acceleration = 0.3f;
                    Deceleration = 0.6f;

                    MineCooldown = 0;
                    MineLimit = 0;
                    MineStun = 0;

                    AiParams.MoveFromMineTime = 100;
                    AiParams.MinePlacementChance = 0.02f;
                    AiParams.ShootChance = 0.2f;

                    AiParams.SmartRicochets = true;
                    break;

                case TankTier.Commando:
                    Armor = new(this, 3);
                    AiParams.MeanderAngle = MathHelper.ToRadians(30);
                    AiParams.MeanderFrequency = 10;
                    AiParams.TurretMeanderFrequency = 15;
                    AiParams.TurretSpeed = 0.05f;
                    AiParams.AimOffset = 0.03f;
                    AiParams.Inaccuracy = MathHelper.ToRadians(10);

                    AiParams.ProjectileWarinessRadius = 140;
                    AiParams.MineWarinessRadius = 140;

                    TurningSpeed = 0.05f;
                    MaximalTurn = MathHelper.ToRadians(20);

                    ShootStun = 25;
                    ShellCooldown = 50;
                    ShellLimit = 1;
                    ShellSpeed = 6f;
                    ShellType = ShellTier.TrailedRocket;
                    RicochetCount = 0;

                    Invisible = false;
                    Stationary = false;
                    ShellHoming = new();

                    TreadPitch = -0.08f;
                    MaxSpeed = 1.4f;
                    Acceleration = 0.3f;
                    Deceleration = 0.6f;

                    MineCooldown = 0;
                    MineLimit = 0;
                    MineStun = 0;

                    AiParams.MoveFromMineTime = 100;
                    AiParams.MinePlacementChance = 0.02f;
                    AiParams.ShootChance = 0.2f;
                    break;
                    #endregion
            }
            if (Difficulties.Types["TanksAreCalculators"])
                if (RicochetCount >= 1)
                    if (HasTurret)
                        AiParams.SmartRicochets = true;

            if (Difficulties.Types["UltraMines"])
                AiParams.MineWarinessRadius *= 3;

            if (Difficulties.Types["AllInvisible"])
            {
                Invisible = true;
                CanLayTread = false;
            }
            if (Difficulties.Types["AllStationary"])
                Stationary = true;

            if (Difficulties.Types["AllHoming"])
            {
                ShellHoming = new();
                ShellHoming.radius = 200f;
                ShellHoming.speed = ShellSpeed;
                ShellHoming.power = 0.1f * ShellSpeed;
                // ShellHoming.isHeatSeeking = true;

                AiParams.Inaccuracy *= 4;
            }

            if (Difficulties.Types["Armored"])
            {
                if (Armor is null)
                    Armor = new(this, 3);
                else
                    Armor = new(this, Armor.HitPoints + 3);
            }

            /*foreach (var aifld in AiParams.GetType().GetProperties())
                if (aifld.GetValue(AiParams) is int)
                    aifld.SetValue(AiParams, GameHandler.GameRand.Next(1, 60));
                else if (aifld.GetValue(AiParams) is float)
                    aifld.SetValue(AiParams, GameHandler.GameRand.NextFloat(0.01f, 2f));
                else if (aifld.GetValue(AiParams) is bool)
                    aifld.SetValue(AiParams, GameHandler.GameRand.Next(0, 2) == 0);
            foreach (var fld in GetType().GetProperties())
            {
                if (fld.SetMethod != null && fld == typeof(Enum) && !fld.Name.ToLower().Contains("behavior") && !fld.Name.Contains("Id"))
                {
                    if (fld.GetValue(this) is int)
                        fld.SetValue(this, GameHandler.GameRand.Next(1, 60));
                    else if (fld.GetValue(this) is float)
                        fld.SetValue(this, GameHandler.GameRand.NextFloat(0.01f, 60));
                    else if (fld.GetValue(this) is bool && fld.Name != "Dead")
                        fld.SetValue(this, GameHandler.GameRand.Next(0, 2) == 0);
                }
            }*/
        }
        public override void Update()
        {
            base.Update();

            CannonMesh.ParentBone.Transform = Matrix.CreateRotationY(TurretRotation + TankRotation);

            if (tier == TankTier.Commando)
            {
                Model.Meshes["Laser_Beam"].ParentBone.Transform = Matrix.CreateRotationY(TurretRotation + TankRotation);
                Model.Meshes["Barrel_Laser"].ParentBone.Transform = Matrix.CreateRotationY(TurretRotation + TankRotation);
                Model.Meshes["Dish"].ParentBone.Transform = Matrix.CreateRotationY(TurretRotation + TankRotation);
            }

            if (!Dead && IsIngame)
            {

                // TargetTankRotation = (GeometryUtils.ConvertWorldToScreen(Vector3.Zero, World, View, Projection) - GameUtils.MousePosition).ToRotation() - MathHelper.PiOver2;

                timeSinceLastAction++;

                if (!GameHandler.InMission || IntermissionSystem.IsAwaitingNewMission)
                {
                    Velocity = Vector2.Zero;
                }
                else
                    DoAi(true, true, true);
            }

            oldPosition = Position;

            Model.Root.Transform = World;

            Model.CopyAbsoluteBoneTransformsTo(boneTransforms);
        }

        public override void Remove()
        {
            Dead = true;
            Behaviors = null;
            SpecialBehaviors = null;
            GameHandler.AllAITanks[AITankId] = null;
            GameHandler.AllTanks[WorldId] = null;
            base.Remove();
        }

        public override void LayMine()
        {
            base.LayMine();
        }
        public override void Damage(TankHurtContext context)
        {
            base.Damage(context);
        }
        public override void Destroy(TankHurtContext context)
        {
            if (context == TankHurtContext.ByPlayerBullet || context == TankHurtContext.ByPlayerMine)
                PlayerTank.TanksKilledThisCampaign++;
            GameHandler.AllAITanks[AITankId] = null;
            GameHandler.AllTanks[WorldId] = null;
            base.Destroy(context);
        }
        public override void Shoot()
        {
            base.Shoot();
        }

        public override void LayFootprint(bool alt)
        {
            base.LayFootprint(alt);
        }

        public bool pathBlocked;

        public bool isEnemySpotted;

        private bool seeks;
        private float seekRotation = 0;

        public bool nearDestructibleObstacle;

        // make a new method for just any rectangle
        private List<Tank> GetTanksInPath(Vector2 pathDir, out Vector2 rayEndpoint, bool draw = false, Vector2 offset = default, float missDist = 0f, Func<Block, bool> pattern = null, bool doBounceReset = true)
        {
            rayEndpoint = new(-999999, -999999);
            List<Tank> tanks = new();
            if (pattern is null)
                pattern = (c) => c.IsSolid || c.Type == Block.BlockType.Teleporter;

            const int MAX_PATH_UNITS = 1000;
            const int PATH_UNIT_LENGTH = 8;

            // 20, 30

            var whitePixel = GameResources.GetGameResource<Texture2D>("Assets/textures/WhitePixel");
            var pathPos = Position + offset.RotatedByRadians(-TurretRotation);

            pathDir.Y *= -1;
            pathDir *= PATH_UNIT_LENGTH;
            int pathRicochetCount = 0;

            int uninterruptedIterations = 0;

            bool goneThroughTeleporter = false;
            int tpidx = -1;
            Vector2 tpos = Vector2.Zero;

            for (int i = 0; i < MAX_PATH_UNITS; i++)
            {
                var dummyPos = Vector2.Zero;

                uninterruptedIterations++;

                if (pathPos.X < MapRenderer.MIN_X || pathPos.X > MapRenderer.MAX_X)
                {
                    pathDir.X *= -1;
                    pathRicochetCount++;
                    resetIterations();
                }
                if (pathPos.Y < MapRenderer.MIN_Y || pathPos.Y > MapRenderer.MAX_Y)
                {
                    pathDir.Y *= -1;
                    pathRicochetCount++;
                    resetIterations();
                }

                var pathHitbox = new Rectangle((int)pathPos.X - 2, (int)pathPos.Y - 2, 4, 4);

                // Why is velocity passed by reference here lol
                Collision.HandleCollisionSimple_ForBlocks(pathHitbox, pathDir, ref dummyPos, out var dir, out var block, out bool corner, false, pattern);
                if (corner)
                    return tanks;

                if (block is not null)
                {
                    if (block.Type == Block.BlockType.Teleporter)
                    {
                        if (!goneThroughTeleporter)
                        {
                            var otherTp = Block.AllBlocks.FirstOrDefault(bl => bl != null && bl != block && bl.TpLink == block.TpLink);

                            if (Array.IndexOf(Block.AllBlocks, otherTp) > -1)
                            {
                                //pathPos = otherTp.Position;
                                tpos = otherTp.Position;
                                goneThroughTeleporter = true;
                                tpidx = i + 1;
                            }
                        }
                    }
                    else
                    {
                        switch (dir)
                        {
                            case CollisionDirection.Up:
                            case CollisionDirection.Down:
                                pathDir.Y *= -1;
                                pathRicochetCount++;
                                resetIterations();
                                break;
                            case CollisionDirection.Left:
                            case CollisionDirection.Right:
                                pathDir.X *= -1;
                                pathRicochetCount++;
                                resetIterations();
                                break;
                        }
                    }
                }

                if (goneThroughTeleporter && i == tpidx)
                {
                    pathPos = tpos;
                }

                void resetIterations() { if (doBounceReset) uninterruptedIterations = 0; }

                if (i == 0 && Block.AllBlocks.Any(x => x is not null && x.Hitbox.Intersects(pathHitbox) && pattern is not null ? pattern.Invoke(x) : false))
                {
                    rayEndpoint = pathPos;
                    return tanks;
                }

                if (i < (int)ShellSpeed / 2 && pathRicochetCount > 0)
                {
                    rayEndpoint = pathPos;
                    return tanks;
                }

                if (pathRicochetCount > RicochetCount)
                {
                    rayEndpoint = pathPos;
                    return tanks;
                }

                pathPos += pathDir;
                var realMiss = 1f + (missDist * uninterruptedIterations);
                if (draw)
                {
                    var pathPosScreen = GeometryUtils.ConvertWorldToScreen(Vector3.Zero, Matrix.CreateTranslation(pathPos.X, 11, pathPos.Y), TankGame.GameView, TankGame.GameProjection);
                    TankGame.spriteBatch.Draw(whitePixel, pathPosScreen, null, Color.White * 0.9f, 0, whitePixel.Size() / 2, /*2 + (float)Math.Sin(i * Math.PI / 5 - TankGame.GameUpdateTime * 0.1f) * */realMiss, default, default);
                    DebugUtils.DrawDebugString(TankGame.spriteBatch, $"{goneThroughTeleporter}:{(block is not null ? $"{block.Type}" : "N/A")}", GeometryUtils.ConvertWorldToScreen(new Vector3(0, 11, 0), Matrix.CreateTranslation(pathPos.X, 0, pathPos.Y), View, Projection), 1, centered: true);
                }

                foreach (var enemy in GameHandler.AllTanks)
                    if (enemy is not null)
                    {
                        if (!tanks.Contains(enemy))
                        {
                            if (i > 15)
                            {
                                if (Vector2.Distance(enemy.Position, pathPos) <= realMiss)
                                    tanks.Add(enemy);
                            }
                            else if (enemy.CollisionBox2D.Intersects(pathHitbox))
                            {
                                tanks.Add(enemy);
                            }
                        }
                    }

            }
            return tanks;
        }

        private bool IsObstacleInWay(int checkDist, Vector2 pathDir, out Vector2 endpoint, out Vector2[] reflectPoints, bool draw = false)
        {
            const int PATH_UNIT_LENGTH = 1;

            bool hasCollided = false;

            var list = new List<Vector2>();

            // 20, 30

            var whitePixel = GameResources.GetGameResource<Texture2D>("Assets/textures/WhitePixel");
            var pathPos = Position + Vector2.Zero.RotatedByRadians(-TurretRotation);

            pathDir.Y *= -1;
            pathDir *= PATH_UNIT_LENGTH;

            for (int i = 0; i < checkDist; i++)
            {
                var dummyPos = Vector2.Zero;

                if (pathPos.X < MapRenderer.MIN_X || pathPos.X > MapRenderer.MAX_X)
                {
                    pathDir.X *= -1;
                    hasCollided = true;
                    list.Add(pathPos);
                }
                if (pathPos.Y < MapRenderer.MIN_Y || pathPos.Y > MapRenderer.MAX_Y)
                {
                    pathDir.Y *= -1;
                    hasCollided = true;
                    list.Add(pathPos);
                }

                var pathHitbox = new Rectangle((int)pathPos.X, (int)pathPos.Y, 1, 1);

                // Why is velocity passed by reference here lol
                Collision.HandleCollisionSimple_ForBlocks(pathHitbox, pathDir, ref dummyPos, out var dir, out var block, out bool corner, false, null);

                switch (dir)
                {
                    case CollisionDirection.Up:
                    case CollisionDirection.Down:
                        hasCollided = true;
                        pathDir.Y *= -1;
                        list.Add(pathPos);
                        break;
                    case CollisionDirection.Left:
                    case CollisionDirection.Right:
                        pathDir.X *= -1;
                        hasCollided = true;
                        list.Add(pathPos);
                        break;
                }

                pathPos += pathDir;

                if (draw)
                {
                    var pathPosScreen = GeometryUtils.ConvertWorldToScreen(Vector3.Zero, Matrix.CreateTranslation(pathPos.X, 11, pathPos.Y), TankGame.GameView, TankGame.GameProjection);
                    TankGame.spriteBatch.Draw(whitePixel, pathPosScreen, null, Color.White, 0, whitePixel.Size() / 2, 2 + (float)Math.Sin(i * Math.PI / 5 - TankGame.GameUpdateTime * 0.3f), default, default);
                }
            }
            reflectPoints = list.ToArray();
            endpoint = pathDir;
            return hasCollided;
        }

        public void DoAi(bool doMoveTowards = true, bool doMovements = true, bool doFire = true)
        {
            if (GameHandler.InMission)
            {
                for (int i = 0; i < Behaviors.Length; i++)
                    Behaviors[i].Value++;

                var treadPlaceTimer = (int)Math.Round(14 / Velocity.Length()) != 0 ? (int)Math.Round(14 / Velocity.Length()) : 1;

                if (Position - oldPosition != Vector2.Zero && !Stationary)
                {
                    if (TankGame.GameUpdateTime % MathHelper.Clamp(treadPlaceTimer / 2, 4, 6) == 0)
                    {
                        var treadPlace = GameResources.GetGameResource<SoundEffect>($"Assets/sounds/tnk_tread_place_{GameHandler.GameRand.Next(1, 5)}");
                        var sfx = SoundPlayer.PlaySoundInstance(treadPlace, SoundContext.Effect, 0.05f);
                        sfx.Pitch = TreadPitch;
                    }

                    if (TankGame.GameUpdateTime % treadPlaceTimer == 0)
                        LayFootprint(tier == TankTier.White ? true : false);
                }
                enactBehavior = () =>
                {
                    var enemy = GameHandler.AllTanks.FirstOrDefault(tnk => tnk is not null && !tnk.Dead && (tnk.Team != Team || tnk.Team == TankTeam.NoTeam) && tnk != this);

                    foreach (var tank in GameHandler.AllTanks)
                    {
                        if (tank is not null && !tank.Dead && (tank.Team != Team || tank.Team == TankTeam.NoTeam) && tank != this)
                            if (Vector2.Distance(tank.Position, Position) < Vector2.Distance(enemy.Position, Position))
                                if ((tank.Invisible && tank.timeSinceLastAction < 60) || !tank.Invisible)
                                    enemy = tank;
                    }

                    var tanksNearMe = new List<Tank>();
                    var cubesNearMe = new List<Block>();

                    foreach (var tank in GameHandler.AllTanks)
                        if (tank != this && tank is not null && !tank.Dead && Vector2.Distance(tank.Position, Position) <= AiParams.TeammateTankWariness)
                            tanksNearMe.Add(tank);

                    foreach (var block in Block.AllBlocks)
                        if (block is not null && Vector2.Distance(Position, block.Position) < AiParams.BlockWarinessDistance)
                            cubesNearMe.Add(block);

                    #region TurretHandle

                    TargetTurretRotation %= MathHelper.TwoPi;

                    TurretRotation %= MathHelper.TwoPi;

                    var diff = TargetTurretRotation - TurretRotation;

                    if (diff > MathHelper.Pi)
                        TargetTurretRotation -= MathHelper.TwoPi;
                    else if (diff < -MathHelper.Pi)
                        TargetTurretRotation += MathHelper.TwoPi;

                    TurretRotation = GameUtils.RoughStep(TurretRotation, TargetTurretRotation, seeks ? AiParams.TurretSpeed * 3 : AiParams.TurretSpeed);
                    bool targetExists = Array.IndexOf(GameHandler.AllTanks, enemy) > -1 && enemy is not null;
                    if (targetExists)
                    {
                        if (!seeks)
                        {
                            if (Behaviors[1].IsModOf(AiParams.TurretMeanderFrequency))
                            {
                                isEnemySpotted = false;
                                if (enemy.Invisible && enemy.timeSinceLastAction < 60)
                                {
                                    aimTarget = enemy.Position.ExpandZ();
                                    isEnemySpotted = true;
                                }

                                if (!enemy.Invisible)
                                {
                                    aimTarget = enemy.Position.ExpandZ();
                                    isEnemySpotted = true;
                                }

                                var dirVec = Position - aimTarget.FlattenZ();
                                TargetTurretRotation = -dirVec.ToRotation() - MathHelper.PiOver2 + GameHandler.GameRand.NextFloat(-AiParams.AimOffset, AiParams.AimOffset);
                            }
                        }

                        if (doFire)
                        {
                            SeesTarget = false;

                            bool tooCloseToExplosiveShell = false;

                            List<Tank> tanksDef;

                            if (ShellType == ShellTier.Explosive)
                            {
                                tanksDef = GetTanksInPath(Vector2.UnitY.RotatedByRadians(TurretRotation - MathHelper.Pi), out var rayEndpoint, offset: Vector2.UnitY * 20, pattern: x => (!x.IsDestructible && x.IsSolid) || x.Type == Block.BlockType.Teleporter, missDist: AiParams.Inaccuracy, doBounceReset: AiParams.BounceReset);
                                if (Vector2.Distance(rayEndpoint, Position) < 150f) // TODO: change from hardcode to normalcode :YES:
                                    tooCloseToExplosiveShell = true;
                            }
                            else
                                tanksDef = GetTanksInPath(Vector2.UnitY.RotatedByRadians(TurretRotation - MathHelper.Pi), out var rayEndpoint, offset: Vector2.UnitY * 20, missDist: AiParams.Inaccuracy, doBounceReset: AiParams.BounceReset);

                            var findsEnemy = tanksDef.Any(tnk => tnk is not null && (tnk.Team != Team || tnk.Team == TankTeam.NoTeam));
                            var findsSelf = tanksDef.Any(tnk => tnk is not null && tnk == this);
                            var findsFriendly = tanksDef.Any(tnk => tnk is not null && (tnk.Team == Team && tnk.Team != TankTeam.NoTeam));

                            if (findsEnemy && isEnemySpotted && !tooCloseToExplosiveShell)
                                SeesTarget = true;

                            // ChatSystem.SendMessage($"tier: {tier} | enemy: {findsEnemy} | self: {findsSelf} | friendly: {findsFriendly} | Count: {tanksDef.Count}", Color.White);

                            if (AiParams.SmartRicochets)
                            {
                                //if (!seeks)
                                seekRotation += AiParams.TurretSpeed;
                                var canShoot = !(CurShootCooldown > 0 || OwnedShellCount >= ShellLimit);
                                if (canShoot)
                                {
                                    var tanks = GetTanksInPath(Vector2.UnitY.RotatedByRadians(seekRotation), out var rayEndpoint, false, default, AiParams.Inaccuracy, doBounceReset: AiParams.BounceReset);

                                    var findsEnemy2 = tanks.Any(tnk => tnk is not null && (tnk.Team != Team || tnk.Team == TankTeam.NoTeam) && tnk != this);
                                    // var findsSelf2 = tanks.Any(tnk => tnk is not null && tnk == this);
                                    var findsFriendly2 = tanks.Any(tnk => tnk is not null && (tnk.Team == Team && tnk.Team != TankTeam.NoTeam));
                                    // ChatSystem.SendMessage($"{findsEnemy2} {findsFriendly2} | seek: {seeks}", Color.White);
                                    if (findsEnemy2/* && !findsFriendly2*/)
                                    {
                                        seeks = true;
                                        TargetTurretRotation = seekRotation - MathHelper.Pi;
                                    }
                                }

                                if (TurretRotation == TargetTurretRotation || !canShoot)
                                    seeks = false;
                            }

                            bool checkNoTeam = Team == TankTeam.NoTeam ? true : !tanksNearMe.Any(x => x.Team == Team);

                            if (SeesTarget && checkNoTeam && !findsSelf && !findsFriendly)
                                if (CurShootCooldown <= 0)
                                    Shoot();
                        }
                    }

                    #endregion
                    if (doMovements)
                    {
                        if (Stationary)
                            return;

                        bool isBulletNear = TryGetShellNear(AiParams.ProjectileWarinessRadius, out var shell);
                        bool isMineNear = TryGetMineNear(AiParams.MineWarinessRadius, out var mine);

                        #region CubeNav

                        if (Behaviors[2].IsModOf(AiParams.BlockReadTime) && !isMineNear && !isBulletNear)
                        {
                            pathBlocked = IsObstacleInWay(AiParams.BlockWarinessDistance, Vector2.UnitY.RotatedByRadians(-TargetTankRotation), out var travelPath, out var refPoints);
                            if (pathBlocked)
                            {
                                if (refPoints.Length > 0)
                                {
                                    // float AngleSmoothStep(float angle, float target, float amount) => GameUtils.AngleLerp(angle, target, amount * amount * (3f - 2f * amount));
                                    // why does this never work no matter what i do
                                    var refAngle = GameUtils.DirectionOf(Position, travelPath - new Vector2(400, 0)).ToRotation();

                                    // AngleSmoothStep(TargetTankRotation, refAngle, refAngle / 3);
                                    GameUtils.RoughStep(ref TargetTankRotation, refAngle, refAngle / 6);
                                }
                            }

                            // TODO: i literally do not understand this
                        }

                        #endregion

                        #region GeneralMovement

                        if (!isMineNear && !isBulletNear && !IsTurning && CurMineStun <= 0 && CurShootStun <= 0)
                        {
                            if (!pathBlocked)
                            {
                                if (Behaviors[0].IsModOf(AiParams.MeanderFrequency))
                                {
                                    float dir = -100;

                                    if (targetExists)
                                        dir = GameUtils.DirectionOf(Position, enemy.Position).ToRotation();

                                    var random = GameHandler.GameRand.NextFloat(-AiParams.MeanderAngle / 2, AiParams.MeanderAngle / 2);

                                    TargetTankRotation += random;
                                }
                                if (targetExists)
                                {
                                    if (AiParams.PursuitFrequency != 0)
                                    {
                                        if (Behaviors[0].IsModOf(AiParams.PursuitFrequency))
                                        {
                                            float dir = -100;

                                            if (targetExists)
                                                dir = GameUtils.DirectionOf(Position, enemy.Position).ToRotation();

                                            var random = GameHandler.GameRand.NextFloat(-AiParams.MeanderAngle / 2, AiParams.MeanderAngle / 2);

                                            var meanderRandom = dir != -100 ? random + (dir + MathHelper.PiOver2) + (0.2f * AiParams.PursuitLevel) : random;

                                            TargetTankRotation = meanderRandom;
                                        }
                                    }
                                }
                            }
                        }
                        #endregion

                        #region ShellAvoidance

                        var indif = 1;

                        if (Behaviors[6].IsModOf(indif) && !isMineNear)
                        {
                            if (isBulletNear)
                            {
                                if (shell is not null)
                                {
                                    if (shell.Owner == this)
                                    {
                                        if (shell.LifeTime > 30)
                                        {
                                            var dire = Vector2.UnitY.RotatedByRadians(shell.Position2D.DirectionOf(Position, false).ToRotation());

                                            TargetTankRotation = dire.ToRotation();
                                        }
                                    }
                                    else
                                    {
                                        var direction = Vector2.UnitY.RotatedByRadians(shell.Position2D.DirectionOf(Position, false).ToRotation());

                                        TargetTankRotation = direction.ToRotation();
                                    }
                                }
                            }
                        }

                        #endregion

                        #region MineHandle / MineAvoidance
                        if (MineLimit > 0)
                        {
                            if (Behaviors[4].IsModOf(60))
                            {
                                if (!tanksNearMe.Any(x => x.Team == Team))
                                {
                                    nearDestructibleObstacle = cubesNearMe.Any(c => c.IsDestructible);
                                    if (AiParams.SmartMineLaying)
                                    {
                                        if (nearDestructibleObstacle)
                                        {
                                            TargetTankRotation = new Vector2(100, 100).RotatedByRadians(GameHandler.GameRand.NextFloat(0, MathHelper.TwoPi)).ExpandZ().ToRotation();
                                            LayMine();
                                        }
                                    }
                                    else
                                    {
                                        if (GameHandler.GameRand.NextFloat(0, 1) <= AiParams.MinePlacementChance)
                                        {
                                            TargetTankRotation = new Vector2(100, 100).RotatedByRadians(GameHandler.GameRand.NextFloat(0, MathHelper.TwoPi)).ExpandZ().ToRotation();
                                            LayMine();
                                        }
                                    }
                                }
                            }
                        }
                        if (isMineNear)
                        {
                            if (Behaviors[5].IsModOf(10))
                            {
                                var direction = Vector2.UnitY.RotatedByRadians(mine.Position.DirectionOf(Position, false).ToRotation());

                                TargetTankRotation = direction.ToRotation();
                            }
                        }
                        #endregion

                    }

                    #region Special Tank Behavior

                    if (tier == TankTier.Creeper)
                    {
                        if (Array.IndexOf(GameHandler.AllTanks, enemy) > -1 && enemy is not null)
                        {
                            float explosionDist = 90f;
                            if (Vector2.Distance(enemy.Position, Position) < explosionDist)
                            {
                                Destroy(TankHurtContext.ByExplosion);

                                new Explosion(Position, 10f, this, 0.2f);
                            }
                        }
                    }

                    if (tier == TankTier.Commando)
                    {
                        SpecialBehaviors[0].Value++;
                        if (SpecialBehaviors[0].Value > 500)
                        {
                            SpecialBehaviors[0].Value = 0;

                            var crate = Crate.SpawnCrate(CubeMapPosition.Convert3D(new CubeMapPosition(GameHandler.GameRand.Next(0, CubeMapPosition.MAP_WIDTH),
                                GameHandler.GameRand.Next(0, CubeMapPosition.MAP_HEIGHT))) + new Vector3(0, 500, 0), 2f);
                            crate.TankToSpawn = new TankTemplate()
                            {
                                AiTier = PickRandomTier(),
                                IsPlayer = false,
                                Team = Team
                            };

                            /*foreach (var mesh in Model.Meshes)
                            {
                                foreach (BasicEffect effect in mesh.Effects)
                                {
                                    if (mesh.Name == "Dish")
                                    {
                                        effect.AmbientLightColor = Color.Red.ToVector3();
                                        effect.SpecularColor = Color.Red.ToVector3();
                                        effect.DiffuseColor = Color.Red.ToVector3();
                                        effect.FogColor = Color.Red.ToVector3();
                                        effect.EmissiveColor = Color.Red.ToVector3();
                                    }
                                }
                            }*/
                        }
                    }

                    #endregion

                    #region TankRotation

                    var targ = TargetTankRotation + MathHelper.Pi;

                    if (doMoveTowards)
                    {
                        if (IsTurning)
                        {
                            var real = TankRotation + MathHelper.PiOver2;
                            if (targ - TankRotation >= MathHelper.PiOver2)
                                TankRotation += MathHelper.Pi;
                            else if (targ - TankRotation <= -MathHelper.PiOver2)
                                TankRotation -= MathHelper.Pi;
                        }

                        if (TankRotation > targ - MaximalTurn && TankRotation < targ + MaximalTurn)
                        {
                            IsTurning = false;
                            Speed += Acceleration;
                            if (Speed > MaxSpeed)
                                Speed = MaxSpeed;
                            // TankRotation = targ;

                            // velocity *= MaxSpeed;
                        }
                        else
                        {
                            Speed -= Deceleration;
                            if (Speed < 0)
                                Speed = 0;
                            treadPlaceTimer = (int)Math.Round(14 / TurningSpeed) != 0 ? (int)Math.Round(14 / TurningSpeed) : 1;
                            if (TankGame.GameUpdateTime % treadPlaceTimer == 0)
                            {
                                LayFootprint(tier == TankTier.White);
                            }
                            IsTurning = true;
                            Velocity = Vector2.Zero;
                        }

                        var dir = Vector2.UnitY.RotatedByRadians(TankRotation);
                        Velocity.X = dir.X;
                        Velocity.Y = dir.Y;

                        Velocity.Normalize();

                        Velocity *= Speed;
                        TankRotation = GameUtils.RoughStep(TankRotation, targ, TurningSpeed);
                    }

                    #endregion
                };
            }
            enactBehavior?.Invoke();
        }
        private void RenderModel()
        {
            foreach (ModelMesh mesh in Model.Meshes)
            {
                foreach (BasicEffect effect in mesh.Effects)
                {
                    effect.World = boneTransforms[mesh.ParentBone.Index];
                    effect.View = View;
                    effect.Projection = Projection;

                    effect.TextureEnabled = true;

                    if (!HasTurret)
                        if (mesh.Name == "Cannon")
                            return;

                    if (mesh.Name == "Shadow")
                    {
                        effect.Texture = _shadowTexture;
                        if (IsIngame)
                        {
                            effect.Alpha = 0.5f;
                            mesh.Draw();
                        }
                    }
                    else
                    {
                        if (IsHoveredByMouse)
                            effect.EmissiveColor = Color.White.ToVector3();
                        else
                            effect.EmissiveColor = Color.Black.ToVector3();

                        var tex = _tankTexture;

                        effect.Texture = tex;

                        effect.Alpha = 1;
                        mesh.Draw();
                        /*var ex = new Color[1024];

                        Array.Fill(ex, new Color(GameHandler.GameRand.Next(0, 256), GameHandler.GameRand.Next(0, 256), GameHandler.GameRand.Next(0, 256)));

                        effect.Texture.SetData(0, new Rectangle(0, 8, 32, 15), ex, 0, 480);*/

                        /*if (Team != Team.NoTeam)
                        {
                            var ex = new Color[1024];

                            Array.Fill(ex, (Color)typeof(Color).GetProperty(Team.ToString(), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public).GetValue(null));

                            tex.SetData(0, new Rectangle(0, 0, 32, 9), ex, 0, 288);
                            tex.SetData(0, new Rectangle(0, 23, 32, 9), ex, 0, 288);
                        }*/
                        // removed temporarily
                    }

                    effect.SetDefaultGameLighting_IngameEntities();
                }
            }
        }
        internal void DrawBody()
        {
            if (Dead)
                return;

            if (IsIngame)
            {
                if (DebugUtils.DebugLevel == 1)
                {
                    if (AiParams.SmartRicochets)
                        GetTanksInPath(Vector2.UnitY.RotatedByRadians(seekRotation), out var rayEndpoint, true, missDist: AiParams.Inaccuracy, doBounceReset: AiParams.BounceReset);
                    var poo = GetTanksInPath(Vector2.UnitY.RotatedByRadians(TurretRotation - MathHelper.Pi), out var rayEnd, true, offset: Vector2.UnitY * 20, pattern: x => x.IsSolid | x.Type == Block.BlockType.Teleporter, missDist: AiParams.Inaccuracy, doBounceReset: AiParams.BounceReset);
                    DebugUtils.DrawDebugString(TankGame.spriteBatch, $"{tier}: {poo.Count}", GeometryUtils.ConvertWorldToScreen(new Vector3(0, 11, 0), World, View, Projection), 1, centered: true);
                    if (!Stationary)
                    {
                        IsObstacleInWay(AiParams.BlockWarinessDistance, Vector2.UnitY.RotatedByRadians(-TargetTankRotation), out var travelPos, out var refPoints, true);
                        DebugUtils.DrawDebugString(TankGame.spriteBatch, "TRAVELENDPOINT", GeometryUtils.ConvertWorldToScreen(Vector3.Zero, Matrix.CreateTranslation(travelPos.X, 11, travelPos.Y), View, Projection), 1, centered: true);
                        DebugUtils.DrawDebugString(TankGame.spriteBatch, "ENDPOINT", GeometryUtils.ConvertWorldToScreen(Vector3.Zero, Matrix.CreateTranslation(rayEnd.X, 11, rayEnd.Y), View, Projection), 1, centered: true);

                        /*var pos = GameUtils.DirectionOf(Position, travelPos);
                        var rot = pos.ToRotation();
                        var posNew = new Vector2(50, 0).RotatedByRadians(rot);
                        DebugUtils.DrawDebugString(TankGame.spriteBatch, "here?",
                            GeometryUtils.ConvertWorldToScreen(new(posNew.X, 11, posNew.Y),
                            Matrix.CreateTranslation(Position.X,
                            0, Position.Y), View, Projection),
                            1, centered: true);*/

                        foreach (var pt in refPoints)
                        {
                            DebugUtils.DrawDebugString(TankGame.spriteBatch, "pt", GeometryUtils.ConvertWorldToScreen(new Vector3(0, 11, 0), Matrix.CreateTranslation(pt.X, 0, pt.Y), View, Projection), 1, centered: true);
                        }

                        DebugUtils.DrawDebugString(TankGame.spriteBatch, "end", GeometryUtils.ConvertWorldToScreen(Vector3.Zero, Matrix.CreateTranslation(GameUtils.DirectionOf(Position, travelPos).X, 0, GameUtils.DirectionOf(Position, travelPos).Y), View, Projection), 1, centered: true);
                        DebugUtils.DrawDebugString(TankGame.spriteBatch, "me", GeometryUtils.ConvertWorldToScreen(Vector3.Zero, Matrix.Identity, View, Projection/*Matrix.CreateTranslation(Position.X, 11, Position.Y), View, Projection)*/), 1, centered: true);
                        //TankGame.spriteBatch.Draw(GameResources.GetGameResource<Texture2D>("Assets/textures/WhitePixel"), new Rectangle((int)travelPos.X - 1, (int)travelPos.Y - 1, 20, 20), Color.White);
                    }
                }
            }

            //TankGame.spriteBatch.Draw(GameResources.GetGameResource<Texture2D>("Assets/textures/WhitePixel"), new Rectangle((int)Position.X - 1, (int)Position.Y - 1, 20, 20), Color.White);

            var info = new string[]
            {
                $"Team: {Team}",
                $"Armor: {(Armor != null ? Armor.HitPoints : "N/A")}",
                $"Rotation/Target: {TankRotation}/{TargetTankRotation + MathHelper.Pi}",
            };

            for (int i = 0; i < info.Length; i++)
                DebugUtils.DrawDebugString(TankGame.spriteBatch, info[i], GeometryUtils.ConvertWorldToScreen(Vector3.Zero, World, View, Projection) - new Vector2(0, (info.Length * 20) + (i * 20)), 1, centered: true);

            if (Invisible && GameHandler.InMission)
                return;

            RenderModel();
            Armor?.Render();
        }

        public bool TryGetShellNear(float distance, out Shell shell)
        {
            shell = null;

            foreach (var bullet in Shell.AllShells)
            {
                if (bullet is not null)
                {
                    if (bullet.LifeTime > 30)
                    {
                        if (Vector2.Distance(Position, bullet.Position2D) < distance)
                        {
                            var rotationTo = GameUtils.DirectionOf(Position, bullet.Position2D).ToRotation();

                            if (Math.Abs(rotationTo - TurretRotation) < MathHelper.PiOver2 /*|| Vector2.Distance(Position, bullet.Position2D) < distance / 2*/)
                            {
                                shell = bullet;
                                return true;
                            }
                            return true;
                        }
                    }
                }
            }
            return false;
        }
        public bool TryGetMineNear(float distance, out Mine mine)
        {
            mine = null;
            foreach (var yours in Mine.AllMines)
            {
                if (yours is not null)
                {
                    if (Vector2.Distance(Position, yours.Position) < distance)
                    {
                        mine = yours;
                        return true;
                    }
                }
            }
            return false;
        }

        private static readonly TankTier[] workingTiers =
        {
            TankTier.Brown, TankTier.Marine, TankTier.Yellow, TankTier.Black, TankTier.White, TankTier.Pink, TankTier.Purple, TankTier.Green, TankTier.Ash,
            TankTier.Bronze, TankTier.Silver, TankTier.Sapphire, TankTier.Ruby, TankTier.Citrine, TankTier.Amethyst, TankTier.Emerald, TankTier.Gold, TankTier.Obsidian,
            TankTier.Granite, TankTier.Bubblegum, TankTier.Water, TankTier.Crimson, TankTier.Tiger, TankTier.Creeper, TankTier.Gamma, TankTier.Marble,
            TankTier.Assassin
        };
        public static TankTier PickRandomTier()
            => workingTiers[GameHandler.GameRand.Next(0, workingTiers.Length)];
    }
}