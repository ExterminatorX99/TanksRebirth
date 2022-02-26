﻿using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WiiPlayTanksRemake.Graphics;
using WiiPlayTanksRemake.Internals;
using WiiPlayTanksRemake.Internals.Common;
using WiiPlayTanksRemake.Internals.Common.Utilities;
using FontStashSharp;

namespace WiiPlayTanksRemake.GameContent.Systems.Coordinates
{
    public class PlacementSquare
    {
        public static bool displayHeights = true;

        internal static List<PlacementSquare> Placements = new();

        private Vector3 _position;

        private BoundingBox _box;

        private Model _model;

        public bool IsHovered => GameUtils.GetMouseToWorldRay().Intersects(_box).HasValue;

        private int _cubeId = -1;

        private Action<PlacementSquare> _onClick = null;

        public PlacementSquare(Vector3 position, float dimensions)
        {
            _position = position;
            _box = new(position - new Vector3(dimensions / 2, 0, dimensions / 2), position + new Vector3(dimensions / 2, 0, dimensions / 2));

            _model = GameResources.GetGameResource<Model>("Assets/check");


            Placements.Add(this);
        }
        public static void InitializeLevelEditorSquares()
        {
            for (int i = 0; i < CubeMapPosition.MAP_WIDTH; i++)
            {
                for (int j = 0; j < CubeMapPosition.MAP_HEIGHT; j++)
                {
                    new PlacementSquare(new CubeMapPosition(i, j), Block.FULL_BLOCK_SIZE)
                    {
                        _onClick = (place) =>
                        {
                            if (place._cubeId <= -1)
                            {
                                ChatSystem.SendMessage("Added!", Color.Red);
                                var cube = new Block((Block.BlockType)GameHandler.BlockType, GameHandler.CubeHeight)
                                {
                                    position = place._position
                                };
                                place._cubeId = cube.worldId;
                            }
                            else
                            {
                                ChatSystem.SendMessage("Removed!", Color.Red);
                                Block.blocks[place._cubeId] = null;
                                place._cubeId = -1;
                            }
                        }
                    };
                }
            }
        }
        // TODO: need a sound for placement
        public void Update()
        {
            if (IsHovered && Input.CanDetectClick())
                _onClick?.Invoke(this);
        }

        public void Render()
        {
            foreach (var mesh in _model.Meshes)
            {
                foreach (BasicEffect effect in mesh.Effects)
                {
                    effect.World = Matrix.CreateScale(0.768f) * Matrix.CreateTranslation(_position +  new Vector3(0, 0.1f, 0));
                    effect.View = TankGame.GameView;
                    effect.Projection = TankGame.GameProjection;

                    if (_cubeId > -1)
                        if (displayHeights && Block.blocks[_cubeId] is not null)
                            TankGame.spriteBatch.DrawString(TankGame.TextFont, $"{Block.blocks[_cubeId].height}", GeometryUtils.ConvertWorldToScreen(Vector3.Zero, effect.World, effect.View, effect.Projection), Color.White, new(1f), 0f, TankGame.TextFont.MeasureString($"{Block.blocks[_cubeId].height}") / 2);

                    effect.TextureEnabled = true;
                    effect.Texture = GameResources.GetGameResource<Texture2D>("Assets/textures/WhitePixel");
                    if (IsHovered)
                        effect.Alpha = 0.5f;
                    else
                        effect.Alpha = 0f;

                    effect.SetDefaultGameLighting_IngameEntities();
                }
                mesh.Draw();
            }
        }
    }
}
