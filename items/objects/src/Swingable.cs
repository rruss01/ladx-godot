using Godot;

// All swingable items (swords, wands)
[GlobalClass]
public partial class Swingable : ItemData
{
	[Export] public bool Chargeable { get; set; } = true;
	[Export] public int ChargeTicks = 40;
	public override void OnPress(Player player, ItemButton button)
	{
		if (player.SwordOut) { return; }
		player.StartSwing(Chargeable, button);
	}

	public override void OnHeld(Player player, ItemButton button) {
		// only the button that swung the sword charges it; wand never sets SwordOut.
		if (!player.SwordOut || button != player.SwordButton) return;

		int before = player.Charge;
		player.Charge = Mathf.Min(before + 1, ChargeTicks);
		if (before < ChargeTicks && player.Charge == ChargeTicks) {
			player.PlayChargedSound();
			player.Charged = true;
		}
	}

	public override void OnRelease(Player player, ItemButton button) {
		if (!player.SwordOut || button != player.SwordButton) return;

		bool charged = player.Charge >= ChargeTicks;
   		player.EndSwordHold();
		if (charged) player.StartSpin(); 
	}
}
