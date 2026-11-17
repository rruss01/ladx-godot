using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

public enum Interaction { Slash, Lift, Push, Bomb }

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

	// A TileMapLayer at (0, 0) with a 16x16 TileSet made from tiles16_atlas.png.
	// Draw order: above the baked map, below the player.
	[Export] public TileMapLayer Objects { get; set; }

	// tile_ids.png: one pixel per 16x16 map cell, R = tile id low byte, G = high byte.
	[Export] public Texture2D TileIds { get; set; }

	[Export] public string LabelsPath { get; set; } = "res://interactive_tiles.json";

	// Prints what every interaction attempt did, to the Output panel. For debugging.
	[Export] public bool DebugLog { get; set; }

	[Export] public int AtlasSourceId { get; set; } = 0;
	[Export] public int AtlasColumns { get; set; } = 32; // tiles per row in tiles16_atlas.png

	[ExportGroup("Leaf effect (bushes)")]
	[Export] public Texture2D LeafTexture { get; set; } // optional; empty = built-in placeholder
	[Export] public int LeafTicksPerFrame { get; set; } = 4; // original: 4 game frames per pose
	[Export] public int LeafTotalTicks { get; set; } = 30;   // original: about 30 game frames

	[ExportGroup("Rock effect (rocks, pots)")]
	[Export] public Texture2D RockTexture { get; set; } // optional; empty = built-in placeholder
	[Export] public int RockTicksPerFrame { get; set; } = 4;
	[Export] public int RockTotalTicks { get; set; } = 14;

	private readonly Dictionary<int, TileBehavior> _behaviors = new();
	private readonly Dictionary<Vector2I, int> _current = new();   // this room: cell -> tile id (-1 = inert)
	private readonly Dictionary<Vector2I, int> _permanent = new(); // cell -> result tile id, kept across rooms

	private int[] _ids;
	private Vector2I _gridSize;

	private Vector2I CellsPerRoom => new Vector2I(Loader.RoomSize.X / CellSize, Loader.RoomSize.Y / CellSize);

	public override void _EnterTree()
	{
		Instance = this;
	}

	public override void _Ready()
	{
		LoadLabels();
		LoadTileIds();

		// Wait for the collision to be built first, so erasing cells actually sticks.
		Loader.RoomLoaded += OnRoomLoaded;
	}

	public override void _ExitTree()
	{
		if (IsInstanceValid(Loader))
			Loader.RoomLoaded -= OnRoomLoaded;
	}

	// The one entry point. Returns true if the point hit a tile that reacted.
	// 'power' is the caller's strength (e.g. power bracelet level); reactions can demand more.
	public bool TryInteract(Vector2 worldPoint, Interaction kind, int power = 0)
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

		if (reaction.Spawns != "")
			EmitSignal(SignalName.EntitySpawnRequested, reaction.Spawns, CellCenter(cell));

		if (reaction.Effect != "")
			SpawnEffect(reaction.Effect, new Vector2(cell.X * CellSize, cell.Y * CellSize));

		return true;
	}

	private void OnRoomLoaded(Vector2I room)
	{
		Objects.Clear();
		_current.Clear();

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

		burst.ZIndex = 10; // above the player and the objects layer
		Objects.GetParent().AddChild(burst);
		burst.GlobalPosition = topLeft;
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

	private void LoadTileIds()
	{
		Image img = TileIds.GetImage();
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

	private void LoadLabels()
	{
		string text = Godot.FileAccess.GetFileAsString(LabelsPath);
		if (string.IsNullOrEmpty(text))
		{
			GD.PushError($"InteractiveTiles: could not read {LabelsPath}");
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
