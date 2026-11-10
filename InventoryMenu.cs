using System.Collections.Generic;
using Godot;

public partial class InventoryMenu : CanvasLayer
{
	[Export] public GridContainer Grid { get; set; }
	[Export] public Control Cursor { get; set; }

	// Optional: lets the menu slide the HUD bar to the top of the screen while open.
	[Export] public Hud BottomBar { get; set; }

	private readonly List<TextureRect> _icons = new();
	private bool _open;
	private int _selected;

	public override void _Ready()
	{
		// Keep running while the tree is paused, so we can close the menu.
		ProcessMode = ProcessModeEnum.Always;
		Visible = false;

		foreach (Node child in Grid.GetChildren())
			if (child is TextureRect icon)
				_icons.Add(icon);

		// Only DIRECT children of Grid that are TextureRects count as slots.
		if (_icons.Count < Inventory.ItemSlots)
			GD.PushWarning($"InventoryMenu: found {_icons.Count} TextureRect slots under Grid, expected {Inventory.ItemSlots}.");
	}

	public override void _Process(double delta)
	{
		if (!_open)
		{
			// Don't open mid-screen-scroll: the camera has the tree paused then.
			if (Input.IsActionJustPressed("start") && !GetTree().Paused)
				Open();
			return;
		}

		if (Input.IsActionJustPressed("start")) { Close(); return; }

		if (Input.IsActionJustPressed("ui_right")) Move(1, 0);
		if (Input.IsActionJustPressed("ui_left")) Move(-1, 0);
		if (Input.IsActionJustPressed("ui_down")) Move(0, 1);
		if (Input.IsActionJustPressed("ui_up")) Move(0, -1);

		if (Input.IsActionJustPressed("button_b")) EquipSelected(ItemButton.B);
		if (Input.IsActionJustPressed("button_a")) EquipSelected(ItemButton.A);

		// Every frame, so it stays correct after the grid finishes its layout.
		Cursor.GlobalPosition = _icons[_selected].GlobalPosition;
	}

	private void Open()
	{
		if (_icons.Count == 0)
			return; // nothing to select; opening would just freeze the game

		_open = true;
		Visible = true;
		GetTree().Paused = true;
		BottomBar?.SetMenuOpen(true);
		Refresh();
	}

	private void Close()
	{
		_open = false;
		Visible = false;
		GetTree().Paused = false;
		BottomBar?.SetMenuOpen(false);
	}

	private void Move(int dx, int dy)
	{
		int columns = Grid.Columns;
		int x = _selected % columns + dx;
		int y = _selected / columns + dy;
		int target = y * columns + x;

		if (x < 0 || x >= columns || y < 0 || target < 0 || target >= Inventory.ItemSlots)
			return;
		_selected = target;
	}

	private void EquipSelected(ItemButton button)
	{
		Inventory.Instance.Equip(_selected, button);
		Refresh();
	}

	private void Refresh()
	{
		for (int i = 0; i < _icons.Count && i < Inventory.ItemSlots; i++)
			_icons[i].Texture = Inventory.Instance.GetSlot(i)?.Icon;
	}
}
