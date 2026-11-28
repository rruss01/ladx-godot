using Godot;
using System;

[GlobalClass]
public partial class ItemWand : ItemData
{
	[Export] public PackedScene FireballScene { get; set; }
	[Export] public AudioStream CastSound { get; set; }

	private const int MaxInAir = 2;
	private const int PoseTicks = 14;
	private const int SpawnAtTicksLeft = 12;
	private const string ProjectileGroup = "projectile";

	public override void OnPress(Player player, ItemButton button)
	{
		if (player.IsCarrying) return;
		if (player.GetTree().GetNodesInGroup(ProjectileGroup).Count >= MaxInAir) return;

		player.FaceDpad();
		player.BeginItemPose(PoseTicks, ticksLeft =>
		{
			if (ticksLeft != SpawnAtTicksLeft) return;
			player.FaceDpad();
			var fireball = FireballScene.Instantiate<WandProjectile>();
			player.GetParent().AddChild(fireball);
			fireball.Launch(player.GlobalPosition, player.Facing);
			player.PlayOneShot(CastSound);
		}, true);
	}
}
