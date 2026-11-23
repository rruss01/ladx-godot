using Godot;
using System;

[GlobalClass]
public partial class ItemData : Resource
{
	[Export] public string DisplayName { get; set; } = "";
	[Export] public Texture2D Icon { get; set; }

	public virtual void OnPress(Player player, ItemButton button) {}
	public virtual void OnHeld(Player player, ItemButton button) {}
	public virtual void OnRelease(Player player, ItemButton button) {}
	public virtual void Cancel(Player player) {}
	public virtual int GetCount() => -1;
}
