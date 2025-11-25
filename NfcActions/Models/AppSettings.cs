using System.Collections.Generic;

namespace NfcActions.Models;

public enum SuffixType
{
    None,
    Enter,
    Tab,
    Comma,
    Colon,
    Semicolon,
    Period
}

public class AppSettings
{
    public HashSet<string> DisabledReaders { get; set; } = new();
    public bool CopyToClipboard { get; set; } = false;
    public bool LaunchUrls { get; set; } = true;
    public bool TypeAsKeyboard { get; set; } = false;
    public SuffixType Suffix { get; set; } = SuffixType.None;
}
