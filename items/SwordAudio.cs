using Godot;

// Sword sounds. Add as a child Node of the Player named "SwordAudio" and drag your sound
// files into the fields. The original triggers them like this (LADX-Disassembly):
//   * a swing starts          -> one of 4 swing sounds, picked at random
//   * charge reaches maximum  -> a "fully charged" jingle, once
//   * button released at full charge -> the spin attack sound
public partial class SwordAudio : Node
{
	// The original has four swing sounds and picks one at random every swing.
	[Export] public AudioStream[] SwingSounds { get; set; } = new AudioStream[0];
	[Export] public AudioStream ChargedSound { get; set; }
	[Export] public AudioStream SpinAttackSound { get; set; }

	// The audio bus to play on. Change it if you have an SFX bus.
	[Export] public StringName Bus { get; set; } = "Master";

	public override void _Ready()
	{
		// Let sounds finish even if the game pauses (menu, screen scroll).
		ProcessMode = ProcessModeEnum.Always;
	}

	public void PlaySwing()
	{
		if (SwingSounds.Length == 0)
			return;

		Play(SwingSounds[(int)(GD.Randi() % (uint)SwingSounds.Length)]);
	}

	public void PlayCharged() => Play(ChargedSound);
	public void PlaySpinAttack() => Play(SpinAttackSound);

	// One short-lived player per sound, so quick repeated swings can overlap.
	private void Play(AudioStream stream)
	{
		if (stream == null)
			return;

		AudioStreamPlayer player = new AudioStreamPlayer { Stream = stream, Bus = Bus };
		player.Finished += player.QueueFree;
		AddChild(player);
		player.Play();
	}
}
