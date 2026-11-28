using Godot;
using System;

public partial class Powder : FxBurst
{
	[Export] public AudioStream PowderSound { get; set; }
	private const int ProbeTick = 16;
	private static readonly Vector2 ProbeOffset = new(7, 7);

	private static readonly Vector2[] SpawnOffset = [
		new( 14,   8),   // right
		new(-14,   8),   // left
		new(  6, -8),   // up
		new(  0,  16),   // down
	];

	public static Vector2 SpawnPosition(Vector2 spriteTopLeft, Vector2I facing) =>
	spriteTopLeft + SpawnOffset[SwordHitbox.DirectionIndex(facing)];

	protected override void OnTick(int tick)
	{
		if (tick != ProbeTick)
			return;

		// The original reads the object under the sprinkle exactly once, at this tick.
		InteractiveTiles.Instance?.TryInteract(GlobalPosition + ProbeOffset, Interaction.Powder);
	}
	
	public Powder()
	{
		TotalTicks = 23;   // countdown starts at 23 and the entity clears at 0; 6 poses x 4 ticks, the last shown for 3
	}

	private static readonly Piece[][] Table =
	{
		[new Piece( 1, -1, false, false), new Piece(3, -3, false, false), new Piece( 5, -2, false, false)], // pose 0
		[new Piece( 1,  0, false, false), new Piece(3, -2, false, false), new Piece( 5, -1, false, false)], // pose 1
		[new Piece( 0,  1, false, false), new Piece(4, -1, false, false), new Piece( 6,  0, false, false)], // pose 2
		[new Piece(-1,  3, false, false), new Piece(4,  1, false, false), new Piece( 9,  2, false, false)], // pose 3
		[new Piece(-2,  5, false, false), new Piece(4,  2, false, false), new Piece(10,  4, false, false)], // pose 4
		[new Piece(-2,  6, false, false), new Piece(4,  3, false, false), new Piece(10,  5, false, false)], // pose 5
	};

	protected override Piece[][] Poses => Table;	
}
