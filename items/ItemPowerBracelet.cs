using Godot;

// The Power Bracelet. Like the Shield, it is mostly just a label: Player.cs does the work,
// because grabbing and lifting depend on holding the button, not on pressing it once.
//
// Make two resources from this class (replace bracelet1.tres and bracelet2.tres):
//   bracelet1: Power 0, LiftStageTicks 8
//   bracelet2: Power 1, LiftStageTicks 4
[GlobalClass]
public partial class ItemPowerBracelet : ItemData
{
	// The strength this bracelet gives. A tile's "p" in interactive_tiles.json is the minimum
	// Power it needs: 0 for bushes and plain rocks, 1 for the heavy rocks (bracelet 2 only).
	[Export] public int Power { get; set; } = 0;

	// How many ticks you must pull away from the object before it lifts. Original: 8
	// (3 with the Piece of Power, which we don't have yet).
	[Export] public int PullTicks { get; set; } = 8;

	// How long each of the two slow stages of the lift takes. Original: 8 for bracelet 1, 4 for bracelet 2.
	[Export] public int LiftStageTicks { get; set; } = 8;

	// Original: WAVE_SFX_LIFT_UP when the lift starts, JINGLE_FALL_DOWN when you throw.
	[Export] public AudioStream LiftSound { get; set; }
	[Export] public AudioStream ThrowSound { get; set; }
}
