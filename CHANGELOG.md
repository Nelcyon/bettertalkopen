# What's new in BetterTalk

Each version's notes here are shown in Settings → Updates when you press Check for updates.

## 0.4.4

- **BetterTalk has its own icon now:** the same yellow speech bubble as the icon near the clock. You see it on
  the installer, the desktop and Start menu shortcuts, the taskbar, and in Windows' list of installed apps,
  instead of the plain Windows program picture. (If a shortcut still shows the old picture, it changes after
  the next restart of Windows.)

## 0.4.3

- **Clicking the corner to open BetterTalk no longer clicks the program underneath.** Before, that click also
  reached whatever was in the corner, so a full-screen video (Peacock, Netflix, YouTube and the like) could
  pause or a game could react. Now BetterTalk keeps the click for itself and only opens. When BetterTalk is
  already showing in that corner, clicks on its own keys work exactly as before.

## 0.4.2

- **The last word is never cut short.** With the downloaded voices (Piper, Kokoro), the end of a message could
  be clipped or sound faded, because BetterTalk treated the voice as finished while the speakers were still
  playing the last fraction of a second. Then other sounds came back up and the window could hide right on top
  of it. BetterTalk now waits until the speakers have really played everything, with a moment of silence after
  the last word, before it counts the message as finished. Only then do other sounds come back up and the
  window hide by itself.
- **Other sounds go quieter while BetterTalk speaks: a quarter of their volume instead of half,** so you are
  easier to hear over the TV or music. This applies to everyone, including if you had changed it in 0.4.0.
  Choose another level in Settings → Voice & speaking → Other sounds while speaking (now in steps of 5%).

## 0.4.1

- **Stop and quick phrases are dependable with the downloaded voices.** Pressing a quick phrase, a red button or
  an Itch word while a downloaded voice (Piper, Kokoro) was still speaking could leave the new phrase silent and
  BetterTalk stuck as "speaking" (with other sounds kept turned down) until you pressed Stop. Each thing
  BetterTalk says now has its own stop, so stopping one can never be undone by the next one starting.

## 0.4.0

- **Other sounds get quieter while BetterTalk speaks.** Music, videos, calls and every other program's sound on
  the computer drop to half their volume the moment BetterTalk starts speaking (Speak, the red buttons, quick
  phrases and the Itch words), so people can hear you over the TV. When the voice finishes or you press Stop,
  each program goes back to exactly its own volume. A program you open while BetterTalk is speaking is turned
  down too, and if you change a program's volume yourself meanwhile, your choice is kept.
- **Change it or switch it off** in Settings → Voice & speaking → Other sounds while speaking: untick it, or
  choose how quiet other sounds go (50% is half as loud, 0% silences them while BetterTalk speaks).
- If BetterTalk ever closes unexpectedly while speaking, the other programs' volumes are put back the next time
  it starts.

## 0.3.9

- **Send feedback from inside BetterTalk.** Settings → Updates → Send feedback (also in the tray menu) turns the
  main window into a feedback box with a teal frame. Type with BetterTalk's own keyboard and suggestions, then
  press Send feedback where Speak usually is. Nothing is spoken or learned, and your unfinished message comes
  back afterwards. Only your feedback words, the BetterTalk version and the Windows version are sent, and no
  account is needed. Without internet, the feedback is kept on your PC and sent the next time BetterTalk starts.
- **Older button files (from before 0.3.5) keep the plain starting buttons when they update.**

## 0.3.8

- **New installs start with the plain buttons again.** A fresh install gets the general starting red and quick
  buttons (Help!, I need the bathroom., I'm in pain., Yes., No. and the usual quick phrases).
- **Your buttons stay exactly as they are.** Updating never changes the buttons already in your buttons file,
  whichever version you update from. To use a saved set of buttons, choose Settings → Buttons → Load buttons
  from a file.

## 0.3.7

- **Numbers on the keyboard.** A new row above the letters has 1 to 0, with a comma and a full stop at its
  right end. The keyboard stays the same width; the keys get a little shorter to make room, so drag the window
  a bit taller if you want them back to their old height.

## 0.3.6

- **The likeliest word is in the middle of the green row.** The word BetterTalk thinks you want most is now in
  the centre, and the next ones alternate right and left outward, so the best guess is always the shortest move
  away. To go back to likeliest-on-the-left, pick it in Settings → Screen & window → Green suggested words.

## 0.3.5

- **New starting buttons.** New installs start with a different set of red and quick buttons.
- **Your buttons are kept in your buttons file for good.** Older versions kept the red buttons you never
  changed as built-in starting buttons, so a new version could change them. Now every row is written into the
  file, so no update can change them. If your red and quick buttons were never changed, this update gives you
  the new starting buttons once (a copy of the old file is kept as buttons.before-upgrade.json); buttons you
  changed yourself are never touched.

## 0.3.4

- **Wider Settings window.** The whole QuadStick card now fits without scrolling sideways, and all the tabs
  sit in one row.

## 0.3.3

- **QuadStick card.** A new Settings tab laid out like a paper sip-and-puff card: the tubes across, soft puff,
  puff, sip and soft sip down. Each square shows what that gesture sends and what BetterTalk does with it, in red
  for puffs and blue for sips. Click a square to change it. It starts from a common QuadStick set-up, and it is
  another view of the keys in the Hotkeys tab, so a change in one shows in the other.
- **Enter speaks.** Enter (a sip on many cards) now speaks the message. While you step through the keys with the
  arrows, it still presses the key in the frame.
- **Copy, cut and paste.** Ctrl-C copies the whole message, Ctrl-X cuts it (Undo brings it back) and Ctrl-V
  pastes into it.
- **Mouse buttons as keys.** The keys in Settings → Hotkeys ("While BetterTalk is open" and the Itch figure) can
  be a right, middle or side mouse button: click it in the box. Handy when a QuadStick sends a right click, for
  example to type a space. The left button always just presses buttons.
- The update notes in Settings no longer end with install instructions you don't need.

## 0.3.2

- **The Itch arrow buttons can be moved.** Up, Down, Left and Right are now part of the words said out loud:
  in button edit mode, tap one and use ◀ Earlier / Later ▶ to put it where you like (or rename or remove it).
- **Reorder any button.** In button edit mode the edit box has ◀ Earlier and Later ▶, for the red buttons,
  Quick, Phrases and the Itch words.
- No more little tooltips popping up when the pointer rests on a button.

## 0.3.1

- **Updates in Settings.** Check for updates shows what is new, then you choose. Updating keeps a snapshot of
  the version you had and your setup, so you can go back with one button.
- **Move the panels.** Untick Lock UI (Settings → Screen & window, or the tray menu) and drag the Itch figure
  and its controls anywhere. They snap to and slide along each other's edges. Press Lock UI when you are done.
  Only the keyboard and the panels are drawn, so each panel is just the size of what is in it.
- **Itch.** While Itch is open, the red urgent buttons along the top become the words said out loud (Keep
  going, Scratch harder and so on) with Up, Down, Left and Right. Every way of moving the figure has its own
  button, so left clicks are enough; the 🎮 icon shows or hides those controls, and stays the way you left it.
  Clicking the figure puts the place in the message box; Speak says it.
- **Hotkeys.** No fixed show/hide shortcut any more: the Hotkeys tab lets you give any action a key, including
  a key that types a space (handy for a sip or chin button) that only works inside BetterTalk.
- **Any corner of any screen.** The window can sit in any corner, and grows away from it.
- **Set-up guide** on a fresh install, and again any time from Settings or the tray.
- **Button edit mode** is easy to spot and leave: a red frame, striped edges on the buttons you can change, and
  a Done editing button. Settings → Buttons also lets you rename them all at once as lists.
- **Settings and the set-up guide** have a dark, readable look.
- **Learning is more private:** a message said only once is kept as a scrambled fingerprint, never as text.
- The yellow Hide button is always there, and hiding by itself when the mouse leaves is off unless you turn it on.
