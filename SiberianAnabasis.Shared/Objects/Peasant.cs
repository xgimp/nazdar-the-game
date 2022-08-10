﻿using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SiberianAnabasis.Screens;
using SiberianAnabasis.Shared;
using static SiberianAnabasis.Enums;

namespace SiberianAnabasis.Objects
{
    public class Peasant : BasePerson
    {
        private bool isFast = false;
        private int speed = 61;
        private int centerRadius = 96;
        public Rectangle? IsBuildingHere = null;

        private Animation anim;

        private List<Animation> animations = new List<Animation>()
        {
            new Animation(Assets.Images["PeasantRight"], 4, 10),
            new Animation(Assets.Images["PeasantRight"], 4, 10),
            new Animation(Assets.Images["PeasantLeft"], 4, 10),
            new Animation(Assets.Images["PeasantLeft"], 4, 10),
        };

        public Peasant(int x, int y, Direction direction)
        {
            this.anim = this.animations[(int)Direction.Left];
            this.Hitbox = new Rectangle(x, y, this.anim.FrameWidth, this.anim.FrameHeight);
            this.Direction = direction;
            this.Health = 100;
            this.Alpha = 1f;

            this.particleBlood = new ParticleSource(
                new Vector2(this.X, this.Y),
                new Tuple<int, int>(this.Width / 2, this.Height / 2),
                Direction.Down,
                2,
                Assets.ParticleTextureRegions["Blood"]
            );
        }

        public new void Update(float deltaTime)
        {
            base.Update(deltaTime);

            // particles
            this.particleBlood.Update(deltaTime, new Vector2(this.X, this.Y));

            // is he moving?
            bool isMoving = false;
            if (Tools.GetRandom(8) < 2 || this.isFast)
            {
                if (this.Direction == Direction.Right)
                {
                    this.X += (int)(deltaTime * this.speed);
                    isMoving = true;
                }
                else if (this.Direction == Direction.Left)
                {
                    this.X -= (int)(deltaTime * this.speed);
                    isMoving = true;
                }
            }

            if (isMoving)
            {
                this.anim.Loop = true;
                this.anim = this.animations[(int)this.Direction];
            }
            else
            {
                this.anim.Loop = false;
                this.anim.ResetLoop();
            }

            this.anim.Update(deltaTime);

            this.isFast = true;

            // is he building something? should run there
            if (this.IsBuildingHere.HasValue)
            {
                if (this.X < IsBuildingHere.GetValueOrDefault().X)
                {
                    this.Direction = Direction.Right;
                }
                if (this.X >= (IsBuildingHere.GetValueOrDefault().X + IsBuildingHere.GetValueOrDefault().Width - this.Width))
                {
                    this.Direction = Direction.Left;
                }
            }
            else
            {
                // otherwise always run towards town center
                if (this.X < VillageScreen.MapWidth / 2 - this.centerRadius)
                {
                    this.Direction = Direction.Right;
                }
                else if (this.X > VillageScreen.MapWidth / 2 + this.centerRadius)
                {
                    this.Direction = Direction.Left;
                }

                // when near the base, can be slow and randomly change direction
                if (this.X < VillageScreen.MapWidth / 2 + this.centerRadius && this.X > VillageScreen.MapWidth / 2 - this.centerRadius)
                {
                    this.isFast = false;
                    if (Tools.GetRandom(128) < 2)
                    {
                        this.ChangeDirection();
                    }
                }
            }
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            this.anim.Draw(spriteBatch, this.Hitbox, this.FinalColor);
            //this.DrawHealth(spriteBatch);
            // particles
            this.particleBlood.Draw(spriteBatch);
        }
    }
}