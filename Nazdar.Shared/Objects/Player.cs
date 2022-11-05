﻿using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nazdar.Controls;
using Nazdar.Screens;
using Nazdar.Shared;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Nazdar.Enums;
using Keyboard = Nazdar.Controls.Keyboard;

namespace Nazdar.Objects
{
    public class Player : BasePerson
    {
        private static readonly int moneyLimit = 60;
        private static readonly int cartridgeLimit = 30;

        public List<Bullet> Bullets
        {
            get;
            private set;
        }

        private List<Animation> animations = new List<Animation>()
        {
            new Animation(Assets.Images["PlayerRight"], 4, 10),
            new Animation(Assets.Images["PlayerRight"], 4, 10),
            new Animation(Assets.Images["PlayerLeft"], 4, 10),
            new Animation(Assets.Images["PlayerLeft"], 4, 10),
        };

        private int money;
        public int Money
        {
            get
            {
                return this.money;
            }
            set
            {
                if (value > moneyLimit)
                {
                    Game1.MessageBuffer.AddMessage("Cant hold all this money", MessageType.Danger);
                    this.money = moneyLimit;
                }
                else
                {
                    this.money = value;
                }
            }
        }
        public bool CanAddMoney()
        {
            return this.Money < moneyLimit;
        }

        private int cartridges;
        public int Cartridges
        {
            get
            {
                return this.cartridges;
            }
            set
            {
                if (value > cartridgeLimit)
                {
                    Game1.MessageBuffer.AddMessage("Cant hold all this cartridges", MessageType.Danger);
                    this.cartridges = cartridgeLimit;
                }
                else
                {
                    this.cartridges = value;
                }
            }
        }
        public bool CanAddCartridge()
        {
            return this.Cartridges < cartridgeLimit;
        }

        public int Days { get; set; }

        public Enums.PlayerAction? Action { get; set; }
        public int ActionCost { get; set; }
        public string ActionName { get; set; }

        private ParticleSource particleSmoke;

        private bool canJump = true;

        public int Kills { get; set; } = 0;

        public Player(int x, int y, int startingMoney)
        {
            this.Anim = this.animations[(int)Direction.Right];
            this.Direction = Direction.Right;
            this.Bullets = new List<Bullet>();
            this.Hitbox = new Rectangle(x, y, this.Anim.FrameWidth, this.Anim.FrameHeight);
            this.Health = 100;
            this.Caliber = 30;
            this.Days = 0;
            this.Speed = 121;
            this.Money = startingMoney;

            this.particleBlood = new ParticleSource(
                new Vector2(this.X, this.Y),
                new Tuple<int, int>(this.Width / 2, this.Height / 2),
                Direction.Down,
                2,
                Assets.ParticleTextureRegions["Blood"]
            );

            this.particleSmoke = new ParticleSource(
               new Vector2(this.X, this.Y),
               new Tuple<int, int>(0, this.Height / 2),
               Direction.Up,
               0.5f,
               Assets.ParticleTextureRegions["Smoke"]
            );
        }

        public void Load(dynamic saveData)
        {
            // load player
            this.Hitbox = new Rectangle((int)saveData.Hitbox.X, this.Y, this.Anim.FrameWidth, this.Anim.FrameHeight);
            this.Direction = (Direction)saveData.Direction;
            this.Health = (int)saveData.Health;
            this.Days = (int)saveData.Days;
            this.Money = (int)saveData.Money;
            this.Cartridges = (int)saveData.Cartridges;
            this.Kills = (int)saveData.Kills;

            // load bullets
            if (saveData.ContainsKey("Bullets"))
            {
                foreach (var bullet in saveData.GetValue("Bullets"))
                {
                    this.Bullets.Add(new Bullet((int)bullet.Hitbox.X, (int)bullet.Hitbox.Y, (Direction)bullet.Direction, (int)bullet.Caliber));
                }
            }
        }

        public new void Update(float deltaTime)
        {
            base.Update(deltaTime);

            // moving?
            bool isMoving = false;
            if ((Keyboard.IsPressed(ControlKeys.Right) || Gamepad.IsPressedThumbstick(Direction.Right) || TouchControls.IsPressedRight()) && this.X < VillageScreen.MapWidth - this.Width)
            {
                this.X += (int)(deltaTime * this.Speed);
                this.Direction = Direction.Right;
                isMoving = true;
            }
            else if ((Keyboard.IsPressed(ControlKeys.Left) || Gamepad.IsPressedThumbstick(Direction.Left) || TouchControls.IsPressedLeft()) && this.X > 0)
            {
                this.X -= (int)(deltaTime * this.Speed);
                this.Direction = Direction.Left;
                isMoving = true;
            }

            if (isMoving)
            {
                this.Anim.Loop = true;
                this.Anim = this.animations[(int)this.Direction];
            }
            else
            {
                this.Anim.Loop = false;
                this.Anim.ResetLoop();
            }

            this.Anim.Update(deltaTime);

            // jump?
            if ((Keyboard.IsPressed(ControlKeys.Jump) || Gamepad.HasBeenPressed(ControlButtons.Jump) || TouchControls.HasBeenPressedJump()) && this.Y == Enums.Offset.Floor)
            {
                this.Jump();
                isMoving = true;
            }

            // bullets
            if (Keyboard.HasBeenPressed(ControlKeys.Shoot) || Gamepad.HasBeenPressed(ControlButtons.Shoot) || TouchControls.HasBeenPressedShoot())
            {
                Audio.PlaySound("GunFire");
                this.Bullets.Add(new Bullet(this.X + (this.Width / 2), this.Y + (this.Height / 2), this.Direction, this.Caliber));
                this.particleSmoke.Run(50);
            }
            foreach (var bullet in this.Bullets)
            {
                bullet.Update(deltaTime);
            }
            this.Bullets.RemoveAll(p => p.ToDelete);

            // particles
            this.particleBlood.Update(deltaTime, this.Position);
            this.particleSmoke.Update(deltaTime, new Vector2(this.X + (this.Direction == Direction.Left ? 0 : this.Width), this.Y));
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            this.Anim.Draw(spriteBatch, this.Hitbox);

            // bullets
            foreach (var bullet in this.Bullets)
            {
                bullet.Draw(spriteBatch);
            }

            // particles
            this.particleBlood.Draw(spriteBatch);
            this.particleSmoke.Draw(spriteBatch);

            // some action?
            if (this.Action != null)
            {
                // can afford?
                float alpha = (this.ActionCost <= this.Money ? 1f : 0.6f);

                // static image from spritesheet
                Coin.DrawStatic(spriteBatch, this.ActionCost, this.X, this.Y, alpha);

                // text
                spriteBatch.DrawString(
                    Assets.Fonts["Small"],
                    this.Action.ToString() + " " + this.ActionName,
                    new Vector2(this.X, this.Y - 10),
                    Color.White * alpha
                );
            }
        }

        private async void Jump()
        {
            if (!this.canJump)
            {
                return;
            }

            this.canJump = false;
            Audio.PlayRandomSound("Jumps");

            int h0 = Enums.Offset.Floor;
            int v0 = 25;
            int g = 10;
            float timeDivider = 5;

            for (int i = 0; i < 200; i++)
            {
                float t = i / timeDivider;
                int newY = h0 - (int)(v0 * t - 0.5 * g * Math.Pow(t, 2));
                if (newY > h0)
                {
                    this.Y = h0;
                    break;
                }

                this.Y = newY;

                await Task.Delay(20);
            }

            this.canJump = true;
        }
    }
}
