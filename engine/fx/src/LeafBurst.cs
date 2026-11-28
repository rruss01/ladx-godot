using System.Collections.Generic;
using Godot;

// Data from CutLeavesSpriteRect in LADX-Disassembly (src/code/entities/bank19.asm)
public partial class LeafBurst : FxBurst
{	
	public LeafBurst()
	{
		TotalTicks = 30;
	}

	private static readonly Piece[][] Table =
	{
		new[] { new Piece(-4, 2, false, false), new Piece(4, -5, true, true), new Piece(6, 5, false, false), new Piece(10, 1, true, false) }, // pose 0
		new[] { new Piece(-1, 1, false, false), new Piece(4, -7, true, true), new Piece(6, 8, false, false), new Piece(7, 2, true, false) }, // pose 1
		new[] { new Piece(0, 0, true, false), new Piece(2, -8, true, true), new Piece(4, 4, true, false), new Piece(7, 10, true, false) }, // pose 2
		new[] { new Piece(1, -2, true, false), new Piece(1, 4, true, true), new Piece(5, 4, true, false), new Piece(7, 12, true, false) }, // pose 3
		new[] { new Piece(0, -3, true, false), new Piece(-2, 4, true, true), new Piece(8, 8, true, false), new Piece(9, 14, true, false) }, // pose 4
		new[] { new Piece(-1, -4, false, false), new Piece(-6, 4, false, true), new Piece(9, 8, true, false), new Piece(10, 15, false, false) }, // pose 5
		new[] { new Piece(-2, -5, false, false), new Piece(-7, 3, false, true), new Piece(12, 8, false, false), new Piece(11, 17, false, false) }, // pose 6
		new[] { new Piece(-3, -6, false, false), new Piece(-9, 1, false, true), new Piece(13, 9, false, false), new Piece(12, 15, false, false) }, // pose 7
	};

	protected override Piece[][] Poses => Table;
}
