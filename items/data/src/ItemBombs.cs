using Godot;
using System;

[GlobalClass]
public partial class ItemBombs : ItemData
{
	[Export] public PackedScene BombScene { get; set; }

	private const string Group = "bomb";
	private const int ItemPoseTicks = 12;
	
	public override int GetCount() => Inventory.Instance.GetAmmo(AmmoType.Bombs);
	
	public override void OnPress(Player player, ItemButton button)
	{
		if (BombScene == null)
			return;

		if (player.GetTree().GetNodesInGroup(Group).Count > 0)
			return;

		if (Inventory.Instance.TrySpendAmmo(AmmoType.Bombs, 1)) {
			player.FaceDpad();
			player.BeginItemPose(ItemPoseTicks);
			Bomb bomb = BombScene.Instantiate<Bomb>();
			bomb.AddToGroup(Group);
			player.GetParent().AddChild(bomb);
			bomb.Place(player);
		}
	}
	
}
