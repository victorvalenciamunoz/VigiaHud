namespace VigiaHud.App.Hud;

/// <summary>
/// The family's look (CieloHud's palette, by role rather than by hue): a dark background, text in four levels, and a
/// few signal colors. Published as app resources so XAML reads them as <c>DynamicResource</c>.
/// </summary>
public static class HudTheme
{
    public static readonly Color Background = Color.FromArgb("#0B1218");
    public static readonly Color Text = Color.FromArgb("#E8EEF4");
    public static readonly Color TextSoft = Color.FromArgb("#C5D0DB");
    public static readonly Color TextMuted = Color.FromArgb("#8FA0B0");
    public static readonly Color TextDim = Color.FromArgb("#5A6A7A");

    /// <summary>Good: armed and watching, the service alive.</summary>
    public static readonly Color Good = Color.FromArgb("#7CFFB2");

    /// <summary>Guidance: the family cyan; titles and chips.</summary>
    public static readonly Color Guide = Color.FromArgb("#4DD2FF");

    /// <summary>Marks: disarmed, a warning that is not an alarm.</summary>
    public static readonly Color Marker = Color.FromArgb("#FFC42E");

    /// <summary>Danger: a person detected, the service down, no power.</summary>
    public static readonly Color Alert = Color.FromArgb("#FF5C5C");

    public static void Apply(Application app)
    {
        var resources = app.Resources;
        resources["HudBackground"] = Background;
        resources["HudText"] = Text;
        resources["HudTextSoft"] = TextSoft;
        resources["HudTextMuted"] = TextMuted;
        resources["HudTextDim"] = TextDim;
        resources["HudGood"] = Good;
        resources["HudGuide"] = Guide;
        resources["HudMarker"] = Marker;
        resources["HudAlert"] = Alert;
    }
}
