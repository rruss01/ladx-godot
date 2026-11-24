using Godot;

public partial class Hud : CanvasLayer
{
	[ExportGroup("Hearts")]
	[Export] public Texture2D HeartFull { get; set; }
	[Export] public Texture2D HeartHalf { get; set; }
	[Export] public Texture2D HeartEmpty { get; set; }
 
	[ExportGroup("Rupee digits")]
	[Export] public Godot.Collections.Array<Texture2D> DigitTextures { get; set; } = new();

	private TextureRect _bIcon;
	private TextureRect _aIcon;
	private Control _hearts;
	private Control _rupeeDigits;
	private Control _bar;
	private Control _bDigits;
	private Control _aDigits;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready() {
		_bIcon = GetNode<TextureRect>("BottomBar/IconB");
		_aIcon = GetNode<TextureRect>("BottomBar/IconA");
		_bDigits = GetNode<Control>("BottomBar/IconB/AmmoDigits");
		_aDigits = GetNode<Control>("BottomBar/IconA/AmmoDigits");
		_hearts = GetNode<Control>("BottomBar/Hearts");
		_rupeeDigits = GetNode<Control>("BottomBar/RupeeDigits");
		_bar = GetNode<Control>("BottomBar");
		
		Inventory.Instance.EquippedChanged += OnEquippedChanged;
		Inventory.Instance.AmmoChanged += RefreshAmmo;
		Inventory.Instance.RupeesChanged += SetRupees;
		Inventory.Instance.HealthChanged += SetHearts;
		SetItemB(Inventory.Instance.GetEquipped(ItemButton.B));
		SetItemA(Inventory.Instance.GetEquipped(ItemButton.A));
		SetHearts(Inventory.Instance.CurrentHealth, Inventory.Instance.MaxHealth);
		SetRupees(Inventory.Instance.RupeeCount);
		RefreshAmmo();
	}

	public void SetItemB(ItemData item) => _bIcon.Texture = item?.Icon;
	public void SetItemA(ItemData item) => _aIcon.Texture = item?.Icon;
	
	public void SetMenuOpen(bool open) {
		_bar.Position = open ? Vector2.Zero : new Vector2(0, 128);
	}
	
	private void OnEquippedChanged(int button, ItemData item) {
		if (button == (int)ItemButton.B) SetItemB(item); else SetItemA(item);
		RefreshAmmo();
	}
	
	private void RefreshAmmo()
	{
		RefreshAmmoFor(ItemButton.B, _bDigits);
		RefreshAmmoFor(ItemButton.A, _aDigits);
	}
	
	private void RefreshAmmoFor(ItemButton button, Control digits)
	{
		ItemData item = Inventory.Instance.GetEquipped(button);
		int count = item?.GetCount() ?? -1;    // no item in the slot also means no digits

		digits.Visible = count >= 0;
		if (count >= 0)
			SetDigits(digits, count);
	}

	public void SetHearts(int currentHealth, int maxHealth)
	{
		int count = _hearts.GetChildCount();
		for (int i = 0; i < count; i++)
		{
			TextureRect heart = _hearts.GetChild<TextureRect>(i);
			heart.Visible = i * 2 < maxHealth;

			int remaining = currentHealth - i * 2;
			if (remaining >= 2)
				heart.Texture = HeartFull;
			else if (remaining == 1)
				heart.Texture = HeartHalf;
			else
				heart.Texture = HeartEmpty;
		}
	}
	
	private void SetDigits(Control digits, int amount) {
		int digitCount = digits.GetChildCount();
		int maxValue = 1;
		for (int i = 0; i < digitCount; i++)
			maxValue *= 10;
		amount = Mathf.Clamp(amount, 0, maxValue - 1);
 
		for (int i = digitCount - 1; i >= 0; i--)
		{
			digits.GetChild<TextureRect>(i).Texture = DigitTextures[amount % 10];
			amount /= 10;
		}
	}

	public void SetRupees(int amount) => SetDigits(_rupeeDigits, amount);
}
