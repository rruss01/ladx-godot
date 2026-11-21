using Godot;
using System;

public partial class ItemRocsFeather : ItemData
{
		[Export] public AudioStream JumpSound { get; set; }
		public override void OnPress(Player player, ItemButton button) {
			player.Jump(JumpSound);
		}
}
