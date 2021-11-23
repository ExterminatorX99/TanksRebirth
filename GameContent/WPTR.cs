﻿using WiiPlayTanksRemake.Internals;
using WiiPlayTanksRemake.Internals.UI;
using WiiPlayTanksRemake.Internals.Common;
using WiiPlayTanksRemake.Internals.Common.Utilities;
using WiiPlayTanksRemake.Internals.Common.GameInput;
using WiiPlayTanksRemake.Internals.Common.GameUI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System.Linq;
using WiiPlayTanksRemake.Enums;
using System;
using Microsoft.Xna.Framework.Audio;
using WiiPlayTanksRemake.GameContent.Systems;
using System.Collections.Generic;
using WiiPlayTanksRemake.Internals.Core.Interfaces;
using Microsoft.Xna.Framework.Graphics;
using WiiPlayTanksRemake.Internals.Core;

namespace WiiPlayTanksRemake.GameContent
{
    public class WPTR
    {
        public static List<AITank> AllAITanks { get; } = new();

        public static List<PlayerTank> AllPlayerTanks { get; } = new();

        public static List<Tank> AllTanks { get; } = new();

        public static float FloatForTesting;

        public static Logger BaseLogger { get; } = new($"{TankGame.ExePath}", "client_logger");

        private static UIElement lastElementClicked;

        public static bool WindowBorderless { get; set; }

        public static TankMusicSystem tankMusicHandler;

        public delegate void MissionStartEvent(List<PlayerTank> players, List<AITank> aiTanks);

        /// <summary>
        /// Fired when a mission is started.
        /// </summary>
        public static MissionStartEvent OnMissionStart;

        public static Matrix UIMatrix => Matrix.CreateOrthographicOffCenter(0, TankGame.Instance.GraphicsDevice.Viewport.Width, TankGame.Instance.GraphicsDevice.Viewport.Height, 0, -1, 1);

        internal static void Update()
        {
            tankMusicHandler.Update();

            foreach (var bind in Keybind.AllKeybinds)
                bind?.Update();

            foreach (var tank in AllPlayerTanks)
                tank.Update();
            foreach (var tank in AllAITanks)
                tank.Update();

            foreach (var mine in Mine.AllMines)
                mine?.Update();

            foreach (var bullet in Shell.AllShells)
                bullet?.Update();

            foreach (var cube in Cube.cubes)
                cube?.Update();

            FloatForTesting = MathHelper.Clamp(FloatForTesting, -1, 1);

            /*if (Input.MouseRight)
            {
                if (TankGame.GameUpdateTime % 5 == 0)
                {
                    var treadPlace = GameResources.GetGameResource<SoundEffect>($"Assets/sounds/tnk_tread_place_{new Random().Next(1, 5)}");
                    var treadPlaceSfx = treadPlace.CreateInstance();
                    treadPlaceSfx.Play();
                    treadPlaceSfx.Volume = 0.2f;
                    treadPlaceSfx.Pitch = FloatForTesting;
                }
            }*/

            if (Input.KeyJustPressed(Keys.Insert))
                DebugUtils.DebuggingEnabled = !DebugUtils.DebuggingEnabled;

            if (Input.AreKeysJustPressed(Keys.RightAlt, Keys.Enter))
            {
                WindowBorderless = !WindowBorderless;
            }

            if (Input.CurrentKeySnapshot.IsKeyDown(Keys.Left))
            {
                FloatForTesting -= 0.01f;
            }
            if (Input.CurrentKeySnapshot.IsKeyDown(Keys.Right))
            {
                FloatForTesting += 0.01f;
            }
        }
        internal static void Draw()
        {
            MapRenderer.DrawWorldModels();

            foreach (var tank in AllPlayerTanks)
               tank.DrawBody();
            foreach (var tank in AllAITanks)
                tank.DrawBody();

            foreach (var cube in Cube.cubes)
                cube?.Draw();

            foreach (var mine in Mine.AllMines)
                mine?.Draw();

            foreach (var bullet in Shell.AllShells)
                bullet?.Render();

            // TODO: Fix translation
            // TODO: Scaling with screen size.

            foreach (var element in UIElement.AllUIElements) {
                element.Position = Vector2.Transform(element.Position, UIMatrix * Matrix.CreateTranslation(element.Position.X, element.Position.Y, 0));

                element?.Draw(TankGame.spriteBatch);
            }

            DebugUtils.DrawDebugString(TankGame.spriteBatch, $"TestFloat: {FloatForTesting}" +
                $"\nHighestTier: {AITank.GetHighestTierActive()}" +
                $"\n", new(10, GameUtils.WindowHeight / 3));

            for (int i = 0; i < Enum.GetNames<TankTier>().Length; i++)
            {
                DebugUtils.DrawDebugString(TankGame.spriteBatch, $"{Enum.GetNames<TankTier>()[i]}: {AITank.GetTankCountOfType((TankTier)i)}", new(10, GameUtils.WindowHeight * 0.6f + (i * 20)));
            }

            if (TankGame.Instance.IsActive) {
                foreach (var element in UIElement.AllUIElements.ToList()) {
                    DebugUtils.DrawDebugString(TankGame.spriteBatch, element.Hitbox, new(200, 200));
                    DebugUtils.DrawDebugString(TankGame.spriteBatch, GameUtils.MousePosition, new(200, 250));
                    if (!element.MouseHovering && element.Hitbox.Contains(GameUtils.MousePosition)) {
                        element?.MouseOver();
                        element.MouseHovering = true;
                    }
                    else if (element.MouseHovering && !element.Hitbox.Contains(GameUtils.MousePosition)) {
                        element?.MouseLeave();
                        element.MouseHovering = false;
                    }
                    if (Input.MouseLeft && GameUtils.MouseOnScreenProtected && element != lastElementClicked) {
                        element?.MouseClick();
                        lastElementClicked = element;
                    }
                    if (Input.MouseRight && GameUtils.MouseOnScreenProtected && element != lastElementClicked) {
                        element?.MouseRightClick();
                        lastElementClicked = element;
                    }
                    if (Input.MouseMiddle && GameUtils.MouseOnScreenProtected && element != lastElementClicked) {
                        element?.MouseMiddleClick();
                        lastElementClicked = element;
                    }
                }
                if (!Input.MouseLeft && !Input.MouseRight && !Input.MouseMiddle) {
                    lastElementClicked = null;
                }
            }
        }
        public static PlayerTank myTank;
        public static void Initialize()
        {
            foreach (var aitank in AllAITanks)
                AllTanks.Add(aitank);
            foreach (var playertank in AllPlayerTanks)
                AllTanks.Add(playertank);

            DebugUtils.DebuggingEnabled = true;
            MapRenderer.InitializeRenderers();

            new Cube(Vector3.Zero);

            // OnMissionStart.Invoke(AllPlayerTanks, AllAITanks);
            tankMusicHandler = new();
            myTank = new PlayerTank(new Vector3(new Random().Next(-200, 201), 0, new Random().Next(-500, 600)), playerType: PlayerType.Blue);

            //for (int i = 0; i < 6; i++)
            //{
                //new AITank(new Vector3(new Random().Next(-200, 201), 0, new Random().Next(-500, 600)), /*(TankTier)new Random().Next(1, 10)*/ TankTier.Marine)
                //{
                 //   TankRotation = (float)new Random().NextDouble() * new Random().Next(1, 10)
                //};
            //}
            new AITank(new Vector3(new Random().Next(-200, 201), 0, new Random().Next(-500, 600)), TankTier.Marine)
            {
                TankRotation = (float)new Random().NextDouble() * new Random().Next(1, 10)
            };
            new AITank(new Vector3(new Random().Next(-200, 201), 0, new Random().Next(-500, 600)), TankTier.Yellow)
            {
                TankRotation = (float)new Random().NextDouble() * new Random().Next(1, 10),
                TurretRotation = (float)new Random().NextDouble() * new Random().Next(1, 10)
            };

            UI.IngameUI.Initialize();
            tankMusicHandler.LoadMusic();
            //MusicContent.green1.Play();
        }
    }
}
