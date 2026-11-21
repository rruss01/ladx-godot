using Godot;

// All swingable items (swords, wands)
[GlobalClass]
public partial class Swingable : ItemData
{
	[Export] public bool Chargeable { get; set; } = true;
	public int ChargeTicks = 40;
	public override void Use(Player player)
	{
		if (player.SwordOut) { return; }
		player.StartSwing(Chargeable);
	}

	public override void OnHeld(Player player, ItemButton button) {
		// only the button that swung the sword charges it; wand never sets SwordOut.
		if (!player.SwordOut || button != player.SwordButton) return;

		int before = player.Charge;
		player.Charge = Mathf.Min(before + 1, ChargeTicks);
		if (before < ChargeTicks && player.Charge == ChargeTicks)
			player.PlayChargedSound();
	}

	public override void OnRelease(Player player, ItemButton button) {
		if (!player.SwordOut || button != player.SwordButton) return;

		bool charged = player.Charge >= ChargeTicks;
   		player.EndSwordHold();           // Charge = 0, SwordOut = false
		if (charged) player.StartSpin(); // make this public
	}
}
