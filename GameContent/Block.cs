using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WiiPlayTanksRemake;
using WiiPlayTanksRemake.GameContent.Systems.Coordinates;
using WiiPlayTanksRemake.Graphics;
using WiiPlayTanksRemake.Internals;
using WiiPlayTanksRemake.Internals.Common.Utilities;
using WiiPlayTanksRemake.Internals.Core.Interfaces;

namespace WiiPlayTanksRemake.GameContent
{
    /// <summary>A class that is used for obstacles for <see cref="Tank"/>s.</summary>
    public class Block : IGameSystem
    {
        public enum BlockType
        {
            Wood = 1,
            Cork = 2,
            Hole = 3
        }

        public BlockType Type { get; set; }

        public static Block[] blocks = new Block[CubeMapPosition.MAP_WIDTH * CubeMapPosition.MAP_HEIGHT];

        // public static Cube[,] cubes = new Cube[CubeMapPosition.MAP_WIDTH + 1, CubeMapPosition.MAP_HEIGHT + 1];

        public Vector3 position;

        public Model model;

        public Matrix World;
        public Matrix View;
        public Matrix Projection;

        public BoundingBox collider;

        public Rectangle collider2d;

        public Texture2D meshTexture;

        public int height;

        public const int MAX_BLOCK_HEIGHT = 7;

        public const float FULL_BLOCK_SIZE = 24.5f;
        public const float SLAB_SIZE = 13f;

        // 36, 18 respectively for normal size

        public const float FULL_SIZE = 100.8f;

        // 141 for normal

        public int worldId;

        public bool IsDestructible { get; set; }
        public bool IsSolid { get; } = true;

        public bool AffectedByOffset { get; set; } = true;

        public Block(BlockType type, int height)
        {
            meshTexture = type switch
            {
                BlockType.Wood => GameResources.GetGameResource<Texture2D>("Assets/textures/ingame/block.1"),
                BlockType.Cork => GameResources.GetGameResource<Texture2D>("Assets/textures/ingame/block.2"),
                BlockType.Hole => GameResources.GetGameResource<Texture2D>("Assets/textures/ingame/block_harf.2"),
                _ => null
            };

            switch (type)
            {
                case BlockType.Wood:
                    meshTexture = GameResources.GetGameResource<Texture2D>($"{MapRenderer.assetsRoot}block.1");
                    model = TankGame.CubeModel;
                    break;
                case BlockType.Cork:
                    IsDestructible = true;
                    meshTexture = GameResources.GetGameResource<Texture2D>($"{MapRenderer.assetsRoot}block.2");
                    model = TankGame.CubeModel;
                    break;
                case BlockType.Hole:
                    model = GameResources.GetGameResource<Model>("Assets/check");
                    IsSolid = false;
                    meshTexture = GameResources.GetGameResource<Texture2D>($"{MapRenderer.assetsRoot}block_harf.2");
                    AffectedByOffset = false;
                    break;
            }

            this.height = MathHelper.Clamp(height, 0, 7); // if 0, it will be a hole.

            Type = type;

            position = new(-1000, 0, 0);

            // TODO: Finish collisions

            int index = Array.IndexOf(blocks, blocks.First(cube => cube is null));

            worldId = index;

            blocks[index] = this;

            // cubes[position.X, position.Y] = this;

            // cubes.Add(this);
        }

        public void Destroy()
        {
            // blah blah particle chunk thingy

            blocks[worldId] = null;

            // cubes[position.X, position.Y] = null;
        }

        public void Render()
        {
            var whitePixel = GameResources.GetGameResource<Texture2D>("Assets/textures/WhitePixel");

            //var deconstruct = GeometryUtils.ConvertWorldToScreen(Vector3.Zero, World, View, Projection);
            //var rect = new Rectangle((int)deconstruct.X - collider2d.Width / 2, (int)deconstruct.Y - collider2d.Height / 2, collider2d.Width, collider2d.Height);
            //TankGame.spriteBatch.Draw(whitePixel, rect, null, Color.White, 0f, /*rect.Size.ToVector2() / 2*/ Vector2.Zero, default, 1f);

            foreach (var mesh in model.Meshes)
            {
                foreach (BasicEffect effect in mesh.Effects)
                {
                    effect.View = TankGame.GameView;
                    effect.World = World;
                    effect.Projection = TankGame.GameProjection;
                    effect.EnableDefaultLighting();

                    effect.TextureEnabled = true;
                    effect.Texture = meshTexture;

                    effect.SetDefaultGameLighting_IngameEntities();

                    effect.DirectionalLight0.Direction *= 0.1f;

                    effect.Alpha = 1f;
                }

                mesh.Draw();
            }
            // TankGame.spriteBatch.Draw(GameResources.GetGameResource<Texture2D>("Assets/textures/WhitePixel"), collider2d, Color.White * 0.75f);
        }
        public void Update()
        {
            collider2d = new((int)(position.X - FULL_BLOCK_SIZE / 2), (int)(position.Z - FULL_BLOCK_SIZE / 2), (int)FULL_BLOCK_SIZE, (int)FULL_BLOCK_SIZE);
            collider = new BoundingBox(position - new Vector3(FULL_BLOCK_SIZE / 2 + 4, FULL_SIZE, FULL_BLOCK_SIZE / 2 + 4), position + new Vector3(FULL_BLOCK_SIZE / 2 + 4, FULL_SIZE, FULL_BLOCK_SIZE / 2 + 4));
            Vector3 offset = new();

            if (AffectedByOffset)
            {
                switch (height)
                {
                    case 0:
                        offset = new(0, FULL_SIZE, 0);
                        // this thing is a hole, therefore you're mom; work on later
                        break;
                    case 1:
                        offset = new(0, FULL_SIZE - FULL_BLOCK_SIZE, 0);
                        break;
                    case 2:
                        offset = new(0, FULL_SIZE - (FULL_BLOCK_SIZE + SLAB_SIZE), 0);
                        break;
                    case 3:
                        offset = new(0, FULL_SIZE - (FULL_BLOCK_SIZE * 2 + SLAB_SIZE), 0);
                        break;
                    case 4:
                        offset = new(0, FULL_SIZE - (FULL_BLOCK_SIZE * 2 + SLAB_SIZE * 2), 0);
                        break;
                }
            }
            else
                offset.Y -= 0.05f;

            World = Matrix.CreateScale(0.7f) * Matrix.CreateTranslation(position - offset);
            Projection = TankGame.GameProjection;
            View = TankGame.GameView;
        }

        public enum CubeCollisionDirection
        {
            Up,
            Down,
            Left,
            Right
        }
    }
}