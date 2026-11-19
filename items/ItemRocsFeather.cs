using Godot;
using System;

public partial class ItemRocsFeather : ItemData
{
		[Export] public AudioStream JumpSound { get; set; }
		
		public override void Use(Player player) {
			player.Jump(JumpSound);
		}
}
