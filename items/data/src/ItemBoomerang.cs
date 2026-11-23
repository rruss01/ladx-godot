using Godot;
using System;

[GlobalClass]
public partial class ItemBoomerang : ItemData
{
	[Export] public PackedScene BoomerangScene { get; set; }

	private const int Straight = 32; // $20
	private const int Diagonal = 24; // $18

	private const string Group = "boomerang";

	public override void OnPress(Player player, ItemButton button)
	{
		if (BoomerangScene == null)
			return;

		if (player.GetTree().GetNodesInGroup(Group).Count > 0)
			return;

		Boomerang boomerang = BoomerangScene.Instantiate<Boomerang>();
		boomerang.AddToGroup(Group);
		boomerang.Launch(player, ChooseVelocity(player));

		player.GetParent().AddChild(boomerang);
		boomerang.GlobalPosition = player.GlobalPosition;
	}

	private static Vector2I ChooseVelocity(Player player)
	{
		bool right = Input.IsActionPressed("ui_right");
		bool left = Input.IsActionPressed("ui_left");
		bool up = Input.IsActionPressed("ui_up");
		bool down = Input.IsActionPressed("ui_down");
 
		int x = (right ? 1 : 0) - (left ? 1 : 0);
		int y = (down ? 1 : 0) - (up ? 1 : 0);
		int held = (right ? 1 : 0) + (left ? 1 : 0) + (up ? 1 : 0) + (down ? 1 : 0);
 
		if (held >= 2 && (x != 0 || y != 0))
			return new Vector2I(x * Diagonal, y * Diagonal);
		
		return new Vector2I(
			Mathf.RoundToInt(player.Facing.X * Straight),
			Mathf.RoundToInt(player.Facing.Y * Straight));
	}
}
