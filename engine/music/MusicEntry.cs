using Godot;

[GlobalClass]
public partial class MusicEntry : Resource
{
	[Export] public MusicTrack Track { get; set; }
	[Export] public AudioStream Stream { get; set; }
}
