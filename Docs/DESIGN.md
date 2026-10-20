# Ashfall County: open-county design

Ashfall County is an open-world zombie survival game. A quarantined county several kilometres across holds walkers, freaks that you should run from, rival crews who loot the same towns you do, and named survivors you can meet, trade with, recruit or be hunted by. You lead a small **community** from a home base, State of Decay style: scavenge resources in rucksacks, build facilities, keep morale up, switch between members, and work toward cleansing the county of its plague hearts.

The earlier build was a Project Zomboid-style single-survivor simulation. This rebuild keeps its engine (host-authoritative C# simulation, chunk-streamed isometric view, buildings with rooms, doors, windows and containers, noise, vehicles and saves) and replaces the micro-survival game design.

## Pillars

1. **A big, dangerous county.** The default map is 4 km × 4 km, with a 6 km "Massive" option if performance allows. It has towns, farms, forest, the Ash River and highways. Landmarks include Fort Ashby (military), the regional airfield, the hospital, the radio station, Reed's Garage and the Red Talon raider yard. You pick where your group sets down on a drop map, PUBG style.
2. **Know what you can fight.** Every threat shows its tier (1–5 skulls). Walkers are easy. Screamers and bloaters punish carelessness. Ferals are dangerous alone. Juggernauts and plague hearts are group fights or something to run from.
3. **Other people are the wildcard.** Rival crews play the same game you do: they land, loot, fight zombies, chase airdrops and kill each other. Groups differ in how eagerly they try to kill you (Friendly, Wary, Opportunist, Hunter). Named special characters have personalities, missions and consequences.
4. **Community over individual.** Your members share base stock (Food, Meds, Ammo, Materials, Fuel), morale and influence. Each member has health, stamina, fatigue, injuries, traits and skills. Rest the tired and injured ones; switch to someone fresh. Death is permanent per character. The game ends only when the whole community is gone.
5. **Living world events.** Guard supply drops parachute in with military loot and draw everyone nearby. Guard artillery "red zones" shell a warned circle. Hordes roam the roads and sometimes besiege your base.

## Core loop

Leave base → scavenge buildings (rucksacks for the base, gear for yourself) → handle threats (sneak, fight, flee, team up) → return and deposit → spend materials and influence (facilities, outposts, recruits) → take missions → push into harder regions → destroy plague hearts.

## Threats (bestiary)

| Kind | Tier | Behaviour | Counter |
|---|---|---|---|
| Walker | 1 | Shambles, follows noise, crowds | Melee, shove, avoid crowds |
| Crawler | 1 | Low and slow, hard to see | Stomp |
| Screamer | 2 | Flees from you; its scream (big noise, brief stun) pulls everything nearby | Kill it first, quietly or at range |
| Bloater | 2 | Slow; bursts into a toxic gas cloud when killed | Ranged or thrown weapons; never melee |
| Armored | 2 | Riot gear absorbs blows from the front | Knock it down or hit it from behind |
| Feral | 3 | Fast; pounces to knock you down; more common at night | Guns, a companion, a car |
| Juggernaut | 4 | Huge HP; charges and grabs; can kill a weakened survivor outright | Explosives, several shooters, or a car; otherwise run |
| Plague heart | 5 | Pulsing growth inside a building; spawns plague zombies; screams when hit | Group attack with explosives and melee |

Plague zombies (red-eyed, near hearts) can give **blood plague**. It rises over in-game hours and kills unless a plague cure is used. Cures are made at the infirmary from plague samples, or found in airdrops.

## Humans

- **Factions.** Faction 0 is your community. Enclaves are neutral settlements that trade and give missions. Rival crews roam and loot. The Red Talon raiders hold a compound and hunt. A faction's **standing** toward you moves with your deeds; very low standing turns them hostile.
- **AI players.** Every non-player human is a `Survivor` driven by an AI brain that submits the same input and commands a player would. They loot containers, equip weapons, fight zombies, flee when hurt, go for airdrops and attack hostiles. Far from every player-controlled survivor, they are simulated abstractly (`offscreen`).
- **Named characters** (Characters in `Humans.cs`): Ada Pike (radio operator, radio station enclave), Jonah Reed (mechanic, Reed's Garage, fuel and vehicles), Doc Hollis (field medic, hospital, recruitable), Wren Calder (loner sniper, recruitable with standing), Tess Monroe (rival scavenger, opportunist, races you to airdrops), Marshal Kade (Guard deserter, leads a hunter crew), "Big Sal" Varga (Red Talon boss, hunter, heavy weapons) and the Pilgrim (wandering trader with rare goods).
- **Companions.** One community member can follow the controlled survivor. Orders are Follow, Hold and Attack. Companions fight, cover you and make group fights possible.
- **Kill feed.** A PUBG-style feed shows notable deaths ("Tess Monroe killed a Feral", "Kade's crew member was killed by a Juggernaut"). The HUD shows how many survivors are still alive in the county.

## Community and base

- **Stock** of Food, Meds, Ammo, Materials and Fuel, capped by Storage. At 06:00 each day every living member eats 1 Food. Injured members use Meds, and some facilities use Fuel. A shortfall costs morale.
- **Rucksacks** (`ItemDef.resource`) are heavy single items found in containers by building type: food in groceries and diners, meds in pharmacies and the hospital, ammo at the police station and the military base, materials at hardware stores and warehouses, fuel at gas stations and garages. Deposit them at base, or from a car trunk parked at base.
- **Facilities** fill base slots (by base size): Storage and Beds (built in), Infirmary (faster injury recovery, plague cures), Workshop (repairs and explosives), Watchtower (a resident shoots zombies near base), Garden (daily food), Kitchen (morale) and Radio Room (scouting and missions). Building costs Materials and takes in-game hours.
- **Influence** is earned by depositing rucksacks, killing freaks and hearts, finishing missions and trading. It is spent to recruit, claim outposts and trade.
- **Outposts** are cleared buildings you claim. Each adds a daily resource bonus and a safe point.
- **Morale** (0–100) moves with stock, deaths, victories and facilities. When it is low, members may leave; when it is high, they recover faster.
- **Switching.** At base you can take control of any healthy member. If the controlled member dies, control passes to another living member.
- **Missions** come from your community and from enclaves over the radio: rescue a survivor (recruit), bring supplies, clear an infestation, kill a freak, hunt a hostile crew, trade, or defend the base. They expire and pay influence, standing or recruits.
- **Legacy.** Destroying every plague heart cleanses the county (outcome `legacy`). Losing every member ends the game (outcome `dead`).

## World events (PUBG layer)

- **Drop map.** New Game shows the county with named landing zones. The community sets down at the chosen zone and claims a nearby home site.
- **Airdrops.** Every few in-game hours (configurable) a radio warning comes, a plane flies over and a crate parachutes down with red smoke. It holds tier-2 and tier-3 loot: rifles, armour vests, helmets, plague cures and grenades. Nearby crews and zombies converge on it.
- **Red zones.** Guard artillery warns about a circle, then shells it for a short time. Shells kill zombies and survivors alike. Leave the circle.
- **Loot tiers.** Common, Uncommon, Rare and Military (colour-coded names). Military gear concentrates at Fort Ashby and in airdrops.

## What was removed from the Zomboid build

Personal hunger, thirst, boredom, wetness and smoking; fishing; skill books; utility shut-off and water containers; cooking minutiae; and per-plot farming (replaced by the base Garden). Food items now restore stamina and health. Wounds are simplified to injuries that bleed until bandaged and lower maximum health until healed. Doors, windows, barricades, containers, noise, vehicles and saves remain.

## Controls (target)

| Action | Keyboard / mouse | Controller |
|---|---|---|
| Move / aim | WASD / mouse | Left stick / right stick |
| Attack / aim | LMB / RMB | RT / LT |
| Shove | Space | LB |
| Run / sneak | Shift / Ctrl | L3 / R3 |
| Interact / all actions | E / Q | A / Y |
| Throw (grenade, molotov) | G | RB (hold) |
| Companion order (cycle Follow/Hold/Attack) | C | d-pad up |
| Inventory / community / map / missions | Tab / K / M / J | Select / d-pad left / d-pad down / d-pad right |
| Reload / flashlight | R / F | X / — |
| Pause | Esc | Start |

## Architecture

- `Game/Assets/Ashfall/Core` holds the pure C# host simulation, with no Unity references. All rules and AI live here. Owners submit `InputState` and `Command`s; the host validates everything, including reach, line of sight, influence, stock and faction rules.
- `Game/Assets/Ashfall/Unity` holds presentation only: chunk-streamed county, actor views, effects, audio, uGUI/TextMeshPro menus and HUD, and the native QA probe.
- Saves are seed plus delta, gzip, version 3. Systems add tagged sections (`bestiary`, `humans`, `community`, `events`, `player`, `roles`).
- Owned art and audio are restored privately from hash-pinned selections (`ContentSource/Art/*.json`) into the isolated build snapshot. Procedural stand-ins are used when a pack is absent.
