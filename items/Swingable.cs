using Godot;

// All swingable items (swords, wands)
[GlobalClass]
public partial class Swingable : ItemData
{
	[Export] public bool Chargeable { get; set; } = true;
	public override void Use(Player player)
	{
		player.StartSwing(Chargeable);
	}
}
