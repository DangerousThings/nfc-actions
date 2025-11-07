using System.Collections.Generic;

namespace NfcActions.Models;

public class AppSettings
{
    public HashSet<string> DisabledReaders { get; set; } = new();
    public bool CopyToClipboard { get; set; } = false;
    public bool LaunchUrls { get; set; } = true;
    public bool TypeAsKeyboard { get; set; } = false;
}
