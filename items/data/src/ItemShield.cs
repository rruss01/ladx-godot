using Godot;
using System;

[GlobalClass]
public partial class ItemShield : ItemData
{
	[Export] public int Level { get; set; } = 1;
	[Export] public AudioStream ShieldSound { get; set; }
	
	public override void OnPress(Player player, ItemButton button) {
		player.PlayOneShot(ShieldSound);
	}
	
	public override void OnHeld(Player player, ItemButton button) {
		if (player.IsCarrying) return;
		player.RaiseShield();
	}
}
