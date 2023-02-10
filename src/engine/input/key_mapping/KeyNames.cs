// NO_DUPLICATE_CHECK

using System.Globalization;
using Godot;

internal static class KeyNames
{
    /// <summary>
    ///   Translates a KeyCode to a printable string
    /// </summary>
    /// <param name="keyCode">The keyCode to translate</param>
    /// <returns>A human readable string</returns>
    public static string Translate(uint keyCode)
    {
        var key = (Key)keyCode;
        return key switch
        {
            Key.Exclam => "!",
            Key.Quotedbl => "\"",
            Key.Numbersign => "#",
            Key.Dollar => "$",
            Key.Percent => "%",
            Key.Ampersand => "&",
            Key.Apostrophe => "'",
            Key.Parenleft => "(",
            Key.Parenright => ")",
            Key.Asterisk => "*",
            Key.Plus => "+",
            Key.Comma => ",",
            Key.Minus => "-",
            Key.Period => ".",
            Key.Slash => "/",
            Key.Key0 => "0",
            Key.Key1 => "1",
            Key.Key2 => "2",
            Key.Key3 => "3",
            Key.Key4 => "4",
            Key.Key5 => "5",
            Key.Key6 => "6",
            Key.Key7 => "7",
            Key.Key8 => "8",
            Key.Key9 => "9",
            Key.Colon => ":",
            Key.Semicolon => ";",
            Key.Less => "<",
            Key.Equal => "=",
            Key.Greater => ">",
            Key.Question => "?",
            Key.At => "@",
            Key.Bracketleft => "[",
            Key.Bracketright => "]",
            Key.Asciicircum => "^",
            Key.Underscore => "_",
            Key.Quoteleft => "`",
            Key.Braceleft => "{",
            Key.Bar => "|",
            Key.Braceright => "}",
            Key.Asciitilde => "~",

            // ReSharper disable CommentTypo
            /*
            KeyList.Exclamdown => "¡",
            KeyList.Cent => "¢",
            KeyList.Sterling => "£",
            KeyList.Currency => "¤",
            KeyList.Yen => "¥",
            KeyList.Brokenbar => "¦",
            KeyList.Section => "§",
            KeyList.Diaeresis => "¨",
            KeyList.Copyright => "©",
            KeyList.Ordfeminine => "ª",
            KeyList.Guillemotleft => "«",
            KeyList.Notsign => "¬",
            KeyList.Hyphen => "-",
            KeyList.Registered => "®",
            KeyList.Macron => "¯",
            KeyList.Degree => "°",
            KeyList.Plusminus => "±",
            KeyList.Twosuperior => "²",
            KeyList.Threesuperior => "³",
            KeyList.Acute => "´",
            KeyList.Mu => "µ",
            KeyList.Paragraph => "¶",
            KeyList.Periodcentered => "·",
            KeyList.Cedilla => "¸",
            KeyList.Onesuperior => "¹",
            KeyList.Masculine => "º",
            KeyList.Guillemotright => "»",
            KeyList.Onequarter => "¼",
            KeyList.Onehalf => "½",
            KeyList.Threequarters => "¾",
            KeyList.Questiondown => "¿",
            KeyList.Agrave => "À",
            KeyList.Aacute => "Á",
            KeyList.Acircumflex => "Â",
            KeyList.Atilde => "Ã",
            KeyList.Adiaeresis => "Ä",
            KeyList.Aring => "Å",
            KeyList.Ae => "Æ",
            KeyList.Ccedilla => "Ç",
            KeyList.Egrave => "È",
            KeyList.Eacute => "É",
            KeyList.Ecircumflex => "Ê",
            KeyList.Ediaeresis => "Ë",
            KeyList.Igrave => "Ì",
            KeyList.Iacute => "Í",
            KeyList.Icircumflex => "Î",
            KeyList.Idiaeresis => "Ï",
            KeyList.Eth => "Ð",
            KeyList.Ntilde => "Ñ",
            KeyList.Ograve => "Ò",
            KeyList.Oacute => "Ó",
            KeyList.Ocircumflex => "Ô",
            KeyList.Otilde => "Õ",
            KeyList.Odiaeresis => "Ö",
            KeyList.Multiply => "×",
            KeyList.Ooblique => "Ø",
            KeyList.Ugrave => "Ù",
            KeyList.Uacute => "Ú",
            KeyList.Ucircumflex => "Û",
            KeyList.Udiaeresis => "Ü",
            KeyList.Yacute => "Ý",
            KeyList.Thorn => "Þ",
            KeyList.Ssharp => "ß",
            KeyList.Division => "÷",
            KeyList.Ydiaeresis => "ÿ",
            */

            // ReSharper enable CommentTypo

            // Key names that would conflict with simple words in translations
            Key.Forward => TranslationServer.Translate("KEY_FORWARD"),
            Key.Tab => TranslationServer.Translate("KEY_TAB"),
            Key.Enter => TranslationServer.Translate("KEY_ENTER"),
            Key.Insert => TranslationServer.Translate("KEY_INSERT"),
            Key.Delete => TranslationServer.Translate("KEY_DELETE"),
            Key.Pause => TranslationServer.Translate("KEY_PAUSE"),
            Key.Clear => TranslationServer.Translate("KEY_CLEAR"),
            Key.Home => TranslationServer.Translate("KEY_HOME"),
            Key.End => TranslationServer.Translate("KEY_END"),
            Key.Left => TranslationServer.Translate("KEY_LEFT"),
            Key.Up => TranslationServer.Translate("KEY_UP"),
            Key.Right => TranslationServer.Translate("KEY_RIGHT"),
            Key.Down => TranslationServer.Translate("KEY_DOWN"),
            Key.Menu => TranslationServer.Translate("KEY_MENU"),
            Key.Help => TranslationServer.Translate("KEY_HELP"),
            Key.Back => TranslationServer.Translate("KEY_BACK"),
            Key.Stop => TranslationServer.Translate("KEY_STOP"),
            Key.Refresh => TranslationServer.Translate("KEY_REFRESH"),
            Key.Search => TranslationServer.Translate("KEY_SEARCH"),
            Key.Standby => TranslationServer.Translate("KEY_STANDBY"),
            Key.Openurl => TranslationServer.Translate("KEY_OPENURL"),
            Key.Homepage => TranslationServer.Translate("KEY_HOMEPAGE"),
            Key.Favorites => TranslationServer.Translate("KEY_FAVORITES"),
            Key.Print => TranslationServer.Translate("KEY_PRINT"),

            // Fallback to using the key name (in upper case) to translate. These must all be defined in Keys method
            _ => TranslationServer.Translate(key.ToString().ToUpper(CultureInfo.InvariantCulture)),
        };
    }

    // ReSharper disable once UnusedMember.Local
    /// <summary>
    ///   Useless method that only exists to tell the translation system specific strings
    /// </summary>
    private static void Keys()
    {
        // Names are from Godot so we need to have these as-is
        // ReSharper disable StringLiteralTypo
        TranslationServer.Translate("SPACE");
        TranslationServer.Translate("BACKSLASH");
        TranslationServer.Translate("ESCAPE");
        TranslationServer.Translate("BACKSPACE");
        TranslationServer.Translate("KPENTER");
        TranslationServer.Translate("SYSREQ");
        TranslationServer.Translate("PAGEUP");
        TranslationServer.Translate("PAGEDOWN");
        TranslationServer.Translate("CAPSLOCK");
        TranslationServer.Translate("NUMLOCK");
        TranslationServer.Translate("SCROLLLOCK");
        TranslationServer.Translate("SUPERL");
        TranslationServer.Translate("SUPERR");
        TranslationServer.Translate("HYPERL");
        TranslationServer.Translate("HYPERR");
        TranslationServer.Translate("DIRECTIONL");
        TranslationServer.Translate("DIRECTIONR");
        TranslationServer.Translate("VOLUMEDOWN");
        TranslationServer.Translate("VOLUMEMUTE");
        TranslationServer.Translate("VOLUMEUP");
        TranslationServer.Translate("BASSBOOST");
        TranslationServer.Translate("BASSUP");
        TranslationServer.Translate("BASSDOWN");
        TranslationServer.Translate("TREBLEUP");
        TranslationServer.Translate("TREBLEDOWN");
        TranslationServer.Translate("MEDIAPLAY");
        TranslationServer.Translate("MEDIASTOP");
        TranslationServer.Translate("MEDIAPREVIOUS");
        TranslationServer.Translate("MEDIANEXT");
        TranslationServer.Translate("MEDIARECORD");
        TranslationServer.Translate("LAUNCHMAIL");
        TranslationServer.Translate("LAUNCHMEDIA");
        TranslationServer.Translate("LAUNCH0");
        TranslationServer.Translate("LAUNCH1");
        TranslationServer.Translate("LAUNCH2");
        TranslationServer.Translate("LAUNCH3");
        TranslationServer.Translate("LAUNCH4");
        TranslationServer.Translate("LAUNCH5");
        TranslationServer.Translate("LAUNCH6");
        TranslationServer.Translate("LAUNCH7");
        TranslationServer.Translate("LAUNCH8");
        TranslationServer.Translate("LAUNCH9");
        TranslationServer.Translate("LAUNCHA");
        TranslationServer.Translate("LAUNCHB");
        TranslationServer.Translate("LAUNCHC");
        TranslationServer.Translate("LAUNCHD");
        TranslationServer.Translate("LAUNCHE");
        TranslationServer.Translate("LAUNCHF");
        TranslationServer.Translate("KPMULTIPLY");
        TranslationServer.Translate("KPDIVIDE");
        TranslationServer.Translate("KPSUBTRACT");
        TranslationServer.Translate("KPPERIOD");
        TranslationServer.Translate("KPADD");
        TranslationServer.Translate("KP0");
        TranslationServer.Translate("KP1");
        TranslationServer.Translate("KP2");
        TranslationServer.Translate("KP3");
        TranslationServer.Translate("KP4");
        TranslationServer.Translate("KP5");
        TranslationServer.Translate("KP6");
        TranslationServer.Translate("KP7");
        TranslationServer.Translate("KP8");
        TranslationServer.Translate("KP9");
        TranslationServer.Translate("UNKNOWN");

        // ReSharper restore StringLiteralTypo
    }
}
