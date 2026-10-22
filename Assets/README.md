# Replaceable sidebar artwork

Smooth classic tabs are the default. Enable Worn notebook in the main Notes library under Appearance & hover to use weathered edges, paper grain and your chosen colors. The texture overlay below applies only in Worn notebook mode; custom icons apply in either mode. Icons come from the symbol/emoji you choose in note settings.

To supply your own artwork, place PNGs in this Assets folder beside note.app.exe:

* edge-tab-texture.png: a transparent texture overlay, ideally square (88 or 176 px). The chosen tab color stays underneath. Transparent scuffs/grain work best. Opaque artwork covers the tab color. The worn silhouette and corner accents remain.
* edge-icons/default.png: a transparent icon used for every tab.
* edge-icons/<note-id>.png: overrides the default icon for one note. Find the note id in notes.json in %LOCALAPPDATA%\flac.app\StickyNotes, or in its copied stickynote://note/<note-id> link.

Icons display inside a 23 x 23 logical-pixel area; 92 x 92 PNGs work well. Custom PNGs keep their own colors. Labels and icon/tab opacity settings still apply. Deleting a custom file restores the built-in symbol. Unreadable or invalid images fall back automatically. Restart the app after swapping files (or reopen settings/save the tab appearance to refresh it).

Keep your custom PNGs with the app when copying it. Rebuilding/publishing preserves destination-only PNG files. Add them here in the source Assets folder if you want them included in future builds.

