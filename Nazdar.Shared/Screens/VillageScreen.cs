﻿using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended;
using MonoGame.Extended.Screens;
using Nazdar.Controls;
using Nazdar.Objects;
using Nazdar.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using static Nazdar.Enums;
using Keyboard = Nazdar.Controls.Keyboard;

namespace Nazdar.Screens
{
    public class VillageScreen : GameScreen
    {
        private new Game1 Game => (Game1)base.Game;

        public VillageScreen(Game1 game) : base(game) { }

        private readonly Camera camera = new Camera();

        private readonly FileIO saveFile = new FileIO();

        // will be calculated
        public static int MapWidth = Enums.Screen.Width * 4; // 640 * 4 = 2560px

        // game components
        private Player player;
        private List<Enemy> enemies = new List<Enemy>();
        private List<Soldier> soldiers = new List<Soldier>();
        private List<Homeless> homelesses = new List<Homeless>();
        private List<Peasant> peasants = new List<Peasant>();

        private List<Coin> coins = new List<Coin>();

        private List<BuildingSpot> buildingSpots = new List<BuildingSpot>();
        private Center center;
        private List<Armory> armories = new List<Armory>();
        private List<Tower> towers = new List<Tower>();

        // day and night, timer
        private DayPhase dayPhase = DayPhase.Day;
        private double dayPhaseTimer = (int)DayNightLength.Day;

        // some settings - random 0-X == 1
        private int newEnemyProbability = 192; // every day, it gets -2
        private int newEnemyProbabilityLowLimit = 64;
        private int newHomelessProbability = 512 * 3;
        private int newCoinProbability = 768;

        private int? leftmostTowerX = null;
        private int? rightmostTowerX = null;

        public override void Initialize()
        {
            // create player in the center of the map
            this.player = new Player(MapWidth / 2, Offset.Floor);

            // load building spots from tileset
            foreach (var buildingSpot in Assets.TilesetGroups["village1"].GetObjects("objects", "BuildingSpot"))
            {
                this.buildingSpots.Add(
                    new BuildingSpot(
                        (int)buildingSpot.x,
                        (int)buildingSpot.y,
                        (int)buildingSpot.width,
                        (int)buildingSpot.height,
                        buildingSpot.type
                    )
                );
            }

            // set save slot and maybe load?
            this.saveFile.File = Game.SaveSlot;
            this.Load();

            // play songs
            Audio.StopSong();
            Audio.CurrentSongCollection = this.dayPhase == DayPhase.Day ? "Day" : "Night";

            base.Initialize();
        }

        public override void Update(GameTime gameTime)
        {
            if (Keyboard.HasBeenPressed(Keys.Escape) || Gamepad.HasBeenPressed(Buttons.Start) || Gamepad.HasBeenPressed(Buttons.B) || TouchControls.HasBeenPressedSelect())
            {
                // save
                this.Save();

                // back to menu
                this.Game.LoadScreen(typeof(Screens.MapScreen));
            }

            // player
            this.player.Update(this.Game.DeltaTime);
            this.camera.Follow(this.player);

            // game objects
            this.UpdateEnemies();
            this.UpdateSoldiers();
            this.UpdatePeasants();
            this.UpdateHomelesses();
            this.UpdateCoins();

            // buildings
            if (this.center != null)
            {
                this.center.Update(this.Game.DeltaTime);
            }
            this.UpdateArmories();
            this.UpdateTowers();

            // collisions
            this.UpdatePeoplesCollisions();
            this.UpdateActionsCollisions();
            this.UpdateThingsCollisions();

            // other
            this.UpdateDayPhase();
        }

        private void UpdateEnemies()
        {
            // create enemy?
            // - at night
            // - in first half on night
            // - random - every day it gets more difficult
            int randomBase = this.newEnemyProbability - this.player.Days * 2;
            if (randomBase < this.newEnemyProbabilityLowLimit)
            {
                randomBase = this.newEnemyProbabilityLowLimit;
            }

            if (this.dayPhase == DayPhase.Night && this.dayPhaseTimer >= (int)Enums.DayNightLength.Night / 2 && Tools.GetRandom(randomBase) == 0)
            {
                Audio.PlayRandomSound("EnemySpawns");
                Game1.MessageBuffer.AddMessage("New enemy!", MessageType.Danger);

                // choose direction
                if (Tools.GetRandom(2) == 0)
                {
                    this.enemies.Add(new Enemy(0, Offset.Floor, Direction.Right));
                }
                else
                {
                    this.enemies.Add(new Enemy(MapWidth, Offset.Floor, Direction.Left));
                }
            }

            // update enemies
            foreach (Enemy enemy in this.enemies)
            {
                enemy.Update(this.Game.DeltaTime);
            }
        }

        private void UpdateSoldiers()
        {
            bool left = true;
            foreach (Soldier soldier in this.soldiers)
            {
                left = !left;

                soldier.DeploymentX = null;
                if (left && this.leftmostTowerX != null)
                {
                    // leftmost tower exists?
                    soldier.DeploymentX = this.leftmostTowerX;
                }
                else if (!left && this.rightmostTowerX != null)
                {
                    // rightmost tower exists?
                    soldier.DeploymentX = this.rightmostTowerX;
                }

                // can shoot at closest enemy?
                soldier.CanShoot = false;
                int range = Enums.Screen.Width / 3; // third of the visible screen
                foreach (Enemy enemy in this.enemies.Where(enemy => enemy.Dead == false).OrderBy(e => Math.Abs(e.X - soldier.X)))
                {
                    if (Math.Abs(enemy.X - soldier.X) < range)
                    {
                        soldier.CanShoot = true;
                        soldier.Direction = (enemy.X + enemy.Width / 2) < (soldier.X + soldier.Width / 2) ? Direction.Left : Direction.Right;
                        break;
                    }
                }

                soldier.Update(this.Game.DeltaTime);
            }
        }

        private void UpdatePeasants()
        {
            // reset his intentions
            foreach (Peasant peasant in this.peasants)
            {
                peasant.IsBuildingHere = null;
                peasant.IsRunningForItem = null;
            }

            // something to build?
            foreach (var armory in this.armories.Where(a => a.Status == Building.Status.InProcess))
            {
                this.Build(armory);
            }
            foreach (var tower in this.towers.Where(a => a.Status == Building.Status.InProcess))
            {
                this.Build(tower);
            }
            if (this.center != null && this.center.Status == Building.Status.InProcess)
            {
                this.Build(this.center);
            }

            // something to get?
            foreach (var armory in this.armories.Where(a => a.WeaponsCount > 0))
            {
                this.Pick(armory);
            }

            foreach (Peasant peasant in this.peasants)
            {
                peasant.Update(this.Game.DeltaTime);
            }
        }

        private void Build(BaseBuilding building)
        {
            // is someone building it?
            building.IsBeingBuilt = false;
            foreach (var peasant in this.peasants.Where(p => p.Hitbox.Intersects(building.Hitbox)))
            {
                building.IsBeingBuilt = true;
                peasant.IsBuildingHere = building.Hitbox;
                break;
            }

            // noone is building it
            if (building.IsBeingBuilt == false && this.peasants.Count > 0)
            {
                var nearestPeasant = this.peasants.OrderBy(p => Math.Abs(p.X - building.X)).FirstOrDefault();
                nearestPeasant.IsBuildingHere = building.Hitbox;
            }
        }

        private void Pick(BaseBuilding building)
        {
            if (this.peasants.Count > 0)
            {
                var nearestPeasant = this.peasants.OrderBy(p => Math.Abs(p.X - building.X)).FirstOrDefault();
                nearestPeasant.IsRunningForItem = building.Hitbox;
            }
        }

        private void UpdateHomelesses()
        {
            // create new?
            if (Tools.GetRandom(this.newHomelessProbability) == 1)
            {
                Game1.MessageBuffer.AddMessage("New homeless available to hire!", MessageType.Opportunity);
                // choose side 
                if (Tools.GetRandom(2) == 0)
                {
                    this.homelesses.Add(new Homeless(0, Offset.Floor, Direction.Right));
                }
                else
                {
                    this.homelesses.Add(new Homeless(MapWidth, Offset.Floor, Direction.Left));
                }
            }

            // update 
            foreach (Homeless homeless in this.homelesses)
            {
                homeless.Update(this.Game.DeltaTime);
            }
        }

        private void UpdateCoins()
        {
            // create new?
            if (Tools.GetRandom(this.newCoinProbability) == 1)
            {
                Game1.MessageBuffer.AddMessage("New coin!", MessageType.Opportunity);
                this.coins.Add(new Coin(Tools.GetRandom(VillageScreen.MapWidth), Offset.Floor2));
            }

            // update 
            foreach (Coin coin in this.coins)
            {
                coin.Update(this.Game.DeltaTime);
            }
        }

        private void UpdateArmories()
        {
            foreach (Armory armory in this.armories)
            {
                armory.Update(this.Game.DeltaTime);
            }
        }

        private void UpdateTowers()
        {
            this.leftmostTowerX = this.rightmostTowerX = null;

            foreach (Tower tower in this.towers)
            {
                // is it left or rightmost tower (to send soldiers to this tower)
                if (this.center != null)
                {
                    if (tower.X < this.center.X && (this.leftmostTowerX == null || tower.X < this.leftmostTowerX))
                    {
                        this.leftmostTowerX = tower.X;
                    }
                    else if (tower.X > (this.center.X + this.center.Width) && (this.rightmostTowerX == null || tower.X > this.rightmostTowerX))
                    {
                        this.rightmostTowerX = tower.X + tower.Width;
                    }
                }

                // can shoot at closest enemy?
                tower.CanShoot = false;
                if (tower.Status == Building.Status.Built)
                {
                    int range = Enums.Screen.Width / 2; // half of the visible screen
                    foreach (Enemy enemy in this.enemies.Where(enemy => enemy.Dead == false).OrderBy(e => Math.Abs(e.X - tower.X)))
                    {
                        if (Math.Abs(enemy.X - tower.X) < range)
                        {
                            tower.CanShoot = true;
                            tower.PrepareToShoot(
                                (enemy.X + enemy.Width / 2) < (tower.X + tower.Width / 2) ? Direction.Left : Direction.Right,
                                enemy.X,
                                range
                            );
                            break;
                        }
                    }
                }

                tower.Update(this.Game.DeltaTime);
            }
        }

        private void UpdatePeoplesCollisions()
        {
            // enemies and player bullets
            foreach (Enemy enemy in this.enemies.Where(enemy => enemy.Dead == false))
            {
                foreach (Bullet bullet in this.player.Bullets.Where(bullet => bullet.ToDelete == false))
                {
                    if (enemy.Hitbox.Intersects(bullet.Hitbox))
                    {
                        bullet.ToDelete = true;

                        if (!enemy.TakeHit(bullet.Caliber))
                        {
                            enemy.Dead = true;
                            this.player.Kills++;
                            Audio.PlayRandomSound("EnemyDeaths");
                            Game1.MessageBuffer.AddMessage("Bullet kill by player", MessageType.Success);
                        }
                    }
                }
            }

            // enemies and tower bullets
            foreach (Enemy enemy in this.enemies.Where(enemy => enemy.Dead == false))
            {
                foreach (Tower tower in this.towers)
                {
                    foreach (Bullet bullet in tower.Bullets.Where(bullet => bullet.ToDelete == false))
                    {
                        if (enemy.Hitbox.Intersects(bullet.Hitbox))
                        {
                            bullet.ToDelete = true;

                            if (!enemy.TakeHit(bullet.Caliber))
                            {
                                enemy.Dead = true;
                                this.player.Kills++;
                                Audio.PlayRandomSound("EnemyDeaths");
                                Game1.MessageBuffer.AddMessage("Bullet kill by tower", MessageType.Success);
                            }
                        }
                    }
                }
            }

            // enemies and soldiers bullets
            foreach (Enemy enemy in this.enemies.Where(enemy => enemy.Dead == false))
            {
                foreach (Soldier soldier in this.soldiers)
                {
                    foreach (Bullet bullet in soldier.Bullets.Where(bullet => bullet.ToDelete == false))
                    {
                        if (enemy.Hitbox.Intersects(bullet.Hitbox))
                        {
                            bullet.ToDelete = true;

                            if (!enemy.TakeHit(bullet.Caliber))
                            {
                                enemy.Dead = true;
                                this.player.Kills++;
                                Audio.PlayRandomSound("EnemyDeaths");
                                Game1.MessageBuffer.AddMessage("Bullet kill by soldier", MessageType.Success);
                            }
                        }
                    }
                }
            }

            // enemies and player
            foreach (Enemy enemy in this.enemies.Where(enemy => enemy.Dead == false))
            {
                if (this.player.Hitbox.Intersects(enemy.Hitbox))
                {
                    enemy.Dead = true;
                    this.player.Kills++;

                    Audio.PlayRandomSound("EnemyDeaths");
                    Game1.MessageBuffer.AddMessage("Bare hands kill", MessageType.Success);

                    if (!this.player.TakeHit(enemy.Caliber))
                    {
                        this.Game.LoadScreen(typeof(Screens.GameOverScreen));
                    }
                }
            }

            // enemies and soldiers
            foreach (Enemy enemy in this.enemies.Where(enemy => enemy.Dead == false))
            {
                foreach (Soldier soldier in this.soldiers.Where(soldier => soldier.Dead == false))
                {
                    if (!enemy.Dead && !soldier.Dead && enemy.Hitbox.Intersects(soldier.Hitbox))
                    {
                        if (!enemy.TakeHit(soldier.Caliber))
                        {
                            Game1.MessageBuffer.AddMessage("Enemy killed by soldier", MessageType.Success);
                            Audio.PlayRandomSound("EnemyDeaths");
                            enemy.Dead = true;
                            this.player.Kills++;
                        }
                        if (!soldier.TakeHit(enemy.Caliber))
                        {
                            Game1.MessageBuffer.AddMessage("Soldier killed by enemy", MessageType.Fail);
                            Audio.PlayRandomSound("SoldierDeaths");
                            soldier.Dead = true;
                        }
                    }
                }
            }


            // enemies and peasants
            foreach (Enemy enemy in this.enemies.Where(enemy => enemy.Dead == false))
            {
                foreach (Peasant peasant in this.peasants.Where(peasant => peasant.Dead == false))
                {
                    if (!enemy.Dead && !peasant.Dead && enemy.Hitbox.Intersects(peasant.Hitbox))
                    {
                        if (!enemy.TakeHit(peasant.Caliber))
                        {
                            Game1.MessageBuffer.AddMessage("Enemy killed by peasant", MessageType.Success);
                            Audio.PlayRandomSound("EnemyDeaths");
                            enemy.Dead = true;
                            this.player.Kills++;
                        }
                        if (!peasant.TakeHit(enemy.Caliber))
                        {
                            Game1.MessageBuffer.AddMessage("Peasant killed by enemy", MessageType.Fail);
                            Audio.PlayRandomSound("SoldierDeaths");
                            peasant.Dead = true;
                        }
                    }
                }

            }

            this.soldiers.RemoveAll(p => p.ToDelete);
            this.enemies.RemoveAll(p => p.ToDelete);
            this.peasants.RemoveAll(p => p.ToDelete);
        }

        private void UpdateThingsCollisions()
        {
            // coins
            foreach (var coin in this.coins)
            {
                if (this.player.Hitbox.Intersects(coin.Hitbox))
                {
                    Game1.MessageBuffer.AddMessage("Coin acquired", MessageType.Success);
                    Audio.PlaySound("Coin");
                    this.player.Money++;
                    coin.ToDelete = true;
                    break;
                }
            }
            this.coins.RemoveAll(p => p.ToDelete);

            // weapons
            foreach (var armory in this.armories.Where(a => a.WeaponsCount > 0))
            {
                foreach (var peasant in this.peasants)
                {
                    if (peasant.Hitbox.Intersects(armory.Hitbox) && armory.WeaponsCount > 0)
                    {
                        // ok, peasant get weapon and turns into soldier
                        Game1.MessageBuffer.AddMessage("Peasant => soldier", MessageType.Success);
                        Audio.PlaySound("SoldierSpawn");
                        peasant.ToDelete = true;
                        armory.DropWeapon();
                        this.soldiers.Add(new Soldier(peasant.Hitbox.X, Offset.Floor, peasant.Direction));
                    }
                }
            }
            this.peasants.RemoveAll(p => p.ToDelete);
        }

        private void UpdateActionsCollisions()
        {
            this.player.Action = null;
            this.player.ActionCost = 0;
            this.player.ActionName = null;
            foreach (var buildingSpot in this.buildingSpots)
            {
                if (!this.player.Hitbox.Intersects(buildingSpot.Hitbox))
                {
                    continue;
                }

                // Center?
                if (buildingSpot.Type == Building.Type.Center && this.center == null)
                {
                    this.player.Action = Enums.PlayerAction.Build;
                    this.player.ActionCost = Center.Cost;
                    this.player.ActionName = Center.Name;

                    if (Keyboard.HasBeenPressed(ControlKeys.Action) || Gamepad.HasBeenPressed(ControlButtons.Action) || TouchControls.HasBeenPressedAction())
                    {
                        if (this.player.Money >= Center.Cost)
                        {
                            Game1.MessageBuffer.AddMessage("Building started", MessageType.Info);
                            Audio.PlaySound("Rock");
                            this.player.Money -= Center.Cost;
                            this.center = new Center(buildingSpot.X, buildingSpot.Y, Building.Status.InProcess);
                        }
                        else
                        {
                            Game1.MessageBuffer.AddMessage("Not enough money", MessageType.Fail);
                        }
                    }
                }
                // Armory?
                else if (buildingSpot.Type == Building.Type.Armory)
                {
                    var armories = this.armories.Where(a => a.Hitbox.Intersects(buildingSpot.Hitbox));
                    if (armories.Count() == 0)
                    {
                        // no armory here - we can build something
                        this.player.Action = Enums.PlayerAction.Build;
                        this.player.ActionCost = Armory.Cost;
                        this.player.ActionName = Armory.Name;

                        if (Keyboard.HasBeenPressed(ControlKeys.Action) || Gamepad.HasBeenPressed(ControlButtons.Action) || TouchControls.HasBeenPressedAction())
                        {
                            if (this.player.Money < Armory.Cost)
                            {
                                Game1.MessageBuffer.AddMessage("Not enough money", MessageType.Fail);
                            }
                            else
                            {
                                Game1.MessageBuffer.AddMessage("Building started", MessageType.Info);
                                Audio.PlaySound("Rock");
                                this.player.Money -= Armory.Cost;
                                this.armories.Add(new Armory(buildingSpot.X, buildingSpot.Y, Building.Status.InProcess));
                            }
                        }
                    }
                    else
                    {
                        // armory exists - create weapons?
                        var armory = armories.First();
                        if (armory.Status == Building.Status.Built)
                        {
                            this.player.Action = Enums.PlayerAction.Create;
                            this.player.ActionCost = Armory.WeaponCost;
                            this.player.ActionName = Weapon.Name;

                            if (Keyboard.HasBeenPressed(ControlKeys.Action) || Gamepad.HasBeenPressed(ControlButtons.Action) || TouchControls.HasBeenPressedAction())
                            {
                                if (this.player.Money >= Armory.WeaponCost)
                                {
                                    if (armory.AddWeapon())
                                    {
                                        Game1.MessageBuffer.AddMessage("Item purchased", MessageType.Info);
                                        Audio.PlaySound("SoldierSpawn");
                                        this.player.Money -= Armory.WeaponCost;
                                    }
                                    else
                                    {
                                        Game1.MessageBuffer.AddMessage("Armory is full", MessageType.Fail);
                                    }
                                }
                                else
                                {
                                    Game1.MessageBuffer.AddMessage("Not enough money", MessageType.Fail);
                                }
                            }
                        }
                    }
                }
                // Tower?
                else if (buildingSpot.Type == Building.Type.Tower)
                {
                    var towers = this.towers.Where(a => a.Hitbox.Intersects(buildingSpot.Hitbox));
                    if (towers.Count() == 0)
                    {
                        // no tower - we can build something
                        this.player.Action = Enums.PlayerAction.Build;
                        this.player.ActionCost = Tower.Cost;
                        this.player.ActionName = Tower.Name;

                        if (Keyboard.HasBeenPressed(ControlKeys.Action) || Gamepad.HasBeenPressed(ControlButtons.Action) || TouchControls.HasBeenPressedAction())
                        {
                            if (this.player.Money < Tower.Cost)
                            {
                                Game1.MessageBuffer.AddMessage("Not enough money", MessageType.Fail);
                            }
                            else
                            {
                                Game1.MessageBuffer.AddMessage("Building started", MessageType.Info);
                                Audio.PlaySound("Rock");
                                this.player.Money -= Tower.Cost;
                                this.towers.Add(new Tower(buildingSpot.X, buildingSpot.Y, Building.Status.InProcess));
                            }
                        }
                    }
                }
            }

            // hire homelesses?
            if (this.player.Action == null)
            {
                foreach (var homeless in this.homelesses)
                {
                    if (this.player.Hitbox.Intersects(homeless.Hitbox))
                    {
                        this.player.Action = Enums.PlayerAction.Hire;
                        this.player.ActionCost = Homeless.Cost;
                        this.player.ActionName = Homeless.Name;

                        // hire homeless man? create peasant
                        if (Keyboard.HasBeenPressed(ControlKeys.Action) || Gamepad.HasBeenPressed(ControlButtons.Action) || TouchControls.HasBeenPressedAction())
                        {
                            if (this.player.Money >= Homeless.Cost)
                            {
                                Game1.MessageBuffer.AddMessage("Homeless hired => peasant", MessageType.Success);
                                Audio.PlaySound("SoldierSpawn");
                                homeless.ToDelete = true;
                                this.player.Money -= Homeless.Cost;
                                this.peasants.Add(new Peasant(homeless.Hitbox.X, Offset.Floor, homeless.Direction));
                            }
                            else
                            {
                                Game1.MessageBuffer.AddMessage("Not enough money", MessageType.Fail);
                            }
                        }
                        break;
                    }
                }
                this.homelesses.RemoveAll(p => p.ToDelete);
            }
        }

        private void UpdateDayPhase()
        {
            this.dayPhaseTimer -= this.Game.DeltaTime;
            if (this.dayPhaseTimer <= 0)
            {

                if (this.dayPhase == DayPhase.Day)
                {
                    Game1.MessageBuffer.AddMessage("Brace yourself", MessageType.Danger);
                    this.dayPhase = DayPhase.Night;
                    this.dayPhaseTimer = (int)DayNightLength.Night;
                }
                else
                {
                    Game1.MessageBuffer.AddMessage("New dawn", MessageType.Success);
                    this.player.Days++;
                    this.dayPhase = DayPhase.Day;
                    this.dayPhaseTimer = (int)DayNightLength.Day;
                }

                Audio.StopSong();
                Audio.CurrentSongCollection = this.dayPhase == DayPhase.Day ? "Day" : "Night";
            }
        }

        public override void Draw(GameTime gameTime)
        {
            this.Game.Matrix = this.camera.Transform;
            this.Game.DrawStart();

            // day or night sky - color transition
            float dayPhaseLength = this.dayPhase == DayPhase.Day ? (float)DayNightLength.Day : (float)DayNightLength.Night;
            float dayProgress = (float)((dayPhaseLength - this.dayPhaseTimer) / dayPhaseLength);
            Color currentColor = Color.Lerp(
                this.dayPhase == DayPhase.Day ? Color.CornflowerBlue : Color.DarkBlue,
                this.dayPhase == DayPhase.Day ? Color.DarkBlue : Color.CornflowerBlue,
                dayProgress
            );
            this.GraphicsDevice.Clear(currentColor);

            // background - tileset
            Assets.TilesetGroups["village1"].Draw("ground", this.Game.SpriteBatch);

            // stats ------------------------------------------------------------------------------------
            int leftOffset = Offset.StatusBarX - (int)this.camera.Transform.Translation.X;

            // healthbar
            this.Game.SpriteBatch.DrawRectangle(
                new Rectangle(leftOffset, Offset.StatusBarY, 100, 10),
                Color.Black
            );
            this.Game.SpriteBatch.DrawRectangle(
                new Rectangle(leftOffset + 1, Offset.StatusBarY + 1, 98, 8),
                Color.LightGreen,
                8
            );
            this.Game.SpriteBatch.DrawRectangle(
                new Rectangle(leftOffset + 1, Offset.StatusBarY + 1, (int)((this.player.Health / 100f) * 98), 8),
                Color.Green,
                8
            );

            // money
            Coin.DrawStatic(this.Game.SpriteBatch, this.player.Money, leftOffset, Offset.StatusBarY + 40, 1);

            // day or night
            this.Game.SpriteBatch.DrawString(
                Assets.Fonts["Small"],
                this.dayPhase.ToString() + " (" + Math.Ceiling(this.dayPhaseTimer).ToString() + ")",
                new Vector2(leftOffset, Offset.StatusBarY + 45),
                Color.Black);

            // right stats
            this.Game.SpriteBatch.DrawString(
                Assets.Fonts["Small"],
                "Homelesses: " + (this.homelesses.Count).ToString(),
                new Vector2(leftOffset + 450, Offset.StatusBarY),
                Color.Black);
            this.Game.SpriteBatch.DrawString(
                Assets.Fonts["Small"],
                "Peasants: " + (this.peasants.Count).ToString(),
                new Vector2(leftOffset + 450, Offset.StatusBarY + 10),
                Color.Black);
            this.Game.SpriteBatch.DrawString(
                Assets.Fonts["Small"],
                "Soldiers: " + (this.soldiers.Count).ToString(),
                new Vector2(leftOffset + 450, Offset.StatusBarY + 20),
                Color.Black);
            this.Game.SpriteBatch.DrawString(
                Assets.Fonts["Small"],
                "Score: " + Tools.GetScore(this.player.Days, this.player.Money, this.peasants.Count, this.soldiers.Count, this.player.Kills),
                new Vector2(leftOffset + 450, Offset.StatusBarY + 30),
                Color.Black);
            this.Game.SpriteBatch.DrawString(
                Assets.Fonts["Small"],
                "Village " + this.Game.Village.ToString(),
                new Vector2(leftOffset + 450, Offset.StatusBarY + 40),
                Color.Black);
            this.Game.SpriteBatch.DrawString(
               Assets.Fonts["Small"],
               "Day " + this.player.Days.ToString() + ".",
               new Vector2(leftOffset + 450, Offset.StatusBarY + 50),
               Color.Black);

            // messages
            Game1.MessageBuffer.Draw(this.Game.SpriteBatch, this.camera.Transform.Translation.X);

            // touch controls
            TouchControls.Draw(this.Game.SpriteBatch, this.camera.Transform.Translation.X);

            // game objects
            foreach (BuildingSpot buildingSpot in this.buildingSpots)
            {
                buildingSpot.Draw(this.Game.SpriteBatch);
            }

            if (this.center != null)
            {
                this.center.Draw(this.Game.SpriteBatch);
            }

            foreach (Armory armory in this.armories)
            {
                armory.Draw(this.Game.SpriteBatch);
            }

            foreach (Tower tower in this.towers)
            {
                tower.Draw(this.Game.SpriteBatch);
            }

            foreach (Enemy enemy in this.enemies)
            {
                enemy.Draw(this.Game.SpriteBatch);
            }

            foreach (Soldier soldier in this.soldiers)
            {
                soldier.Draw(this.Game.SpriteBatch);
            }

            foreach (Homeless homeless in this.homelesses)
            {
                homeless.Draw(this.Game.SpriteBatch);
            }

            foreach (Peasant peasant in this.peasants)
            {
                peasant.Draw(this.Game.SpriteBatch);
            }

            foreach (Coin coin in this.coins)
            {
                coin.Draw(this.Game.SpriteBatch);
            }

            // player - camera follows him
            this.player.Draw(this.Game.SpriteBatch);

            this.Game.DrawEnd();
        }

        private void Load()
        {
            dynamic saveData = this.saveFile.Load();
            if (saveData == null)
            {
                return;
            }

            if (saveData.ContainsKey("player"))
            {
                this.player.Load(saveData.GetValue("player"));
            }

            if (saveData.ContainsKey("enemies"))
            {
                foreach (var enemy in saveData.GetValue("enemies"))
                {
                    if ((bool)enemy.Dead)
                    {
                        continue;
                    }
                    this.enemies.Add(new Enemy((int)enemy.Hitbox.X, (int)enemy.Hitbox.Y, (Direction)enemy.Direction, (int)enemy.Health, (int)enemy.Caliber));
                }
            }

            if (saveData.ContainsKey("soldiers"))
            {
                foreach (var soldier in saveData.GetValue("soldiers"))
                {
                    if ((bool)soldier.Dead)
                    {
                        continue;
                    }

                    var newSoldier = new Soldier((int)soldier.Hitbox.X, (int)soldier.Hitbox.Y, (Direction)soldier.Direction, (int)soldier.Health, (int)soldier.Caliber);
                    if (soldier.ContainsKey("Bullets"))
                    {
                        foreach (var bullet in soldier.GetValue("Bullets"))
                        {
                            newSoldier.Bullets.Add(new Bullet((int)bullet.Hitbox.X, (int)bullet.Hitbox.Y, (Direction)bullet.Direction, (int)bullet.Caliber));
                        }
                    }
                    this.soldiers.Add(newSoldier);
                }
            }

            if (saveData.ContainsKey("homelesses"))
            {
                foreach (var homeless in saveData.GetValue("homelesses"))
                {
                    this.homelesses.Add(new Homeless((int)homeless.Hitbox.X, (int)homeless.Hitbox.Y, (Direction)homeless.Direction));
                }
            }

            if (saveData.ContainsKey("peasants"))
            {
                foreach (var peasant in saveData.GetValue("peasants"))
                {
                    this.peasants.Add(new Peasant((int)peasant.Hitbox.X, (int)peasant.Hitbox.Y, (Direction)peasant.Direction));
                }
            }

            if (saveData.ContainsKey("coins"))
            {
                foreach (var coin in saveData.GetValue("coins"))
                {
                    this.coins.Add(new Coin((int)coin.Hitbox.X, (int)coin.Hitbox.Y));
                }
            }

            if (saveData.ContainsKey("dayPhase") && saveData.ContainsKey("dayPhaseTimer"))
            {
                this.dayPhase = (DayPhase)saveData.GetValue("dayPhase");
                this.dayPhaseTimer = (double)saveData.GetValue("dayPhaseTimer");
            }

            if (saveData.ContainsKey("center") && saveData.GetValue("center") != null)
            {
                this.center = new Center((int)saveData.GetValue("center").X, (int)saveData.GetValue("center").Y, (Building.Status)saveData.GetValue("center").Status);
            }

            if (saveData.ContainsKey("armories"))
            {
                foreach (var armory in saveData.GetValue("armories"))
                {
                    this.armories.Add(new Armory((int)armory.Hitbox.X, (int)armory.Hitbox.Y, (Building.Status)armory.Status, (int)armory.WeaponsCount));
                }
            }

            if (saveData.ContainsKey("towers"))
            {
                foreach (var tower in saveData.GetValue("towers"))
                {
                    var newTower = new Tower((int)tower.Hitbox.X, (int)tower.Hitbox.Y, (Building.Status)tower.Status);
                    if (tower.ContainsKey("Bullets"))
                    {
                        foreach (var bullet in tower.GetValue("Bullets"))
                        {
                            newTower.Bullets.Add(new Bullet((int)bullet.Hitbox.X, (int)bullet.Hitbox.Y, (Direction)bullet.Direction, (int)bullet.Caliber, BulletType.Cannonball));
                        }
                    }
                    this.towers.Add(newTower);
                }
            }

            Game1.MessageBuffer.AddMessage("Game loaded", MessageType.Info);
        }

        private void Save()
        {
            this.saveFile.Save(new
            {
                player = this.player,
                enemies = this.enemies,
                soldiers = this.soldiers,
                homelesses = this.homelesses,
                peasants = this.peasants,
                center = this.center,
                armories = this.armories,
                towers = this.towers,
                coins = this.coins,
                dayPhase = this.dayPhase,
                dayPhaseTimer = this.dayPhaseTimer,
                village = this.Game.Village,
            });
        }
    }
}