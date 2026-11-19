using Godot;
using System.Collections.Generic;

// Keeps one WarpTrigger alive per warp in the room the player is standing in.
//
//   MapInfo.Warps (all warps of the map, as data)
//        |  SetMap(): sorted into a dictionary, room -> list
//        v
//   Scroller.RoomChanged  ->  delete the old triggers, spawn this room's
//
// Add as a child of Game and fill in Scroller. MapManager calls SetMap().
public partial class WarpManager : Node
{
	[Export] public ScreenScroller Scroller { get; set; }

	// Layers the triggers listen on. 0 = copy the player's own collision layer (recommended).
	[Export(PropertyHint.Layers2DPhysics)] public uint TriggerMask { get; set; } = 0;

	// Print what spawns on every room change.
	[Export] public bool DebugLog { get; set; }

	private readonly Dictionary<Vector2I, List<WarpData>> _byRoom = new();
	private readonly List<WarpTrigger> _live = new();
	private Vector2I _roomSize = new Vector2I(160, 128);

	public override void _Ready()
	{
		if (Scroller != null)
			Scroller.RoomChanged += OnRoomChanged;
	}

	public override void _ExitTree()
	{
		if (IsInstanceValid(Scroller))
			Scroller.RoomChanged -= OnRoomChanged;
	}

	// Called by MapManager each time a different map scene is loaded.
	public void SetMap(MapInfo map)
	{
		ClearTriggers();
		_byRoom.Clear();
		_roomSize = map.RoomSize;

		foreach (WarpData warp in map.Warps)
		{
			if (warp == null)
				continue;

			if (!_byRoom.TryGetValue(warp.Room, out List<WarpData> list))
				_byRoom[warp.Room] = list = new List<WarpData>();

			list.Add(warp);
		}

		Log($"map '{map.MapId}': {map.Warps.Count} warps in {_byRoom.Count} rooms");
	}

	private void OnRoomChanged(Vector2I room)
	{
		// Physics objects can't be added in the middle of a physics step, and this signal can
		// arrive from one. Waiting until the end of the frame avoids that error.
		Callable.From(() => Rebuild(room)).CallDeferred();
	}

	private void Rebuild(Vector2I room)
	{
		ClearTriggers();

		if (!_byRoom.TryGetValue(room, out List<WarpData> warps))
		{
			Log($"room {room}: no warps");
			return;
		}

		// Without an explicit mask, listen for whatever layer the player is on.
		uint mask = TriggerMask;
		if (mask == 0 && Scroller?.Target is CollisionObject2D body)
			mask = body.CollisionLayer;

		Vector2 roomOrigin = new Vector2(room.X * _roomSize.X, room.Y * _roomSize.Y);

		foreach (WarpData warp in warps)
		{
			WarpTrigger trigger = new WarpTrigger
			{
				DestinationMap = warp.DestinationMap,
				DestinationRoom = warp.DestinationRoom,
				DestinationScreenPosition = warp.DestinationScreenPosition,
				CollisionLayer = 0,        // nothing needs to collide WITH a trigger
				CollisionMask = mask,
				Monitorable = false,
			};

			// The shape is centred on its node, so put the node in the rectangle's middle.
			Vector2 size = new Vector2(warp.Area.Size.X, warp.Area.Size.Y);
			trigger.Position = roomOrigin + new Vector2(warp.Area.Position.X, warp.Area.Position.Y) + size / 2f;
			trigger.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = size } });

			AddChild(trigger);
			_live.Add(trigger);
		}

		Log($"room {room}: spawned {_live.Count} warps");
	}

	private void ClearTriggers()
	{
		foreach (WarpTrigger trigger in _live)
			if (IsInstanceValid(trigger))
				trigger.QueueFree();

		_live.Clear();
	}

	private void Log(string message)
	{
		if (DebugLog)
			GD.Print("[WarpManager] " + message);
	}
}
