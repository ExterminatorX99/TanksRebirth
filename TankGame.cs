﻿using System;
using System.IO;
using System.Text.Json;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using TanksRebirth.Internals.Common;
using TanksRebirth.Internals.Common.Utilities;
using TanksRebirth.GameContent;
using TanksRebirth.Internals;
using TanksRebirth.Internals.UI;
using TanksRebirth.Internals.Core.Interfaces;
using System.Collections.Generic;
using System.Linq;
using TanksRebirth.Internals.Common.IO;
using System.Diagnostics;
using TanksRebirth.GameContent.UI;
using TanksRebirth.Graphics;
using System.Management;
using TanksRebirth.Internals.Common.Framework.Input;
using TanksRebirth.Internals.Core;
using TanksRebirth.Localization;
using FontStashSharp;
using TanksRebirth.Internals.Common.Framework.Graphics;
using TanksRebirth.GameContent.Systems;
using TanksRebirth.Net;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Audio;

namespace TanksRebirth
{
    // TODO: Implement block once all of above things are done
    // TODO: AI in the middle to far future
    // TODO: add some finishing touches to TankMusicSystem

    public class TankGame : Game
    {
        private static string GetGPU()
        {
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                return "Unavailable: Only supported on Windows";
            using var searcher = new ManagementObjectSearcher("select * from Win32_VideoController");

            foreach (ManagementObject obj in searcher.Get())
            {
                return $"{obj["Name"]} - {obj["DriverVersion"]}";
            }
            return "Data not retrieved.";
        }

        public static string GetHardware(string hwclass, string syntax)
        {
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                return "Unavailable: Only supported on Windows";
            using var searcher = new ManagementObjectSearcher($"SELECT * FROM {hwclass}");

            foreach (var obj in searcher.Get())
            {
                return $"{obj[syntax]}";
            }
            return "Data not retrieved.";
        }

        public static Language GameLanguage = new();

        public static class MemoryParser
        {
            public static ulong FromBits(long bytes)
            {
                return (ulong)bytes * 8;
            }
            public static long FromKilobytes(long bytes)
            {
                return bytes / 1000;
            }
            public static long FromMegabytes(long bytes)
            {
                return bytes / 1000 / 1000;
            }
            public static long FromGigabytes(long bytes)
            {
                return bytes / 1000 / 1000 / 1000;
            }
            public static long FromTerabytes(long bytes)
            {
                return bytes / 1000 / 1000 / 1000 / 1000;
            }
        }

        internal static float GetInterpolatedFloat(float value)
            => value * (float)LastGameTime.ElapsedGameTime.TotalSeconds;

        public const int LevelEditorVersion = 2;

        public static readonly byte[] LevelFileHeader = { 84, 65, 78, 75 };

        public static Camera GameCamera;

        public static readonly string SysGPU = $"GPU: {GetGPU()}";
        public static readonly string SysCPU = $"CPU: {GetHardware("Win32_Processor", "Name")}";
        public static readonly string SysKeybd = $"Keyboard: {GetHardware("Win32_Keyboard", "Name")}";
        public static readonly string SysMouse = $"Mouse: {GetHardware("Win32_PointingDevice", "Name")}";

        private static Stopwatch RenderStopwatch { get; } = new();
        private static Stopwatch UpdateStopwatch { get; } = new();

        public static TimeSpan RenderTime { get; private set; }
        public static TimeSpan LogicTime { get; private set; }

        public static double LogicFPS { get; private set; }
        public static double RenderFPS { get; private set; }

        public static long GCMemory => GC.GetTotalMemory(false);
        public static long ProcessMemory
        {
            get
            {
                using Process process = Process.GetCurrentProcess();
                return process.PrivateMemorySize64;
            }
            private set { }
        }

        public static GameTime LastGameTime { get; private set; }
        public static uint GameUpdateTime { get; private set; }

        public static Texture2D WhitePixel;

        public static TankGame Instance { get; private set; }
        public static readonly string ExePath = Assembly.GetExecutingAssembly().Location.Replace(@$"\WiiPlayTanksRemake.dll", string.Empty);
        public static SpriteBatch spriteBatch;

        public readonly GraphicsDeviceManager graphics;

        private static List<IGameSystem> systems = new();

        public static GameConfig Settings;

        public JsonHandler<GameConfig> SettingsHandler;

        public static readonly string SaveDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Tanks Rebirth");

        public static Matrix GameView;
        public static Matrix GameProjection;

        private FontSystem _fontSystem;

        public static SpriteFontBase TextFont;
        public static SpriteFontBase TextFontLarge;

        public static event EventHandler<IntPtr> OnFocusLost;
        public static event EventHandler<IntPtr> OnFocusRegained;

        private bool _wasActive;

        public readonly string GameVersion;

        internal static Internals.Common.GameUI.UIPanel cunoSucksElement;

        public static OSPlatform OperatingSystem;
        public static bool IsWindows;
        public static bool IsMac;
        public static bool IsLinux;

        public TankGame() : base()
        {
            Directory.CreateDirectory(SaveDirectory);
            Directory.CreateDirectory(Path.Combine(SaveDirectory, "Texture Packs", "Scene"));
            Directory.CreateDirectory(Path.Combine(SaveDirectory, "Texture Packs", "Tank"));
            GameHandler.ClientLog = new($"{SaveDirectory}", "client");
            try
            {
                // check if platform is windows, mac, or linux
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    OperatingSystem = OSPlatform.Windows;
                    IsWindows = true;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    OperatingSystem = OSPlatform.OSX;
                    IsMac = true;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    OperatingSystem = OSPlatform.Linux;
                    IsLinux = true;
                }

                // IOUtils.SetAssociation(".mission", "MISSION_FILE", "TanksRebirth.exe", "Tanks Rebirth mission file");

                graphics = new(this) { PreferHalfPixelOffset = true };
                graphics.HardwareModeSwitch = false;

                Content.RootDirectory = "Content";
                Instance = this;
                Window.Title = "Tanks! Remake";
                Window.AllowUserResizing = true;

                IsMouseVisible = false;

                graphics.IsFullScreen = false;

                _fontSystem = new();

                GameVersion = typeof(TankGame).Assembly.GetName().Version.ToString();
            }
            catch (Exception e)
            {
                GameHandler.ClientLog.Write($"Error: {e.Message}\n{e.StackTrace}", LogType.Error);
                throw;
            }
        }

        private long _memBytes;

        protected override void Initialize()
        {
            try
            {

                GameHandler.MapEvents();

                DiscordRichPresence.Load();

                systems = ReflectionUtils.GetInheritedTypesOf<IGameSystem>(Assembly.GetExecutingAssembly());

                ResolutionHandler.Initialize(graphics);

                GameCamera = new Camera(GraphicsDevice);

                spriteBatch = new(GraphicsDevice);

                graphics.PreferMultiSampling = true;

                graphics.ApplyChanges();

                base.Initialize();
            }
            catch (Exception e)
            {
                GameHandler.ClientLog.Write($"Error: {e.Message}\n{e.StackTrace}", LogType.Error);
                throw;
            }
        }

        public static void Quit()
            => Instance.Exit();

        protected override void OnExiting(object sender, EventArgs args)
        {
            GameHandler.ClientLog.Dispose();
            SettingsHandler = new(Settings, Path.Combine(SaveDirectory, "settings.json"));
            JsonSerializerOptions opts = new()
            {
                WriteIndented = true
            };
            SettingsHandler.Serialize(opts, true);

            DiscordRichPresence.Terminate();
        }

        protected override void LoadContent()
        {
            try
            {

                var s = Stopwatch.StartNew();

                UIElement.UIPanelBackground = GameResources.GetGameResource<Texture2D>("Assets/UIPanelBackground");

                Thunder.SoftRain = GameResources.GetGameResource<SoundEffect>("Assets/sounds/ambient/soft_rain").CreateInstance();
                Thunder.SoftRain.IsLooped = true;

                OnFocusLost += TankGame_OnFocusLost;
                OnFocusRegained += TankGame_OnFocusRegained;

                WhitePixel = GameResources.GetGameResource<Texture2D>("Assets/MagicPixel");

                _fontSystem.AddFont(File.ReadAllBytes(@"Content/Assets/fonts/en_US.ttf"));
                _fontSystem.AddFont(File.ReadAllBytes(@"Content/Assets/fonts/ja_JP.ttf"));
                _fontSystem.AddFont(File.ReadAllBytes(@"Content/Assets/fonts/es_ES.ttf"));
                _fontSystem.AddFont(File.ReadAllBytes(@"Content/Assets/fonts/ru_RU.ttf"));

                TextFont = _fontSystem.GetFont(30);
                TextFontLarge = _fontSystem.GetFont(120);

                if (!File.Exists(Path.Combine(SaveDirectory, "settings.json")))
                {
                    Settings = new();
                    SettingsHandler = new(Settings, Path.Combine(SaveDirectory, "settings.json"));
                    JsonSerializerOptions opts = new()
                    {
                        WriteIndented = true
                    };
                    SettingsHandler.Serialize(opts, true);
                }
                else
                {
                    SettingsHandler = new(Settings, Path.Combine(SaveDirectory, "settings.json"));
                    Settings = SettingsHandler.DeserializeAndSet();
                }

                #region Config Initialization

                graphics.SynchronizeWithVerticalRetrace = Settings.Vsync;
                Window.IsBorderless = Settings.BorderlessWindow;
                PlayerTank.controlUp.AssignedKey = Settings.UpKeybind;
                PlayerTank.controlDown.AssignedKey = Settings.DownKeybind;
                PlayerTank.controlLeft.AssignedKey = Settings.LeftKeybind;
                PlayerTank.controlRight.AssignedKey = Settings.RightKeybind;
                PlayerTank.controlMine.AssignedKey = Settings.MineKeybind;
                MapRenderer.Theme = Settings.GameTheme;

                graphics.PreferredBackBufferWidth = Settings.ResWidth;
                graphics.PreferredBackBufferHeight = Settings.ResHeight;

                Tank.SetAssetNames();
                MapRenderer.LoadTexturePack(Settings.MapPack);
                Tank.LoadTexturePack(Settings.TankPack);
                graphics.ApplyChanges();

                Language.LoadLang(ref GameLanguage, Settings.Language);

                #endregion

                GameHandler.SetupGraphics();

                GameUI.Initialize();
                MainMenu.Initialize();

                GameHandler.ClientLog.Write($"Content loaded in {s.Elapsed}.", LogType.Debug);

                DecalSystem.Initialize(spriteBatch, GraphicsDevice);

                cunoSucksElement = new() { IsVisible = false };

                s.Stop();
            }
            catch (Exception e)
            {
                GameHandler.ClientLog.Write($"Error: {e.Message}\n{e.StackTrace}", LogType.Error);
                throw;
            }
        }

        private void TankGame_OnFocusRegained(object sender, IntPtr e)
        {
            if (Thunder.SoftRain.IsPaused())
                Thunder.SoftRain.Resume();
        }

        private void TankGame_OnFocusLost(object sender, IntPtr e)
        {
            if (Thunder.SoftRain.IsPlaying())
                Thunder.SoftRain.Pause();
        }

        public const float DEFAULT_ORTHOGRAPHIC_ANGLE = 0.75f;
        internal static Vector2 CameraRotationVector = new(0, DEFAULT_ORTHOGRAPHIC_ANGLE);

        public const float DEFAULT_ZOOM = 3.3f;
        internal static float AddativeZoom = 1f;

        internal static Vector2 CameraFocusOffset;

        internal static bool FirstPerson;

        public static bool OverheadView = false;

        private int transitionTimer;

        public static Vector2 MouseVelocity => GameUtils.GetMouseVelocity(GameUtils.WindowCenter);

        protected override void Update(GameTime gameTime)
        {
            //try
            {
                MouseRenderer.ShouldRender = TankGame.FirstPerson ? (GameUI.Paused || MainMenu.Active) : true;
                if (UIElement.delay > 0)
                    UIElement.delay--;
                UpdateStopwatch.Start();

                if (NetPlay.CurrentClient is not null)
                    Client.clientNetManager.PollEvents();
                if (NetPlay.CurrentServer is not null)
                    Server.serverNetManager.PollEvents();
                UIElement.UpdateElements();
                GameUI.UpdateButtons();

                if (GameUpdateTime % 30 == 0)
                {
                    DiscordRichPresence.Update();
                    _memBytes = ProcessMemory;
                }

                LastGameTime = gameTime;

                if (_wasActive && !IsActive)
                    OnFocusLost?.Invoke(this, Window.Handle);
                if (!_wasActive && IsActive)
                    OnFocusRegained?.Invoke(this, Window.Handle);

                if (Input.KeyJustPressed(Keys.J))
                {
                    transitionTimer = 100;
                    OverheadView = !OverheadView;
                }
                if (!FirstPerson)
                {
                    if (transitionTimer > 0)
                    {
                        transitionTimer--;
                        if (OverheadView)
                        {
                            GameUtils.SoftStep(ref CameraRotationVector.Y, MathHelper.PiOver2, 0.08f);
                            GameUtils.SoftStep(ref AddativeZoom, 0.7f, 0.08f);
                            GameUtils.RoughStep(ref CameraFocusOffset.Y, 82f, 2f);
                        }
                        else
                        {
                            GameUtils.SoftStep(ref CameraRotationVector.Y, DEFAULT_ORTHOGRAPHIC_ANGLE, 0.08f);
                            GameUtils.SoftStep(ref AddativeZoom, 1f, 0.08f);
                            GameUtils.RoughStep(ref CameraFocusOffset.Y, 0f, 2f);
                        }
                    }

                    /*GameCamera.SetPosition(new Vector3(0, 0, 350));
                    GameCamera.SetLookAt(new Vector3(0, 0, 0));
                    GameCamera.Zoom(DEFAULT_ZOOM * AddativeZoom);

                    GameCamera.RotateX(CameraRotationVector.Y);
                    GameCamera.RotateY(CameraRotationVector.X);

                    GameCamera.SetCameraType(CameraType.Orthographic);

                    GameCamera.Translate(new Vector3(CameraFocusOffset.X, -CameraFocusOffset.Y + 40, 0));

                    GameCamera.SetViewingDistances(-2000f, 5000f);*/

                    GameView =
                            Matrix.CreateScale(DEFAULT_ZOOM * AddativeZoom) *
                            Matrix.CreateLookAt(new(0f, 0, 350f), Vector3.Zero, Vector3.Up) *
                            Matrix.CreateRotationX(CameraRotationVector.Y) *
                            Matrix.CreateRotationY(CameraRotationVector.X) *
                            Matrix.CreateTranslation(CameraFocusOffset.X, -CameraFocusOffset.Y + 40, 0);
                    //Matrix.CreateTranslation(CameraFocusOffset.X, -CameraFocusOffset.Y, 0);

                    GameProjection = Matrix.CreateOrthographic(GameUtils.WindowWidth, GameUtils.WindowHeight, -2000, 5000);
                }
                else
                {
                    var pos = Vector3.Zero;
                    if (GameHandler.AllPlayerTanks.Count(x => x is not null && !x.Dead) > 0)
                    {
                        pos = GameHandler.AllPlayerTanks[0].Position.ExpandZ();
                        /*GameCamera.SetPosition(GameHandler.AllPlayerTanks[0].Position.ExpandZ());
                        GameCamera.SetLookAt(GameHandler.AllPlayerTanks[0].Position.ExpandZ());
                        GameCamera.Zoom(DEFAULT_ZOOM * AddativeZoom);

                        GameCamera.RotateX(CameraRotationVector.Y);
                        GameCamera.RotateY(CameraRotationVector.X);

                        GameCamera.SetCameraType(CameraType.Orthographic);

                        GameCamera.Translate(new Vector3(CameraFocusOffset.X, -CameraFocusOffset.Y + 40, 0));

                        GameCamera.SetViewingDistances(-2000f, 5000f);*/

                        GameView = Matrix.CreateLookAt(pos,
                            pos + new Vector3(0, 0, 20).FlattenZ().RotatedByRadians(-GameHandler.AllPlayerTanks[0].TurretRotation).ExpandZ()
                            , Vector3.Up) * Matrix.CreateScale(AddativeZoom) * Matrix.CreateRotationX(CameraRotationVector.Y - MathHelper.PiOver4) * Matrix.CreateRotationY(CameraRotationVector.X) * Matrix.CreateTranslation(0, -20, -40);

                        GameProjection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(90), GraphicsDevice.Viewport.AspectRatio, 0.1f, 1000);
                    }
                    else
                    {
                        var x = GameHandler.AllAITanks.FirstOrDefault(x => x is not null && !x.Dead);

                        if (x is not null && Array.IndexOf(GameHandler.AllAITanks, x) > -1)
                        {
                            /*pos = x.Position3D;
                            var t = GameUtils.MousePosition.X / GameUtils.WindowWidth;
                            // GameCamera.Zoom(DEFAULT_ZOOM * AddativeZoom);
                            GameCamera.SetPosition(pos - new Vector3(0, 0, 100).FlattenZ().RotatedByRadians(-x.TurretRotation).ExpandZ());

                            GameCamera.SetLookAt(pos + new Vector3(0, 0, 20).FlattenZ().RotatedByRadians(-x.TurretRotation).ExpandZ());
                            GameCamera.Zoom(GameUtils.MousePosition.X / GameUtils.WindowWidth * 5);
                            GameCamera.SetFov(90);
                            //GameCamera.SetPosition(pos);
                            //GameCamera.RotateY(GameUtils.MousePosition.X / 400);
                            //GameCamera.RotateY(DEFAULT_ORTHOGRAPHIC_ANGLE);
                            //GameCamera.RotateX(GameUtils.MousePosition.X / 400);

                            GameCamera.RotateX(CameraRotationVector.Y - MathHelper.PiOver4);
                            GameCamera.RotateY(CameraRotationVector.X);

                            GameCamera.Translate(new Vector3(0, -20, -40));

                            GameCamera.SetViewingDistances(0.1f, 10000f);

                            GameCamera.SetCameraType(CameraType.FieldOfView);*/

                            pos = x.Position.ExpandZ();

                            GameView = Matrix.CreateLookAt(pos,
                                pos + new Vector3(0, 0, 20).FlattenZ().RotatedByRadians(-x.TurretRotation).ExpandZ()
                                , Vector3.Up) * Matrix.CreateScale(AddativeZoom) * Matrix.CreateRotationX(CameraRotationVector.Y - MathHelper.PiOver4) * Matrix.CreateRotationY(CameraRotationVector.X) * Matrix.CreateTranslation(0, -20, -40) * Matrix.CreateScale(AddativeZoom);

                            GameProjection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(90), GraphicsDevice.Viewport.AspectRatio, 0.1f, 1000);
                        }
                    }
                }

                if (!GameUI.Paused && !MainMenu.Active && !OverheadView)
                {
                    if (Input.MouseRight)
                    {
                        CameraRotationVector += MouseVelocity / 500;
                    }

                    if (Input.CurrentKeySnapshot.IsKeyDown(Keys.Add))
                        AddativeZoom += 0.01f;
                    if (Input.CurrentKeySnapshot.IsKeyDown(Keys.Subtract))
                        AddativeZoom -= 0.01f;
                    //if (Input.KeyJustPressed(Keys.Q))
                    //FirstPerson = !FirstPerson;

                    FirstPerson = Difficulties.Types["ThirdPerson"];

                    if (Input.MouseMiddle)
                    {
                        CameraFocusOffset += MouseVelocity;
                    }
                    GameUtils.GetMouseVelocity(GameUtils.WindowCenter);

                    IsFixedTimeStep = !Input.CurrentKeySnapshot.IsKeyDown(Keys.Tab);
                }

                FixedUpdate(gameTime);

                //GameView = GameCamera.GetView();
                //GameProjection = GameCamera.GetProjection();

                LogicTime = UpdateStopwatch.Elapsed;

                UpdateStopwatch.Stop();

                LogicFPS = Math.Round(1f / gameTime.ElapsedGameTime.TotalSeconds);

                _wasActive = IsActive;
            }
            //catch (Exception e)
            {
                //GameHandler.ClientLog.Write($"Error: {e.Message}\n{e.StackTrace}", LogType.Error);
                //throw;
            }
        }

        public void FixedUpdate(GameTime gameTime)
        {
            GameUpdateTime++;

            GameShaders.UpdateShaders();

            Input.HandleInput();

            bool shouldUpdate = Client.IsConnected() ? true : IsActive && !GameUI.Paused;

            if (IsActive && !GameUI.Paused)
            {
                foreach (var type in systems)
                    type?.Update();

                GameHandler.UpdateAll();

                Tank.CollisionsWorld.Step(1);

                if (!MainMenu.Active && OverheadView)
                {
                    foreach (var tnk in GameHandler.AllTanks)
                    {
                        if (tnk is not null && !tnk.Dead)
                        {
                            if (GameUtils.GetMouseToWorldRay().Intersects(tnk.Worldbox).HasValue)
                            {
                                if (Input.KeyJustPressed(Keys.K))
                                {
                                    // var tnk = WPTR.AllAITanks.FirstOrDefault(tank => tank is not null && !tank.Dead && tank.tier == AITank.GetHighestTierActive());

                                    if (Array.IndexOf(GameHandler.AllTanks, tnk) > -1)
                                        tnk?.Destroy(TankHurtContext.Other);
                                }

                                if (Input.CanDetectClick(rightClick: true))
                                {
                                    tnk.TankRotation += MathHelper.PiOver2;
                                    tnk.TurretRotation += MathHelper.PiOver2;
                                    if (tnk is AITank ai)
                                    {
                                        ai.TargetTurretRotation += MathHelper.PiOver2;
                                        ai.TargetTankRotation += MathHelper.PiOver2;

                                        if (ai.TargetTurretRotation >= MathHelper.Tau)
                                            ai.TargetTurretRotation -= MathHelper.Tau;
                                        if (ai.TargetTankRotation >= MathHelper.Tau)
                                            ai.TargetTankRotation -= MathHelper.Tau;
                                    }
                                    if (tnk.TankRotation >= MathHelper.Tau)
                                        tnk.TankRotation -= MathHelper.Tau;

                                    if (tnk.TurretRotation >= MathHelper.Tau)
                                        tnk.TurretRotation -= MathHelper.Tau;
                                }

                                tnk.IsHoveredByMouse = true;
                            }
                            else
                                tnk.IsHoveredByMouse = false;
                        }
                    }
                }
            }

            foreach (var bind in Keybind.AllKeybinds)
                bind?.Update();

            foreach (var music in Music.AllMusic)
                music?.Update();

            UiScale = GameUtils.MousePosition.X / GameUtils.WindowWidth * 5;
        }

        public static Matrix Matrix2D;

        public static float UiScale = 1f;

        public static Color ClearColor = Color.Black;

        protected override void Draw(GameTime gameTime)
        {
            try
            {
                RenderStopwatch.Start();

                Matrix2D = Matrix.CreateOrthographicOffCenter(0, GameUtils.WindowWidth, GameUtils.WindowHeight, 0, -1, 1)
                    * Matrix.CreateScale(UiScale);

                GraphicsDevice.Clear(ClearColor);

                DecalSystem.UpdateRenderTarget();


                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied/*, transformMatrix: Matrix2D*/);



                if (DebugUtils.DebuggingEnabled)
                    spriteBatch.DrawString(TextFont, "Debug Level: " + DebugUtils.CurDebugLabel, new Vector2(10), Color.White, new Vector2(0.6f));
                DebugUtils.DrawDebugString(spriteBatch, $"Garbage Collection: {MemoryParser.FromMegabytes(GCMemory)} MB" +
                    $"\nProcess Memory: {MemoryParser.FromMegabytes(_memBytes)} MB", new(8, GameUtils.WindowHeight * 0.15f));
                DebugUtils.DrawDebugString(spriteBatch, $"{SysGPU}\n{SysCPU}", new(8, GameUtils.WindowHeight * 0.2f));

                GraphicsDevice.DepthStencilState = new DepthStencilState() { };

                GameHandler.RenderAll();

                spriteBatch.End();

                base.Draw(gameTime);

                spriteBatch.Begin(blendState: BlendState.AlphaBlend, effect: GameShaders.MouseShader);

                MouseRenderer.DrawMouse();

                spriteBatch.End();

                foreach (var triangle in Triangle2D.triangles)
                    triangle.DrawImmediate();
                foreach (var qu in Quad.quads)
                    qu.Render();

                RenderTime = RenderStopwatch.Elapsed;

                RenderStopwatch.Stop();
                RenderFPS = Math.Round(1f / gameTime.ElapsedGameTime.TotalSeconds);
            }
            catch (Exception e)
            {
                GameHandler.ClientLog.Write($"Error: {e.Message}\n{e.StackTrace}", LogType.Error);
                throw;
            }
        }
    }
}
