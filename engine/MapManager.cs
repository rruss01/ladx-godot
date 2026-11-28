using Godot;

// Loads and swaps map scenes. Lives in the persistent Game scene, next to the player, camera,
// collision loader and interactive tiles, none of which are ever unloaded.
//
//   Game
//   |- Player, ScreenScroller, CollisionLoader, InteractiveTiles, HUD, InventoryMenu
//   |- MapManager
//   '- CurrentMap (Node2D)   <- the loaded map scene is added here
//
// Maps are loaded by PATH, on demand, so two maps can lead to each other without
// referencing each other (scene references in a loop are an error in Godot).
public partial class MapManager : Node
{
	public static MapManager Instance { get; private set; }

	[Export] public Node2D MapRoot { get; set; }
	[Export] public ScreenScroller Scroller { get; set; }
	[Export] public CollisionLoader Collision { get; set; }
	[Export] public InteractiveTiles Tiles { get; set; }
	[Export] public AnimatedTiles Animated { get; set; }
	[Export] public WarpManager Warps { get; set; }

	[ExportGroup("Starting point")]
	[Export(PropertyHint.File, "*.tscn")] public string StartMap { get; set; } = "";
	[Export] public Vector2I StartRoom { get; set; } = Vector2I.Zero;
	[Export] public Vector2I StartScreenPosition { get; set; } = new Vector2I(80, 64);

	// Fade between maps, in physics ticks (60 per second). Placeholder numbers: measure the
	// ROM's door transition the way we did for the menu, then set them to match.
	[ExportGroup("Transition")]
	[Export] public Color FadeColor { get; set; } = Colors.Black;
	[Export] public int FadeOutFrames { get; set; } = 12;
	[Export] public int HoldFrames { get; set; } = 4;
	[Export] public int FadeInFrames { get; set; } = 12;
	[Export] public int FadeSteps { get; set; } = 4; // 0 = smooth

	// After arriving, warp triggers are ignored this long, so landing on a trigger
	// (stairs, a door right next to the arrival point) can't bounce you straight back.
	[Export] public int ArrivalCooldownFrames { get; set; } = 20;

	public string CurrentMapId => _current?.MapId ?? "";

	private enum Phase { None, FadeOut, Hold, FadeIn }

	private MapInfo _current;
	private string _currentPath = "";
	private Phase _phase = Phase.None;
	private int _frame;
	private int _cooldown;
	private ColorRect _fade;

	private string _pendingMap;
	private Vector2I _pendingRoom;
	private Vector2I _pendingScreen;

	public override void _EnterTree()
	{
		Instance = this;
	}

	public override void _Ready()
	{
		// Keep running while the tree is paused for a transition.
		ProcessMode = ProcessModeEnum.Always;

		_fade = new ColorRect
		{
			Color = new Color(FadeColor, 0f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false,
		};
		CanvasLayer layer = new CanvasLayer { Layer = 100 };
		layer.AddChild(_fade);
		AddChild(layer);

		// Wait until every node's _Ready has run (the loader must be listening first).
		CallDeferred(MethodName.LoadStart);
	}

	private void LoadStart()
	{
		LoadMap(StartMap, StartRoom, StartScreenPosition);
	}

	// Fades out, swaps to the destination, warps the player, fades back in.
	// An empty 'mapScene' (or the current map's path) means "same map": just warp within it.
	public void GoTo(string mapScene, Vector2I room, Vector2I screenPosition)
	{
		if (_phase != Phase.None || _cooldown > 0)
			return;

		_pendingMap = mapScene;
		_pendingRoom = room;
		_pendingScreen = screenPosition;

		_phase = Phase.FadeOut;
		_frame = 0;
		_fade.Size = GetViewport().GetVisibleRect().Size;
		GetTree().Paused = true;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_cooldown > 0)
			_cooldown--;

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
					LoadMap(_pendingMap, _pendingRoom, _pendingScreen); // screen is fully covered
				}
				break;

			case Phase.Hold:
				if (_frame >= HoldFrames)
				{
					_phase = Phase.FadeIn;
					_frame = 0;
				}
				break;

			case Phase.FadeIn:
				SetFade(FadeLevel(_frame, FadeInFrames, rising: false));
				if (_frame >= FadeInFrames)
				{
					SetFade(0f);
					_phase = Phase.None;
					_cooldown = ArrivalCooldownFrames;
					GetTree().Paused = false;
				}
				break;
		}
	}

	private void LoadMap(string path, Vector2I room, Vector2I screen)
	{
		bool sameMap = path == "" || path == _currentPath;

		if (!sameMap)
		{
			PackedScene scene = ResourceLoader.Load<PackedScene>(path);
			if (scene == null)
			{
				GD.PushError($"MapManager: could not load map scene '{path}'.");
				return;
			}

			if (_current != null)
			{
				MapRoot.RemoveChild(_current);
				_current.QueueFree();
			}

			_current = scene.Instantiate<MapInfo>();
			_currentPath = path;
			MapRoot.AddChild(_current);

			// Order matters: the camera bounds first, then the systems that read the map.
			Scroller.SetBounds(_current.MinRoom, _current.MaxRoom, _current.RoomSize);
			Collision.SetMap(_current);
			Tiles.SetMap(_current);
			Animated?.SetMap(_current);
			Warps?.SetMap(_current);

			// Music: a region map picks by room, any other map has one song. Play() ignores a
			// song that is already playing, so e.g. two caves in a row don't restart it.
			if (MusicManager.Instance != null)
			{
				if (_current.RegionMusic)
					MusicManager.Instance.EnterOverworld(room);
				else
					MusicManager.Instance.EnterMap(_current.Music);
			}
		}

		// Moves the player, snaps the camera, and (through RoomChanged) loads the room's
		// collision and interactive tiles.
		Scroller.Warp(room.X, room.Y, screen.X, screen.Y);
	}

	private float FadeLevel(int frame, int total, bool rising)
	{
		float p = Mathf.Clamp((float)frame / Mathf.Max(1, total), 0f, 1f);

		if (FadeSteps <= 0)
			return rising ? p : 1f - p;

		return rising
			? Mathf.Ceil(p * FadeSteps) / FadeSteps
			: 1f - Mathf.Floor(p * FadeSteps) / FadeSteps;
	}

	private void SetFade(float alpha)
	{
		_fade.Color = new Color(FadeColor, alpha);
		_fade.Visible = alpha > 0f;
	}
}
