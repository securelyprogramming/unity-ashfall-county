using System;
using System.Collections.Generic;

namespace Mayo.Frontier
{
    [Serializable] public sealed class Config
    {
        public string id = "ashfall-county", title = "Ashfall County", mode = "ashfall-county";
        public int seed = 1, difficulty = 1;
    }
    [Serializable] public sealed class Command
    {
        public int sequence, targetId;
        public string kind = "", option = "";
        public float x, z, aimX, aimZ;
    }
    [Serializable] public sealed class ActionResult
    {
        public bool accepted;
        public string reason;
        public ActionResult(bool accepted, string reason) { this.accepted = accepted; this.reason = reason; }
    }
    [Serializable] public sealed class Player
    {
        public int id, lastSequence = -1, level = 1;
        public float x, z, hp = 100, maxHp = 100, oxygen = 100, attackCooldown, dx, dz, faceX = 1, faceZ;
        public int food = 2, wood = 8, metal = 8, coins = 20, ammo = 12, herbs, relics, carryCount;
        public string carried = "";
        public bool dead, connected = true;
    }
    [Serializable] public sealed class Enemy
    {
        public int id, targetId;
        public string kind = "zombie", state = "approach", need = "";
        public float x, z, hp = 40, maxHp = 40, speed = 1.3f, attackCooldown, patience = 70;
        public bool alive = true;
    }
    [Serializable] public sealed class Structure
    {
        public int id, ownerId, level = 1, stock;
        public string kind = "barrier", item = "";
        public float x, z, hp = 100, maxHp = 100, cooldown, progress;
        public bool active = true;
    }
    [Serializable] public sealed class Pickup
    {
        public int id, amount = 1;
        public string kind = "wood";
        public float x, z;
        public bool active = true;
    }
    [Serializable] public sealed class TaskState
    {
        public int id, progress, required = 1, reward;
        public string kind = "rescue", label = "", status = "open", item = "";
        public float x, z, remaining;
    }
    [Serializable] public sealed class World
    {
        public string configId, mode, phase = "active", stage = "", objective = "", message = "";
        public int chapter = 1, tick, progress, goal, wave, score, misses;
        public float elapsed, remaining, baseHealth = 100, morale = 100, zoneRadius = 24, routeProgress, pressure = 45;
        public List<Player> players = new List<Player>();
        public List<Enemy> enemies = new List<Enemy>();
        public List<Structure> structures = new List<Structure>();
        public List<Pickup> pickups = new List<Pickup>();
        public List<TaskState> tasks = new List<TaskState>();
    }
}
