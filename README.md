# Dino Visualizer

A retro, terminal-style music visualizer and "Now Playing" overlay for Windows. It shows what Spotify, YouTube, YouTube Music (in any browser) or most other players are playing, with album art, player controls and a live visualizer. It can also put an always-on-top Now Playing widget on your screen in one of five styles.

Download **DinoVisualizer.exe** from the [latest release](https://github.com/SaosinLLC/dino-visualizer/releases/latest) and run it. It's a single file with nothing to install and no sign-in (only the equalizer needs the free Equalizer APO add-on; see below). It runs on Windows 10 and 11, and the Lyrics and Info tabs need an internet connection. The song info comes from the Windows media controls (the ones in the volume flyout), and the visualizer follows how loud the player actually is.

## The window

- **Now Playing**: the album art (video thumbnails are cropped square), the title, artist and album, the app it's playing in and that app's volume. Click or drag along the progress bar to jump to that point in the song.
- **Visualizer**: a big live visualizer in your accent color, in one of 21 styles:
  - *Bars*: spectrum bars with peak caps.
  - *Scope*: a scrolling oscilloscope wave whose height follows the song's loud and quiet parts and spikes on beats.
  - *Mirror*: bars mirrored up and down from a center line.
  - *LED*: segmented hi-fi meter columns with a red top segment.
  - *Wave*: a filled, smooth wave through the bar tops with a glowing edge.
  - *Dots*: a dot-matrix display of round lights, with a bright dot riding on each peak.
  - *Ripples*: rings spreading out from a glowing center on every beat.
  - *Radial*: spectrum spokes all around a pulsing ring.
  - *Orb*: a glowing blob whose outline is the spectrum.
  - *Waterfall*: a scrolling spectrogram of the last few seconds, low notes at the bottom.
  - *Sparks*: sparks shooting up from the bottom, more on beats, arcing back down.
  - *Starfield*: flying through stars, faster when it's loud.
  - *Lissajous*: an X-Y oscilloscope figure that opens up with the music and shivers on beats.
  - *Skyline*: bars as buildings with lit windows, mirrored in the water below.
  - *VU Meters*: two analog meters with swinging needles, lows on the left, highs on the right.
  - *Helix*: a turning double helix whose strands spread apart with the spectrum.
  - *Mountains*: three ranges of hills, the far ones following the music more slowly.
  - *Rain*: digital rain, falling faster when it's loud.
  - *Circle Scope*: the Scope wave wrapped around a circle.
  - *Earth*: the real Earth from space, made from NASA's satellite maps: a slowly turning globe with real cloud cover, sun glinting off the oceans, a glowing atmosphere and city lights on the night side. The music drives it:
    - the glint and the atmosphere brighten with the loudness;
    - a glowing band of light rises from the sunlit edge as a spectrum, with low notes on the left;
    - faint rings of light leave the planet on strong beats, and storms flash on the dark side;
    - it turns faster when the music is loud.

    On the XP widget's screen it becomes a horizon seen from low orbit. The Earth keeps its natural colors (the Colors option doesn't tint it).
  - *Raining Blood*: blood soaks the top edge and swells into drips that let go, while drops rain down with streaks and glints. Louder music makes the rain faster and heavier, and beats bring a downpour. The drops splash into a pool of blood at the bottom whose surface waves with the spectrum (low notes on the left). It's always blood red: the Accent Hue, Accent Saturation and Colors options don't change it.
- **Equalizer** (the second tab): see [The equalizer](#the-equalizer).
- **Lyrics** (the third tab): the words of the song that's playing.
  - **Synced lyrics** follow along: the line being sung is lit up in the middle, and the view scrolls smoothly as the song goes. Click any line to jump the song to it.
  - **Plain lyrics** (no timing) scroll through at the song's pace.
  - The mouse wheel scrolls by hand; the view drifts back to the song 4 seconds later.
  - The top-right corner says *Synced*, *Not Synced*, *Searching*, *Still Looking*, *Not Found*, *Instrumental* or *Offline*.
  - **Lyrics + Visualizer** (in Options) shows both at once: the lyrics scroll over the visualizer, which is dimmed behind them so the words stay readable.

  Lyrics come from [LRCLIB](https://lrclib.net), a free, open lyrics database (no account or key). Songs are only looked up while lyrics are on screen or going to the VRChat chatbox, and the lookup sends only the song's title, artist, album and length. YouTube titles like "Artist - Song (Official Video)" and Spotify's "- Remastered 2011" endings are tidied up first so they're found. If nothing turns up, it keeps looking for a minute (*Still Looking*), then says *Not Found* and checks again every 30 seconds.
- **Info**: the playing song and its artist.
  - **The song** (left): its album, release date, genre, track number and length from the iTunes catalog and, when the song has its own Wikipedia article, what it is ("1991 single by Nirvana") and the article's opening.
  - **The artist** (right): their picture, what they are ("American rock band") and the opening of their Wikipedia article. When the name alone means something else (Nirvana), it finds the band's article.
  - **Song On Wikipedia** and **Artist On Wikipedia** open the full articles in your browser.

  Both services are free and need no account or key. Each song is looked up once, only while the Info tab is open, and the lookup sends only the song's title and artist.
- On the right of the Now Playing box, a Spinosaurus skull turns slowly as a 3D hologram above a glowing projector pad, in your accent color.
- **Previous**, **Play / Pause** and **Next** sit in the Now Playing box, above the progress bar, and control the player (they're grayed out while nothing is playing).
- The bottom bar:
  - **Quit** closes Dino Visualizer completely: the window, the widget and the tray icon.
  - **Check For Updates** looks for a new version. Once one is found, it becomes **Update To v…**, which installs it (see [Updates](#updates)).
  - **Show Widget / Hide Widget** turns the widget on or off.
  - **Minimize** puts the window away on the taskbar.

Closing the window keeps Dino Visualizer running in the notification area (the tray), so the widget stays up. Click the tray icon to open the window again, or right-click it to show or hide the widget or exit. Starting the exe again while it's running just brings its window back.

## Playlists

The **Playlists** tab shows your Spotify playlists and your YouTube playlists (which are also your YouTube Music playlists, plus YouTube's *Liked videos*). Your playlists are listed on the left with a green **SP** or red **YT** badge, the cover and how many songs each has. Click one to see its songs on the right.

- **Click a song** to play the playlist from that song. **Play** plays the whole playlist.
  - **Spotify** plays it in your Spotify app (on whatever device Spotify is open). If Spotify isn't open anywhere, the playlist opens in the Spotify app instead, so you just press play.
  - **YouTube** songs open in YouTube Music or on YouTube in your browser. The **Open YouTube in** button switches between the two. If a YouTube tab is already open (in Chrome, Edge, Brave, Opera, Vivaldi or Firefox), the song plays in that tab instead of a new one, and you stay in Dino Visualizer.
- **Refresh** reloads the playlists. Each account's button also has *Reload Playlists*, *Change Developer Key...* and *Sign Out*.
- Spotify only shows the songs of playlists you made or collaborate on. Playlists you just follow are listed, and **Play** opens them in Spotify.

### Connecting your accounts (once)

Spotify and Google only share your playlists with apps that have a **developer key**. It's free, takes about 5 minutes each, and you only do it once. Click **Spotify: Connect** or **YouTube: Connect** on the Playlists tab and a window walks you through it, with links to the right pages:

**Spotify**
1. Open the [Spotify developer dashboard](https://developer.spotify.com/dashboard) and log in. Spotify requires the account that makes the key to have **Premium**.
2. Click **Create app**. Give it any name and description. Under **Redirect URIs** add exactly `http://127.0.0.1:43117/callback` (the window has a Copy button), tick **Web API**, agree and save.
3. Copy the app's **Client ID** into the window and click **Sign In**. Approve in your browser.

**YouTube / YouTube Music**

There's a **[step-by-step guide](YOUTUBE-SETUP.md)** for this one, with what each screen looks like and what to do if something goes wrong. In short:

1. [Create a Google Cloud project](https://console.cloud.google.com/projectcreate) (any name).
2. Turn on the [YouTube Data API v3](https://console.cloud.google.com/apis/library/youtube.googleapis.com) for it.
3. Open [Google Auth Platform](https://console.cloud.google.com/auth/overview) and click **Get started**: app name, your email, audience **External**, your email again, agree, **Create**.
4. Under [Audience](https://console.cloud.google.com/auth/audience), add your Google account under **Test users**. (Skip **Publish app**: Google only publishes apps that have a website and a privacy policy.)
5. Under [Clients](https://console.cloud.google.com/auth/clients), create a **Desktop app** client. Keep the box that appears open (Google shows the secret only once) and copy its **Client ID** and **Client secret** into the window.
6. Click **Sign In**. Google warns that it hasn't verified the app. It's your own key, so click **Continue** (or **Advanced**, then **Go to Dino Visualizer**). Google signs test users out every 7 days; when that happens, just click **Sign In** again.

**How signing in works:** Dino Visualizer opens Spotify's or Google's own sign-in page in your browser, so it never sees your password. When you approve, the browser hands a one-time code back to the app on `127.0.0.1` (your own PC only). The app only asks to *read* your playlists, plus permission to start playback in Spotify. Your developer keys and sign-ins are saved encrypted for your Windows account in `%AppData%\Dino Visualizer\accounts.dat`, and **Sign Out** removes the sign-in.

## The Secret tab

The **Secret** tab, next to Playlists, holds two easter eggs: a strawberry and a beetle. Each has its own **On / Off** switch, and the switch next to the tab turns them all on or off at once. Your choices are remembered, and switching one off while it's on screen removes it right away.

### The strawberry

While Dance Gavin Dance is playing (as the artist, or in a video's title), a strawberry drifts around your screen like the old DVD player idle logo. It bounces off the edges of the screen and changes color at every bounce: red, pink, orange, purple, blue, gold and green. It floats above everything, never takes focus and lets clicks pass straight through.

When the song ends, the strawberry explodes into chunks of fruit, drops of juice and seeds that fade in about a second. If the next song is Dance Gavin Dance too, a fresh strawberry comes out. If you pause partway through or play something else, it just goes away.

### The beetle

While Saosin is playing, an atlas beetle does the same: it drifts around the screen DVD-style and changes shell color at every bounce (deep navy, midnight blue, violet, emerald, bronze, crimson and steel). When its song ends, it shatters into shards of shell and broken legs.

## The equalizer

A 10-band graphic equalizer (31 Hz to 16 kHz, -12 to +12 dB) that changes everything you hear: music, videos, games, every app. Windows doesn't let one app change another app's sound, so it works through **Equalizer APO**, a free, open-source audio add-on for Windows.

**One-time setup:**
1. Open the **Equalizer** tab and click **Get Equalizer APO**. Download and run its installer.
2. At the end, the installer shows the Configurator. Tick your speakers and headphones, then restart Windows.
3. Open Dino Visualizer again. The sliders appear in the Equalizer tab by themselves.

**Reading it:** each slider is one range of the sound, from the lowest notes on the left to the highest on the right. Up makes that range louder, down makes it quieter, and 0 dB leaves it unchanged. Hover over a slider to see what it changes and how far it's turned up or down.

| Slider | Name | What it changes |
|---|---|---|
| 31 Hz | Rumble | Sub-bass you feel more than hear (movie booms, 808s) |
| 62 Hz | Bass | The kick drum's thump and the low bass notes |
| 125 Hz | Punch | The punch and weight of bass guitar and drums |
| 250 Hz | Warmth | Warmth and fullness; too much sounds muddy |
| 500 Hz | Body | The body of most instruments; too much sounds boxy |
| 1 kHz | Vocals | The center of voices, guitars and pianos |
| 2 kHz | Clarity | Clarity and bite; too much sounds harsh |
| 4 kHz | Presence | Detail and edge, like the "s" and "t" in words |
| 8 kHz | Treble | Cymbals, hi-hats and crispness |
| 16 kHz | Air | The faint shimmer at the very top |

**Using it:**
- **Drag a slider** up or down to set a band, in half-dB steps. The sound changes as you drag.
- **Scroll** over a slider to nudge it.
- **Double-click** a slider to put it back to 0 dB.
- **EQ Preset** in Options picks a starting point: *Flat*, *Bass Boost*, *Treble Boost*, *Bass + Treble*, *Vocal*, *Rock*, *Pop*, *Electronic*, *Hip-Hop*, *Jazz*, *Classical*, *Acoustic*, *Loudness* or *Podcast*. Moving a slider afterwards makes it *Custom*.
- **Equalizer** in Options turns it on or off.

Boosts are balanced by turning the overall level down by the same amount (a preamp), so they don't distort. The equalizer keeps working after Dino Visualizer is closed, until you turn it off.

The bands are saved in `DinoVisualizer.txt` in Equalizer APO's config folder (`C:\Program Files\EqualizerAPO\config`), and one `Include: DinoVisualizer.txt` line is added to its `config.txt`. Anything else you've set up in Equalizer APO is left as it is.

## The widget

Press `Ctrl+F11` anywhere to show or hide the widget, in the last style you picked (you can change those keys on the [Keys tab](#keybinds-the-keys-tab)). Every style shows the song and album art, with player buttons and a volume control that sets just that app's volume (so does the mouse wheel over the card). Drag the middle to move it, and drag any corner to resize it (60% to 250%). It remembers its position, size and style, never takes focus from what you're doing, and shows up in recordings and streams when it's inside the captured area.

- **Dino Style**: a small card in the app's own look, with a progress bar you can click or drag to seek, and a volume slider.
- **Windows Style**: like the Windows 10 media flyout. Its volume panel stays hidden until you hover over it or the volume changes, and fades away 2 seconds later.
- **Aero Style**: a glossy Frutiger Aero / Windows Vista gadget with sky-blue glass, green aurora swooshes, drifting bubbles and glass orb buttons.
- **XP Style**: a Windows XP-era media player with a curved Luna-blue wing, a drop-down to pick the visualizer, a black screen whose green visualizer (any of the 21 styles) follows the music, and a chrome play/pause orb. Three lights on the top strip work like window buttons:
  - **red** closes the widget;
  - **yellow** rolls it up into a thin bar with just the title and artist, and back down;
  - **green** expands it to the biggest size that fits the screen, and back.
- **Orb Style**: inspired by early-2000s green console dashboards: a dark green glass card with a faint wireframe grid behind it.
  - **The orb:** on the left, a big glowing green orb plays the visualizer inside it, pulsing with the music. Pick any of the 21 visualizer styles with the **VISUALIZER** drop-down in the panel's header (the same setting as the Visualizer option).
  - **The time box:** under the orb, it shows play/pause and the time.
  - **The panel:** on the right, it holds the album art, title, artist and album, a glowing lime progress bar (click or drag to seek), ◄◄ / play-pause / ►► buttons and the volume.

  It always stays green.

The Aero and XP styles and the Windows style's volume slider follow the Accent Hue and Accent Saturation sliders.

## VRChat chatbox

The app can show what you're listening to in the chatbox bubble above your head in VRChat.

To set it up:

1. In VRChat, open the Action Menu, go to Options > OSC and turn on **Enabled**. You only need to do this once.
2. In this app, pick a **VRChat Chatbox** option for the song, and/or switch on a [status](#status).

There are three options:

- **Song**: `♫ Song — Artist`
- **Song + Time**: adds `1:23/3:45` on a second line.
- **Song + Progress Bar**: adds `1:23 ━━━●─────── 3:45` on a second line. **Chatbox Bar** picks how the bar looks:
  - *Line* `━━━●───────`
  - *Blocks* `███░░░░░░░`
  - *Squares* `■■■□□□□□□□`
  - *Dots* `●●●○○○○○○○`
  - *Hearts* `♥♥♥♡♡♡♡♡♡♡`
  - *Stars* `★★★☆☆☆☆☆☆☆`
  - *Classic* `[===>-------]`
  - *Arrows* `▶▶▶▷▷▷▷▷▷▷`
  - *Diamonds* `◆◆◆◇◇◇◇◇◇◇`
  - *Pills* `▰▰▰▱▱▱▱▱▱▱`
  - *Shades* `▓▓▓░░░░░░░`
  - *Small Squares* `▪▪▪▫▫▫▫▫▫▫`
  - *Notes* `♪♪♪·······`
  - *Moon* `●●●◐○○○○○○○`
  - *Hash* `[###.......]`

  If one shows as boxes in VRChat, use *Classic* or *Hash*, which are plain text and always work.

Each option also has a lyrics version (**Song + Lyrics**, **Song + Time + Lyrics** and **Song + Bar + Lyrics**) that adds the line being sung under the song. A `♪` shows between verses.

- Lyrics come from LRCLIB, the same as the Lyrics tab, and only synced lyrics can be followed. Without them, you just get the song (and the time or bar).
- Each new line is sent as it starts. Lines closer together than 2 seconds can't all show.
- **Chatbox Adlibs** sets how background vocals and adlibs (the parts of a line in brackets) look. VRChat's chatbox has no text sizes, so tiny Unicode letters or small capitals stand in for a smaller font:
  - *Tiny*: `I keep on falling ⁽ᶠᵃˡˡⁱⁿᵍ ᵈᵒʷⁿᵎ⁾` (the default)
  - *Small Caps*: `I keep on falling (ꜰᴀʟʟɪɴɢ ᴅᴏᴡɴ!)`
  - *Normal*: unchanged
  - *Hidden*: leaves them out

  There's no tiny "q", so it stays normal size. If the tiny letters show as boxes in VRChat, use *Small Caps*.

**Chatbox Clock** puts your computer's time at the top of the bubble, above your status and the song: *12-Hour* (`3:45 PM`) or *24-Hour* (`15:45`). It works on its own too, even with VRChat Chatbox set to *Off* and nothing playing.

How it behaves:

- It uses the same song as the window, with the text kept short so as much as possible fits in VRChat's 144 characters:
  - YouTube titles are cleaned up: "Saosin - Voices (Official Video) | Epitaph Records #metal" becomes `Voices — Saosin`. Hashtags, `【tags】` and anything after ` | ` (usually the channel or label) are removed.
  - Featured artists move after the artist: "Unanswered (feat. Phil Bozeman)" by Suicide Silence becomes `Unanswered — Suicide Silence ft. Phil Bozeman`.
  - A short ending after a dash goes in brackets: `Die For You (Remix)`.
  - When it's too long, the song name is shortened first (at the end of a word, with `…`), so the artist still shows.
- When you pause or stop, the bubble clears (or shows just your status, if it's on). It also clears when you turn everything off or quit the app.
- VRChat drops chatbox messages that come too fast, so the app sends at most one every 2 seconds. That's why the time moves in 2-second steps.
- The bubble fades after a while, so the text is sent again every 10 seconds.
- Your own typed chatbox messages get replaced while a song plays.

### Discord status

With **Discord Status** on in Options, your Discord profile shows what's playing: *Playing Dino Visualizer*, with the song, the artist, the album, how far in it is, and a **Get Dino Visualizer** button.

- It goes through the Discord app on your computer (its Rich Presence), so Discord has to be running. Nothing is sent over the internet by Dino Visualizer itself, and no Discord account details are needed.
- It clears while nothing is playing or the song is paused, and when you quit.
- The status line at the top says *Discord: On* when it's connected, or *Waiting For Discord* while Discord isn't running.
- Discord only shows it if **Share my activity** is on, under *Activity Privacy* in Discord's settings.

### Status

The **Status** tab is for lines of your own, like `chilling in the club`. The one you pick shows in the bubble above the song, or on its own when nothing is playing. The status works even with VRChat Chatbox set to *Off*.

- **On/Off**: the switch next to the tab (or **Status On** inside it) turns the status on or off.
- **Writing statuses**: type in the box and press Enter (or click **Add**). A status can be up to 100 characters; above a song it's shortened to 70 so everything fits.
- **Changing the list**: each status has **Edit** (Enter saves, Escape cancels), **Delete**, and ▲ ▼ to change the order.
- **Picking one**: click a status to show it now. The one showing is lit up.
- **Cycle**: *Off*, *5 s*, *10 s*, *30 s*, *1 min* or *5 min*. With a time picked, your statuses take turns in list order, and the one showing says how long until the next one.
- **In VRChat Now**: with a wide enough window, a box on the right shows exactly what the bubble says right now.

Your statuses are kept in `%AppData%\Dino Visualizer\statuses.txt`, one per line.

Technical details:

- It works through OSC: messages go to `/chatbox/input` on this computer only (`127.0.0.1`, UDP port 9000). Nothing goes over the internet.
- If you've moved VRChat's OSC port, add `vrcport=<port>` to `settings.ini`.

## Keybinds (the Keys tab)

The **Keys** tab is for your own keyboard shortcuts. They work from any app, so they work in a game, in VRChat or with a headset on.

- **Add Keybind**, then press the keys you want (like `Ctrl+Alt+P`). To change a keybind's keys, click its key box and press new ones. Esc cancels, and Backspace clears the keys.
- Click the box next to it to pick what the keys do:
  - **The music**: Play / Pause, Next Song, Previous Song, Volume Up and Volume Down (in steps of 5%, and they keep going while held).
  - **The windows**: Show / Hide Widget, Show / Hide Window, Next and Previous Visualizer.
  - **Turning features on and off**: the VRChat Chatbox, the Chatbox Status (and Next Chatbox Status), the Chatbox Clock, Discord Status, VR Wrist, VR Batteries, Viz Background, Lyrics + Visualizer, the Equalizer, the Sleep Timer, Hue Cycle and Random Colors. Each one works like switching it in Options. Turning one back on brings back the choice it had before, so the VRChat Chatbox comes back as *Song + Lyrics* if that's what it was.
- When the window isn't in front, a quick notification says what a keybind switched.
- A letter, number or other typing key needs Ctrl or Alt with it, so it doesn't stop that key typing in every app. F-keys and media keys can be used on their own.
- *Used by another app* means another program already has those keys (pick others); *Same keys as above* means two of your keybinds share them.
- `Ctrl+F11` for the widget is there to start with. Delete it or change it like any other.

## Options

- **Widget**: *Off*, *Dino Style*, *Windows Style*, *Aero Style*, *XP Style* or *Orb Style*.
- **Widget Opacity**: how see-through the widget is, 20% to 100%. It changes live while you drag the slider.
- **Visualizer**: the visualizer style for this window and the XP widget's screen. The XP widget's own drop-down changes it too.
- **Lyrics + Visualizer**: shows the lyrics over the visualizer (dimmed behind them) on the Visualizer and Lyrics tabs. Turning it on from another tab switches to the Visualizer tab.
- **Viz Background**: replaces the window's black background with the visualizer, running behind all the panels and buttons. *Dim* keeps it in the background so everything reads clearly; *Bright* lets it show more. The panels turn lighter so it shows through them. It only runs while the window is open.
- **Equalizer**: turns the equalizer on or off.
- **EQ Preset**: one of the 14 equalizer presets, or *Custom* after you move a slider. Picking one switches to the Equalizer tab.
- **Close To Tray**: on (the default), closing the window keeps the app running in the tray; off, closing the window quits.
- **Sleep Timer**: *Off*, *15 min*, *30 min*, *45 min*, *1 hour*, *1.5 hours* or *2 hours*. When the time's up, the music pauses.
  - Over the last 30 seconds the player's own volume fades down; a moment after the pause it goes back to where it was.
  - The top bar counts down (`Sleep in 00:29:59`).
  - It isn't remembered when the app restarts.
- **Updates**: what happens when there's a new version (see [Updates](#updates)).
  - *Notify Me* (the default): the update button at the bottom says so, and the tray tells you once.
  - *Install Automatically*: it updates and restarts by itself.
  - *Off*: it doesn't check (the button still checks when you click it).
- **Auto Start**: starts Dino Visualizer when you sign in to Windows. *Off*, *On* (it opens its window) or *On (In Tray)* (it starts hidden in the tray, with the widget and the VRChat chatbox running).
  - It shows up as "Dino Visualizer" in Task Manager's Startup apps.
  - If you move the app's folder, open the app once from the new place and the entry follows it.
- **VRChat Chatbox**: *Off*, *Song*, *Song + Time*, *Song + Progress Bar*, or any of those with the lyrics. It shows what's playing above your head in VRChat (see [VRChat chatbox](#vrchat-chatbox)).
- **VR Wrist**, **VR Wrist Look** and **VR Wrist Size**: the hologram card on your wrist in SteamVR (see [VR wrist](#vr-wrist-steamvr)).
- **VR Batteries**: shows the battery of your headset, controllers and trackers under the wrist card.
- **Chatbox Adlibs**: how background vocals and adlibs look in the chatbox's lyrics: *Tiny*, *Small Caps*, *Normal* or *Hidden*.
- **Chatbox Bar**: how the chatbox's progress bar looks. There are 15 styles, from *Line* to *Hash*; each choice in the list shows a sample.
- **Chatbox Clock**: your computer's time at the top of the chatbox: *Off*, *12-Hour* or *24-Hour*.
- **Discord Status**: shows what you're playing as your Discord status (see [Discord status](#discord-status)).
- **Scanlines**, **Color Preset**, **Accent Hue**, **Accent Saturation**: the app's CRT scanlines and accent color.
- **Hue Cycle**: *Off*, *Slow*, *Medium* or *Fast*. The accent color keeps turning around the color wheel: a full turn takes 2 minutes (Slow), 40 seconds (Medium) or 12 seconds (Fast). Everything that follows the accent color turns with it, including the window, the visualizer and the widget's tinted styles.
- **Random Colors**: *Off*, *On Beats*, *Every 5 s* or *Every 15 s*. The accent color glides to a new random color on the music's beats or on a timer, and with more than one color the palette is reshuffled too. It works together with Hue Cycle.
- **Colors**: how many colors the visualizers use at once: *1 Color* (the accent color), *2*, *3*, *4* or *6 Colors* spread around the color wheel from the accent hue, or *Rainbow*. The colors blend across the visualizer from left to right, in every style, in this window and on the XP widget's screen (which is classic green with 1 Color).

  While the color is moving, the hue slider shows where it is; moving the slider or picking a preset carries on from there. Turning the moving colors off keeps the color they were on.

When the window is too short for all the options, scroll the Options panel with the mouse wheel.

Options are remembered between launches in `%AppData%\Dino Visualizer\settings.ini`. The first time it runs, it brings over the widget's style, position, size and opacity, the visualizer and the accent color from Dino Recorder, if you've used Dino Recorder's widget.

## Notes

- Windows doesn't let apps read a player's actual frequencies, so the visualizers follow the player's loudness meter about 30 times a second, spread over a moving pattern.
- Dino Recorder has its own copy of the widget. If both apps are running, only the first one started gets `Ctrl+F11`; the Keys tab shows *Used by another app* and you can pick other keys there.

## VR wrist (SteamVR)

In SteamVR, a hologram of the Now Playing card can sit on your wrist. Turn your wrist toward your face to see it, then point at its buttons with your other hand and pull the trigger to skip back, play or pause, or skip ahead. Turning your wrist toward your face twice quickly opens Dino Visualizer's whole window floating in front of you.

To set it up, pick a wrist in **VR Wrist** in Options: *Left Wrist (Inside)*, *Left Wrist (Outside)*, *Right Wrist (Inside)* or *Right Wrist (Outside)*.
- **Inside** is the palm side of your wrist; **outside** is the back of it, where a watch goes.
- The top bar says *VR Wrist: Waiting For SteamVR* until SteamVR is running, then *VR Wrist: On*.
- It never starts SteamVR itself. It connects when SteamVR is running and lets go when SteamVR closes.

**VR Wrist Look** picks which of the widget's looks the hologram has: *Dino*, *Windows*, *Aero*, *XP* or *Orb*. Each look is drawn see-through and glowing in your accent color, with scanlines and a slight flicker, and the XP and Orb looks' visualizers move with the music, as on the desktop.

How to use it:
- **Show it**: hold the trigger on that hand and turn the chosen side of your wrist toward your face. It fades in within about 40 degrees of facing you, and fades out once you turn it well away. Once it's up you can let go of the trigger: it stays while you look at it, so your other hand is free to point at it.
  - **VR Wrist Shows** in Options picks *Trigger + Look* (the default) or *On A Look* (no trigger: it comes up whenever you look at your wrist).
  - The trigger is a SteamVR Input action, *Show the wrist card*, bound to the trigger on Index, Touch, Vive, Cosmos and Windows Mixed Reality controllers. It's listed under Dino Visualizer in SteamVR's controller bindings, where it can be moved to another button. On controllers it has no binding for, the card comes up on a look alone.
- **Press a button**: point your other controller at the card. SteamVR's laser beam comes out of it, the button under the beam lights up, and pulling the trigger presses it (Previous, Play/Pause, Next).
- **The card's other controls** work with the laser too, on every look that has them:
  - **Volume**: hold the trigger on the volume slider and move the laser along it; let go to leave it there.
  - **Seek**: hold the trigger on the progress bar, move to the spot, and let go to jump the song there.
  - **Visualizer** (XP and Orb looks): each trigger press on the visualizer box moves on to the next visualizer, for the whole app.
  - While the card is showing, your controllers point at it instead of the game; that ends when you turn your wrist away.
- **The buttons under the card** work the same way:

  | Button | What it does |
  |---|---|
  | **Open App** / **Close App** | Opens or closes Dino Visualizer's window in front of you |
  | **Smaller** / **Bigger** | Resizes the card (remembered, and shown in **VR Wrist Size**) |
  | **Rotate L** / **Rotate R** | Turns the card on your wrist, 15° a click (remembered) |
- **Move it by hand**: point at the handle in the card's corner (the four-way arrow at the end of the buttons) and hold the trigger. The card follows your hand; let go and it stays there, still on your wrist (it moves with your arm from then on, and is remembered). Double-click the handle, or pick a different wrist in Options, to put it back in its usual spot.
- **Resize it by hand**: point at the card (not at a button), hold the trigger, and move that hand: right or up makes it bigger, left or down smaller. Let go to keep the size.
- **Batteries**: with **VR Batteries** on in Options, a row under the buttons shows the battery of your headset, controllers and trackers, e.g. `Headset 82%`, `Left 64%`, `Waist 15%`.
  - Each battery is green, then amber under 50% and red under 20%, with a bolt while it's charging. They're read again every 5 seconds.
  - Trackers are named after the role you gave them in SteamVR's **Manage Trackers** (*Waist*, *L Foot*, …), or *Tracker 1*, *Tracker 2* without one.
  - Devices that don't report a battery (base stations, a wired headset like the Index) aren't listed.
- **Open the app's window in VR**: turn your wrist toward your face twice within about a second (show it, turn away, show it again).
  - Dino Visualizer's window appears about 80 cm in front of where you're looking, a meter wide, even while it's hidden in the tray on the desktop.
  - It's live: the visualizer, lyrics and everything else move.
  - Point at anything with the laser and pull the trigger to click it: tabs, player buttons, options, drop-downs. Sliders drag while the trigger is held, and the thumbstick scrolls lyrics, lists and the Options panel.
  - The buttons on its top bar are clicked like anything else:

    | Button | What it does |
    |---|---|
    | **Bring Here** | Moves the window in front of wherever you're looking now |
    | **Smaller** / **Bigger** | Resizes it (remembered) |
    | **Turn Left** / **Turn Right** | Turns it, 15° a click |
    | **✕** | Closes it (so does the double turn, or **Close App** under the wrist card) |

**VR Wrist Size** in Options makes the wrist card *Small*, *Medium* or *Large*. It changes straight away.

It uses SteamVR's own `openvr_api.dll` from your SteamVR folder, so nothing extra is installed. The card sits up your forearm, clear of the controller, because SteamVR draws controllers in front of overlays.

## Updates

Dino Visualizer updates itself from its GitHub releases. It checks 30 seconds after starting and then every 6 hours, or when you click **Check For Updates** at the bottom of the window.

When there's a new version:
1. The button changes to **Update To v1.2.3**; click it to install. With **Updates** set to *Install Automatically* in Options, this happens by itself.
2. It downloads the new `DinoVisualizer.exe`, checks that it's really Dino Visualizer at that version, and swaps it in.
3. It restarts into the new version, back in the tray if that's where it was.

The old exe is deleted the next time it starts. The updater only fetches the latest release's details from GitHub's API and the exe attached to it, over HTTPS. Nothing about you or your music is sent.

## Rebuilding

The source is in `src\DinoVisualizer.cs`, and `make-icon.ps1` draws the icon. The Earth visualizer's maps are in the `earth` folder, and `build.ps1` builds them into the exe (without them, it generates a planet instead). To rebuild:

```
powershell -ExecutionPolicy Bypass -File build.ps1
```

## Credits

The Earth visualizer's maps are NASA imagery, credit **NASA Earth Observatory**:
- `land_shallow_topo_2048.jpg`: Blue Marble land surface, shallow water and shaded topography (NASA Goddard Space Flight Center, Reto Stöckli).
- `cloud_combined_2048.jpg`: Blue Marble global cloud cover.
- `BlackMarble_2016_01deg_gray.jpg`: Black Marble 2016 night lights (Suomi NPP VIIRS).

All three come from NASA's image server (eoimages.gsfc.nasa.gov) and are used under NASA's media guidelines.

The VR wrist talks to SteamVR using function layouts from Valve's OpenVR SDK (`openvr_api.cs`, github.com/ValveSoftware/openvr, BSD-3-Clause license, Copyright (c) 2015, Valve Corporation).
