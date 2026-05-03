# MagicTranscript (VoiceTyper)

MagicTranscript is a Windows desktop dictation app that records your voice, transcribes it with OpenAI, and pastes the result into your active app using global hotkeys.

## Features

- Global push-to-talk hotkeys (normal and professional modes)
- System tray app with quick status and controls
- Microphone selection and language hint settings
- Optional automatic cleanup and professional rewrite passes
- Auto-paste into the currently focused window

## Requirements

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- An OpenAI API key
- A working microphone

## Setup

1. Clone the repository:

   ```bash
   git clone https://github.com/<your-username>/MagicTranscript.git
   cd MagicTranscript
   ```

2. Set your OpenAI API key in your environment:

   **PowerShell (current session):**

   ```powershell
   $env:OPENAI_API_KEY="your_api_key_here"
   ```

   **PowerShell (persist for your user):**

   ```powershell
   [Environment]::SetEnvironmentVariable("OPENAI_API_KEY", "your_api_key_here", "User")
   ```

3. Restore dependencies:

   ```bash
   dotnet restore VoiceTyper/VoiceTyper.csproj
   ```

## Run

```bash
dotnet run --project VoiceTyper/VoiceTyper.csproj
```

The app runs in the system tray. Double-click the tray icon (or use the tray menu) to open settings.

## Build and Publish

Use these commands from the repository root:

```bash
dotnet build
dotnet run
dotnet publish -c Release -o C:\Tools\VoiceTyper
```

## Usage

- Hold your configured **Transcript hotkey** to record in normal mode.
- Hold your configured **Professional hotkey** to record and rewrite in a more professional tone.
- Release the key/modifier to stop recording and start transcription.
- If **Auto-paste** is enabled, text is inserted into the active window.

## Configuration

Settings are saved per-user on each PC:

- `%AppData%/VoiceTyper/settings.json`

Word usage is also saved per-user:

- `%AppData%/VoiceTyper/word-usage.json`

Key configurable options include:

- Transcript and professional hotkeys
- Microphone device
- Language hint (for example: `en`, `es`, `fr`)
- Auto-paste toggle
- Dictation enabled/disabled from the tray menu

## Notes

- If `OPENAI_API_KEY` is missing, the app starts but dictation will not work.
- Audio is recorded as temporary WAV files and cleaned up after processing.
- This app uses `gpt-4o-mini-transcribe` for transcription and `gpt-4o-mini` for text rewriting/cleanup.

## Sound Files

Optional custom sounds are loaded from:

- `VoiceTyper/bin/<Configuration>/net8.0-windows/Assets/Sounds/ping.mp3`
- `VoiceTyper/bin/<Configuration>/net8.0-windows/Assets/Sounds/transform.mp3`

If these files do not exist, the app falls back to the system beep.

## Project Structure

- `VoiceTyper/` - WPF app source code
- `VoiceTyper/Services/` - recording, hotkeys, transcription, cleanup, tray, insertion
- `VoiceTyper/Models/` - app settings model
