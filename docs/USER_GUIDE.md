## Run

Run `note.app-0.8.2-setup.exe` to install (your notes are kept on uninstall).
The portable Windows x64 EXE needs no installation.

Enable **Open on system startup** in the library's **⚙** preferences to launch at Windows sign-in. Only one copy of note. starts at sign-in: turning the option on (or off) clears any entry left by another copy, such as an older portable build, and if Windows still starts a different copy, a line under the option says which. If you move the portable EXE, turn the option on again from its new location. Right-click the EXE and choose **Send to → Desktop (create shortcut)** for a desktop shortcut with the app icon.

## Library and compact controls

The central **Notes library** opens at startup. It lets you create notes (**+**), search by title, and click any row to open it, even if hidden or docked.

- Drag **≡** to move the note; drag its borders to resize.
- **⋯** opens paper swatches and a shared opacity slider, followed by Settings, Delete note (red), Copy note link, the library, and insert/link tools. Copy note link copies the stable `stickynote://note/...` URL for hyperlinking.
- The **pin** toggles always-on-top.
- The bottom row provides a font picker (**Aa**), bold, italic, underline, strikethrough, bullets, checklist, text alignment, and a link button. **Aa** sets the font and size of the selected text (or of what you type next), so different parts of a note can use different fonts; type in its search box to find a locally installed font.

Ctrl+N creates a note. Ctrl+B = bold, Ctrl+I = italic, Ctrl+S = strikethrough, Ctrl+U = underline, and Ctrl+P = points (bullets). Underline and strikethrough can be combined. Ctrl+Z and Ctrl+Y undo/redo. Ctrl+K adds links (or edits the link under the cursor); Ctrl+click opens them. The alignment button shows the current alignment and opens **left / center / right / justify**; Ctrl+L, Ctrl+E, Ctrl+R and Ctrl+J do the same from the keyboard. Only the lines you have selected (or the line with the cursor) move, including lines joined with Shift+Enter. Images inline with the text move with their line. Alt+F4 hides the note without deleting it.

### Custom edge tabs

In **⋯ → Settings → Edge tab appearance**, choose a tab color (swatches or `#RRGGBB`), pick or type an icon/emoji, and add separate top and bottom labels of up to 8 characters each. Whole-tab opacity and icon opacity are independently adjustable from 0–100%. The actual-size preview updates as you edit. **Save settings** applies the appearance; Cancel leaves the existing tab unchanged. Tab colors are independent of note paper color, and text switches between light and dark for contrast.

Drag a collapsed tab up or down to reposition it. Drag it across the screen to switch sides; releasing snaps it to the nearest left or right edge and clamps it inside the work area. Its side, height, and monitor are saved separately from the note's desktop position. A click without dragging still opens the note.

### Drag images in and resize them

Drag one or more image files from File Explorer into the note editor. Drops insert at the pointer without replacing other selected text. Bitmap drag payloads are also supported; browser drags that supply only a URL are treated as text rather than downloaded images. PNG, JPEG, GIF, BMP, TIFF and decoder-supported WebP files can be inserted. Corrupt/unsupported images are skipped.

### Links between notes

Choose **⋯ → Link another note…**, search for a target, and insert it. Selected text or an image becomes the link; otherwise the target's title is inserted. You can also use **Copy link** in the library, then paste into a note or use Ctrl+K. These links use stable IDs and survive renaming and app restarts. Link labels keep the text originally inserted until you edit them. Deleted targets display an unavailable message. These are internal app links; no Windows URL protocol registration is required.

Choose **Insert image…** to embed an image, or paste a clipboard image. Choose **Settings…** for fonts, paper colors, docking edge, app triggers, and optional Windows sign-in startup. Changing font/size applies to the entire note. Use the tray menu to show/hide all notes or exit.

Web URLs become hyperlinks after pasting or typing a space/Enter. Hover a direct image URL ending in `.png`, `.jpg`, `.jpeg`, `.gif`, `.bmp`, or `.webp` for a preview; query strings are supported. Previews load on demand, require a supported Windows image decoder, and may fail for authenticated or non-image URLs. Remote previews make a network request and are capped at 8 MB. Images inserted into notes are embedded and resized for storage (up to 1200 pixels wide); they do not depend on the original file afterwards.

### Linking files and folders

Drag files or folders from Explorer onto a note, or copy them in Explorer and paste into a note: images are embedded, and anything else becomes a link named after the file or folder. Ctrl+K (or **⋯ → Add a link…**) also accepts a path, with **File…** and **Folder…** pickers. Clicking a file or folder link asks before opening it.

To change a link, right-click it and choose **Edit link…**, or place the cursor in it and press the link button in the bottom row (or Ctrl+K). You can set the text it shows and its address; for example, a pasted `stickynote://note/…` address can read **General** and still open that note. The right-click menu also opens the link or copies its address.

## App triggers

In note settings, enter executable names such as `notepad, chrome, Code`, with or without `.exe`. The app checks every three seconds. It opens the matching note when a process first appears, including processes already open when note. starts. Hiding a note while its trigger app remains open keeps it hidden until that app exits and starts again. note. must be running. Closing the trigger app leaves its note open.

## Storage and recovery

Data lives in `%LOCALAPPDATA%\flac.app\StickyNotes` unless you choose another folder. The **⚙** preferences window shows the current notes folder and has three buttons:

- **Back up…** saves every note, image and setting into one `.zip` file. Open edits are saved first, so the app can stay open.
- **Change location…** copies your notes to another folder (for example a synced or external drive) and keeps them there from then on. The old folder keeps its copy until you delete it. If the chosen folder already holds note. data, you are asked before it is replaced. The choice is remembered in `%LOCALAPPDATA%\flac.app\StickyNotes-location.txt`. If that folder is missing at startup (for example an unplugged drive), note. says so and uses the default folder until it is back.
- **Open folder** shows the notes folder in File Explorer.

The folder holds:

- `notes.json`: titles, positions, visibility, appearance, startup choices, and triggers.
- `preferences.json`: app preferences, including library dark mode.
- `<note-id>.xamlpkg`: rich content and embedded images.
- `.bak`: previous saved versions, kept by atomic replacement.

Edits autosave after 650 ms; hiding and exiting also save. To restore a backup, close the app and unzip it into the notes folder.
