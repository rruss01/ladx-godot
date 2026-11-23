using Godot;
using System;

[GlobalClass]
public partial class ItemBombs : ItemData
{
	[Export] public PackedScene BombScene { get; set; }

	private const string Group = "bomb";
	
	public override int GetCount() => Inventory.Instance.BombCount;
	
	public override void OnPress(Player player, ItemButton button)
	{
		if (BombScene == null)
			return;

		if (player.GetTree().GetNodesInGroup(Group).Count > 0)
			return;

		if (Inventory.Instance.TryPlaceBomb()) {
			Bomb bomb = BombScene.Instantiate<Bomb>();
			bomb.AddToGroup(Group);
			player.GetParent().AddChild(bomb);
			bomb.Place(player);
		}
	}
	
}
