"""
Recreation of the Steambirds Alliance dungeons of build 190828.2045.

The Dungeons/dungeon_* files of that configBinary are empty ({}), like in 190426 and 201219: the room layouts
lived only on the official server. What the client still has, and what this file uses:
  - every unit of each dungeon (Units/60D..999D folders): names, health, role (boss, miniboss, treasure, wall...)
  - the dungeon names and tiers (Texts/en-US/dungeons), keys (Items/Consumables/dungeonkey)
  - the floor/wall look of each dungeon (territoryTiles) and the floor pieces (ground_assets.txt of 201219)
  - the entrance signs (lore *dungeonsign1/2) and the boss lines (bark)
  - the dungeon quests (Quests/quests_daily: The Vaults, The Dig, Throne of Meowza)
  - public notes (wiki.steambirds.com, fandom wiki, Steam patch notes): minibosses, boss order, altar rooms,
    The Dig milestones, The Vaults key escort, arena wave counts.
Room shapes, sizes and enemy placement are a reconstruction and are marked as such in each file.

Run:  python dungeons_190828.py <output folder>
"""

import os
import sys

from dungeon_builder import (DungeonMap, pat, dump, FACE_MINUS_V, FACE_PLUS_V, FACE_PLUS_U, FACE_MINUS_U)
from unit_types import add_types, make_type

SOURCE = ("Build 190828.2045: units, names, barks, signs, quests and territory come from the configBinary; "
          "the Dungeons/dungeon_* file of that build is empty, so the room format and the enemy positions "
          "were recreated.")

PIECES = {
    "dungeonfactory2": [17626, 17623, 17624, 17621, 17622, 17619, 17620, 17633],
    "dungeonobstacle": [39691, 39690, 39689],
    "dungeonarena": [31643, 55660, 25809, 7626, 43311, 1792, 37477],
    "dungeonarena2": [42241, 42244, 42243, 42238, 42237],
    "dungeongrindhouse": [37616, 37615, 37614],
    "dungeonpalace": [45502, 45499, 45500, 45497],
    "dungeonfire": [29934, 29933],
    "dungeonruinssnow": [5754, 29771, 65456, 23937],
    "industrialboss": [58814, 28963, 64648],
    "dungeonthief": [48638, 18787, 54472],
    "dungeonmilitary": [20761, 20760, 20759, 20766],
    "dungeoncave": [34315, 34314],
    "dungeonboss": [42135, 42134, 42133, 42132, 42131, 42130, 42129, 42128, 42127, 6451, 6450, 6449],
    "dungeonboss2": [58053, 28202, 63887, 10700, 46385, 16534, 52219, 64568, 34717, 22369, 58054, 28203],
}


# ---------------------------------------------------------------------------------------------- helpers

def common(m, rooms, tier, key=None, portal_item=None, sign1=None, sign2=None, sign2_room=None,
           entry="entrance"):
    first = m.rooms[entry]
    u, v = first.uv(0.5, 0.2)
    m.spawn = {"u": u, "v": v, "rot": FACE_PLUS_V}
    for r in rooms:
        if r.kind in ("room", "boss", "miniboss", "arena"):
            m.checkpoint(r)
    if sign1:
        m.sign(sign1, first, 0.5, 0.6)
    if sign2:
        m.sign(sign2, m.rooms[sign2_room], 0.5, 0.12)
    m.values["tier"] = tier
    m.meta["source"] = SOURCE
    if key:
        m.meta["key"] = key
    if portal_item:
        m.meta["portalItem"] = portal_item


def boss(m, room, unit_type, wreck=None, fy=0.7, barks=None, phases=None, extra=None, portal_fy=0.35):
    m.point("boss", room, 0.5, fy, FACE_MINUS_V)
    m.point("portal", room, 0.5, portal_fy, 0, note="exit carrier (appears when the boss dies)")
    over = {}
    if wreck:
        over["onDeath"] = wreck
    if barks:
        over["barks"] = barks
    if phases:
        over["phases"] = phases
    if extra:
        over.update(extra)
    m.types[unit_type] = make_type(unit_type, **over)
    m.put(room, unit_type, pat("center", fy=fy))
    m.encounter(room.name, wakes="onEnter", boss=True, closesOnEnter=True)
    m.meta["exit"] = {"point": "portal", "when": "death:" + unit_type}


def treasure(m, room, unit_type, fx=0.5, fy=0.6):
    m.put(room, unit_type, pat("at", pts=[(fx, fy)]))


def finish(m):
    used = set()
    for e in m.encounters:
        for i in e.get("enemies", []):
            used.add(i["type"])
        for w in e.get("waves", []):
            for g in w["groups"]:
                used.add(g["type"])
    for g in m.gates + m.side_gates:
        used.add(g["type"])
    for t in list(m.types.values()):
        for k in ("onDeath",):
            if k in t:
                used.add(t[k])
        for f in t.get("phases", []):
            if "summon" in f:
                used.add(f["summon"])
        for s in t.get("sequence", []):
            used.add(s)
    add_types(m, sorted(used))
    dps_chests(m)
    for t in m.types.values():
        if "periodicSummon" in t and t["periodicSummon"]["type"] not in m.types:
            add_types(m, [t["periodicSummon"]["type"]])
    return m


def dps_chests(m):
    """Big Treasure: in the original, enough damage in a short time turned the chest into <name>_reward, otherwise it
    became <name>_dud. The server runs it as "dpsChest": the first hit starts the clock; broken in time -> reward.
    The health and the time are a reconstruction scaled by the dungeon tier (the unit has 10000 hp in the client,
    used by the original DPS meter, not as real health)."""
    tier = float(m.values.get("tier", 1))
    for tid in list(m.types):
        if not tid.endswith("treasureBIG"):
            continue
        reward, dud = tid + "_reward", tid + "_dud"
        t = m.types[tid]
        try:
            add_types(m, [reward])
        except KeyError:
            continue
        chest = {"seconds": 25, "reward": reward}
        try:
            add_types(m, [dud])
            chest["fail"] = dud
        except KeyError:
            pass
        t.pop("onDeath", None)
        t.pop("opensAfterHitSeconds", None)
        t["behavior"] = "idle"
        t["health"] = int(500 + 250 * tier)
        t["dpsChest"] = chest
        t["note"] = ("DPS chest: starts counting at the first hit; breaking it in %d s swaps it for %s%s. Health and time "
                     "are a reconstruction (change 'health' and 'dpsChest.seconds' freely)."
                     % (chest["seconds"], reward, (", senao " + dud) if "fail" in chest else ""))


def wave(name, groups, interval=4, message=None):
    w = {"name": name, "delayBeforeSeconds": interval, "groups": groups}
    if message:
        w["message"] = message
    return w


def group(room, unit_type, pattern):
    pos = []
    for fx, fy, rot in pattern:
        u, v = room.uv(fx, fy)
        pos.append({"u": u, "v": v, "rot": rot})
    return {"type": unit_type, "pos": pos}


def barrel_types(m, barrel, boom, **boom_over):
    """Stored Bomb / Flame Probe Cache: in the original the barrel is the visible thing and the invisible gun unit
    (<barrel>boom) only exists to fire ONE ring of bullets where the barrel blew up. So the barrel carries
    onDeath = boom, and the boom lives a moment, awake, firing at once in any direction and without holding the room."""
    m.types[barrel] = make_type(barrel, onDeath=boom,
                                note="Visible barrel. When destroyed it releases the ring of bullets (%s)." % boom)
    over = dict(behavior="turret", aggroRadius=0, firstShotDelay=0.0, shotRange=999,
                excludeFromRoomClear=True, durationSeconds=1.5,
                note="The original's invisible shooter: it only exists for a moment after the barrel blows up, "
                     "fires the ring and disappears. It is not placed loose in the rooms.")
    over.update(boom_over)
    m.types[boom] = make_type(boom, **over)



def bomb_types(m):
    """The Bomb Factory units that have no weapon in the client (they hurt by exploding or by what they carry):
    - Loose Bomb (homingbomb) and Bomber Jet: kamikaze. They fly into the player and explode, which spawns the
      invisible ring of bullets (bombbarrelboom) where they were.
    - Bomb Carrier (homingbomblayer) lays Loose Bombs; the blimps drop mines (Munitions Engineers).
    Everything laid or dropped does not count to clear the room (the carrier or blimp does)."""
    m.types["homingbomb"] = make_type("homingbomb", behavior="kamikaze", onDeath="bombbarrelboom", aggroRadius=30,
                                      note="Homing bomb: no gun in the client. Dives at the player and explodes on contact "
                                           "(bullet ring of bombbarrelboom).")
    m.types["bomberjet"] = make_type("bomberjet", behavior="kamikaze", onDeath="bombbarrelboom", aggroRadius=30,
                                     note="Bomber jet: no gun in the client. Dives at the player and explodes on contact.")
    lay = {"type": "homingbomb", "seconds": 6, "count": 1, "max": 2, "countsForRoomClear": False}
    for tpl in ("homingbomblayer", "homingbomblayercenter"):
        m.types[tpl] = make_type(tpl, periodicSummon=dict(lay),
                                 note="No gun in the client: drops a Loose Bomb every 6 s (reconstruction).")
    m.types["flakblimp"] = make_type("flakblimp", periodicSummon={"type": "flakblimpexploder", "seconds": 9,
                                     "count": 1, "max": 2, "countsForRoomClear": False},
                                     note="No gun in the client: drops a robot mine every 9 s (reconstruction).")
    m.types["flakblimpbig"] = make_type("flakblimpbig", periodicSummon={"type": "flakblimpexploderstrong", "seconds": 9,
                                        "count": 1, "max": 2, "countsForRoomClear": False},
                                        note="No gun in the client: drops a strong robot mine every 9 s (reconstruction).")


# ---------------------------------------------------------------------------------------------- dungeons

def bomb_factory():
    m = DungeonMap("dungeon_bomb", "Bomb Factory", "DungeonBomb", 41, PIECES["dungeonfactory2"],
                   "The Empire's ammunition factory (T2, later T3). Assembly lines with stored bombs that "
                   "explode in a chain, a bomb depot, barracks, the blimp hangar, Overseer Charles and "
                   "The Bomb Maker. Since the 2019 patch the factory drew random rooms every run; here it is "
                   "a fixed version with the typical rooms.")
    rooms = m.chain([
        {"name": "entrance", "w": 12, "h": 10},
        {"name": "assembly_line", "w": 20, "h": 16, "gap": 8},
        {"name": "depot", "w": 16, "h": 14, "dx": 4},
        {"name": "barracks", "w": 20, "h": 14, "dx": -2},
        {"name": "hangar", "w": 24, "h": 16, "gap": 8},
        {"name": "overseer_room", "w": 20, "h": 16, "kind": "miniboss"},
        {"name": "boss_room", "w": 24, "h": 24, "gap": 8, "kind": "boss"},
        {"name": "vault", "w": 12, "h": 8, "gap": 4, "kind": "treasure"},
    ])
    R = m.rooms
    t1 = m.side_room(R["depot"], "depot_treasure", "R", 10, 10)
    t2 = m.side_room(R["hangar"], "hangar_treasure", "L", 10, 10)
    common(m, rooms, 2, "key_bomb", "dungeon_bomb", "bombdungeonsign1", "bombdungeonsign2", "depot")

    m.put(R["assembly_line"], "bombfactoryworker", pat("scatter", 3, seed=3))
    m.put(R["assembly_line"], "bombturret", pat("leftwall", 2, y0=0.35, y1=0.8))
    m.put(R["assembly_line"], "bombbarrel", pat("grid", rows=2, cols=3, y0=0.35, y1=0.65))
    m.put(R["assembly_line"], "bombbarrel", pat("at", pts=[(0.3, 0.85), (0.7, 0.85)]))
    m.put(R["depot"], "homingbomblayer", pat("line", 2, fy=0.75))
    m.put(R["depot"], "homingbomb", pat("scatter", 4, seed=11, y0=0.4, y1=0.7))
    # The bombfuse of the original is a tiny fuse spark (art scale 0.4, 6 tracers with 1.5 range): too small to
    # play with, so the right wall gets two more Factory Turrets.
    m.put(R["depot"], "bombturret", pat("rightwall", 2, y0=0.3, y1=0.7))
    m.put(R["barracks"], "altar", pat("topwall", 2))
    m.put(R["barracks"], "bombguard", pat("line", 3, fy=0.6))
    m.put(R["barracks"], "flakblimpexploder", pat("at", pts=[(0.2, 0.4), (0.8, 0.4)]))
    m.put(R["hangar"], "flakblimp", pat("line", 3, fy=0.75))
    m.put(R["hangar"], "flakblimpexploderstrong", pat("at", pts=[(0.3, 0.5), (0.7, 0.5)]))
    m.put(R["hangar"], "bomberjet", pat("at", pts=[(0.15, 0.9), (0.85, 0.9)]))
    m.put(R["hangar"], "bomberjetexploder", pat("bothwalls", 2, y0=0.4, y1=0.6))
    m.put(R["overseer_room"], "fatblimp", pat("center", fy=0.65),
          note="Overseer Charles (miniboss). No guns in the game: the attack came from the bombs and the helpers.")
    m.types["fatblimp"] = make_type("fatblimp", periodicSummon={"type": "homingbomb", "seconds": 7, "count": 1,
                                                                    "max": 3},
                                    note="Overseer Charles: no guns; here he drops a homing bomb every 7 s "
                                         "(reconstrucao).")
    m.put(R["overseer_room"], "flakblimpbig", pat("at", pts=[(0.2, 0.75), (0.8, 0.75)]))
    m.put(R["overseer_room"], "homingbomblayercenter", pat("center", fy=0.4))
    m.encounter("overseer_room", miniboss=True)

    boss(m, R["boss_room"], "bombboss", "bombbossDEATH",
         barks={"onWake": "bombboss", "onDeath": "bombbossDEATH"},
         extra={"periodicSummon": {"type": "bombbossexploder", "seconds": 5, "count": 2, "max": 6},
                "note": "The Bomb Maker has no guns (0 gunMounts): the attack is the rain of robot mines "
                        "(bombbossexploder). The fire rate is a reconstruction."},
         phases=[{"belowHealth": 0.66, "summon": "bombbossexploder", "summonCount": 3},
                {"belowHealth": 0.33, "summon": "bombbossexploderBIG", "summonCount": 1,
                 "behavior": "charge", "speedMultiplier": 1.2}])
    m.put(R["boss_room"], "bombbarrel", pat("corners", 4, m=0.15))
    barrel_types(m, "bombbarrel", "bombbarrelboom")
    bomb_types(m)
    treasure(m, R["vault"], "bombdungeontreasureBIG")
    m.encounter("vault", wakes="script", note="Big Treasure (DPS chest): breaking the chest in time swaps it for "
                "bombdungeontreasureBIG_reward; otherwise the _dud stays.")
    treasure(m, t1, "bombdungeontreasure")
    treasure(m, t2, "bombdungeontreasure")

    m.gate_before(R["barracks"], "barracks barricade", "bombdungeondestroyablewall", damageable=True)
    m.gate_before(t1, "depot treasure wall", "bombdungeondestroyablewall", damageable=True)
    m.gate_before(t2, "hangar treasure wall", "bombdungeondestroyablewallstrong", damageable=True)
    m.gate_before(R["overseer_room"], "overseer gate", "bombdungeondestroyablewallstrong",
                  opens_after="cleared:hangar")
    m.gate_before(R["boss_room"], "boss gate", "bombdungeondestroyablewallstrong",
                  opens_after="cleared:overseer_room")
    m.gate_before(R["vault"], "vault gate", "bombdungeondestroyablewallstrong", opens_after="death:bombboss")
    add_types(m, ["bombdungeontreasureBIG_reward", "bombdungeontreasureBIG_dud"])
    return finish(m)


def laser_playground():
    m = DungeonMap("dungeon_obstacle", "Laser Playground", "DungeonObstacle", 44, PIECES["dungeonobstacle"],
                   "Feline nobility's obstacle course (T3, later T4): laser traps (Mincer, Grater, "
                   "Dicer, Slicer), bullet conveyors, Bouncers and Harassers. Miniboss The Grinder; boss Lazer, "
                   "the Victorious, an auditorium host, in an arena with rows of bullet conveyors "
                   "(the 2019 patch removed the middle row).")
    rooms = m.chain([
        {"name": "entrance", "w": 12, "h": 10},
        {"name": "grinder_pit", "w": 20, "h": 16, "gap": 8},
        {"name": "graters", "w": 16, "h": 20},
        {"name": "conveyor_corridor", "w": 12, "h": 18, "gap": 4},
        {"name": "slicer", "w": 20, "h": 18, "gap": 4},
        {"name": "grinder_room", "w": 20, "h": 18, "kind": "miniboss"},
        {"name": "boss_stage", "w": 28, "h": 26, "gap": 8, "kind": "boss"},
        {"name": "prize", "w": 12, "h": 8, "gap": 4, "kind": "treasure"},
    ])
    R = m.rooms
    t1 = m.side_room(R["graters"], "side_treasure", "R", 10, 10)
    common(m, rooms, 3, "key_obstacle", "dungeon_obstacle", "obstacledungeonsign1", "obstacledungeonsign2", "grinder_pit")

    m.put(R["grinder_pit"], "turbine", pat("center", fy=0.55), note="Mincer: spins and spits lasers around it")
    m.put(R["grinder_pit"], "buzzard", pat("line", 3, fy=0.85))
    m.put(R["grinder_pit"], "trapbutton", pat("at", pts=[(0.1, 0.1)]), note="button that triggers the traps")
    m.put(R["graters"], "wavemakerW", pat("leftwall", 2, y0=0.3, y1=0.7))
    m.put(R["graters"], "wavemakerN", pat("topwall", 1))
    m.put(R["graters"], "fathuntermini", pat("line", 2, fy=0.5))
    m.put(R["conveyor_corridor"], "bulletbeltchunkyE", pat("leftwall", 3, y0=0.2, y1=0.8))
    m.put(R["conveyor_corridor"], "bulletbeltchunkyW2", pat("rightwall", 3, y0=0.3, y1=0.9))
    m.put(R["slicer"], "giantgate", pat("center", fy=0.35), note="Dicer")
    m.put(R["slicer"], "mazedrawer", pat("at", pts=[(0.2, 0.75), (0.8, 0.75)]), note="Slicer")
    m.put(R["slicer"], "shockwaver", pat("line", 2, fy=0.9))
    m.put(R["slicer"], "fathunter", pat("center", fy=0.6))
    m.put(R["grinder_room"], "wavemaster", pat("center", fy=0.65), miniboss=True)
    m.put(R["grinder_room"], "wavemasterminion", pat("ring", 4, r=0.3, cy=0.6))
    m.put(R["grinder_room"], "wavemaker4_inv", pat("corners", 2, m=0.1))
    m.encounter("grinder_room", miniboss=True)
    m.types["wavemaster"] = make_type("wavemaster", miniboss=True, behavior="turret",
                                      note="The Grinder: miniboss according to the wiki (his loot has no 'miniboss')")

    boss(m, R["boss_stage"], "turbineking", "turbinekingDEATH",
         barks={"onWake": "turbineking", "onDeath": "turbinekingDEATH"},
         phases=[{"belowHealth": 0.75, "summon": "turbinekingattack", "summonCount": 1},
                {"belowHealth": 0.5, "summon": "turbinekingattack2", "summonCount": 1},
                {"belowHealth": 0.25, "summon": "turbinekingattack3", "summonCount": 1}])
    m.put(R["boss_stage"], "turbinekingbuzzard", pat("line", 2, fy=0.5))
    # rows of bullet belts at the bottom and top edges of the stage (middle row removed in 2019)
    m.put(R["boss_stage"], "bulletbeltchunkyN", pat("line", 4, fy=0.08, rot=FACE_PLUS_V))
    m.put(R["boss_stage"], "bulletbeltchunkyS", pat("line", 4, fy=0.96))
    treasure(m, R["prize"], "obstacledungeontreasureBIG")
    m.types["obstacledungeontreasureBIG"] = make_type("obstacledungeontreasureBIG",
                                                      onDeath="obstacledungeontreasureBIG_reward")
    m.encounter("prize", wakes="script")
    treasure(m, t1, "obstacledungeontreasure")

    m.gate_before(R["conveyor_corridor"], "barricade", "obstacledungeondestroyablewall_barricade", damageable=True)
    m.gate_before(t1, "treasure wall", "obstacledungeondestroyablewall", damageable=True)
    m.gate_before(R["grinder_room"], "grinder gate", "obstacledungeondestroyablewallstrong",
                  opens_after="cleared:slicer")
    m.gate_before(R["boss_stage"], "stage gate", "obstacledungeondestroyablewallstrong",
                  opens_after="cleared:grinder_room")
    m.gate_before(R["prize"], "prize gate", "obstacledungeondestroyablewallstrong",
                  opens_after="death:turbineking")
    return finish(m)


def arena(elder=False):
    if not elder:
        m = DungeonMap("dungeon_arena", "The Arena", "DungeonArena1", 64, PIECES["dungeonarena"],
                       "Colosseum (T5). A single arena with waves announced by horns and a cheering crowd. 10 waves; on the 10th "
                       "two identical minibosses come at the same time (March 2019 patch). Then Commodus. The "
                       "the original waves were driven by the invisible units arenamaster/arenawave_*.")
        tier, key, item = 5, "key_arena1", "dungeon_arena1"
    else:
        m = DungeonMap("dungeon_arena2", "The Elder Arena", "DungeonArena2", 65, PIECES["dungeonarena2"],
                       "Elder arena (T10, lightning). After the patch: 15 waves with 2 miniboss phases, 2 s "
                       "less between waves and Death Wish (spotters) faster after the first shot. Boss: Dark "
                       "Sphinx, with laser walls and cannons. The waves were driven by arena2master/arena2wave_*.")
        tier, key, item = 10, "key_arena2", "dungeon_arena2"
    rooms = m.chain([
        {"name": "entrance", "w": 12, "h": 10},
        {"name": "arena", "w": 32, "h": 32, "gap": 8, "kind": "arena"},
        {"name": "prize", "w": 12, "h": 8, "gap": 4, "kind": "treasure"},
    ])
    R = m.rooms
    A = R["arena"]
    common(m, rooms, tier, key, item)
    m.point("center", A, 0.5, 0.5)
    edges = {"N": pat("line", 4, fy=0.95), "S": pat("line", 4, fy=0.06, rot=FACE_PLUS_V),
             "L": pat("leftwall", 4), "O": pat("rightwall", 4)}

    if not elder:
        W = [
            wave("Torcida", [group(A, "arenaswarm", edges["N"] + edges["L"])], 6, "Onda 1"),
            wave("Fumaca", [group(A, "arenacloudleader", pat("line", 2, fy=0.9)), group(A, "arenacloud", edges["O"])]),
            wave("Hecklers", [group(A, "arenalasereye", pat("ring", 4, r=0.4))]),
            wave("Acolitos", [group(A, "arenasniper", pat("corners", 4)), group(A, "arenaswarm", edges["S"])]),
            wave("Bruiser", [group(A, "arenatetra", pat("center", fy=0.8))], message="Wave 5: Bruiser"),
            wave("Carruagens", [group(A, "arenaangel", pat("line", 3, fy=0.9))]),
            wave("Rinocerontes", [group(A, "arenatackler", pat("bothwalls", 2)), group(A, "arenaswarm", edges["N"])]),
            wave("Tubaroes", [group(A, "arenawhaleshark", pat("line", 2, fy=0.85))]),
            wave("Tudo junto", [group(A, "arenasniper", pat("corners", 2)), group(A, "arenalasereye", pat("line", 2, fy=0.9)),
                                group(A, "arenacloudleader", pat("center", fy=0.85))]),
            wave("Two minibosses", [group(A, "arenatetra", pat("at", pts=[(0.3, 0.8), (0.7, 0.8)]))],
                 message="Wave 10: two Bruisers!"),
            wave("Bonus", [group(A, "bonusswarm", pat("ring", 3, r=0.25))], 3, "Pinata bonus!"),
            wave("Commodus", [group(A, "swarmboss", pat("center", fy=0.75))], 6, "Commodus enters the arena!"),
        ]
        m.encounter("arena", wakes="onEnter", closesOnEnter=True, waves=W, boss=True,
                    controllers=["arenamaster", "arenamasterbaby", "arenawave_harassers_1..3", "arenawave_swarm_1..3",
                                   "arenaswarmspawner", "arenaswarminstantleader1..4", "swarmbossswarmspawner"])
        m.types["swarmboss"] = make_type("swarmboss", onDeath="swarmbossDEATH",
                                         phases=[{"belowHealth": 0.7, "summon": "swarmbossswarm", "summonCount": 6},
                                                {"belowHealth": 0.4, "summon": "swarmbossswarm", "summonCount": 8},
                                                {"belowHealth": 0.15, "summon": "swarmbosstrap", "summonCount": 2}])
        m.point("boss", A, 0.5, 0.75)
        m.point("portal", A, 0.5, 0.4, 0, note="exit carrier")
        m.meta["exit"] = {"point": "portal", "when": "death:swarmboss"}
        treasure(m, R["prize"], "arenadungeontreasureBIG")
        m.types["arenadungeontreasureBIG"] = make_type("arenadungeontreasureBIG", onDeath="arenadungeontreasureBIG_reward")
        m.put(R["entrance"], "arenadungeontreasure", pat("at", pts=[(0.15, 0.85)]))
        m.gate_before(R["prize"], "prize gate", "arena2_destructiblewall1", opens_after="death:swarmboss")
    else:
        W = []
        plan = [
            ("Hornets", [("arena2beehex", pat("line", 4, fy=0.9))]),
            ("Underdogs", [("arena2stardog", edges["L"] + edges["O"])]),
            ("Laser X", [("arena2lasereye", pat("ring", 4, r=0.4))]),
            ("Death Wish", [("arena2spotter", pat("corners", 4))]),
            ("Ninjas", [("arena2warper", pat("line", 2, fy=0.85)), ("arena2beehex", pat("line", 3, fy=0.15, rot=FACE_PLUS_V))]),
            ("Hex", [("arena2tripodminionmaster", pat("line", 2, fy=0.8))]),
            ("Mistura", [("arena2stardog", pat("line", 3, fy=0.9)), ("arena2lasereye", pat("bothwalls", 2))]),
            ("Miniboss: Corpse Pulveriser", [("arena2tripod", pat("center", fy=0.75))]),
            ("Death Wish 2", [("arena2spotter", pat("ring", 6, r=0.42))]),
            ("Riddles", [("arenacubespiral", pat("line", 2, fy=0.85)), ("arenacubetendrilrandom", pat("corners", 4))]),
            ("Ninjas 2", [("arena2warper", pat("corners", 4))]),
            ("Hornets 2", [("arena2beehex", pat("ring", 6, r=0.4)), ("arena2stardog", pat("line", 2, fy=0.9))]),
            ("Laser X 2", [("arena2lasereye", pat("corners", 4)), ("arenacubelasercross", pat("center", fy=0.8))]),
            ("Tudo junto", [("arena2spotter", pat("line", 3, fy=0.9)), ("arena2warper", pat("bothwalls", 2)),
                            ("arena2tripodminionmaster", pat("center", fy=0.85))]),
            ("Miniboss: Chains", [("arena2ballnchain", pat("at", pts=[(0.3, 0.75), (0.7, 0.75)]))]),
        ]
        for i, (name, gs) in enumerate(plan):
            W.append(wave(name, [group(A, t, p) for t, p in gs], 4 if i else 6, "Onda %d: %s" % (i + 1, name)))
        W.append(wave("Bonus", [group(A, "bonusswarm", pat("ring", 3, r=0.25))], 3, "Pinata bonus!"))
        W.append(wave("Dark Sphinx", [group(A, "arenacube", pat("center", fy=0.6)),
                                      group(A, "arenacubenode", pat("corners", 4, m=0.15))], 6, "The Dark Sphinx awakens!"))
        m.encounter("arena", wakes="onEnter", closesOnEnter=True, waves=W, boss=True,
                    controllers=["arena2master", "arena2masterbaby", "arena2wave_harassers_1..3", "arena2wave_swarm_1..3",
                                   "arena2spotterspawner", "arenacubenodemaster1..3", "arena2ballnchainsleeping"])
        m.types["arenacube"] = make_type("arenacube", onDeath="arenacubeDEATH",
                                         phases=[{"belowHealth": 0.75, "summon": "arenacubebiglaserwall", "summonCount": 1},
                                                {"belowHealth": 0.5, "summon": "arenacubemini", "summonCount": 2},
                                                {"belowHealth": 0.3, "summon": "arenacubebiglaserwall2", "summonCount": 1},
                                                {"belowHealth": 0.15, "summon": "arenacubetendril", "summonCount": 3}])
        m.put(A, "beartrap", pat("ring", 4, r=0.2, a0=45), note="Sphinx traps on the floor")
        m.point("boss", A, 0.5, 0.6)
        m.point("portal", A, 0.5, 0.3, 0, note="porta-avioes de saida")
        m.meta["exit"] = {"point": "portal", "when": "death:arenacube"}
        m.gate_before(R["prize"], "prize gate", "arena2_destructiblewall1", opens_after="death:arenacube")
        m.put(R["prize"], "arena2_destructiblewall3", pat("at", pts=[(0.5, 0.9)]),
              note="no chest of its own in 190828: the loot comes from the Dark Sphinx")
    return finish(m)


def home_for_special_children():
    m = DungeonMap("dungeon_grind", "Home for Special Children", "DungeonGrind", 43, PIECES["dungeongrindhouse"],
                   "Dr. Geckelston's radioactive orphanage-laboratory (T6, lightning). A nursery with Orphans that evolve, "
                   "an infirmary with Blood Nurses on rails, a corridor of electric conveyors, a radioactive wing, the "
                   "Nannies' daycare (miniboss N.A.N.A.) and the doctor's laboratory.")
    rooms = m.chain([
        {"name": "entrance", "w": 12, "h": 10},
        {"name": "nursery", "w": 20, "h": 16, "gap": 8},
        {"name": "infirmary", "w": 24, "h": 18},
        {"name": "electric_corridor", "w": 12, "h": 20, "gap": 4},
        {"name": "radioactive_wing", "w": 20, "h": 16, "gap": 4, "dx": 3},
        {"name": "daycare", "w": 22, "h": 18, "kind": "miniboss"},
        {"name": "laboratory", "w": 26, "h": 26, "gap": 8, "kind": "boss"},
    ])
    R = m.rooms
    t1 = m.side_room(R["infirmary"], "patients_room", "L", 12, 10, kind="side")
    t2 = m.side_room(R["radioactive_wing"], "sample_storage", "R", 10, 10)
    common(m, rooms, 6, "key_grind", "dungeon_grind", "grindhousedungeonsign1", "grindhousedungeonsign2", "infirmary")

    m.put(R["nursery"], "evohatcher", pat("line", 2, fy=0.8), note="Unstable Womb: hatches Orphans")
    m.put(R["nursery"], "evo", pat("scatter", 4, seed=5, y0=0.3, y1=0.7))
    m.put(R["nursery"], "evo2", pat("center", fy=0.6))
    # rail nurses: the 10 nodes of rail A make a loop in the ward
    E = R["infirmary"]
    loop = [(0.15, 0.2), (0.5, 0.15), (0.85, 0.2), (0.9, 0.5), (0.85, 0.8), (0.5, 0.88), (0.15, 0.8), (0.1, 0.5),
            (0.3, 0.5), (0.7, 0.5)]
    route = []
    for fx, fy in loop:
        u, v = E.uv(fx, fy)
        route.append({"u": u, "v": v})
    m.route["trilhoA"] = route
    m.put(E, "raildocA", pat("at", pts=[(0.15, 0.2)]), route="trilhoA", behavior="patrol",
          note="Blood Nurse on rail A (the units raildocA_node_1..10 marked the path)")
    m.put(E, "raildocAmini", pat("at", pts=[(0.85, 0.8)]), route="trilhoA", behavior="patrol")
    m.put(E, "docmini", pat("line", 3, fy=0.5))
    m.put(E, "spiral", pat("at", pts=[(0.5, 0.75)]), note="Orderly")
    m.put(t1, "raildoc_patient", pat("line", 3, fy=0.6), note="Patients (they do not attack)")
    m.put(t1, "dochatcher", pat("center", fy=0.85))
    m.put(R["electric_corridor"], "electricbeltA_start_6", pat("leftwall", 2, y0=0.25, y1=0.75))
    m.put(R["electric_corridor"], "electricbeltB_start_6", pat("rightwall", 2, y0=0.4, y1=0.9))
    m.put(R["electric_corridor"], "teslaclone", pat("center", fy=0.9), note="Shock Doc")
    m.put(R["radioactive_wing"], "zombiehatcher", pat("corners", 2, m=0.15), note="Radiant Womb")
    m.put(R["radioactive_wing"], "zombieshatcherzombie", pat("scatter", 4, seed=9))
    m.put(R["radioactive_wing"], "evo3", pat("center", fy=0.75), note="Monstrous Orphan")
    m.put(R["radioactive_wing"], "doc", pat("line", 2, fy=0.45))
    m.put(t2, "grindhousedungeontreasure", pat("center", fy=0.6))
    m.put(R["daycare"], "cloudleaderbig", pat("center", fy=0.7), miniboss=True, note="N.A.N.A.")
    m.put(R["daycare"], "cloudleader", pat("line", 2, fy=0.5))
    m.put(R["daycare"], "cloud", pat("scatter", 4, seed=21))
    m.types["cloudleaderbig"] = make_type("cloudleaderbig", barks={"onWake": "cloudleaderbig"},
                                          phases=[{"belowHealth": 0.5, "summon": "cloudleaderbigminion", "summonCount": 3}])
    m.encounter("daycare", miniboss=True)

    boss(m, R["laboratory"], "summonermaster", "summonermasterDEATH",
         barks={"onWake": "summonermaster", "onDeath": "summonermasterDEATH"},
         phases=[{"belowHealth": 0.8, "summon": "summoner", "summonCount": 1},
                {"belowHealth": 0.6, "summon": "summonercentipede01", "summonCount": 1},
                {"belowHealth": 0.45, "summon": "summoner2", "summonCount": 1},
                {"belowHealth": 0.3, "summon": "summonerzombie28days", "summonCount": 2},
                {"belowHealth": 0.15, "summon": "summonerzombiefinal", "summonCount": 2,
                 "message": "Geckel-Clones!"}])
    m.put(R["laboratory"], "summonerhunter", pat("line", 2, fy=0.45), note="Flesh Disposal")
    m.put(R["laboratory"], "summonersquirrel", pat("corners", 2, m=0.2), note="Sticky Fledgling")

    m.gate_before(R["electric_corridor"], "wall", "grindhousedungeondestroyablewall", damageable=True)
    m.gate_before(t2, "sample wall", "grindhousedungeondestroyablewallstrong", damageable=True)
    m.gate_before(R["daycare"], "daycare gate", "grindhousedungeondestroyablewallstrong", opens_after="cleared:radioactive_wing")
    m.gate_before(R["laboratory"], "laboratory gate", "grindhousedungeondestroyablewallstrong",
                  opens_after="cleared:daycare")
    return finish(m)


def crypt_of_cathulhu():
    m = DungeonMap("dungeon_eye", "Crypt of Cathulhu", "DungeonEye", 42, PIECES["dungeonpalace"],
                   "Ancient temple of the Deep Ones (T9, water). Eye rooms: Laser Eyes, fixed laser pointers, "
                   "spinning lasers, Gazers (5-shot laser at once), Scanners (a lot of damage up close), Cat Eyes "
                   "in orbit and Poppers. Two Psychopus (minibosses) and the Cyclopus in a room with rotating lasers.")
    rooms = m.chain([
        {"name": "entrance", "w": 12, "h": 10},
        {"name": "eye_gallery", "w": 20, "h": 16, "gap": 8},
        {"name": "gazer_hall", "w": 22, "h": 18},
        {"name": "laser_corridor", "w": 12, "h": 20, "gap": 4},
        {"name": "atrium", "w": 20, "h": 16, "gap": 4},
        {"name": "cyclopus_hall", "w": 28, "h": 28, "gap": 8, "kind": "boss"},
    ])
    R = m.rooms
    pL = m.side_room(R["atrium"], "psychopus_chamber_1", "L", 16, 16, gap=6, kind="miniboss")
    pR = m.side_room(R["atrium"], "psychopus_chamber_2", "R", 16, 16, gap=6, kind="miniboss")
    t1 = m.side_room(R["gazer_hall"], "treasure", "R", 10, 10)
    common(m, rooms, 9, "key_eye", "dungeon_eye", "eyedungeonsign1", "eyedungeonsign2", "eye_gallery")

    m.put(R["eye_gallery"], "lasereye", pat("line", 3, fy=0.8))
    m.put(R["eye_gallery"], "laserpointer_E", pat("leftwall", 2, y0=0.3, y1=0.7))
    m.put(R["eye_gallery"], "laserpointer_W", pat("rightwall", 2, y0=0.4, y1=0.8))
    m.put(R["eye_gallery"], "popper", pat("center", fy=0.5))
    m.put(R["gazer_hall"], "triguardleader", pat("center", fy=0.75), note="Gazer leader")
    m.put(R["gazer_hall"], "triguard", pat("ring", 3, r=0.25, cy=0.6))
    m.put(R["gazer_hall"], "scanner", pat("corners", 2, m=0.12))
    m.put(R["gazer_hall"], "orbiter", pat("ring", 4, r=0.15, cy=0.35), note="Cat Eyes (they orbit the orbitermaster)")
    m.put(t1, "eyedungeontreasure", pat("center", fy=0.6))
    m.put(R["laser_corridor"], "laserspinner_CW_N", pat("at", pts=[(0.5, 0.3)]))
    m.put(R["laser_corridor"], "laserspinner_CCW_N", pat("at", pts=[(0.5, 0.75)]))
    m.put(R["laser_corridor"], "poppermini", pat("line", 2, fy=0.5))
    m.put(R["atrium"], "warper", pat("line", 2, fy=0.8), note="Watchers")
    m.put(R["atrium"], "teslaeye", pat("center", fy=0.5))
    m.put(R["atrium"], "popper2", pat("corners", 4, m=0.15))
    m.put(pL, "cyclopus", pat("center", fy=0.55), miniboss=True)
    m.put(pL, "cyclopuslaser", pat("ring", 3, r=0.3, cy=0.55))
    m.put(pR, "cyclopus2", pat("center", fy=0.55), miniboss=True)
    m.put(pR, "cyclopuslaser", pat("ring", 3, r=0.3, cy=0.55, a0=60))
    m.encounter(pL.name, miniboss=True)
    m.encounter(pR.name, miniboss=True)

    boss(m, R["cyclopus_hall"], "octopus", "octopusDEATH",
         barks={"onWake": "octopus", "onDeath": "octopusDEATH"},
         phases=[{"belowHealth": 0.66, "summon": "octopuslaser2", "summonCount": 2},
                {"belowHealth": 0.33, "summon": "octopuslaser3", "summonCount": 2}])
    m.put(R["cyclopus_hall"], "laserspinner_long_CW_N", pat("at", pts=[(0.25, 0.4)]),
          note="'can spawn in a room with rotating lasers' (wiki)")
    m.put(R["cyclopus_hall"], "laserspinner_long_CCW_N", pat("at", pts=[(0.75, 0.4)]))
    m.put(R["cyclopus_hall"], "octopuslaser", pat("line", 2, fy=0.9), note="Watchers")

    m.gate_before(t1, "treasure wall", "eyedungeondestroyablewall", damageable=True)
    m.gate_before(R["laser_corridor"], "barricade", "eyedungeondestroyablewall_barricade", damageable=True)
    m.gate_before(R["cyclopus_hall"], "cyclopus gate", "eyedungeondestroyablewallstrong",
                  opens_after="cleared:psychopus_chamber_1+cleared:psychopus_chamber_2")
    return finish(m)


def reeducation_camp():
    m = DungeonMap("dungeon_fire", "Reeducation Camp", "DungeonFire", 36, PIECES["dungeonfire"],
                   "Reeducation camp (T10, fire): a courtyard with shielded MaxSec Guards and sniper towers, "
                   "cells with prisoners, a furnace with flamethrowers and lava pillars, a corridor of heat vents "
                   "(heat vents), a 'therapy' room and the 'anti-rush barricades' of the patch. Miniboss Converted "
                   "Prisoner; boss Traitor Phoenix, who is reborn from the egg twice (phoenix -> egg -> phoenix3 -> "
                   "egg -> phoenix5). The boss room used the fireboss territory (71).")
    rooms = m.chain([
        {"name": "entrance", "w": 12, "h": 10},
        {"name": "courtyard", "w": 24, "h": 18, "gap": 8},
        {"name": "cell_block", "w": 20, "h": 16},
        {"name": "heat_corridor", "w": 12, "h": 22, "gap": 4},
        {"name": "furnace", "w": 22, "h": 18, "gap": 4},
        {"name": "therapy_room", "w": 18, "h": 14, "dx": -3},
        {"name": "mess_hall", "w": 22, "h": 18, "kind": "miniboss"},
        {"name": "phoenix_nest", "w": 28, "h": 28, "gap": 8, "kind": "boss"},
    ])
    R = m.rooms
    cel = m.side_room(R["cell_block"], "cells", "L", 12, 12)
    t1 = m.side_room(R["furnace"], "depot", "R", 10, 10)
    common(m, rooms, 10, "key_fire", "dungeon_fire", "firedungeonsign1", "firedungeonsign2", "cell_block")
    m.values["bossTerritory"] = 71

    m.put(R["courtyard"], "riotguard", pat("line", 3, fy=0.65), note="MaxSec Guard (with riotguardshield)")
    m.put(R["courtyard"], "riotguardshield", pat("line", 3, fy=0.58))
    m.put(R["courtyard"], "riotguardleader", pat("center", fy=0.85))
    m.put(R["courtyard"], "prisonsniper", pat("corners", 4, m=0.06), note="Sniper Tower (wall unit)")
    m.put(R["courtyard"], "scavengerleader", pat("at", pts=[(0.5, 0.35)]), note="Prison Klaxon: calls the Fire Guards")
    m.put(R["courtyard"], "scavenger", pat("line", 2, fy=0.3))
    m.put(R["cell_block"], "flamebat", pat("scatter", 4, seed=13), note="Mad prisoners")
    m.put(R["cell_block"], "scavengerclose", pat("line", 2, fy=0.8))
    m.put(cel, "prisoncage", pat("line", 3, fy=0.7))
    m.put(cel, "prisonescapee", pat("line", 3, fy=0.4), note="prisoners: released when the cage breaks")
    m.put(cel, "firedungeontreasure", pat("center", fy=0.9))
    m.put(R["heat_corridor"], "heatventA", pat("leftwall", 2, y0=0.2, y1=0.6))
    m.put(R["heat_corridor"], "heatventB", pat("rightwall", 2, y0=0.3, y1=0.7))
    m.put(R["heat_corridor"], "heatventC", pat("leftwall", 1, y0=0.85, y1=0.85))
    m.put(R["heat_corridor"], "heatventD", pat("rightwall", 1, y0=0.9, y1=0.9))
    m.encounter("heat_corridor", controllers=["trapmaster_fast"],
                note="Vents A-D light up in sequence (trapmaster of DungeonTraps)")
    m.put(R["furnace"], "firesprinkler", pat("center", fy=0.6), note="Furnace")
    m.put(R["furnace"], "fireeyecorner", pat("corners", 2, m=0.08))
    m.put(R["furnace"], "fireeyerotate", pat("at", pts=[(0.5, 0.25)]))
    m.put(R["furnace"], "lavapillar", pat("at", pts=[(0.25, 0.45), (0.75, 0.45), (0.5, 0.85)]))
    m.put(R["furnace"], "firebarrel", pat("line", 2, fy=0.9), note="Flame Probe Cache")
    m.put(t1, "firebarrel", pat("line", 2, fy=0.3))
    # The fire ring has 24 shots at 1 per second in the data; here it lasts 3 s so it cannot fill the room.
    barrel_types(m, "firebarrel", "firebarrelboom", durationSeconds=3.0)
    m.put(t1, "firedungeontreasure", pat("center", fy=0.7))
    m.put(R["therapy_room"], "dronette", pat("line", 3, fy=0.7), note="Augmentation Therapist")
    m.put(R["therapy_room"], "curleader", pat("center", fy=0.4), note="Fire Strafer")
    m.put(R["mess_hall"], "flamebatleader", pat("center", fy=0.7), miniboss=True, note="Converted Prisoner")
    m.put(R["mess_hall"], "flamebat", pat("ring", 5, r=0.3, cy=0.6))
    m.types["flamebatleader"] = make_type("flamebatleader", barks={"onWake": "flamebatleader_0", "phase": "flamebatleader_1"})
    m.encounter("mess_hall", miniboss=True)

    boss(m, R["phoenix_nest"], "phoenix", None,
         barks={"onWake": "phoenix1"},
         extra={"sequence": ["phoenix2", "phoenix3", "phoenix4", "phoenix5"],
                "note": "Traitor Phoenix: on death it becomes an egg (phoenix2, invulnerable), is reborn (phoenix3), becomes an egg "
                        "again (phoenix4) and returns as phoenix5, which has the loot. Only phoenix5 leaves the phoenixDEATH."})
    m.types["phoenix2"] = make_type("phoenix2", barks={"onWake": "phoenix2"}, durationSeconds=6)
    m.types["phoenix3"] = make_type("phoenix3", barks={"onWake": "phoenix3"})
    m.types["phoenix4"] = make_type("phoenix4", barks={"onWake": "phoenix4"}, durationSeconds=6)
    m.types["phoenix5"] = make_type("phoenix5", onDeath="phoenixDEATH",
                                    barks={"onWake": "phoenix5", "onDeath": "phoenixDEATH"})
    m.meta["exit"] = {"point": "portal", "when": "death:phoenix5"}
    m.put(R["phoenix_nest"], "lavapillar", pat("corners", 4, m=0.15))

    m.gate_before(R["cell_block"], "anti-rush barricade 1", "prisondestroyablewall", damageable=True)
    m.gate_before(R["furnace"], "anti-rush barricade 2", "prisondestroyablewall", damageable=True)
    m.gate_before(t1, "depot wall", "prisondestroyablewall", damageable=True)
    m.gate_before(R["mess_hall"], "mess hall gate", "prisondestroyablewall", opens_after="cleared:therapy_room")
    m.gate_before(R["phoenix_nest"], "phoenix gate", "prisondestroyablewall", opens_after="cleared:mess_hall")
    return finish(m)


def frozen_owl_monastery():
    m = DungeonMap("dungeon_ice", "Frozen Owl Monastery", "DungeonIce", 31, PIECES["dungeonruinssnow"],
                   "Frozen owl monastery (T10, ice). Husky raiders (some frozen, who wake up), "
                   "Frost Serpent nests, a blizzard hall (Blizzard + Blizzard Generator + Ice Nodes), "
                   "Cryosects, Snow Banshees and Frost Giants. Miniboss Husky Gang Leader; boss The Frozen Horror, "
                   "standing in the center with 6 ice nodes around. The boss room used the iceboss territory (72).")
    rooms = m.chain([
        {"name": "entrance", "w": 12, "h": 10},
        {"name": "cloister", "w": 22, "h": 18, "gap": 8},
        {"name": "nests", "w": 20, "h": 16, "dx": 3},
        {"name": "blizzard_hall", "w": 24, "h": 20, "gap": 6},
        {"name": "library", "w": 18, "h": 16, "dx": -3},
        {"name": "husky_camp", "w": 22, "h": 18, "kind": "miniboss"},
        {"name": "hibernation_chamber", "w": 28, "h": 28, "gap": 8, "kind": "boss"},
    ])
    R = m.rooms
    t1 = m.side_room(R["cloister"], "frozen_cell", "L", 10, 10)
    t2 = m.side_room(R["library"], "reliquary", "R", 10, 10)
    common(m, rooms, 10, "key_ice", "dungeon_ice", "icedungeonsign1", "icedungeonsign2", "cloister")
    m.values["bossTerritory"] = 72

    m.put(R["cloister"], "husky", pat("line", 3, fy=0.7))
    m.put(R["cloister"], "huskyfreezing", pat("at", pts=[(0.2, 0.4), (0.8, 0.4)]), note="Frozen Husky")
    m.put(R["cloister"], "mountaingruntleader", pat("center", fy=0.85), note="Cryosect")
    m.put(R["cloister"], "mountaingrunt", pat("ring", 3, r=0.15, cy=0.85))
    m.put(t1, "huskyfreezingminion", pat("line", 2, fy=0.4))
    m.put(t1, "icedungeontreasure", pat("center", fy=0.75))
    m.put(R["nests"], "icesnakeegg", pat("grid", rows=2, cols=3, y0=0.4, y1=0.75))
    m.put(R["nests"], "icesnakeleader", pat("center", fy=0.9))
    m.put(R["nests"], "icesnake", pat("line", 2, fy=0.25))
    m.put(R["blizzard_hall"], "blizzardcore", pat("center", fy=0.6), note="Blizzard Generator")
    m.put(R["blizzard_hall"], "blizzard", pat("at", pts=[(0.25, 0.6), (0.75, 0.6)]))
    m.put(R["blizzard_hall"], "blizzardiciclesmaster", pat("corners", 4, m=0.1), note="Ice Nodes")
    m.put(R["blizzard_hall"], "hedgeleader2", pat("line", 2, fy=0.3), note="Ice Fountain")
    m.put(R["blizzard_hall"], "icecrystalA", pat("leftwall", 1, y0=0.85, y1=0.85))
    m.put(R["blizzard_hall"], "icecrystalB", pat("rightwall", 1, y0=0.85, y1=0.85))
    m.put(R["library"], "mountainhunter", pat("line", 2, fy=0.75), note="Snow Banshee")
    m.put(R["library"], "frostgiant", pat("center", fy=0.5))
    m.put(R["library"], "hedgeminion2", pat("scatter", 3, seed=17))
    m.put(t2, "icedungeontreasure", pat("center", fy=0.6))
    m.put(R["husky_camp"], "huskyleader", pat("center", fy=0.7), miniboss=True)
    m.put(R["husky_camp"], "husky", pat("ring", 4, r=0.3, cy=0.6))
    m.put(R["husky_camp"], "frostgiant_noxp", pat("line", 2, fy=0.9))
    m.types["huskyleader"] = make_type("huskyleader", barks={"onWake": "huskyleader_0", "phase": "huskyleader_3"})
    m.encounter("husky_camp", miniboss=True)

    boss(m, R["hibernation_chamber"], "iceboss", "icebossDEATH", fy=0.6,
         barks={"onWake": "iceboss_0", "phase": "iceboss_1", "onDeath": "icebossDEATH"},
         phases=[{"belowHealth": 0.8, "summon": "icebossfrostmissile", "summonCount": 2},
                {"belowHealth": 0.6, "summon": "icebosshailstorm", "summonCount": 1},
                {"belowHealth": 0.45, "summon": "icebosssnakeleader", "summonCount": 2},
                {"belowHealth": 0.3, "summon": "icebossicestorm", "summonCount": 1},
                {"belowHealth": 0.15, "summon": "icebosssnowflake", "summonCount": 3}])
    nodes = pat("ring", 6, r=0.33, cy=0.6)
    for i, p in enumerate(nodes):
        m.put(R["hibernation_chamber"], "icebossnode%d" % (i + 1), [p])
    m.encounter("hibernation_chamber", controllers=["icebossnodemaster"],
                note="6 ice nodes around the boss; the icebossnodemaster coordinated them")

    m.gate_before(t1, "cell wall", "icedungeondestroyablewall", damageable=True)
    m.gate_before(t2, "reliquary wall", "icedungeondestroyablewall", damageable=True)
    m.gate_before(R["husky_camp"], "camp gate", "icedungeondestroyablewall",
                  opens_after="cleared:library")
    m.gate_before(R["hibernation_chamber"], "chamber gate", "icedungeondestroyablewall",
                  opens_after="cleared:husky_camp")
    return finish(m)


def venom_compound():
    m = DungeonMap("dungeon_snake", "Venom Compound", "DungeonSnake", 34, PIECES["industrialboss"],
                   "Venom factory / corrupted Egg Temple (T10, poison). Conveyors carrying eggs, poison barriers, "
                   "Cargo Snakes, Offering Shepherds and Scanner Drones. Miniboss Poison Convoy (head, "
                   "neck and cars). Boss: starts as a Giant Egg and hatches the Poison God, with engines and body.")
    rooms = m.chain([
        {"name": "entrance", "w": 12, "h": 10},
        {"name": "conveyors", "w": 24, "h": 18, "gap": 8},
        {"name": "barriers", "w": 16, "h": 18, "gap": 4},
        {"name": "incubator", "w": 22, "h": 18},
        {"name": "convoy_line", "w": 14, "h": 30, "gap": 4, "kind": "miniboss"},
        {"name": "egg_altar", "w": 30, "h": 28, "gap": 8, "kind": "boss"},
    ])
    R = m.rooms
    t1 = m.side_room(R["conveyors"], "cargo_depot", "R", 10, 10)
    t2 = m.side_room(R["incubator"], "egg_chamber", "L", 12, 12)
    common(m, rooms, 10, "key_snake", "dungeon_snake", "snakedungeonsign1", "snakedungeonsign2", "conveyors")

    E = R["conveyors"]
    m.put(E, "conveyorbelt_straight_N_12", pat("at", pts=[(0.25, 0.1, FACE_PLUS_V), (0.75, 0.1, FACE_PLUS_V)]),
          note="conveyors: carry eggs and turrets upward")
    m.put(E, "conveyorbelt_egg", pat("at", pts=[(0.25, 0.4), (0.75, 0.6)]))
    m.put(E, "snakeleader", pat("center", fy=0.75), note="Cargo Collector")
    m.put(E, "snake", pat("line", 3, fy=0.55))
    m.put(E, "snaketurret", pat("at", pts=[(0.5, 0.3)]), note="Scanner Drone")
    m.put(t1, "snakedungeontreasure", pat("center", fy=0.6))
    m.put(R["barriers"], "wallsnakeleader2", pat("line", 2, fy=0.3), note="Venom Barrier")
    m.put(R["barriers"], "wallsnake2", pat("line", 3, fy=0.5))
    m.put(R["barriers"], "wallsnakeleader3", pat("line", 2, fy=0.75), note="Containment Barrier")
    m.put(R["barriers"], "hurtsnake2", pat("scatter", 3, seed=4), note="Factory Security")
    m.put(R["incubator"], "speedsnakeleader", pat("center", fy=0.8), note="Offering Shepherd")
    m.put(R["incubator"], "speedsnake", pat("line", 3, fy=0.6))
    m.put(R["incubator"], "snakebig", pat("at", pts=[(0.2, 0.35), (0.8, 0.35)]))
    m.put(R["incubator"], "poisonventA", pat("leftwall", 1))
    m.put(R["incubator"], "poisonventC", pat("rightwall", 1))
    m.put(t2, "snakeegg2", pat("line", 3, fy=0.4))
    m.put(t2, "snakeegg3", pat("line", 2, fy=0.65))
    m.put(t2, "snakedungeontreasure", pat("center", fy=0.9))
    L = R["convoy_line"]
    m.put(L, "coilsnake01", pat("at", pts=[(0.5, 0.9)]), miniboss=True, note="Poison Convoy (head)")
    m.put(L, "coilsnake01neck", pat("at", pts=[(0.5, 0.82)]))
    m.put(L, "coilsnake02", pat("at", pts=[(0.5, 0.72), (0.5, 0.62)]))
    m.put(L, "coilsnake03", pat("at", pts=[(0.5, 0.52)]))
    m.put(L, "magnetbeltP_N", pat("leftwall", 2, y0=0.2, y1=0.5), note="Poison Belt")
    m.put(L, "magnetbeltP_S", pat("rightwall", 2, y0=0.3, y1=0.6))
    m.encounter("convoy_line", miniboss=True)

    B = R["egg_altar"]
    m.point("boss", B, 0.5, 0.65)
    m.point("portal", B, 0.5, 0.3, 0, note="porta-avioes de saida")
    m.put(B, "dragonsnakeegg", pat("center", fy=0.65), note="Giant Egg: when broken the Poison God hatches")
    m.types["dragonsnakeegg"] = make_type("dragonsnakeegg", sequence=["dragonsnake01"], barks={"onWake": "dragonsnake01_0"})
    m.types["dragonsnake01"] = make_type("dragonsnake01", onDeath="dragonsnakeDEATH",
                                         barks={"onWake": "dragonsnake01_0", "phase": "dragonsnake01_1",
                                                "onDeath": "dragonsnakeDEATH"},
                                         phases=[{"belowHealth": 0.8, "summon": "dragonsnake02", "summonCount": 2,
                                                 "message": "Poison God Engines!"},
                                                {"belowHealth": 0.55, "summon": "dragonsnakeminion01", "summonCount": 4},
                                                {"belowHealth": 0.35, "summon": "dragonsnake03_big", "summonCount": 2},
                                                {"belowHealth": 0.15, "summon": "dragonsnakeminion02", "summonCount": 6,
                                                 "behavior": "charge"}])
    m.put(B, "conveyorbeltZ_N", pat("corners", 4, m=0.08))
    m.encounter("egg_altar", wakes="onEnter", boss=True, closesOnEnter=True, controllers=["dragonsnakemaster"])
    m.meta["exit"] = {"point": "portal", "when": "death:dragonsnake01"}

    m.gate_before(R["barriers"], "barricade", "snakedungeondestroyablewall_barricade", damageable=True)
    m.gate_before(t1, "depot wall", "snakedungeondestroyablewall", damageable=True)
    m.gate_before(t2, "chamber wall", "snakedungeondestroyablewallstrong", damageable=True)
    m.gate_before(R["convoy_line"], "conveyor wall", "belt_start_wall", opens_after="cleared:incubator")
    m.gate_before(R["egg_altar"], "altar gate", "belt_end_wall", opens_after="cleared:convoy_line")
    return finish(m)


def the_vaults():
    m = DungeonMap("dungeon_thief", "The Vaults", "DungeonThief", 59, PIECES["dungeonthief"],
                   "The Empire's vaults (T10, poison). Quest 'Become Worthy of Treasure!': the Strange Altar to "
                   "Horus is near the entrance; go through the 3 Security Rooms (Security Chief 001/002/003, "
                   "signs 'Security Room: Level 00X'), take the Golden Key at the end, escort the key back to the "
                   "altar while Guard Dropships ambush you, touch the Essence of Worthiness and open the treasure. "
                   "Lasers, laser grids, X-Mines and delivery carts on the way. No final boss.")
    rooms = m.chain([
        {"name": "entrance", "w": 12, "h": 10},
        {"name": "altar_hall", "w": 22, "h": 18, "gap": 6},
        {"name": "laser_corridor", "w": 12, "h": 22, "gap": 4},
        {"name": "security_001", "w": 20, "h": 18, "gap": 4, "kind": "miniboss"},
        {"name": "rails", "w": 22, "h": 18, "gap": 6},
        {"name": "security_002", "w": 20, "h": 18, "gap": 6, "kind": "miniboss"},
        {"name": "minefield", "w": 22, "h": 18, "gap": 6},
        {"name": "security_003", "w": 20, "h": 18, "gap": 6, "kind": "miniboss"},
        {"name": "key_chamber", "w": 16, "h": 14, "gap": 6, "kind": "room"},
    ])
    R = m.rooms
    t1 = m.side_room(R["rails"], "side_vault", "R", 10, 10)
    common(m, rooms, 10, "key_thief", "dungeon_thief", "thiefdungeonsign1", "thiefdungeonsign2", "rails")
    m.meta["quest"] = "dungeon_thief_01"
    m.sign("thiefdungeonsign3", R["security_001"], 0.5, 0.08)
    m.sign("thiefdungeonsign4", R["security_002"], 0.5, 0.08)
    m.sign("thiefdungeonsign6", R["security_003"], 0.5, 0.08)

    A = R["altar_hall"]
    m.point("altar", A, 0.5, 0.6, note="Strange Altar to Horus: receives the key and turns into the treasure")
    m.put(A, "thiefchest", pat("center", fy=0.6), note="Strange Altar to Horus (barks thiefchest_pray, _0.._4)")
    m.types["thiefchest"] = make_type("thiefchest", invulnerable=True, onDeath="thiefchesttreasure",
                                      barks={"onWake": "thiefchest_1", "onKeyReceived": "thiefchest_2",
                                             "onOpen": "thiefchest_4"})
    m.put(A, "thieftrooper", pat("line", 2, fy=0.9), note="Treasure Guard")
    m.put(R["laser_corridor"], "lasergateWE", pat("at", pts=[(0.5, 0.3)]))
    m.put(R["laser_corridor"], "lasergateWE", pat("at", pts=[(0.5, 0.7)]))
    m.put(R["laser_corridor"], "shockeye", pat("bothwalls", 2, y0=0.5, y1=0.5))
    for i, (name, tpl, idol) in enumerate([("security_001", "keylock", "thiefidol1"),
                                           ("security_002", "keylock2", "thiefidol2"),
                                           ("security_003", "keylock3", "thiefidol3")]):
        S = R[name]
        m.put(S, tpl, pat("center", fy=0.65), miniboss=True, note="Security Chief 00%d" % (i + 1))
        m.put(S, "keynode", pat("ring", 3, r=0.3, cy=0.6), note="Security Nodes")
        m.put(S, idol, pat("at", pts=[(0.5, 0.95)]), note="idol (Horus, Geb, Thoth): wakes up when the boss falls")
        m.types[tpl] = make_type(tpl, miniboss=True, barks={"onWake": "keylock_0", "onDeath": "keylock_1"})
        m.encounter(name, miniboss=True)
    m.put(R["rails"], "cart", pat("at", pts=[(0.3, 0.15, FACE_PLUS_V), (0.7, 0.85)]),
          note="Delivery Drones on the rails (cartspawner N/S)")
    m.put(R["rails"], "thieffenceleader", pat("center", fy=0.5), note="Security Satellite")
    m.put(R["rails"], "thieffence", pat("ring", 4, r=0.2, cy=0.5))
    m.put(R["rails"], "thiefturretstrong", pat("corners", 2, m=0.08))
    m.encounter("rails", controllers=["cartspawnerN", "cartspawnerS", "cartstopper", "cartwheelmaster"])
    m.put(t1, "thiefdungeontreasure", pat("center", fy=0.6))
    m.put(t1, "trapmineshort", pat("line", 2, fy=0.3), note="X-Mine")
    m.put(R["minefield"], "trapmine", pat("grid", rows=2, cols=3, y0=0.3, y1=0.7), note="X-Mine II")
    m.put(R["minefield"], "thiefwatcher", pat("line", 3, fy=0.9), note="Thief Zappers")
    m.put(R["minefield"], "lasergrid", pat("center", fy=0.5))
    m.put(R["key_chamber"], "thiefguard", pat("line", 2, fy=0.8), note="Immortal Guards")
    m.put(R["key_chamber"], "thiefkey", pat("center", fy=0.55), note="Golden Key: follows the player to the altar")
    m.types["thiefkey"] = make_type("thiefkey", behavior="escort", team=2, freeRoam=True, invulnerable=True,
                                    note="Golden Key. When it gets near the altar it becomes thiefkey_glow (Essence of Worthiness)")
    m.types["thiefkey_glow"] = make_type("thiefkey_glow", behavior="idle", invulnerable=True)
    # the escort back: dropship ambushes along the main corridor
    amb = []
    for name in ("minefield", "rails", "laser_corridor"):
        u, v = R[name].uv(0.5, 0.5)
        amb.append({"u": u, "v": v, "rot": FACE_PLUS_V})
    m.groups["ambushes"] = amb
    m.encounter("key_chamber", wakes="onEnter",
                note="When the key is taken, Guard Dropships (thieftrooperleader) appear at the points of the group "
                     "'ambushes' from time to time until the key reaches the altar.",
                ambush={"type": "thieftrooperleader", "group": "ambushes", "intervalSeconds": 20})
    m.meta["exit"] = {"point": "portal", "when": "death:thiefchesttreasure"}
    m.point("portal", A, 0.5, 0.3, 0, note="exit carrier (after the treasure)")
    add_types(m, ["thieftrooperleader", "thiefchesttreasure", "thiefidol1_awake", "thiefidol2_awake", "thiefidol3_awake"])

    m.gate_before(R["security_002"], "locked door 1", "thiefdungeonwallgate1", opens_after="death:keylock")
    m.gate_before(R["security_003"], "locked door 2", "thiefdungeonwallgate2", opens_after="death:keylock2")
    m.gate_before(R["key_chamber"], "locked door 3", "thiefdungeonwallgate3", opens_after="death:keylock3")
    return finish(m)


def super_secret_base():
    m = DungeonMap("dungeon_space", "Super Secret Base", "DungeonSpace", 58, PIECES["dungeonmilitary"],
                   "Secret military base (T10, ice/lightning). Fixed Sentinels, Defender Cannons, Winter Guards on "
                   "platforms, Concealed Turrets, the Omega squadron and an ambush room (gates close and "
                   "enemies appear). Miniboss Mammoth-1; boss Doomsday Mechanism, the super weapon, with a cannon, "
                   "launch towers, 4 Mammoth-2 and the Omega squadron.")
    rooms = m.chain([
        {"name": "entrance", "w": 12, "h": 10},
        {"name": "control_post", "w": 22, "h": 16, "gap": 8},
        {"name": "winter_courtyard", "w": 24, "h": 20},
        {"name": "ambush_room", "w": 22, "h": 22, "gap": 6},
        {"name": "omega_runway", "w": 20, "h": 18, "gap": 6},
        {"name": "mammoth_hangar", "w": 22, "h": 20, "kind": "miniboss"},
        {"name": "doomsday_silo", "w": 30, "h": 30, "gap": 8, "kind": "boss"},
    ])
    R = m.rooms
    t1 = m.side_room(R["winter_courtyard"], "warehouse", "R", 10, 10)
    common(m, rooms, 10, "key_space", "dungeon_space", "spacedungeonsign1", "spacedungeonsign2", "control_post")

    P = R["control_post"]
    m.put(P, "blocker0", pat("line", 2, fy=0.6), note="Sentinel-1")
    m.put(P, "blocker", pat("center", fy=0.85), note="Sentinel-2")
    m.put(P, "spaceturret", pat("corners", 2, m=0.1), note="Concealed Turret")
    W = R["winter_courtyard"]
    m.put(W, "sunfoxleader", pat("at", pts=[(0.3, 0.7), (0.7, 0.7)]), note="Gun Platform")
    m.put(W, "sunfox", pat("line", 4, fy=0.45), note="Winter Guard")
    m.put(W, "sunfoxbig", pat("center", fy=0.9), note="Winter Commander")
    m.put(W, "mage", pat("at", pts=[(0.5, 0.2)]), note="Defender Cannon")
    m.put(t1, "spacedungeontreasure", pat("center", fy=0.6))
    A = R["ambush_room"]
    m.put(A, "blocker3", pat("corners", 4, m=0.12), note="Sentinel-3")
    m.encounter("ambush_room", wakes="onEnter", closesOnEnter=True,
                controllers=["ambushmaster", "ambushgatespawner", "ambushgate"],
                note="Ambush: the gates close and the waves appear (spawners ambushenemyspawner_*).",
                waves=[wave("Torretas", [group(A, "spaceturret", pat("ring", 4, r=0.38))], 1, "Emboscada!"),
                       wave("Sentinelas", [group(A, "blocker0", pat("line", 3, fy=0.8)),
                                           group(A, "sunfoxleaderchill", pat("line", 2, fy=0.3, rot=FACE_PLUS_V))]),
                       wave("Canhao", [group(A, "mage", pat("center", fy=0.6)),
                                       group(A, "sunfoxtrap", pat("ring", 4, r=0.3))]),
                       wave("Omega", [group(A, "nova1", pat("center", fy=0.85)),
                                      group(A, "nova2", pat("at", pts=[(0.4, 0.8), (0.6, 0.8)]))])])
    O = R["omega_runway"]
    m.put(O, "nova1", pat("at", pts=[(0.5, 0.8)]), note="Omega squadron (nova1 leader, nova2-6 in formation)")
    for i, (fx, fy) in enumerate([(0.38, 0.72), (0.62, 0.72), (0.26, 0.64), (0.74, 0.64), (0.5, 0.64)]):
        m.put(O, "nova%d" % (i + 2), pat("at", pts=[(fx, fy)]))
    m.put(O, "blocker2slider", pat("bothwalls", 2, y0=0.3, y1=0.3), note="Sliding Sentinel-2B")
    H = R["mammoth_hangar"]
    m.put(H, "battlemech", pat("center", fy=0.7), miniboss=True, note="Mammoth-1")
    m.put(H, "blocker4", pat("corners", 2, m=0.1), note="Sentinel-3D")
    m.put(H, "blocker5", pat("at", pts=[(0.1, 0.9), (0.9, 0.9)]), note="Sentinel-2D")
    m.types["battlemech"] = make_type("battlemech", phases=[{"belowHealth": 0.5, "summon": "battlemechshieldbomb",
                                                            "summonCount": 3}])
    m.encounter("mammoth_hangar", miniboss=True)

    boss(m, R["doomsday_silo"], "avatar", "avatarDEATH", fy=0.7,
         barks={"onWake": "avatar", "onDeath": "avatarDEATH"},
         phases=[{"belowHealth": 0.8, "summon": "avatarnova1", "summonCount": 1, "message": "Omega taking off!"},
                {"belowHealth": 0.6, "summon": "avatarbattlemech", "summonCount": 2},
                {"belowHealth": 0.4, "summon": "avatarbattlemechshieldbomb", "summonCount": 4},
                {"belowHealth": 0.25, "summon": "avatarbattlemech3", "summonCount": 2,
                 "message": "Mammoth-2 in the field!"}])
    m.put(R["doomsday_silo"], "avatarcannon", pat("at", pts=[(0.5, 0.92)]), note="Doomsday cannon")
    m.put(R["doomsday_silo"], "avatarlaunchtower", pat("at", pts=[(0.15, 0.85), (0.85, 0.85)]))
    m.put(R["doomsday_silo"], "avatarlaunchtower2", pat("at", pts=[(0.15, 0.5), (0.85, 0.5)]))
    m.encounter("doomsday_silo", controllers=["avatarbattlemechmaster", "avatarcannonmarkerhandler"])

    m.gate_before(t1, "warehouse wall", "spacedungeondestroyablewall", damageable=True)
    m.gate_before(R["ambush_room"], "ambush gate (entrance)", "ambushgate", damageable=True)
    m.gate_before(R["omega_runway"], "ambush gate (exit)", "ambushgate", opens_after="cleared:ambush_room")
    m.gate_before(R["mammoth_hangar"], "hangar gate", "spacedungeondestroyablewall", opens_after="cleared:omega_runway")
    m.gate_before(R["doomsday_silo"], "silo gate", "spacedungeondestroyablewall", opens_after="cleared:mammoth_hangar")
    return finish(m)


def temple_of_bast():
    m = DungeonMap("dungeon_temple", "Temple of Bast", "DungeonTemple", 61, PIECES["dungeonpalace"],
                   "Temple of Bast (T10, water and fire), remade on 2019-08-28: 2 Altar Rooms before the boss. "
                   "In an altar room the aura is dark and each hit gives a random curse (+50% damage "
                   "taken, -90% steam, no regeneration, +50% burning); clearing the room swaps it for a light aura, "
                   "which removes the curses and heals. Miniboss Bast Statue; boss High Priestess Tiffany, whose red Shades "
                   "drain health up close.")
    rooms = m.chain([
        {"name": "entrance", "w": 12, "h": 10},
        {"name": "nave", "w": 22, "h": 18, "gap": 8},
        {"name": "monks_cloister", "w": 20, "h": 16},
        {"name": "altar_room_1", "w": 22, "h": 22, "gap": 6},
        {"name": "statue_gallery", "w": 22, "h": 20, "gap": 6, "kind": "miniboss"},
        {"name": "altar_room_2", "w": 22, "h": 22, "gap": 6},
        {"name": "sanctuary", "w": 30, "h": 30, "gap": 8, "kind": "boss"},
    ])
    R = m.rooms
    t1 = m.side_room(R["monks_cloister"], "treasure_room", "R", 14, 14)
    common(m, rooms, 10, "key_temple", "dungeon_temple", "templedungeonsign1", "templedungeonsign2", "nave")

    N = R["nave"]
    m.put(N, "trooperleader", pat("center", fy=0.8), note="Cultist Dropship")
    m.put(N, "trooper", pat("line", 4, fy=0.55), note="Cultists")
    m.put(N, "templedrone", pat("corners", 2, m=0.15))
    C = R["monks_cloister"]
    m.put(C, "templemonk", pat("bothwalls", 2), note="Maw Guardians")
    m.put(C, "templemonk2", pat("line", 2, fy=0.75), note="Claw Guardians")
    m.put(C, "warpmineleader", pat("center", fy=0.5), note="Templar")
    m.put(C, "warpmine", pat("ring", 3, r=0.15, cy=0.5))
    m.put(t1, "bansheeleader5treasureroom", pat("center", fy=0.7), note="Bast Statue of the treasure room")
    m.put(t1, "templedungeontreasure", pat("center", fy=0.35))
    for name, seed in (("altar_room_1", 31), ("altar_room_2", 37)):
        S = R[name]
        m.put(S, "temple_altar", pat("center", fy=0.6), note="Altar of Bast (dark aura -> light)")
        m.put(S, "temple_spawner_tower_visible", pat("at", pts=[(0.2, 0.85), (0.8, 0.85)]), note="Summoning Circles")
        m.put(S, "bansheesacrifice", pat("ring", 4, r=0.18, cy=0.6), note="Sacrificial Cultists")
        m.put(S, "temple_banishee", pat("scatter", 4, seed=seed), note="Banishees")
        m.put(S, "temple_trooper", pat("line", 3, fy=0.3), note="Priests")
        m.put(S, "temple_cultist", pat("at", pts=[(0.5, 0.9)]), note="Bishop")
        m.put(S, "turret", pat("corners", 2, m=0.08), note="Altar of Death")
        m.encounter(name, altarRoom=True, controllers=["temple_spawner_tower", "temple_spawner_tower2"],
                    note="Dark aura while there are enemies: curse buffs of the configBinary (curse_*); "
                         "cleared -> light aura (heals).")
    G = R["statue_gallery"]
    m.put(G, "bansheeleader5", pat("center", fy=0.7), miniboss=True, note="Bast Statue")
    m.put(G, "duoguard", pat("at", pts=[(0.3, 0.6), (0.7, 0.6)]), note="Bast Guardians")
    m.put(G, "bansheefake", pat("corners", 4, m=0.12), note="fake statues")
    m.types["bansheeleader5"] = make_type("bansheeleader5", miniboss=True, behavior="turret",
                                          barks={"onWake": "bansheeleader5_0", "phase": "bansheeleader5_1"})
    m.types["bansheeleader5treasureroom"] = make_type("bansheeleader5treasureroom", behavior="turret",
                                                      barks={"onWake": "bansheeleader5treasureroom_0"})
    m.encounter("statue_gallery", miniboss=True, controllers=["duoguardmaster"])

    boss(m, R["sanctuary"], "smokeboss", "smokebossDEATH",
         barks={"onWake": "smokeboss_0", "phase": "smokeboss_1", "curse": "smokeboss_curse",
                "summon": "smokeboss_feed", "onDeath": "smokebossDEATH"},
         phases=[{"belowHealth": 0.8, "summon": "smokebossswarm", "summonCount": 4, "message": "Shades!"},
                {"belowHealth": 0.6, "summon": "temple_droneBIG", "summonCount": 2},
                {"belowHealth": 0.4, "summon": "smokebossswarm", "summonCount": 6},
                {"belowHealth": 0.2, "summon": "temple_fireball", "summonCount": 2}])
    m.put(R["sanctuary"], "temple_cultistBIG", pat("at", pts=[(0.2, 0.85), (0.8, 0.85)]))
    m.put(R["sanctuary"], "warpmine2", pat("corners", 2, m=0.15), note="Moon Templars")
    m.encounter("sanctuary", controllers=["smokebossmarker", "smokebosstrainA"])

    m.gate_before(t1, "treasure wall", "templedungeondestroyablewall", damageable=True)
    m.gate_before(R["altar_room_1"], "altar gate 1", "templedungeondestroyablewall", damageable=True)
    m.gate_before(R["statue_gallery"], "gallery gate", "templedungeondestroyablewallstrong",
                  opens_after="cleared:altar_room_1")
    m.gate_before(R["altar_room_2"], "altar gate 2", "templedungeondestroyablewallstrong",
                  opens_after="cleared:statue_gallery")
    m.gate_before(R["sanctuary"], "sanctuary gate", "templedungeondestroyablewallstrong",
                  opens_after="cleared:altar_room_2")
    return finish(m)


def iron_lair():
    m = DungeonMap("dungeon_sneezy", "Iron Lair", "DungeonSneezy", 67, PIECES["industrialboss"],
                   "Mini-dungeon (T10) opened by the world's Mysterious Black Box. Spawn room (added in a 2019 "
                   "patch) and SN-Z's arena, which launches beacons and calls the Robo Squadrons I-III. The client "
                   "has no territory of its own for it; it uses industrialboss (67).")
    rooms = m.chain([
        {"name": "entrance", "w": 14, "h": 12},
        {"name": "snz_arena", "w": 30, "h": 30, "gap": 8, "kind": "boss"},
    ])
    R = m.rooms
    common(m, rooms, 10, "key_sneezy", "dungeon_sneezy")
    # SN-Z: the 190426 build still has its AI layer "toggle_safety": a 10 s shield ("Shielded") at 90/70/50/30/10%
    # health. The gun mounts of the unit come in groups named by phase (mainphase1, mainphase2xx, mainphase301,
    # mainphase40x, finalattack*), so each shield also switches the weapons. Which squadron comes with which shield
    # is a reconstruction (the summons lived in the server AI, not in the client).
    MG = [0, 1, 2]
    boss(m, R["snz_arena"], "sneezy", "sneezyDEATH", fy=0.6,
         barks={"onDeath": "sneezyDEATH"},
         extra={"guns": MG + [4, 5],
                "note": "Guns by gunMounts index of SN-Z: 0-2 machine guns, 4-5 mainphase1, 7-12 mainphase2, "
                        "14 mainphase3 (missile), 16-18 mainphase4, 19-21 finalattack. 3/6/13/15 are the 'duds'."},
         phases=[{"belowHealth": 0.9, "shieldSeconds": 10, "visualEffect": "tankShield",
                 "summon": "sneezyforce1", "summonCount": 2, "message": "Robo Squadron I"},
                {"belowHealth": 0.7, "shieldSeconds": 10, "visualEffect": "tankShield", "guns": MG + [7, 8, 9, 10, 11, 12],
                 "summon": "sneezybomb", "summonCount": 3},
                {"belowHealth": 0.5, "shieldSeconds": 10, "visualEffect": "tankShield", "guns": MG + [14],
                 "summon": "sneezyforce2", "summonCount": 3, "message": "Robo Squadron II"},
                {"belowHealth": 0.3, "shieldSeconds": 10, "visualEffect": "tankShield", "guns": [16, 17, 18],
                 "summon": "sneezyforce3", "summonCount": 3, "message": "Robo Squadron III"},
                {"belowHealth": 0.1, "shieldSeconds": 10, "visualEffect": "tankShield", "guns": [16, 17, 18, 19, 20, 21],
                 "summon": "sneezybomb", "summonCount": 2}])
    m.encounter("snz_arena", controllers=["sneezymaster"])
    m.gate_before(R["snz_arena"], "arena gate", "spacedungeondestroyablewall", damageable=True)
    return finish(m)


def the_dig():
    m = DungeonMap("dungeon_rescue", "The Dig", "DungeonRescue", 45, PIECES["dungeoncave"],
                   "The Dig (T10). Quest 'Defend the Drill': the allied drill (1000 health) digs through 3 tunnels; at "
                   "each milestone a boss (Deep Leviathan, Stone Worm, Buried Guardian). Repair Drones in the side "
                   "tunnels heal the drill. If the drill falls, the loot from there on is lost; at the end, the Legendary "
                   "Supplies. The enemies almost never aim at the drill. The original had dynamic difficulty.")
    rooms = m.chain([
        {"name": "entrance", "w": 14, "h": 12},
        {"name": "tunnel_1", "w": 12, "h": 30, "gap": 4},
        {"name": "leviathan_cave", "w": 26, "h": 22, "gap": 4, "kind": "miniboss"},
        {"name": "tunnel_2", "w": 12, "h": 30, "gap": 4, "dx": 4},
        {"name": "worm_cave", "w": 26, "h": 22, "gap": 4, "kind": "miniboss"},
        {"name": "tunnel_3", "w": 12, "h": 30, "gap": 4, "dx": -4},
        {"name": "guardian_chamber", "w": 28, "h": 26, "gap": 4, "kind": "boss"},
        {"name": "destination", "w": 16, "h": 12, "gap": 6, "kind": "treasure"},
    ])
    R = m.rooms
    s1 = m.side_room(R["tunnel_1"], "side_gallery_1", "L", 12, 10, gap=8, fy=0.4)
    s2 = m.side_room(R["tunnel_2"], "side_gallery_2", "R", 12, 10, gap=8, fy=0.6)
    s3 = m.side_room(R["tunnel_3"], "side_gallery_3", "L", 12, 10, gap=8, fy=0.5)
    common(m, rooms, 10, "key_rescue", "dungeon_rescue", "rescuedungeonsign1", "rescuedungeonsign2", "tunnel_1")
    m.meta["quest"] = "dungeon_rescue_01"

    route = []
    for name in ("entrance", "tunnel_1", "leviathan_cave", "tunnel_2", "worm_cave", "tunnel_3",
                 "guardian_chamber", "destination"):
        r = R[name]
        for fy in ((0.1, 0.9) if r.h >= 20 else (0.5,)):
            u, v = r.uv(0.5, fy)
            route.append({"u": u, "v": v})
    m.route["drill"] = route
    m.point("drill", R["entrance"], 0.5, 0.6, FACE_PLUS_V, note="ALLY: Tunneling Drill (escortmission) spawns here")
    m.types["escortmission"] = make_type("escortmission", team=1, behavior="patrol", route="drill",
                                         onDeath="escortmissionDEATH", invulnerableToRegularEnemies=True,
                                         barks={"heal": "escortmission_0", "underAttack": "escortmission_1",
                                                "damage": "escortmission_2", "end": "escortmission_4",
                                                "onDeath": "escortmissionDEATH"},
                                         note="The drill only moves when the tunnel ahead is clear and stops at each milestone "
                                              "until the boss falls.")
    m.encounter("entrance", wakes="onEnter", enemies=[{"type": "escortmission", "pos": [m.points["drill"]]}])
    for i, (tun, side, seed) in enumerate([("tunnel_1", s1, 41), ("tunnel_2", s2, 43), ("tunnel_3", s3, 47)]):
        T = R[tun]
        m.put(T, "escortgrunt1", pat("scatter", 3, seed=seed, y0=0.2, y1=0.5), note="Rust Borers")
        m.put(T, "escortgrunt2" if i == 0 else "escortgrunt2fat", pat("line", 2, fy=0.65))
        m.put(T, "escortturret" if i < 2 else "escortturretstrong", pat("bothwalls", 2, y0=0.8, y1=0.8))
        m.put(T, "escortbomb", pat("at", pts=[(0.5, 0.9)]), note="Magnet Bomb")
        if i >= 1:
            m.put(T, "escortgrunt3", pat("at", pts=[(0.5, 0.45)]), note="Stone Elite")
        if i == 2:
            m.put(T, "escortspotter2", pat("at", pts=[(0.5, 0.75)]), note="Super Relic")
        else:
            m.put(T, "escortspotter1", pat("at", pts=[(0.5, 0.75)]), note="War Relic")
        m.put(side, "escorthealdrone", pat("line", 2, fy=0.5), note="Repair Drones: they go to the drill and heal")
        m.put(side, "escortdungeontreasure" if i < 2 else "escortdungeontreasure2", pat("center", fy=0.85))
        m.types.setdefault("escorthealdrone", make_type("escorthealdrone", team=1, behavior="escort",
                                                        target="escortmission", freeRoam=True,
                                                        barks={"onWake": "escorthealdrone_0", "heal": "escorthealdrone_1"}))
    for i, (cav, tpl, extra) in enumerate([("leviathan_cave", "escortgoliath", ["escortgoliathmasterleft",
                                                                                     "escortgoliathmasterright"]),
                                           ("worm_cave", "escorttrain1", ["escorttrainmaster"])]):
        C = R[cav]
        m.point("marco_%d" % (i + 1), C, 0.5, 0.5, note="escort_milestone%d" % (i + 1))
        m.put(C, tpl, pat("center", fy=0.8), miniboss=True)
        m.put(C, "escort_milestone%d" % (i + 1), pat("center", fy=0.5), note="mission milestone (invisible)")
        m.encounter(cav, miniboss=True, wakes="drill", controllers=extra + ["escort_milestone%d_spawner" % (i + 1)])
    m.types["escortgoliath"] = make_type("escortgoliath", miniboss=True, note="Deep Leviathan",
                                         phases=[{"belowHealth": 0.5, "summon": "escortgoliathmissile", "summonCount": 3}])
    m.types["escorttrain1"] = make_type("escorttrain1", phases=[{"belowHealth": 0.9, "summon": "escorttrain2",
                                                                "summonCount": 4, "message": "Stone Worm!"}])
    G = R["guardian_chamber"]
    boss(m, G, "escortevilorb", "escortevilorbDEATH", fy=0.75, barks={"onDeath": "escortevilorbDEATH"},
         phases=[{"belowHealth": 0.6, "summon": "escortgrunt3_noxp", "summonCount": 3},
                {"belowHealth": 0.3, "summon": "escortturretmissile", "summonCount": 4}])
    m.put(G, "escort_milestone3", pat("center", fy=0.4), note="milestone 3 (invisible)")
    m.encounter("guardian_chamber", wakes="drill", controllers=["escortevilorbmaster", "escort_milestone3_spawner"])
    m.point("milestone_3", G, 0.5, 0.4, note="escort_milestone3")
    D = R["destination"]
    m.put(D, "escortmission_poop", pat("center", fy=0.4), note="drill destination (objective 4)")
    m.put(D, "escortdungeontreasure3", pat("center", fy=0.75), note="Legendary Supplies (objective 5)")
    m.put(D, "escortexit", pat("at", pts=[(0.5, 0.95)]))
    m.encounter("destination", wakes="drill")
    m.points["portal"] = dict(m.points["portal"], u=D.uv(0.5, 0.2)[0], v=D.uv(0.5, 0.2)[1])
    m.meta["exit"] = {"point": "portal", "when": "death:escortdungeontreasure3"}

    m.gate_before(R["leviathan_cave"], "stone wall 1", "escortdungeondestroyablewall", damageable=True)
    m.gate_before(R["worm_cave"], "stone wall 2", "escortdungeondestroyablewall", damageable=True)
    m.gate_before(R["guardian_chamber"], "stone wall 3", "escortdungeondestroyablewall", damageable=True)
    m.gate_before(R["destination"], "old vault door", "escort_wallgate", opens_after="death:escortevilorb")
    m.gate_before(s1, "side laser 1", "escortdungeondestroyablewall", damageable=True)
    return finish(m)


def command_center():
    m = DungeonMap("dungeon_finalboss", "The Great Leader's Command Center", "DungeonFinalBoss", 35,
                   PIECES["dungeonboss"],
                   "Meowza's Throne (Godleader Meowza, phase 1). Quest dungeon_finalboss_01/02: 'Defeat the Throne of "
                   "Meowza'. At the entrance, The Viceroy tells you to leave the entrance area and incinerates it in 60 s (barks "
                   "meowzablocker_countdown_*); the laser gate (meowzalasergateWE) turns on when the quest starts. "
                   "Meowza has phases B, C and D with lasers, nukes and Spirit Vessels. When she dies she opens the portal to "
                   "the Three Brothers (meowzaDeath3BrothersPortal) besides the exit.")
    rooms = m.chain([
        {"name": "entrance", "w": 14, "h": 12},
        {"name": "worshippers_gallery", "w": 24, "h": 20, "gap": 6},
        {"name": "throne_hall", "w": 34, "h": 34, "gap": 8, "kind": "boss"},
    ])
    R = m.rooms
    common(m, rooms, 10, "key_finalboss", "dungeon_finalboss")
    m.meta["quest"] = "dungeon_finalboss_01"
    E = R["entrance"]
    m.put(E, "meowzablocker", pat("at", pts=[(0.5, 0.95)]), note="The Viceroy: incineration countdown of the entrance area")
    m.types["meowzablocker"] = make_type("meowzablocker", invulnerable=True,
                                         barks={"onWake": "meowzablocker", "countdown": "meowzablocker_countdown_",
                                                "incinerate": "meowzablocker2"})
    m.put(E, "meowza_playerkicker", pat("at", pts=[(0.5, 0.5)]), note="Banishee: removes weak pilots from the dungeon")
    m.types["meowza_playerkicker"] = make_type("meowza_playerkicker", invulnerable=True, behavior="idle",
                                               barks={"onWake": "meowza_playerkicker"},
                                               note="conversation meowza1_warning_belowL30 for pilots below rank 30")
    G = R["worshippers_gallery"]
    m.put(G, "meowzaturtle", pat("line", 3, fy=0.7), note="Acolytes of Meowza")
    m.put(G, "meowzastardog", pat("line", 4, fy=0.45), note="Hounds of Meowza")
    m.put(G, "meowzahurtsnake", pat("scatter", 4, seed=53), note="Supplicants")
    m.encounter("worshippers_gallery", controllers=["meowzastardogspawner1", "meowzastardogspawner2", "meowzahurtsnakespawner"])
    T = R["throne_hall"]
    boss(m, T, "meowza", "meowzaDEATH", fy=0.7,
         barks={"onWake": "BOS1_meowza1", "entrance": "BOS1_meowzaA0", "phaseB": "BOS1_meowzaB0",
                "phaseC": "BOS1_meowzaC0", "phaseD": "BOS1_meowzaD0", "phaseE": "BOS1_meowzaE0", "nuke": "BOS1_meowzanuke",
                "onDeath": "BOS1_meowzaDEATH"},
         phases=[{"belowHealth": 0.85, "summon": "meowzaB1m", "summonCount": 4, "bark": "BOS1_meowzaB0"},
                {"belowHealth": 0.7, "summon": "meowzaB2m", "summonCount": 3},
                {"belowHealth": 0.55, "summon": "meowzaC1m1", "summonCount": 4, "bark": "BOS1_meowzaC0"},
                {"belowHealth": 0.4, "summon": "meowzaC2m", "summonCount": 3},
                {"belowHealth": 0.25, "summon": "meowzanuke1m1", "summonCount": 1, "bark": "BOS1_meowzanuke"},
                {"belowHealth": 0.12, "summon": "meowzaD2m", "summonCount": 4, "bark": "BOS1_meowzaD0"}],
         portal_fy=0.3)
    m.put(T, "meowzahealthnode", pat("corners", 4, m=0.12), note="Spirit Vessels of Meowza")
    for i, tpl in enumerate(["meowzalaserA", "meowzalaserB", "meowzalaserC", "meowzalaserD"]):
        fx, fy = [(0.5, 0.97), (0.03, 0.5), (0.97, 0.5), (0.5, 0.45)][i]
        m.put(T, tpl, pat("at", pts=[(fx, fy)]))
    m.point("portal_3brothers", T, 0.3, 0.3, 0, note="meowzaDeath3BrothersPortal -> dungeon_3brothers")
    m.encounter("throne_hall", controllers=["meowzaB1", "meowzaB2", "meowzaC1", "meowzaC2", "meowzaD2",
                                                 "meowzanuke1", "meowzaportalmaker", "meowzahealthleader2",
                                                 "meowzafinallaserminion"])
    m.meta["nextDungeon"] = {"map": "dungeon_3brothers", "point": "portal_3brothers", "when": "death:meowza"}
    m.gate_before(R["worshippers_gallery"], "laser gate", "meowzalasergateWE", opens_after="quest:dungeon_finalboss_01")
    m.gate_before(R["throne_hall"], "throne door", "meowzadungeondestroyablewall", opens_after="cleared:worshippers_gallery")
    return finish(m)


def three_brothers():
    m = DungeonMap("dungeon_3brothers", "The Three Brothers", "Dungeon3Brothers", 73, PIECES["dungeonboss2"],
                   "Meowza's elite guard, between the Throne and the Sanctum. A corridor with turrets (hallwayturretS), "
                   "then the arena: Golden Kai, Golden Jinn and Golden Rei, one at a time (barks 3brothers_intro1-3 and "
                   "death1-3), with Duelist Protectors. At the end, the portal to the Inner Sanctum (3brothers_portalmaker) "
                   "and the chest 3brothers_reward.")
    rooms = m.chain([
        {"name": "entrance", "w": 14, "h": 12},
        {"name": "sacred_hall", "w": 12, "h": 26, "gap": 4},
        {"name": "brothers_arena", "w": 32, "h": 32, "gap": 6, "kind": "boss"},
    ])
    R = m.rooms
    common(m, rooms, 10)
    m.put(R["sacred_hall"], "hallwayturretS", pat("bothwalls", 6, y0=0.15, y1=0.85))
    m.encounter("sacred_hall", controllers=["hallwayturretcontroller"])
    A = R["brothers_arena"]
    m.point("boss", A, 0.5, 0.65)
    m.point("portal", A, 0.5, 0.3, 0, note="portal to the Inner Sanctum (dungeon_finalboss2)")
    m.encounter("brothers_arena", wakes="onEnter", boss=True, closesOnEnter=True, controllers=["3brothersmaster"],
                waves=[wave("Golden Kai", [group(A, "kaiju3B", pat("center", fy=0.7)),
                                           group(A, "duelist_3Bclone", pat("at", pts=[(0.2, 0.8), (0.8, 0.8)]))], 4,
                            None),
                       wave("Golden Jinn", [group(A, "raijin3B", pat("center", fy=0.7))], 4),
                       wave("Golden Rei", [group(A, "thunderboss3B", pat("center", fy=0.7)),
                                           group(A, "duelist_3Bclone", pat("at", pts=[(0.2, 0.6), (0.8, 0.6)]))], 4)])
    m.types["kaiju3B"] = make_type("kaiju3B", onDeath="kaiju3BDEATH",
                                   barks={"onWake": "3brothers_intro1", "onDeath": "3brothers_death1"})
    m.types["raijin3B"] = make_type("raijin3B", onDeath="raijin3BDEATH",
                                    barks={"onWake": "3brothers_intro2", "onDeath": "3brothers_death2"})
    m.types["thunderboss3B"] = make_type("thunderboss3B", onDeath="thunderboss3BDEATH",
                                         barks={"onWake": "3brothers_intro3", "onDeath": "3brothers_death3"})
    m.put(A, "3brothers_reward", pat("at", pts=[(0.5, 0.45)]), wakes="death:thunderboss3B")
    m.meta["exit"] = {"point": "portal", "when": "death:thunderboss3B"}
    m.meta["nextDungeon"] = {"map": "dungeon_finalboss2", "point": "portal", "when": "death:thunderboss3B"}
    m.gate_before(R["sacred_hall"], "sacred gate", "3brothers_gate", damageable=True)
    m.gate_before(A, "arena wall", "3brothersdungeondestroyablewall", damageable=True)
    add_types(m, ["3brothers_portalmaker", "3brosportalally"])
    return finish(m)


def inner_sanctum():
    m = DungeonMap("dungeon_finalboss2", "The Great Leader's Inner Sanctum", "DungeonFinalBoss2", 73,
                   PIECES["dungeonboss2"],
                   "Meowza's Sanctum (Godleader Meowza, phase 2; music music_meowza_intro_loop / main_loop). Meowza "
                   "stays still in the center (10000 health) with the Defense Grid N/S/E/W and summons the souls of the generals "
                   "the dead (barks meowza2_01..11): frost (Frozen Horror), firering (Phoenix), orochi (Poison God), "
                   "lasereyegrid (Cyclopus), lightningspiral, infinity, claw, nukes and the Meowzaghasts.")
    rooms = m.chain([
        {"name": "entrance", "w": 14, "h": 12},
        {"name": "sanctuary", "w": 36, "h": 36, "gap": 8, "kind": "boss"},
    ])
    R = m.rooms
    S = R["sanctuary"]
    common(m, rooms, 10)
    boss(m, S, "meowza2", "meowza2DEATH", fy=0.55,
         barks={"intro": "meowza2_01..meowza2_11", "onWake": "meowza2_start", "onDeath": "meowza2_death",
                "onDeath2": "meowza2_death2", "silence": "meowza2_silence_short"},
         phases=[{"belowHealth": 0.9, "summon": "meowza2_frost_1", "summonCount": 2},
                {"belowHealth": 0.8, "summon": "meowza2_firering", "summonCount": 1},
                {"belowHealth": 0.7, "summon": "meowza2_orochi_2", "summonCount": 2},
                {"belowHealth": 0.6, "summon": "meowza2_lasereye", "summonCount": 4},
                {"belowHealth": 0.5, "summon": "meowza2_lightningspiral", "summonCount": 2},
                {"belowHealth": 0.4, "summon": "meowza2_claw_2", "summonCount": 2},
                {"belowHealth": 0.3, "summon": "meowza2_infinity_2", "summonCount": 1},
                {"belowHealth": 0.2, "summon": "meowza2_hunter", "summonCount": 3, "message": "Meowzaghasts!"},
                {"belowHealth": 0.1, "summon": "meowza2_nuke", "summonCount": 1}],
         portal_fy=0.25)
    for tpl, (fx, fy) in (("meowza2_turret_N", (0.5, 0.95)), ("meowza2_turret_S", (0.5, 0.06)),
                          ("meowza2_turret_E", (0.06, 0.5)), ("meowza2_turret_W", (0.94, 0.5))):
        m.put(S, tpl, pat("at", pts=[(fx, fy)]), note="Godleader Defense Grid")
    m.put(S, "9star2", pat("ring", 4, r=0.3, cy=0.55, a0=45))
    m.encounter("sanctuary", controllers=["meowza2spawner", "meowza2_hunter_spawner", "meowza2_nukestorm",
                                            "meowza2_lasereyegrid_1", "meowza2_lasereyegrid_2", "meowza2_orochi_1",
                                            "meowza2_infinity_1", "meowza2_claw_1", "9star1"])
    return finish(m)


ALL = [bomb_factory, laser_playground, lambda: arena(False), home_for_special_children, crypt_of_cathulhu,
       reeducation_camp, frozen_owl_monastery, venom_compound, the_vaults, super_secret_base, temple_of_bast,
       iron_lair, lambda: arena(True), the_dig, command_center, three_brothers, inner_sanctum]


def build_all():
    return [f() for f in ALL]


if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else "out"
    os.makedirs(out, exist_ok=True)
    for m in build_all():
        with open(os.path.join(out, m.id + ".json"), "w", encoding="utf-8", newline="\n") as f:
            f.write(dump(m.to_json()))
        print(m.id, len(m.rooms), "rooms")
