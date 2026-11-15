using System.Collections.Generic;
using Godot;

public partial class InventoryMenu : CanvasLayer
{
	[Export] public GridContainer Grid { get; set; }
	[Export] public Control Cursor { get; set; }

	[Export] public Vector2 CursorOffset { get; set; } = Vector2.Zero;

	[Export] public Hud BottomBar { get; set; }
	[Export] public Texture2D EmptySlotTexture { get; set; }

	[ExportGroup("Fade to white (frames)")]
	[Export] public int FadeOutFrames { get; set; } = 16;  // screen -> white
	[Export] public int HoldFrames { get; set; } = 12;     // fully white; menu swaps here
	[Export] public int FadeInFrames { get; set; } = 16;   // white -> new screen
	// Number of brightness levels in each fade. 0 = smooth fade.
	[Export] public int FadeSteps { get; set; } = 6;

	private enum Phase { None, FadeOut, Hold, FadeIn }

	private readonly List<TextureRect> _icons = new();
	private bool _open;
	private int _selected;

	private Phase _phase = Phase.None;
	private bool _opening;
	private int _frame;
	private ColorRect _fade;

	public override void _Ready()
	{
		// Keep running while the tree is paused, so menu can close
		ProcessMode = ProcessModeEnum.Always;
		Visible = false;

		foreach (Node child in Grid.GetChildren())
			if (child is TextureRect icon)
				_icons.Add(icon);

		// Only DIRECT children of Grid that are TextureRects count as slots.
		if (_icons.Count < Inventory.ItemSlots)
			GD.PushWarning($"InventoryMenu: found {_icons.Count} TextureRect slots under Grid, expected {Inventory.ItemSlots}.");

		// White overlay on its own high layer so it covers the HUD and the menu.
		// Added to parent, because hiding this node must not hide it.
		_fade = new ColorRect
		{
			Color = new Color(1, 1, 1, 0),
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false,
		};
		CanvasLayer fadeLayer = new CanvasLayer { Layer = 100, ProcessMode = ProcessModeEnum.Always };
		fadeLayer.AddChild(_fade);
		GetParent().CallDeferred(Node.MethodName.AddChild, fadeLayer);
	}

	public override void _Process(double delta)
	{
		// Keep the cursor on the selected slot (stays correct after the grid's layout settles).
		if (_open && _icons.Count > 0)
			Cursor.GlobalPosition = _icons[_selected].GlobalPosition + CursorOffset;

		// Ignore all input while a fade is running.
		if (_phase != Phase.None)
			return;

		if (!_open) {
			// Don't open mid-screen-scroll: the camera has the tree paused then.
			if (Input.IsActionJustPressed("start") && !GetTree().Paused)
				BeginTransition(opening: true);
			return;
		}

		if (Input.IsActionJustPressed("start")) { BeginTransition(opening: false); return; }

		if (Input.IsActionJustPressed("ui_right")) Move(1, 0);
		if (Input.IsActionJustPressed("ui_left")) Move(-1, 0);
		if (Input.IsActionJustPressed("ui_down")) Move(0, 1);
		if (Input.IsActionJustPressed("ui_up")) Move(0, -1);

		if (Input.IsActionJustPressed("button_b")) EquipSelected(ItemButton.B);
		if (Input.IsActionJustPressed("button_a")) EquipSelected(ItemButton.A);
	}

	// The fade is counted in fixed physics ticks so its length doesn't depend on frame rate.
	public override void _PhysicsProcess(double delta)
	{
		if (_phase == Phase.None)
			return;

		_frame++;

		switch (_phase)
		{
			case Phase.FadeOut:
				SetFade(FadeLevel(_frame, FadeOutFrames, rising: true));
				if (_frame >= FadeOutFrames)
				{
					_phase = Phase.Hold;
					_frame = 0;
					SwapScreens(); // screen is fully white right now
				}
				break;

			case Phase.Hold:
				if (_frame >= HoldFrames) {
					_phase = Phase.FadeIn;
					_frame = 0;
				}
				break;

			case Phase.FadeIn:
				SetFade(FadeLevel(_frame, FadeInFrames, rising: false));
				if (_frame >= FadeInFrames)
					FinishTransition();
				break;
		}
	}

	private void BeginTransition(bool opening)
	{
		if (opening && _icons.Count == 0)
			return; // nothing to select; opening would just freeze the game

		_opening = opening;
		_phase = Phase.FadeOut;
		_frame = 0;
		_fade.Size = GetViewport().GetVisibleRect().Size;

		if (opening)
			GetTree().Paused = true; // freeze the game as soon as the fade starts
	}

	// Runs once, while the screen is completely white.
	private void SwapScreens()
	{
		if (_opening)
		{
			_open = true;
			Visible = true;
			BottomBar?.SetMenuOpen(true);
			Refresh();
		}
		else
		{
			_open = false;
			Visible = false;
			BottomBar?.SetMenuOpen(false);
		}
	}

	private void FinishTransition()
	{
		SetFade(0f);
		_phase = Phase.None;

		// When closing, the game stays frozen until the fade-in has finished.
		if (!_opening)
			GetTree().Paused = false;
	}

	// Brightness (0..1) of the white overlay after 'frame' ticks of a fade lasting 'total' ticks.
	private float FadeLevel(int frame, int total, bool rising)
	{
		float p = Mathf.Clamp((float)frame / Mathf.Max(1, total), 0f, 1f);

		if (FadeSteps <= 0)
			return rising ? p : 1f - p;

		// Quantize to FadeSteps levels: rounds up going to white, down coming back.
		return rising
			? Mathf.Ceil(p * FadeSteps) / FadeSteps
			: 1f - Mathf.Floor(p * FadeSteps) / FadeSteps;
	}

	private void SetFade(float alpha)
	{
		_fade.Color = new Color(1, 1, 1, alpha);
		_fade.Visible = alpha > 0f;
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
		for (int i = 0; i < _icons.Count && i < Inventory.ItemSlots
	; i++)
			_icons[i].Texture = Inventory.Instance.GetSlot(i)?.Icon ?? EmptySlotTexture;
	}
}
