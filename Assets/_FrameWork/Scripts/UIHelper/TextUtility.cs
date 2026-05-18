/// <summary>
/// Text utilities cho playable ads
/// Xóa I2 Localization dependency - dùng text mặc định
/// </summary>
public static class TextUtility
{
    /// <summary>
    /// Playable: Trả về text mặc định thay vì localized
    /// </summary>
    public static string GetI2(string term)
    {
        // Fallback text cho các term phổ biến
        switch (term)
        {
            case "ui_ingame_level": return "Lv";
            case "ui_ingame_capacity": return "Cap";
            case "ui_button_play": return "PLAY";
            case "ui_button_retry": return "RETRY";
            case "ui_button_continue": return "CONTINUE";
            default: return term;
        }
    }

    public static string ToShortNumberString(long num)
    {
        if (num < 1000)
            return num.ToString();

        // Quadrillion (1,000,000,000,000,000)
        if (num >= 1000000000000000L)
            return (num / 1000000000000000.0).ToString("0.#") + "a";

        // Trillion (1,000,000,000,000)
        if (num >= 1000000000000L)
            return (num / 1000000000000.0).ToString("0.#") + "T";

        // Billion (1,000,000,000)
        if (num >= 1000000000L)
            return (num / 1000000000.0).ToString("0.#") + "B";

        // Million (1,000,000)
        if (num >= 1000000)
            return (num / 1000000.0).ToString("0.#") + "M";

        // Thousand (1,000)
        return (num / 1000.0).ToString("0.#") + "K";
    }
}