using System.Collections.Generic;
using Godot;

// The pieces that fly apart when a rock or pot breaks. Data from SmashedRockSpriteRect in
// LADX-Disassembly (src/code/entities/bank19.asm): 4 poses of 4 pieces, 4 frames each.
// The game plays the poses in REVERSE (pose 3, closest to the center, comes first).
public partial class RockBurst : FxBurst
{
	public RockBurst()
	{
		TotalTicks = 14;
	}

	private static readonly Piece[][] Table =
	{
		new[] { new Piece(-4, -3, false, false), new Piece(12, -4, false, false), new Piece(-5, 14, false, false), new Piece(13, 12, false, false) }, // pose 0
		new[] { new Piece(-3, -5, false, false), new Piece(11, -6, false, false), new Piece(-4, 11, false, false), new Piece(12, 9, false, false) }, // pose 1
		new[] { new Piece(-2, -3, false, false), new Piece(10, -4, false, false), new Piece(-3, 11, false, false), new Piece(10, 8, false, false) }, // pose 2
		new[] { new Piece(0, -1, false, false), new Piece(8, 0, false, false), new Piece(-1, 10, false, false), new Piece(9, 8, false, false) }, // pose 3
	};

	protected override Piece[][] Poses => Table;

	protected override int PoseAt(int tick) =>
		Poses.Length - 1 - Mathf.Min(tick / TicksPerFrame, Poses.Length - 1);

}
