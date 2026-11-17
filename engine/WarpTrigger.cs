using Godot;

// A door, staircase or cave mouth. An Area2D with a CollisionShape2D, placed in a map scene.
// Its collision MASK must include the layer the player is on.
public partial class WarpTrigger : Area2D
{
	// Path of the map scene to go to. Leave empty to warp within the current map (stairs).
	[Export(PropertyHint.File, "*.tscn")] public string DestinationMap { get; set; } = "";

	// Which room of that map, and where on its screen (pixels from the top-left).
	// Don't land on top of another trigger: put the arrival point just in front of it.
	[Export] public Vector2I DestinationRoom { get; set; } = Vector2I.Zero;
	[Export] public Vector2I DestinationScreenPosition { get; set; } = new Vector2I(80, 64);

	public override void _Ready()
	{
		BodyEntered += OnBodyEntered;
	}

	private void OnBodyEntered(Node2D body)
	{
		if (body is Player)
			MapManager.Instance.GoTo(DestinationMap, DestinationRoom, DestinationScreenPosition);
	}
}
