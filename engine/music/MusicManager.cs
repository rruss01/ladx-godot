using Godot;
using System.Collections.Generic;

// One node that owns the music. Everything else just says what SHOULD be playing;
// this node only changes the song when it is a different one, so crossing from one
// room into another room of the same region never restarts the music.
//
// Setup: add as a child of the Game root, fill in Tracks and (optionally) Scroller.
//   * Overworld:  call EnterOverworld(room) once, and it follows Scroller.RoomChanged
//   * Anywhere else (dungeon, house, cave): call EnterMap(MusicTrack.TailCave)
//
// Loops: tick "Loop" on each music file's Import tab (ogg/mp3/wav), or let the
// code below set it for streams that support it.
public partial class MusicManager : Node
{
	public static MusicManager Instance { get; private set; }

	[Export] public Godot.Collections.Array<MusicEntry> Tracks { get; set; } = new();
	[Export] public ScreenScroller Scroller { get; set; }

	// Original: until Link finds the sword the whole overworld plays one song.
	[Export] public bool UseSwordlessSong { get; set; } = false;

	[Export] public float FadeSeconds { get; set; } = 0.4f;

	public MusicTrack Current { get; private set; } = MusicTrack.None;

	private readonly Dictionary<MusicTrack, AudioStream> _streams = new();
	private AudioStreamPlayer _player;
	private Tween _fade;
	private bool _overworld;

	public override void _Ready()
	{
		Instance = this;
		ProcessMode = ProcessModeEnum.Always;

		foreach (MusicEntry e in Tracks)
			if (e != null && e.Stream != null)
				_streams[e.Track] = e.Stream;

		_player = new AudioStreamPlayer { Bus = "Master" };
		AddChild(_player);

		if (Scroller != null)
			Scroller.RoomChanged += OnRoomChanged;
	}

	public override void _ExitTree()
	{
		if (Instance == this)
			Instance = null;
		if (IsInstanceValid(Scroller))
			Scroller.RoomChanged -= OnRoomChanged;
	}

	// Call when the player is on the overworld (game start, leaving a dungeon, ...).
	// 'room' is the room the player is ARRIVING in (the scroller hasn't moved there yet).
	public void EnterOverworld(Vector2I room)
	{
		_overworld = true;
		Play(TrackFor(room));
	}

	// Call for any map that has one song for the whole thing.
	public void EnterMap(MusicTrack track)
	{
		_overworld = false;
		Play(track);
	}

	private void OnRoomChanged(Vector2I room)
	{
		if (_overworld)
			Play(TrackFor(room));
	}

	private MusicTrack TrackFor(Vector2I room)
		=> UseSwordlessSong ? MusicTrack.OverworldSwordless : OverworldMusic.ForRoom(room);

	// Does nothing if this song is already the one playing.
	public void Play(MusicTrack track)
	{
		if (track == Current)
			return;

		Current = track;
		_fade?.Kill();

		AudioStream next = null;
		if (track != MusicTrack.None && !_streams.TryGetValue(track, out next))
			GD.PushWarning($"MusicManager: no stream assigned for {track}.");

		_fade = CreateTween();
		if (_player.Playing && FadeSeconds > 0f)
			_fade.TweenProperty(_player, "volume_db", -40f, FadeSeconds);

		_fade.TweenCallback(Callable.From(() => Start(next)));
	}

	private void Start(AudioStream stream)
	{
		_player.Stop();
		_player.VolumeDb = 0f;

		if (stream == null)
			return;

		switch (stream)
		{
			case AudioStreamOggVorbis ogg: ogg.Loop = true; break;
			case AudioStreamMP3 mp3: mp3.Loop = true; break;
			case AudioStreamWav wav: wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward; break;
		}

		_player.Stream = stream;
		_player.Play();
	}
}
