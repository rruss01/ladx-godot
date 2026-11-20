using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

public enum Interaction { Slash, Lift, Push, Bomb }

// What the player is now holding after lifting a tile: enough to draw it and to break it later.
public readonly record struct LiftedTile(int TileId, Vector2I Cell, Texture2D Art, string BreakEffect);

// What happens when one kind of interaction hits a tile.
public sealed class TileReaction
{
	public int Result = -1;     // tile id swapped in; -1 = art unchanged
	public string Spawns = "";  // free-text entity name, e.g. "random item (heart, rupee)"
	public string Effect = "";  // named visual effect, e.g. "leaves"
	public int Power;           // minimum power level the caller must have
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

	// Audio bus the effect sounds play on.
	[Export] public StringName SoundBus { get; set; } = "Master";

	private readonly Dictionary<int, TileBehavior> _behaviors = new();
	private readonly Dictionary<Vector2I, int> _current = new();   // this room: cell -> tile id (-1 = inert)
	private Dictionary<Vector2I, int> _permanent = new();          // this map: cell -> result tile id
	private readonly Dictionary<string, Dictionary<Vector2I, int>> _permanentByMap = new(); // kept per map

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
		_atlasImage = null;   // a different map may use a different atlas
		_liftArt.Clear();

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
		// The tile that is left behind tells us what the object's background looks like (see LiftArtFor).
		int resultId = -1;
		string breakEffect = "rocks";
		if (_behaviors.TryGetValue(id, out TileBehavior behavior)
			&& behavior.Reactions.TryGetValue(Interaction.Lift, out TileReaction lift))
		{
			resultId = lift.Result;
		}
		Texture2D art = LiftArtFor(id, resultId);

		if (_behaviors.TryGetValue(id, out behavior)
			&& behavior.Reactions.TryGetValue(Interaction.Slash, out TileReaction slash)
			&& slash.Effect != "")
		{
			// Anything a sword can also cut is a bush, and a bush breaks into leaves.
			breakEffect = slash.Effect;
		}

		if (!TryInteract(worldPoint, Interaction.Lift, power))
			return false;

		lifted = new LiftedTile(id, cell, art, breakEffect);
		return true;
	}

	// The atlas as an image, and the carried-object pictures made from it so far.
	private Image _atlasImage;
	private readonly Dictionary<(int, int), Texture2D> _liftArt = new();

	// The picture of a lifted object: the tile's art with its BACKGROUND made transparent.
	//
	// Why: every tile in the atlas is a full 16x16 square, background included (the grass around
	// a bush, the floor around a pot). That is right for tiles, but a carried object must show
	// only the object. The tile it leaves behind ('resultId') is exactly the background without
	// the object, so any pixel that matches it AND is connected to the tile's edge is background.
	private Texture2D LiftArtFor(int id, int resultId)
	{
		if (_liftArt.TryGetValue((id, resultId), out Texture2D cached))
			return cached;

		Texture2D art = BuildLiftArt(id, resultId);
		_liftArt[(id, resultId)] = art;
		return art;
	}

	private Texture2D BuildLiftArt(int id, int resultId)
	{
		if (Objects?.TileSet?.GetSource(AtlasSourceId) is not TileSetAtlasSource atlas || atlas.Texture == null)
			return null;

		if (_atlasImage == null)
		{
			_atlasImage = atlas.Texture.GetImage();
			if (_atlasImage.IsCompressed())
				_atlasImage.Decompress();
			_atlasImage.Convert(Image.Format.Rgba8); // needs an alpha channel to hold transparency
		}

		Image art = _atlasImage.GetRegion(atlas.GetTileTextureRegion(AtlasCoords(id)));

		// -1 means the tile doesn't change when lifted, so there is no background to compare against.
		if (resultId >= 0)
		{
			Image under = _atlasImage.GetRegion(atlas.GetTileTextureRegion(AtlasCoords(resultId)));
			RemoveBackground(art, under);
		}

		return ImageTexture.CreateFromImage(art);
	}

	// Flood fill: start at every pixel on the tile's border that matches the tile underneath,
	// make it transparent, and keep spreading to neighbours that match too. Stopping at pixels
	// that differ is what keeps the object's own outline (and its insides) intact. A plain
	// "make every matching pixel transparent" would punch holes wherever the object happens
	// to contain the same colour as the ground.
	private static void RemoveBackground(Image art, Image under)
	{
		int w = art.GetWidth();
		int h = art.GetHeight();
		bool[,] seen = new bool[w, h];
		Stack<Vector2I> todo = new Stack<Vector2I>();

		void Visit(int x, int y)
		{
			if (x < 0 || y < 0 || x >= w || y >= h || seen[x, y])
				return;

			seen[x, y] = true;
			if (art.GetPixel(x, y) != under.GetPixel(x, y))
				return; // part of the object: stop here

			art.SetPixel(x, y, new Color(0, 0, 0, 0));
			todo.Push(new Vector2I(x, y));
		}

		for (int x = 0; x < w; x++) { Visit(x, 0); Visit(x, h - 1); }
		for (int y = 0; y < h; y++) { Visit(0, y); Visit(w - 1, y); }

		while (todo.Count > 0)
		{
			Vector2I p = todo.Pop();
			Visit(p.X + 1, p.Y);
			Visit(p.X - 1, p.Y);
			Visit(p.X, p.Y + 1);
			Visit(p.X, p.Y - 1);
		}
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
				if (id != 4)
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
	public void SpawnEffect(string effect, Vector2 topLeft)
	{
		FxBurst burst;

		switch (effect)
		{
			case "leaves":
				burst = new LeafBurst
				{
					PieceTexture = LeafTexture,
					TicksPerFrame = LeafTicksPerFrame,
					TotalTicks = LeafTotalTicks,
				};
				break;

			case "rocks":
				burst = new RockBurst
				{
					PieceTexture = RockTexture,
					TicksPerFrame = RockTicksPerFrame,
					TotalTicks = RockTotalTicks,
				};
				break;

			default:
				GD.PushWarning($"InteractiveTiles: unknown effect \"{effect}\".");
				return;
		}

		PlaySound(effect == "leaves" ? LeafSound : RockSound);

		burst.ZIndex = 10; // above the player and the objects layer
		Node parent = Objects != null ? Objects.GetParent() : GetParent();
		parent.AddChild(burst);
		burst.GlobalPosition = topLeft;
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
				behavior.Reactions[kind] = reaction;
			}

			_behaviors[int.Parse(tile.Name)] = behavior;
		}
	}
}
