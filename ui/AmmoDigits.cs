using Godot;

public partial class AmmoDigits : HBoxContainer
{
	[Export] public Godot.Collections.Array<Texture2D> DigitTextures { get; set; } = new();

	public void SetCount(int count)    // -1 hides it
	{
		Visible = count >= 0;
		if (count < 0) return;

		int digits = GetChildCount();
		for (int i = digits - 1; i >= 0; i--)
		{
			GetChild<TextureRect>(i).Texture = DigitTextures[count % 10];
			count /= 10;
		}
	}
}
