using Godot;

// Add as a child of the Game root (next to the Player) and fill in the fields.
// It listens to InteractiveTiles.EntitySpawnRequested and rolls the drop.
//
// Original odds for a cut bush (LADX-Disassembly, bank0.asm ".dropRandomItem"):
//   * 1 in 8 bushes drops anything at all  (random byte & 7 == 0)
//   * if it does, it is a rupee or a heart, 50/50
//   => 1/16 rupee, 1/16 heart, 7/8 nothing. Bushes never drop bombs or arrows.
public partial class ItemDrops : Node
{
	[Export] public InteractiveTiles Tiles { get; set; }

	// Optional: when set, drops are removed when the screen scrolls (the original clears all
	// entities on a room change).
	[Export] public ScreenScroller Scroller { get; set; }

	[Export] public PackedScene HeartScene { get; set; }
	[Export] public PackedScene RupeeScene { get; set; }

	// Print every roll to the Output panel while testing.
	[Export] public bool DebugLog { get; set; }

	public override void _Ready()
	{
		if (Tiles != null)
			Tiles.EntitySpawnRequested += OnSpawnRequested;
		if (Scroller != null)
			Scroller.RoomChanged += OnRoomChanged;
	}

	public override void _ExitTree()
	{
		if (IsInstanceValid(Tiles))
			Tiles.EntitySpawnRequested -= OnSpawnRequested;
		if (IsInstanceValid(Scroller))
			Scroller.RoomChanged -= OnRoomChanged;
	}

	private void OnSpawnRequested(string entityName, Vector2 position)
	{
		// interactive_tiles.json says "random item (heart, rupee)" for bushes.
		if (!entityName.StartsWith("random item"))
			return;

		// 1 in 8 drops anything.
		uint roll = GD.Randi();
		if ((roll & 7) != 0)
		{
			Log($"bush at {position}: no drop");
			return;
		}

		// Then 50/50. In the original a clear low bit means rupee, a set one means heart.
		bool heart = (GD.Randi() & 1) == 1;
		PackedScene scene = heart ? HeartScene : RupeeScene;
		Log($"bush at {position}: dropping a {(heart ? "heart" : "rupee")}");

		if (scene == null)
		{
			GD.PushWarning($"ItemDrops: assign the {(heart ? "HeartScene" : "RupeeScene")}.");
			return;
		}

		// Deferred, because the cut usually happens in the middle of a physics step.
		Callable.From(() => Spawn(scene, position)).CallDeferred();
	}

	private void Spawn(PackedScene scene, Vector2 position)
	{
		Node2D drop = scene.Instantiate<Node2D>();
		GetParent().AddChild(drop);
		drop.GlobalPosition = position;
	}

	private void OnRoomChanged(Vector2I room)
	{
		foreach (Node drop in GetTree().GetNodesInGroup("droppable"))
			drop.QueueFree();
	}

	private void Log(string message)
	{
		if (DebugLog)
			GD.Print("[ItemDrops] " + message);
	}
}
