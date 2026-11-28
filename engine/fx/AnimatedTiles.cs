using Godot;
using System.Collections.Generic;
using System.Text.Json;

public partial class AnimatedTiles : Node
{
	private const int CellSize = 16;

	[Export] public CollisionLoader Loader { get; set; }
	[Export] public ScreenScroller Scroller { get; set; }
	[Export] public int AtlasSourceId { get; set; } = 0;   // the source id of animated_tiles.png in the layer's TileSet

	private readonly Dictionary<int, Vector2I> _table = new();   // tile id -> atlas coords
	private TileIdGrid _grid;
	private TileMapLayer _layer;
	private readonly HashSet<Vector2I> _placed = new();

	private Vector2I CellsPerRoom => new(Loader.RoomSize.X / CellSize, Loader.RoomSize.Y / CellSize);

	public override void _Ready() { 
		Loader.RoomLoaded += OnRoomLoaded;
		Scroller.ScrollStarted += PlaceRoom;
	}

	public override void _ExitTree()
	{
		if (IsInstanceValid(Loader)) Loader.RoomLoaded -= OnRoomLoaded;
		if (IsInstanceValid(Scroller)) Scroller.ScrollStarted -= PlaceRoom;
	}

	// Called by MapManager each time a map loads, like InteractiveTiles.SetMap.
	public void SetMap(MapInfo map)
	{
		_table.Clear();
		_placed.Clear();
		_grid = null;
		_layer = null;

		// Maps without an Animated layer, a tile-id image or a table have nothing to do.
		if (map.Animated == null || map.TileIds == null || string.IsNullOrEmpty(map.AnimatedTilesTable))
			return;

		using JsonDocument doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(map.AnimatedTilesTable));
		foreach (JsonProperty entry in doc.RootElement.EnumerateObject())
			_table[int.Parse(entry.Name)] = new Vector2I(entry.Value[0].GetInt32(), entry.Value[1].GetInt32());

		_grid = new TileIdGrid(map.TileIds);
		_layer = map.Animated;
	}

	private void OnRoomLoaded(Vector2I room)
	{
		if (_layer == null) return;
		
		foreach (Vector2I old in new List<Vector2I>(_placed))
			if (old != room)
				RemoveRoom(old);
				
		PlaceRoom(room);
	}
	
	private void PlaceRoom(Vector2I room) {
		
		if (_layer == null || !_placed.Add(room)) return;
		
		Vector2I perRoom = CellsPerRoom;
		Vector2I origin = room * perRoom;
		Vector2I mapOrigin = Scroller.MinRoom * perRoom;

		for (int ty = 0; ty < perRoom.Y; ty++)
			for (int tx = 0; tx < perRoom.X; tx++)
			{
				Vector2I cell = origin + new Vector2I(tx, ty);
				if (_table.TryGetValue(_grid.At(cell - mapOrigin), out Vector2I coords))
					_layer.SetCell(cell, AtlasSourceId, coords);
			}
	}
	
	private void RemoveRoom(Vector2I room) {
		_placed.Remove(room);
		
		Vector2I perRoom = CellsPerRoom;
		Vector2I origin = room * perRoom;
		for (int ty = 0; ty < perRoom.Y; ty++)
			for (int tx = 0; tx < perRoom.X; tx++)
				_layer.EraseCell(origin + new Vector2I(tx, ty));
	}
}
