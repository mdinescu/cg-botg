using System;
using System.Linq;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;

class Player
{
    static readonly double SRT = Math.Sqrt(2);
    static readonly double ISRT = 1.0 / SRT;
    
    enum UnitType { UNIT, HERO, TOWER, GROOT }
    enum HeroType { IRONMAN, HULK, DEADPOOL, VALKYRIE, DOCTOR_STRANGE }
    class BushSpawn {
        public int X;
        public int Y;
        public int Radius;
        public bool IsBush;
    }
    class Point {
        public Point() {}
        public Point(int x, int y) { X = x; Y = y; }
        public int X;
        public int Y;
    }
    class Entity : Point {
        public int Id;
        public int AttackRange;
        public int AttackDamage;
        public int Speed;
        public UnitType Type;
        public HeroType HeroType;
        public int Health;
        public int MaxHealth;
        public int GoldValue;
        public int ItemsOwned;
        public bool Visible;
        public int Mana;
        public int MaxMana;
        public int[] CountDown = new int[3]; 
        public override string ToString() {
            return String.Format("{0}.{1}: {2},{3} (h:{4},r:{5})", Id, Type, X, Y, Health, AttackRange);
        }
    }
    class Item {
        public int Cost;
        public string Name;
        public int Damage;
        public int Health;
        public int MaxHealth;
        public int Mana;
        public int MaxMana;
        public int Speed;
        public int ManaRegen;
        public bool IsPotion;
        public override string ToString() {
            return String.Format("{0} {1} d:{2} h:{3}/{4} s:{5} m:{6}/{7}/{8}{9}",
                Name, Cost, Damage, Health, MaxHealth, Speed, Mana, MaxMana, ManaRegen, (IsPotion ? " (*)" : ""));
        }
    }
    
    static double Dist2(Entity t, Entity o) {
        return (t.X-o.X)*(t.X-o.X)+(t.Y-o.Y)*(t.Y-o.Y);
    }
    static double Dist2(double x, double y, Entity o) {
        return (x-o.X)*(x-o.X)+(y-o.Y)*(y-o.Y);
    }
    static double Dist2(double x, double y, double ox, double oy) {
        return (x-ox)*(x-ox)+(y-oy)*(y-oy);
    }
    
    class Action {        
        public Action(string verb, Entity entity) {
            Verb = verb;
            Entity = entity;
            TX = entity.X;
            TY = entity.Y;
        }
        public static Action Wait(Entity entity) {
            return new Action("WAIT", entity);
        }
        public static Action Attack(Entity entity, Entity other) {
            return new Action("ATTACK", entity) {
                Other = other
            };
        }
        public static Action Move(Entity entity, double x, double y) {
            return new Action("MOVE", entity) {
                TX = x, TY = y
            };
        }
        public static Action MoveAttack(Entity entity, double x, double y, Entity other) {
            return new Action("MOVE_ATTACK", entity) {
                TX = x, TY = y, Other = other
            };
        }
        public static Action Fireball(Entity entity, double x, double y) {
            return new Action("FIREBALL", entity) {
                TX = x, TY = y
            };
        }
        public static Action Burn(Entity entity, double x, double y) {
            return new Action("BURNING", entity) {
                TX = x, TY = y
            };
        }
        public static Action AOEHeal(Entity entity, double x, double y) {
            return new Action("AOEHEAL", entity) {
                TX = x, TY = y
            };
        }
        public double TX; // target x coord after action
        public double TY; // target y coord after action
        public Entity Entity; // entity to perform action
        public Entity Other; // entity to attack
        public string Verb; // action to perform
        public override string ToString() {
            if (Verb == "MOVE") return "MOVE " + TX + " " + TY;
            if (Verb == "MOVE_ATTACK") return String.Format("MOVE_ATTACK {0} {1} {2}", TX, TY, Other.Id);
            if (Verb == "ATTACK") return "ATTACK " + Other.Id;
            if (Verb == "WAIT") return "WAIT";
            if (Verb == "FIREBALL") return "FIREBALL "  + TX + " " + TY;
            if (Verb == "BURNING") return "BURNING " + TX + " " + TY;
            if (Verb == "BLINK") return "BLINK " + TX + " " + TY;
            if (Verb == "AOEHEAL") return "AOEHEAL " + TX + " " + TY;
            return "WAIT";
        }
    }
    
    // if < 1.0 then (me) can attack (him) this turn
    static Action turnTime(Entity me, Entity him) {
        int dist2 = (me.X-him.X)*(me.X-him.X)+(me.Y-him.Y)*(me.Y-him.Y);
        double dist = Math.Sqrt(dist2);
        if (dist < me.AttackRange) {
            // can attack immediately
            return Action.Attack(me, him);
        } else if (dist < me.Speed + me.AttackRange) {        
            double attackTime = me.AttackRange > 150 ? (0.1 * dist / me.AttackRange) : 0.0;            
            // minimum distance needed to travel to hit 
            double moveTime = ((dist - me.AttackRange) + 1.0) / me.Speed;
            if (moveTime + attackTime < 1.0) {
                double pct = (1.0 + (dist - me.AttackRange)) / dist;
                Console.Error.WriteLine(" dist:{0:0.0} ar:{1} mt:{2:0.00} at:{3:0.00} pct:{4:0.000}", dist, me.AttackRange, moveTime, attackTime, pct);
                double mx = me.X + pct * (him.X - me.X);
                double my = me.Y + pct * (him.Y - me.Y);
                return Action.MoveAttack(me, (int)mx, (int)my, him);
            }
        }
        return Action.Wait(me); // can't possibly attack this turn        
    }    
    
    static Item evalPurchase(Entity hero, IEnumerable<Item> items, int gold) {
        Console.Error.WriteLine("have: {0} gold; {1} items", gold, hero.ItemsOwned);
        Item bestItem = null;
        if (hero.ItemsOwned < 2) {            
            foreach(var item in items.Where(itm => !itm.IsPotion && itm.Cost < gold 
                                                   && (itm.Damage >= 5 || itm.Speed >= 20 || itm.MaxHealth >= 80))
                                     .OrderBy(itm => -(itm.Damage * 10  + itm.MaxHealth + itm.Speed * 4) * 1.0 / itm.Cost)) 
            {
                if (bestItem == null) {
                    bestItem = item;
                }
                Console.Error.WriteLine(item);
            }
        }else if (hero.ItemsOwned == 2) {
            // only get silver or better stuff..
            foreach(var item in items.Where(itm => !itm.IsPotion && itm.Cost < gold 
                                                   && !itm.Name.StartsWith("Bronze")
                                                   && (itm.Damage >= 5 || itm.Speed >= 20 || itm.MaxHealth >= 80))
                                     .OrderBy(itm => -(itm.Damage * 10  + itm.MaxHealth + itm.Speed * 4) * 1.0 / itm.Cost)) 
            {
                if (bestItem == null) {
                    bestItem = item;
                }
                Console.Error.WriteLine(item);
            }
        }
        return bestItem;
    }
    
    static Item evalPotion(Entity hero, IEnumerable<Item> items, int gold) {
        Item bestItem = null;
        // only items I can afford, ordered by how much health they offer
        
        foreach(var item in items.Where(itm => itm.Cost < gold && itm.Health > 25)
                                 .OrderBy(itm => -itm.Health)
                                 .Where(itm => itm.IsPotion || hero.ItemsOwned < 3)) 
        {
            if (bestItem == null) {
                bestItem = item;
            }
            Console.Error.WriteLine(item);
        }
        return bestItem;
    }
    
    // compute coordinate to move "me" towards "dest" by "delta" distance units
    static Action GetMove(Entity me, Entity other, double delta) {
        double dist = Math.Sqrt(Dist2(me, other));        
        double pct = delta / dist;
        double mx = (delta > 0 ? me.X : other.X) + pct * (other.X - me.X);
        double my = (delta > 0 ? me.Y : other.Y) + pct * (other.Y - me.Y);
        return Action.Move(me, (int)mx, (int)my);        
    }
    
    static Entity TargetOf(Entity e, IEnumerable<Entity> units) {
        Entity closest = null; double minDist = double.MaxValue;
        foreach(var u in units) {
            var dist = Dist2(e, u);
            if (closest == null || minDist > dist) {
                closest = u; minDist = dist;
            }
        }
        return closest;
    }
    
    static bool IsSafe(Point p, IEnumerable<Entity> my, IEnumerable<Entity> his) {
        foreach(var u in his) {
            var dist = Dist2(p.X, p.Y, u);  // dist to u
            foreach(var m in my) {
                if (m.Type != UnitType.HERO) {
                    var altDist = Dist2(m,u);
                    if (altDist < dist) {
                        // if not a hero, then break
                        if (u.Type != UnitType.HERO) return true; // it's safe                        
                    }                    
                }
            }
            if (Math.Sqrt(dist) - u.Speed - u.AttackRange < 0) return false;
        }
        return true;
    }
    
    static IEnumerable<Point> GetSurounding(Point pt, int delta) {
        double angleDelta = ISRT * delta;
        
        if (pt.Y - delta >= 0) 
            yield return new Point() { X = pt.X, Y = pt.Y - delta };
        if (pt.Y + delta >= 0) 
            yield return new Point() { X = pt.X, Y = pt.Y + delta };
            
        if (pt.X - angleDelta >= 0 && pt.Y - angleDelta >= 0) 
            yield return new Point() { X = (int)(pt.X - angleDelta), Y = (int)(pt.Y - angleDelta) };
        if (pt.X + angleDelta < 1920 && pt.Y - angleDelta >= 0) 
            yield return new Point() { X = (int)(pt.X + angleDelta), Y = (int)(pt.Y - angleDelta) };   
            
        if (pt.X - delta >= 0) 
            yield return new Point() { X = pt.X - delta, Y = pt.Y }; 
        if (pt.X + delta < 1920) 
            yield return new Point() { X = pt.X + delta, Y = pt.Y};
            
        if (pt.X - angleDelta >= 0 && pt.Y + angleDelta < 750) 
            yield return new Point() { X = (int)(pt.X - angleDelta), Y = (int)(pt.Y + angleDelta) };
        
        if (pt.X + angleDelta < 1920 && pt.Y + angleDelta < 750) 
            yield return new Point() { X = (int)(pt.X + angleDelta), Y = (int)(pt.Y + angleDelta) };        
    }
    
    
    static Action TestFireball(Entity hero, IEnumerable<Entity> his) {
        if (hero.HeroType == HeroType.IRONMAN) {
                //   do I have enough mana to FIREBALL? 
            if (hero.Mana > 60 && hero.CountDown[1] == 0) {
                
                var hisHeroes = his.Where(u => u.Type == UnitType.HERO).ToList();
                if (hisHeroes.Count == 2) {
                    var dh = Math.Sqrt(Dist2(hisHeroes[0], hisHeroes[1]));
                    var dh0 = Math.Sqrt(Dist2(hero, hisHeroes[0]));
                    var dh1 = Math.Sqrt(Dist2(hero, hisHeroes[1]));
                    var mpx = (hisHeroes[0].X + hisHeroes[1].X) / 2;
                    var mpy = (hisHeroes[0].Y + hisHeroes[1].Y) / 2;
                    if (dh < 50 && Math.Sqrt(Dist2(mpx, mpy, hero)) < 500) {
                        // pick mid point & fire there
                        Console.Error.WriteLine("fireball to midpoint");
                        return Action.Fireball(hero, mpx, mpy);                            
                    } else {
                        var dmg0 = hero.Mana * 0.2 + 55 * dh0 / 1000;
                        var dmg1 = hero.Mana * 0.2 + 55 * dh1 / 1000;
                        if (dmg0 > dmg1 && dh0 < 500) {
                            Console.Error.WriteLine("fireball to hero 0");
                            return Action.Fireball(hero, hisHeroes[0].X, hisHeroes[0].Y);
                        } else if (dh1 < 500) {
                            Console.Error.WriteLine("fireball to hero 1");
                            return Action.Fireball(hero, hisHeroes[1].X, hisHeroes[1].Y);
                        }
                    }
                } else if (hisHeroes.Count == 1) {
                    if (Math.Sqrt(Dist2(hero, hisHeroes[0])) < 500) {
                        Console.Error.WriteLine("fireball to hero 0");
                        return Action.Fireball(hero, hisHeroes[0].X, hisHeroes[0].Y);
                    }
                }                    
            }
        }
        return Action.Wait(hero);
    }
    
    static Action TestAOEHeal(Entity hero, IEnumerable<Entity> my) {
        if (hero.HeroType == HeroType.DOCTOR_STRANGE) {
            if (hero.Mana > 50 && hero.CountDown[0] == 0) {            
                var oh = my.Where(u => u.Type == UnitType.HERO && 
                                       u.Id != hero.Id).FirstOrDefault();
                var distOh = oh != null ? Math.Sqrt(Dist2(hero, oh)) : 100000;
                if (oh != null && oh.Health < 300 && distOh < 350) {
                    if (distOh < 250) {
                        return Action.AOEHeal(hero, oh.X, oh.Y);
                    } else {
                        var pct = 250.0 / distOh;
                        var px = hero.X + pct * (oh.X - hero.X);
                        var py = hero.Y + pct * (oh.Y - hero.Y);
                        return Action.AOEHeal(hero, px, py);
                    }
                } else if (oh == null && hero.Health < 300) {
                    return Action.AOEHeal(hero, hero.X, hero.Y); // heal myself
                }
            }
        }
        return Action.Wait(hero);
    }
    
    static Action FindSafePosition(Entity hero, IEnumerable<Entity> my, IEnumerable<Entity> his) {
        Entity closest = null; Action action = null; bool closestIsSafe = false;
        if (IsSafe(hero, my, his) && hero.Health > 300) {
            Console.Error.WriteLine("hero {0} safe now! (cd[1]={1})", hero.HeroType, hero.CountDown[1]);
            // I'm already safe.. can I attack?                              
            var fireballAction = TestFireball(hero, his);
            if (fireballAction.Verb != "WAIT") return fireballAction;
            
            var hisTower = his.Where(u => u.Type == UnitType.TOWER).FirstOrDefault();
            var candidates = his.Select(u => new { Unit = u, Action = turnTime(hero, u) }).ToList();
            foreach (var uc in candidates.OrderBy(c => -c.Unit.Health))
            {   
                if (uc.Action.Verb != "WAIT") {
                    Console.Error.WriteLine("{0}", uc.Unit);  
                    bool newPointIsSafe = IsSafe(new Point((int)uc.Action.TX, (int)uc.Action.TY), my, his);

                    if ((newPointIsSafe && uc.Unit.Type == UnitType.GROOT) ||
                          uc.Unit.Health <= hero.AttackDamage
                            || (newPointIsSafe && uc.Unit.Type == UnitType.HERO))                       
                    {                        
                        if (newPointIsSafe) {
                            Console.Error.WriteLine("found safe attack: {0}", uc.Unit);
                        } else {
                            Console.Error.WriteLine("found unsafe attack: {0}", uc.Unit);
                        }
                            
                        // don't get too close to the tower
                        closestIsSafe = newPointIsSafe;
                        closest = uc.Unit; 
                        action = uc.Action;
                        break;
                    }
                }
            }  
        }
        if (action != null) {
            return action;
        }
        
        var safeDelta = hero.Health < 300 ? 100 : 20;
        var unitOrder = hero.Health < 300 ? 1.0 : -1.0;
        // find the location that is right behind my troops
        var myTower = my.Where(e => e.Type == UnitType.TOWER).First();
        closest = my.Where(u => u.Type == UnitType.UNIT)
                        .OrderBy(u => unitOrder * Dist2(myTower, u))
                        .Where(u => GetSurounding(u, safeDelta).Any(p => IsSafe(p, my, his)))
                        .FirstOrDefault();
        Action mv = null;
        if (closest != null) {            
            var safePos = GetSurounding(closest, safeDelta).Where(p => IsSafe(p, my, his)).FirstOrDefault();
            if (safePos != null) {
                mv = Action.Move(hero, safePos.X, safePos.Y);
            }
        }
        
        if (mv != null) {            
            Console.Error.WriteLine("finding spot behind my troops: {0} -> {1:0.0},{2:0.0}", closest.Id, mv.TX, mv.TY);                        
            // now see if any troups are within firing range            
            var fireballAction = TestFireball(hero, his);
            if (fireballAction.Verb != "WAIT" &&
                (Math.Abs(mv.TX - hero.X) < 2 && Math.Abs(mv.TY - hero.Y) < 2)) {
                return fireballAction;
            }
            var aoeHealAction = TestAOEHeal(hero, my);
            if (aoeHealAction.Verb != "WAIT" &&
                (Math.Abs(mv.TX - hero.X) < 2 && Math.Abs(mv.TY - hero.Y) < 2)) {
                return aoeHealAction;
            }
            var target = his.Where(u => Math.Sqrt(Dist2(mv.TX, mv.TY, hero)) < hero.AttackRange)
                            .OrderBy(u => u.Health).FirstOrDefault();
            if (target != null) {
                mv = Action.MoveAttack(hero, mv.TX, mv.TY, target);
            }            
        } else {
            // todo: check to make sure I'm not moving between the enemy and his tower
            var closestEnemy = his.OrderBy(u => Math.Sqrt(Dist2(hero, u)) - u.AttackRange - u.Speed).FirstOrDefault();
            mv = GetMove(hero, closestEnemy, -(closestEnemy.AttackRange + closestEnemy.Speed + 5.0));
            Console.Error.WriteLine("finding spot away from enemy troops: {0} -> {1:0.0},{2:0.0}", closestEnemy.Id, mv.TX, mv.TY);            
            var target = his.Where(u => Math.Sqrt(Dist2(mv.TX, mv.TY, hero)) < hero.AttackRange)
                            .OrderBy(u => u.Health).FirstOrDefault();
            if (target != null) {
                mv = Action.MoveAttack(hero, mv.TX, mv.TY, target);
            }
        }        
        return mv;
    }
    
    static Action MoveAttack(Entity hero, Entity enemy) {
        var dist = Math.Sqrt(Dist2(hero, enemy));
        if (dist < hero.AttackRange) {
            return Action.Attack(hero, enemy);            
        }
        if (dist < hero.AttackRange + hero.Speed) {
            double attackTime = hero.AttackRange > 150 ? (0.1 * dist / hero.AttackRange) : 0.0;            
            // minimum distance needed to travel to hit 
            double moveTime = ((dist - hero.AttackRange) + 1.0) / hero.Speed;
            if (moveTime + attackTime < 1.0) {
                double pct = (1.0 + (dist - hero.AttackRange)) / dist;
                Console.Error.WriteLine(" dist:{0:0.0} ar:{1} mt:{2:0.00} at:{3:0.00} pct:{4:0.000}", dist, hero.AttackRange, moveTime, attackTime, pct);
                double mx = hero.X + pct * (enemy.X - hero.X);
                double my = hero.Y + pct * (enemy.Y - hero.Y);
                return Action.MoveAttack(hero, (int)mx, (int)my, enemy);
            }
        }
        // just move close to the guy
        Console.Error.WriteLine(" just move to him!");
        return Action.Move(hero, enemy.X, enemy.Y);        
    }
    
    static void Main(string[] args)
    {
        string[] inputs;
        int myTeam = int.Parse(Console.ReadLine());
        int bushAndSpawnPointCount = int.Parse(Console.ReadLine());
        var bushes = new BushSpawn[bushAndSpawnPointCount]; 
        for (int i = 0; i < bushAndSpawnPointCount; i++)
        {
            inputs = Console.ReadLine().Split(' ');
            bushes[i] = new BushSpawn() {
                IsBush = ("BUSH" == inputs[0]),
                X = int.Parse(inputs[1]),
                Y = int.Parse(inputs[2]),
                Radius = int.Parse(inputs[3])
            };
        }
        int itemCount = int.Parse(Console.ReadLine());
        var items = new Dictionary<string, Item>();
        for (int i = 0; i < itemCount; i++)
        {
            inputs = Console.ReadLine().Split(' ');
            Item item = new Item() {
                Name = inputs[0], /* contains keywords such as BRONZE, SILVER and BLADE, BOOTS to help you sort */
                Cost = int.Parse(inputs[1]), /* BRONZE items have lowest cost, the most expensive items are LEGENDARY */
                Damage = int.Parse(inputs[2]), /* keyword BLADE is present if the most important item stat is damage */
                Health = int.Parse(inputs[3]),
                MaxHealth = int.Parse(inputs[4]),
                Mana = int.Parse(inputs[5]),
                MaxMana = int.Parse(inputs[6]),
                Speed = int.Parse(inputs[7]), /* keyword BOOTS is present if the most important item stat is moveSpeed */
                ManaRegen = int.Parse(inputs[8]),
                IsPotion = int.Parse(inputs[9]) != 0
            };
            items.Add(item.Name, item);
            Console.Error.WriteLine(item);
        }

        int turn = 0;           
        while (++turn > 0)
        {
            int gold = int.Parse(Console.ReadLine());
            int enemyGold = int.Parse(Console.ReadLine());
            int roundType = int.Parse(Console.ReadLine());
            int entityCount = int.Parse(Console.ReadLine());
            
            var my = new Dictionary<int, Entity>();
            var his = new Dictionary<int, Entity>();
            var myHeros = new List<Entity>(); Entity myTower = null;
            var hisHeros = new List<Entity>(); Entity hisTower = null;
            for (int i = 0; i < entityCount; i++)
            {
                inputs = Console.ReadLine().Split(' ');                
                var entity = new Entity();
                entity.Id = int.Parse(inputs[0]);                                
                entity.Type = (UnitType)Enum.Parse(typeof(UnitType), inputs[2]);
                entity.X = int.Parse(inputs[3]);
                entity.Y = int.Parse(inputs[4]);
                if(int.Parse(inputs[1]) == myTeam) {
                    my.Add(entity.Id, entity);
                    if (entity.Type == UnitType.HERO) {
                        myHeros.Add(entity);                    
                    } else if (entity.Type == UnitType.TOWER) {
                        myTower = entity;
                    }
                } else {
                    his.Add(entity.Id, entity);
                    if (entity.Type == UnitType.HERO) {
                        hisHeros.Add(entity);                    
                    } else if (entity.Type == UnitType.TOWER) {
                        hisTower = entity;
                    }
                }
                entity.AttackRange = int.Parse(inputs[5]);
                entity.Health = int.Parse(inputs[6]);
                entity.MaxHealth = int.Parse(inputs[7]);
                int shield = int.Parse(inputs[8]); // useful in bronze
                entity.AttackDamage = int.Parse(inputs[9]);
                entity.Speed = int.Parse(inputs[10]);
                int stunDuration = int.Parse(inputs[11]); // useful in bronze
                entity.GoldValue = int.Parse(inputs[12]);
                entity.CountDown[0] = int.Parse(inputs[13]); // all countDown and mana variables are useful starting in bronze
                entity.CountDown[1] = int.Parse(inputs[14]);
                entity.CountDown[2] = int.Parse(inputs[15]);
                entity.Mana = int.Parse(inputs[16]);
                entity.MaxMana = int.Parse(inputs[17]);
                int manaRegeneration = int.Parse(inputs[18]);
                if (entity.Type == UnitType.HERO) {
                    entity.HeroType = (HeroType)Enum.Parse(typeof(HeroType), inputs[19]); // DEADPOOL, VALKYRIE, DOCTOR_STRANGE, HULK, IRONMAN
                }
                entity.Visible = int.Parse(inputs[20]) != 0;
                entity.ItemsOwned = int.Parse(inputs[21]);
            }

            if (roundType < 0) {
                if (turn == 1) Console.WriteLine("HULK");
                else Console.WriteLine("DEADPOOL");
            }
            else {
                if (turn <= 5) {                    
                    Item bestItem = evalPurchase(myHeros[0], items.Values, gold);
                    if (bestItem != null) {
                        if (bestItem.Health > bestItem.Damage * 10) {
                            Console.WriteLine("BUY " + bestItem.Name);
                            Console.WriteLine(Action.Move(myHeros[1], myTower.X, myTower.Y));
                        } else {
                            Console.WriteLine(Action.Move(myHeros[0], myTower.X, myTower.Y));
                            Console.WriteLine("BUY " + bestItem.Name);
                        }
                    } else {
                        Console.WriteLine(Action.Move(myHeros[0], myTower.X, myTower.Y));
                        Console.WriteLine(Action.Move(myHeros[1], myTower.X, myTower.Y));
                    }
                } else {
                    var targetEnemy = his.Values
                                     .Where(u => u.Type == UnitType.HERO 
                                              && myHeros.All(mh => 
                                                             Math.Sqrt(Dist2(mh, u)) < mh.Speed + mh.AttackRange))
                                     .OrderBy(u => u.Health).FirstOrDefault();
                    if (targetEnemy != null) { 
                        Console.Error.WriteLine("TGT: {0} {1} (h: {2})", targetEnemy.Id, targetEnemy.HeroType, targetEnemy.Health);
                    }
                    foreach(var myHero in myHeros) {
                        if (myHero.Health < 300) {
                            var potion = evalPotion(myHero, items.Values, gold);
                            if (potion != null) {
                                Console.WriteLine("BUY " + potion.Name);
                                continue;
                            }
                        }
                        if (myHero.HeroType == HeroType.DEADPOOL) {
                            if (myHero.Mana > 40 
                                && Math.Sqrt(Dist2(myHero, hisTower)) < 150 
                                && myHero.CountDown[0] == 0) {
                                Console.WriteLine("COUNTER");
                                continue;
                            }
                        }
                        if (myHero.HeroType == HeroType.HULK) {
                            
                            /*if (myHero.Mana > 40 && myHero.CountDown[2] == 0
                                && Math.Sqrt(Dist2(myHero, hisTower)) < 150) {
                                var closest = his.Values.Where(u => u.Type == UnitType.HERO 
                                                                  && Math.Sqrt(Dist2(myHero, u)) < 150)
                                                        .OrderBy(u => u.Health).FirstOrDefault();
                                if (closest != null) {
                                    Console.WriteLine("BASH " + closest.Id);
                                    continue;
                                }
                            }*/
                            /*if (myHero.Mana > 40 
                                && Math.Sqrt(Dist2(myHero, hisTower)) < 150 
                                && myHero.CountDown[1] == 0) {
                                Console.WriteLine("EXPLOSIVESHIELD");
                                continue;
                            }*/
                        }
                        if (targetEnemy != null) {
                            var action = MoveAttack(myHero, targetEnemy);                            
                            Console.WriteLine(action);
                            continue;                            
                        }
                        
                        Console.WriteLine("ATTACK_NEAREST HERO");                        
                    }
                }
            }
        }
    }
}