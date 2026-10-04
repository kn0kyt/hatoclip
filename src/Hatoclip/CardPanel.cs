namespace Hatoclip;

/// <summary>
/// White rounded card with a 1px border on the window background (the phone page's <c>.card</c>).
/// Its BackColor is the card colour, so plain child controls (labels) blend in.
/// </summary>
public sealed class CardPanel : ThemedControl
{
    public CardPanel()
    {
        BackColor = Palette.Card;
    }

    protected override void OnPaletteChanged() => BackColor = Palette.Card;

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Palette.Background);
        Drawing.FillRoundedRect(e.Graphics, ClientRectangle, S(Theme.CardRadius), Palette.Card, Palette.Border,
            Drawing.HairlineWidth(UiScale));
    }
}
