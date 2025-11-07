using System;
using System.Diagnostics;
using System.Windows;
using WindowsInput;
using WindowsInput.Native;

namespace NfcActions.Services;

public class ActionService
{
    private readonly InputSimulator _inputSimulator = new();

    public void CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception)
        {
            // Failed to set clipboard
        }
    }

    public void LaunchUrl(string text)
    {
        try
        {
            // Check if the text looks like a URL
            if (Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = text,
                    UseShellExecute = true
                });
            }
            else if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                     text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = text,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception)
        {
            // Failed to launch URL
        }
    }

    public void TypeText(string text)
    {
        try
        {
            // Give a small delay to allow user to position cursor if needed
            System.Threading.Thread.Sleep(100);

            _inputSimulator.Keyboard.TextEntry(text);
        }
        catch (Exception)
        {
            // Failed to simulate keyboard input
        }
    }
}
