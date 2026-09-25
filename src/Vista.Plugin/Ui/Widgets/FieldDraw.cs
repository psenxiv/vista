namespace Vista.Plugin.Ui.Widgets;

/// <summary>Draws a field showing <paramref name="value"/>; true when it changes.</summary>
internal delegate bool FieldDraw<T>(ref T value);
