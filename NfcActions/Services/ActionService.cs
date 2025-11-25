using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NfcActions.Models;
using WindowsInput;
using WindowsInput.Native;

namespace NfcActions.Services;

public class ActionService
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private readonly ConcurrentQueue<TypingRequest> _typingQueue = new();
    private bool _isProcessingQueue = false;
    private readonly object _queueLock = new();
    private readonly InputSimulator _inputSimulator = new();
    private readonly uint _currentProcessId;

    private class TypingRequest
    {
        public string Text { get; set; } = "";
        public SuffixType Suffix { get; set; } = SuffixType.None;
    }

    public ActionService()
    {
        _currentProcessId = (uint)Process.GetCurrentProcess().Id;
    }

    private bool IsOwnWindowFocused()
    {
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
            return false;

        GetWindowThreadProcessId(foregroundWindow, out uint foregroundProcessId);
        return foregroundProcessId == _currentProcessId;
    }

    public void CopyToClipboard(string text)
    {
        try
        {
            System.Windows.Clipboard.SetText(text);
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

    public void TypeText(string text, SuffixType suffix = SuffixType.None)
    {
        // Add to queue
        _typingQueue.Enqueue(new TypingRequest { Text = text, Suffix = suffix });

        // Start processing if not already running
        lock (_queueLock)
        {
            if (!_isProcessingQueue)
            {
                _isProcessingQueue = true;
                Task.Run(() => ProcessTypingQueue());
            }
        }
    }

    private void ProcessTypingQueue()
    {
        while (_typingQueue.TryDequeue(out var request))
        {
            try
            {
                // Skip typing if our own window is focused to prevent self-input
                if (IsOwnWindowFocused())
                {
                    continue;
                }

                // Use InputSimulator with SendInput API - more reliable than SendKeys
                // TextEntry batches all characters into a single SendInput call
                // which prevents timing issues and dropped characters

                // Process the text, converting CR/LF to actual Enter keypresses
                string textToType = request.Text;

                // Split by line breaks and type each segment
                var segments = textToType.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

                for (int i = 0; i < segments.Length; i++)
                {
                    if (!string.IsNullOrEmpty(segments[i]))
                    {
                        // TextEntry uses SendInput to type the entire string at once
                        _inputSimulator.Keyboard.TextEntry(segments[i]);
                    }

                    // Press Enter between segments (but not after the last one unless AppendCR)
                    if (i < segments.Length - 1)
                    {
                        _inputSimulator.Keyboard.KeyPress(VirtualKeyCode.RETURN);
                    }
                }

                // Append suffix if specified
                switch (request.Suffix)
                {
                    case SuffixType.Enter:
                        _inputSimulator.Keyboard.KeyPress(VirtualKeyCode.RETURN);
                        break;
                    case SuffixType.Tab:
                        _inputSimulator.Keyboard.KeyPress(VirtualKeyCode.TAB);
                        break;
                    case SuffixType.Comma:
                        _inputSimulator.Keyboard.TextEntry(",");
                        break;
                    case SuffixType.Colon:
                        _inputSimulator.Keyboard.TextEntry(":");
                        break;
                    case SuffixType.Semicolon:
                        _inputSimulator.Keyboard.TextEntry(";");
                        break;
                    case SuffixType.Period:
                        _inputSimulator.Keyboard.TextEntry(".");
                        break;
                    case SuffixType.None:
                    default:
                        break;
                }
            }
            catch (Exception)
            {
                // Failed to simulate keyboard input - continue with next item
            }

            // Small delay between queued items to let target app process
            Thread.Sleep(50);
        }

        lock (_queueLock)
        {
            _isProcessingQueue = false;
        }
    }
}
