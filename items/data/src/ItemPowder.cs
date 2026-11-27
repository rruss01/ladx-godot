using Godot;

[GlobalClass]
public partial class ItemPowder : ItemData
{
	[Export] public AudioStream SprinkleSound { get; set; }   // original: JINGLE_POWDER
	[Export] public AudioStream EmptySound { get; set; }      // the wrong-answer jingle
	[Export] public Texture2D ParticleTexture { get; set; }   // the 8x16 particle (tile $24)

	private const int ItemPoseTicks = 14;    // $0E: wLinkAttackStepAnimationCountdown

	private ulong _nextUseFrame;             // physics frame when powder can be used again

	public override int GetCount() => Inventory.Instance.GetAmmo(AmmoType.Powder);

	public override void OnPress(Player player, ItemButton button)
	{
		if (player.IsCarrying) return;

		ulong now = Engine.GetPhysicsFrames();
		if (now < _nextUseFrame) return;
		
		if (!Inventory.Instance.TrySpendAmmo(AmmoType.Powder, 1))
		{
			player.PlayOneShot(EmptySound);
			return;
		}

		_nextUseFrame = now + ItemPoseTicks;

		Powder sprinkle = new() { PieceTexture = ParticleTexture };
		sprinkle.ZIndex = 10;
		player.GetParent().AddChild(sprinkle);
		sprinkle.GlobalPosition = Powder.SpawnPosition(
			player.GlobalPosition + player.SpriteTopLeftOffset, player.Facing);

		player.PlayOneShot(SprinkleSound);

		// Running out of powder removes it from inventory.
		if (Inventory.Instance.GetAmmo(AmmoType.Powder) == 0)
			Inventory.Instance.Remove(this);
	}
}
