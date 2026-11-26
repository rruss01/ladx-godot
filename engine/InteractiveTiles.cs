using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

public enum Interaction { Slash, Lift, Push, Bomb }

// What the player is now holding after lifting a tile: enough to draw it and to break it later.
public readonly record struct LiftedTile(int TileId, Vector2I Cell, Texture2D Art, string BreakEffect, string Spawns);

// What happens when one kind of interaction hits a tile.
public sealed class TileReaction
{
	public int Result = -1;     // tile id swapped in; -1 = art unchanged
	public string Spawns = "";  // free-text entity name, e.g. "random item (heart, rupee)"
	public string Effect = "";  // named visual effect, e.g. "leaves"
	public int Power;           // minimum power level the caller must have
	public Vector2I Sprite = new(-1, -1); // Lift only: cell in the throwables sheet; (-1,-1) = none
}

public sealed class TileBehavior
{
	public readonly Dictionary<Interaction, TileReaction> Reactions = new();
	public bool Permanent;      // stays changed after the player leaves the room
}

// Makes tiles of the baked map react to the sword, bombs, lifting and pushing.
//
// How it works: for the current room, every interactive 16x16 cell gets an identical
// copy of its art on the "Objects" layer, drawn above the baked map. A reaction swaps
// that copy for the result tile and erases the collision cells underneath it.
// Callers (sword, bombs, ...) only ever call TryInteract().
public partial class InteractiveTiles : Node
{
	private const int CellSize = 16;

	// Raised when a reaction wants something spawned (a drop, a thrown bush, ...).
	[Signal] public delegate void EntitySpawnRequestedEventHandler(string entityName, Vector2 position);

	public static InteractiveTiles Instance { get; private set; }

	// Gives us the collision layer to edit and tells us when a room's collision is ready.
	[Export] public CollisionLoader Loader { get; set; }
	[Export] public ScreenScroller Scroller { get; set; }

	// The map-specific data (objects layer, tile ids, labels) now comes from the loaded
	// map's MapInfo via SetMap(), not from the Inspector.

	// Prints what every interaction attempt did, to the Output panel. For debugging.
	[Export] public bool DebugLog { get; set; }

	[Export] public int AtlasSourceId { get; set; } = 0;
	[Export] public int AtlasColumns { get; set; } = 32; // tiles per row in tiles16_atlas.png

	[ExportGroup("Carried objects")]
	[Export(PropertyHint.File, "*.png")] public string LiftSheetPath { get; set; } = "res://assets/maps/tiles/throwables.png";
	[Export] public int LiftCellSize { get; set; } = 16;

	[ExportGroup("Leaf effect (bushes)")]
	[Export] public Texture2D LeafTexture { get; set; } // optional; empty = built-in placeholder
	[Export] public int LeafTicksPerFrame { get; set; } = 4; // original: 4 game frames per pose
	[Export] public int LeafTotalTicks { get; set; } = 30;   // original: about 30 game frames
	[Export] public AudioStream LeafSound { get; set; }      // original: NOISE_SFX_CUT_GRASS

	[ExportGroup("Rock effect (rocks, pots)")]
	[Export] public Texture2D RockTexture { get; set; } // optional; empty = built-in placeholder
	[Export] public int RockTicksPerFrame { get; set; } = 4;
	[Export] public int RockTotalTicks { get; set; } = 14;
	[Export] public AudioStream RockSound { get; set; }      // original: NOISE_SFX_POT_SMASHED
	
	[ExportGroup("Poof effect")]
	[Export] public PackedScene PoofScene { get; set; }
	[Export] public AudioStream PoofSound { get; set; }

	// Audio bus the effect sounds play on.
	[Export] public StringName SoundBus { get; set; } = "Master";

	private readonly Dictionary<int, TileBehavior> _behaviors = new();
	private readonly Dictionary<Vector2I, int> _current = new();   // this room: cell -> tile id (-1 = inert)
	private Dictionary<Vector2I, int> _permanent = new();          // this map: cell -> result tile id
	private readonly Dictionary<string, Dictionary<Vector2I, int>> _permanentByMap = new(); // kept per map
	private int[] _noCollision = { 4, 50, 259 };

	// From the current map's MapInfo. Null when the map has no image-based interactive tiles.
	private TileMapLayer Objects;

	private int[] _ids;
	private Vector2I _gridSize;

	private Vector2I CellsPerRoom => new Vector2I(Loader.RoomSize.X / CellSize, Loader.RoomSize.Y / CellSize);

	public override void _EnterTree()
	{
		Instance = this;
	}

	public override void _Ready()
	{
		// Wait for the collision to be built first, so erasing cells actually sticks.
		Loader.RoomLoaded += OnRoomLoaded;
	}

	// Called by MapManager each time a map is loaded. Permanent changes are remembered
	// per map id, so leaving a map and coming back keeps them.
	public void SetMap(MapInfo map)
	{
		_current.Clear();
		_behaviors.Clear();
		_ids = null;
		Objects = null;
		_liftArt.Clear();     // fallback art depends on the map's atlas

		if (!_permanentByMap.TryGetValue(map.MapId, out Dictionary<Vector2I, int> remembered))
		{
			remembered = new Dictionary<Vector2I, int>();
			_permanentByMap[map.MapId] = remembered;
		}
		_permanent = remembered;

		// Tile-based maps (houses, shops) have nothing to set up here.
		if (map.TileIds == null || map.Objects == null || string.IsNullOrEmpty(map.TileLabels))
			return;

		LoadLabels(map.TileLabels);
		LoadTileIds(map.TileIds);
		Objects = map.Objects;
	}

	public override void _ExitTree()
	{
		if (IsInstanceValid(Loader))
			Loader.RoomLoaded -= OnRoomLoaded;
	}

	// The one entry point. Returns true if the point hit a tile that reacted.
	// 'power' is the caller's strength (e.g. power bracelet level); reactions can demand more.
	public bool TryInteract(Vector2 worldPoint, Interaction kind, int power = 0, bool allowSpawns = true)
	{
		Vector2I cell = new Vector2I(
			Mathf.FloorToInt(worldPoint.X / CellSize),
			Mathf.FloorToInt(worldPoint.Y / CellSize));

		if (!_current.TryGetValue(cell, out int id) || id < 0)
		{
			Log($"{kind} at {worldPoint} -> cell {cell}: nothing interactive there");
			return false;
		}
		if (!_behaviors.TryGetValue(id, out TileBehavior behavior))
		{
			Log($"cell {cell}: tile #{id} has no behavior");
			return false;
		}
		if (!behavior.Reactions.TryGetValue(kind, out TileReaction reaction))
		{
			Log($"cell {cell}: tile #{id} does not react to {kind}");
			return false;
		}
		if (power < reaction.Power)
		{
			Log($"cell {cell}: tile #{id} needs power {reaction.Power}, caller has {power}");
			return false;
		}

		Log($"cell {cell}: tile #{id} reacted to {kind}, becomes #{reaction.Result}");
		ApplyResult(cell, reaction.Result);

		if (behavior.Permanent)
			_permanent[cell] = reaction.Result;

		if (allowSpawns && reaction.Spawns != "")
			EmitSignal(SignalName.EntitySpawnRequested, reaction.Spawns, CellCenter(cell));

		if (reaction.Effect != "")
			SpawnEffect(reaction.Effect, new Vector2(cell.X * CellSize, cell.Y * CellSize));

		return true;
	}

	// Like TryInteract, but only ASKS: would this point react to 'kind' for a caller of this
	// strength? Nothing changes. Used to decide whether a pull should count towards a lift.
	public bool CanInteract(Vector2 worldPoint, Interaction kind, int power = 0)
	{
		Vector2I cell = new Vector2I(
			Mathf.FloorToInt(worldPoint.X / CellSize),
			Mathf.FloorToInt(worldPoint.Y / CellSize));

		return _current.TryGetValue(cell, out int id) && id >= 0
			&& _behaviors.TryGetValue(id, out TileBehavior behavior)
			&& behavior.Reactions.TryGetValue(kind, out TileReaction reaction)
			&& power >= reaction.Power;
	}

	// Lifts the tile at the point: the tile turns into whatever its "lift" reaction says
	// (the copy that stays behind), and 'lifted' describes the object now in the player's hands.
	public bool TryLift(Vector2 worldPoint, int power, out LiftedTile lifted)
	{
		lifted = default;

		Vector2I cell = new Vector2I(
			Mathf.FloorToInt(worldPoint.X / CellSize),
			Mathf.FloorToInt(worldPoint.Y / CellSize));

		if (!_current.TryGetValue(cell, out int id) || id < 0)
			return false;

		// Grab the art and the kind BEFORE reacting: the reaction replaces the tile.
		Vector2I sprite = new(-1, -1);
		string breakEffect = "rocks";
		string spawns = "";
		if (_behaviors.TryGetValue(id, out TileBehavior behavior))
		{
			if (behavior.Reactions.TryGetValue(Interaction.Lift, out TileReaction lift))
			{
				sprite = lift.Sprite;
				spawns = lift.Spawns; // saved for when the object breaks
			}

			// Anything a sword can also cut is a bush, and a bush breaks into leaves.
			if (behavior.Reactions.TryGetValue(Interaction.Slash, out TileReaction slash) && slash.Effect != "")
				breakEffect = slash.Effect;
		}
		Texture2D art = LiftArtFor(id, sprite);

		// allowSpawns: false. In the original, a bush's drop appears when the thrown bush BREAKS,
		// not when it is picked up, so CarriedObject asks for the drop itself (RequestSpawn).
		if (!TryInteract(worldPoint, Interaction.Lift, power, allowSpawns: false))
			return false;

		lifted = new LiftedTile(id, cell, art, breakEffect, spawns);
		return true;
	}

	private Texture2D _liftSheet;
	private bool _liftSheetTried;
	private readonly Dictionary<int, Texture2D> _liftArt = new(); // tile id -> picture, built once

	// The picture of a lifted object, found by tile id and cached.
	//
	// Normally it is the cell of the throwables sheet the tile's JSON entry names. The tile's own
	// art can't be used directly: it is a full square including its background. If a tile has no
	// sprite yet we fall back to that tile art so the game still works, and warn once so a
	// missing entry is easy to spot.
	private Texture2D LiftArtFor(int id, Vector2I sprite)
	{
		if (_liftArt.TryGetValue(id, out Texture2D cached))
			return cached;

		Texture2D art = null;

		if (sprite.X >= 0 && sprite.Y >= 0)
		{
			if (!_liftSheetTried)
			{
				_liftSheetTried = true;
				_liftSheet = GD.Load<Texture2D>(LiftSheetPath);
				if (_liftSheet == null)
					GD.PushError($"InteractiveTiles: could not load the throwables sheet '{LiftSheetPath}'.");
			}

			if (_liftSheet != null)
			{
				// An AtlasTexture is a window onto part of another texture: no pixels are copied.
				art = new AtlasTexture
				{
					Atlas = _liftSheet,
					Region = new Rect2(sprite.X * LiftCellSize, sprite.Y * LiftCellSize, LiftCellSize, LiftCellSize),
				};
			}
		}
		else
		{
			GD.PushWarning($"InteractiveTiles: tile {id} has no \"s\" sprite on its lift reaction; showing its tile art.");
		}

		art ??= TileArt(id);
		_liftArt[id] = art;
		return art;
	}

	// Fallback: the tile's own 16x16 picture from the atlas the Objects layer draws from.
	private Texture2D TileArt(int id)
	{
		if (Objects?.TileSet?.GetSource(AtlasSourceId) is not TileSetAtlasSource atlas || atlas.Texture == null)
			return null;

		return new AtlasTexture { Atlas = atlas.Texture, Region = atlas.GetTileTextureRegion(AtlasCoords(id)) };
	}

	// True if the point is on a tile that is still interactive (a rock, a pot, a bush that
	// hasn't been cut...), as opposed to a plain wall. Does not trigger any reaction.
	public bool IsInteractiveAt(Vector2 worldPoint)
	{
		Vector2I cell = new Vector2I(
			Mathf.FloorToInt(worldPoint.X / CellSize),
			Mathf.FloorToInt(worldPoint.Y / CellSize));

		return _current.TryGetValue(cell, out int id) && id >= 0;
	}

	private void OnRoomLoaded(Vector2I room)
	{
		_current.Clear();
		if (Objects == null || _ids == null)
			return;

		Objects.Clear();

		Vector2I perRoom = CellsPerRoom;
		Vector2I origin = room * perRoom;

		for (int ty = 0; ty < perRoom.Y; ty++)
		{
			for (int tx = 0; tx < perRoom.X; tx++)
			{
				Vector2I cell = origin + new Vector2I(tx, ty);
				int id = TileIdAt(cell);
				if (id < 0 || !_behaviors.ContainsKey(id))
					continue;

				// An identical copy of the baked art, so nothing looks different yet.
				_current[cell] = id;
				Objects.SetCell(cell, AtlasSourceId, AtlasCoords(id));

				// Interactive objects (bushes, rocks, signs...) block movement until they react,
				// whether or not the collision mask happened to mark them solid.
				if (!_noCollision.Contains(id))
					SetCollision(cell, true);
			}
		}

		Log($"room {room}: {_current.Count} interactive cells");

		// Re-apply changes that were marked permanent on earlier visits.
		foreach (KeyValuePair<Vector2I, int> change in _permanent)
			if (_current.ContainsKey(change.Key))
				ApplyResult(change.Key, change.Value);
	}

	private void ApplyResult(Vector2I cell, int result)
	{
		if (result >= 0)
			Objects.SetCell(cell, AtlasSourceId, AtlasCoords(result));

		// The result may itself be interactive (e.g. a cracked wall); otherwise it's inert.
		bool stillInteractive = result >= 0 && _behaviors.ContainsKey(result);
		_current[cell] = stillInteractive ? result : -1;

		SetCollision(cell, stillInteractive);
	}

	// A 16x16 cell covers 2x2 of the 8x8 collision cells.
	private void SetCollision(Vector2I cell, bool solid)
	{
		TileMapLayer collision = Loader.Collision;
		int factor = CellSize / Loader.TileSize.X;

		for (int dy = 0; dy < factor; dy++)
		{
			for (int dx = 0; dx < factor; dx++)
			{
				Vector2I c = cell * factor + new Vector2I(dx, dy);
				if (solid)
					collision.SetCell(c, Loader.SolidSourceId, Loader.SolidAtlasCoords);
				else
					collision.EraseCell(c);
			}
		}
	}

	// Plays a named effect. 'topLeft' is the top-left corner of the 16x16 object that broke
	// (for a tile, its cell corner). Public so thrown objects can break anywhere.
	// Asks whoever listens (ItemDrops) to spawn something, exactly as a normal reaction would.
	// A carried object calls this when it breaks.
	public void RequestSpawn(string entityName, Vector2 position)
	{
		if (entityName != "")
			EmitSignal(SignalName.EntitySpawnRequested, entityName, position);
	}

	public void SpawnEffect(string effect, Vector2 topLeft)
	{
		Node2D fx;
		AudioStream sound;
		switch (effect)
		{
			case "leaves":
				fx = new LeafBurst
				{
					PieceTexture = LeafTexture,
					TicksPerFrame = LeafTicksPerFrame,
					TotalTicks = LeafTotalTicks,
				};
				sound = LeafSound;
				break;

			case "rocks":
				fx = new RockBurst
				{
					PieceTexture = RockTexture,
					TicksPerFrame = RockTicksPerFrame,
					TotalTicks = RockTotalTicks,
				};
				sound = RockSound;
				break;
				
			case "poof":
				fx = PoofScene.Instantiate<Node2D>();
				sound = PoofSound;
				break;
			
			default:
				GD.PushWarning($"InteractiveTiles: unknown effect \"{effect}\".");
				return;
		}

		PlaySound(sound);

		fx.ZIndex = 10; // above the player and the objects layer
		Node parent = Objects != null ? Objects.GetParent() : GetParent();
		parent.AddChild(fx);
		fx.GlobalPosition = topLeft;
	}

	// One short-lived player per sound. It's a child of this node (not of the burst), so the
	// sound can outlast the visual effect, and it keeps playing while the game is paused.
	private void PlaySound(AudioStream stream)
	{
		if (stream == null)
			return;

		AudioStreamPlayer player = new AudioStreamPlayer
		{
			Stream = stream,
			Bus = SoundBus,
			ProcessMode = ProcessModeEnum.Always,
		};
		player.Finished += player.QueueFree;
		AddChild(player);
		player.Play();
	}

	private void Log(string message)
	{
		if (DebugLog)
			GD.Print("InteractiveTiles: " + message);
	}

	private int TileIdAt(Vector2I cell)
	{
		Vector2I rel = cell - Scroller.MinRoom * CellsPerRoom;
		if (rel.X < 0 || rel.Y < 0 || rel.X >= _gridSize.X || rel.Y >= _gridSize.Y)
			return -1;
		return _ids[rel.Y * _gridSize.X + rel.X];
	}

	private Vector2I AtlasCoords(int id) => new Vector2I(id % AtlasColumns, id / AtlasColumns);

	private static Vector2 CellCenter(Vector2I cell) =>
		new Vector2(cell.X * CellSize + CellSize / 2, cell.Y * CellSize + CellSize / 2);

	private void LoadTileIds(Texture2D source)
	{
		Image img = source.GetImage();
		if (img.IsCompressed())
			img.Decompress();

		_gridSize = new Vector2I(img.GetWidth(), img.GetHeight());
		_ids = new int[_gridSize.X * _gridSize.Y];

		for (int y = 0; y < _gridSize.Y; y++)
		{
			for (int x = 0; x < _gridSize.X; x++)
			{
				Color c = img.GetPixel(x, y);
				_ids[y * _gridSize.X + x] = c.R8 | (c.G8 << 8);
			}
		}
	}

	private void LoadLabels(string path)
	{
		string text = Godot.FileAccess.GetFileAsString(path);
		if (string.IsNullOrEmpty(text))
		{
			GD.PushError($"InteractiveTiles: could not read {path}");
			return;
		}

		using JsonDocument doc = JsonDocument.Parse(text);
		foreach (JsonProperty tile in doc.RootElement.EnumerateObject())
		{
			TileBehavior behavior = new TileBehavior();

			foreach (JsonProperty entry in tile.Value.EnumerateObject())
			{
				if (entry.Name == "perm")
				{
					behavior.Permanent = entry.Value.GetBoolean();
					continue;
				}

				if (!Enum.TryParse(entry.Name, true, out Interaction kind))
					continue;

				TileReaction reaction = new TileReaction();
				if (entry.Value.TryGetProperty("r", out JsonElement r)) reaction.Result = r.GetInt32();
				if (entry.Value.TryGetProperty("e", out JsonElement e)) reaction.Spawns = e.GetString() ?? "";
				if (entry.Value.TryGetProperty("p", out JsonElement p)) reaction.Power = p.GetInt32();
				if (entry.Value.TryGetProperty("fx", out JsonElement fx)) reaction.Effect = fx.GetString() ?? "";
				if (entry.Value.TryGetProperty("s", out JsonElement sp) && sp.GetArrayLength() == 2)
					reaction.Sprite = new Vector2I(sp[0].GetInt32(), sp[1].GetInt32());
				behavior.Reactions[kind] = reaction;
			}

			_behaviors[int.Parse(tile.Name)] = behavior;
		}
	}
}
