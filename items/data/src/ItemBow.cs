using Godot;

[GlobalClass]
public partial class ItemBow : ItemData
{
	[Export] public PackedScene ArrowScene { get; set; }
	[Export] public AudioStream ShootSound { get; set; }   // audio/sfx/player/items/bow_arrow.wav
	[Export] public AudioStream EmptySound { get; set; }   // optional: the wrong-answer jingle

	private const int MaxInAir = 2;          // ARROW_MAX_ACTIVE_COUNT
	private const int CooldownTicks = 16;    // wIsShootingArrow = $10
	private const int ItemPoseTicks = 12;
	private const string ProjectileGroup = "projectile";

	private ulong _nextShotFrame;            // physics frame when the next shot is allowed

	public override int GetCount() => Inventory.Instance.GetAmmo(AmmoType.Arrows);

	public override void OnPress(Player player, ItemButton button)
	{
		if (player.IsCarrying) return;

		ulong now = Engine.GetPhysicsFrames();
		if (now < _nextShotFrame) return;
		if (player.GetTree().GetNodesInGroup(ProjectileGroup).Count >= MaxInAir) return;

		_nextShotFrame = now + CooldownTicks;          // starts even if we're out of arrows

		if (!Inventory.Instance.TrySpendAmmo(AmmoType.Arrows, 1))
		{
			player.PlayOneShot(EmptySound);
			return;
		}

		player.FaceDpad();
		player.BeginItemPose(ItemPoseTicks);
		Arrow arrow = ArrowScene.Instantiate<Arrow>();
		player.GetParent().AddChild(arrow);
		arrow.Launch(player.GlobalPosition, player.Facing);
		player.PlayOneShot(ShootSound);
	}
}
