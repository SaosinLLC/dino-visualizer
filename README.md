# Dino Visualizer

A music visualizer and "Now Playing" overlay in the same retro terminal style as Dino Monitor and Dino Recorder. It shows what Spotify, YouTube, YouTube Music (in any browser) or most other players are playing, with album art, player controls and a live visualizer. It also puts an always-on-top Now Playing widget on your screen in one of four looks.

Run **DinoVisualizer.exe**. It's a single file with nothing to install and no sign-in (only the equalizer needs the free Equalizer APO add-on; see below). The Lyrics tab needs an internet connection. It runs on Windows 10 and 11. The song info comes from the Windows media controls (the ones in the volume flyout), and the visualizer follows how loud the player actually is.

## The window

- **Now Playing**: the album art (video thumbnails are cropped square), the title, artist and album, which app it's playing in and that app's volume. Click or drag along the progress bar to jump to that point in the song.
- **Visualizer**: a big live visualizer in your accent color, in one of 21 styles:
  - *Bars*: spectrum bars with peak caps.
  - *Scope*: a scrolling oscilloscope wave whose height follows the song's loud and quiet parts and spikes on beats.
  - *Mirror*: bars mirrored up and down from a center line.
  - *LED*: segmented hi-fi meter columns with a red top segment.
  - *Wave*: a filled, smooth wave through the bar tops with a glowing edge.
  - *Dots*: a dot-matrix display of round lights, with a bright dot riding on each peak.
  - *Ripples*: rings spreading out from a glowing center on every beat.
  - *Radial*: spectrum spokes all round a pulsing ring.
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
  - *Circle Scope*: the Scope wave wrapped round a circle.
  - *Earth*: the real Earth from space, made from NASA's satellite maps. A slowly turning globe with true continents, real cloud cover casting soft shadows, the sun glinting off the oceans, a blue atmosphere glowing at the edge, city lights on the night side and twinkling stars. The music drives it:
    - the glint and the atmosphere brighten with the loudness;
    - a glowing band of light rises from the sunlit edge as a spectrum, with low notes on the left;
    - faint rings of light leave the planet on strong beats, and storms flash on the dark side;
    - it turns faster when the music is loud.

    On the XP widget's screen it becomes a horizon seen from low orbit. The Earth keeps its natural colors (the Colors option doesn't tint it).
  - *Raining Blood*: blood soaks the top edge and swells into drips that let go, while drops rain down with streaks and glints. Louder music makes the rain faster and heavier, and beats bring a downpour. The drops splash into a pool of blood at the bottom whose surface waves with the spectrum (low notes on the left) and ripples where they land, with a dark red glow rising from it. It's always blood red: the Accent Hue, Accent Saturation and Colors options don't change it.
- **Equalizer**: the visualizer box's second tab (see below).
- **Lyrics**: the third tab: the words of the song that's playing.
  - **Synced lyrics** follow along: the line being sung is lit up in the middle, with the lines around it dimmer, and the view scrolls smoothly as the song goes. Click any line to jump the song to it.
  - **Plain lyrics** (no timing) scroll through at the song's pace.
  - The mouse wheel scrolls by hand; the view drifts back to the song 4 seconds later.
  - The top-right corner says *Synced*, *Not Synced*, *Searching*, *Not Found*, *Instrumental* or *Offline*.

  - **Lyrics + Visualizer** (in Options) shows both at once: the lyrics scroll over the visualizer, which is dimmed behind them so the words stay readable. It works on both the Visualizer tab and the Lyrics tab, with any visualizer style.

  Lyrics come from [LRCLIB](https://lrclib.net), a free, open lyrics database (no account or key). Each song is looked up once, and only while lyrics are on screen or going to the VRChat chatbox; the lookup sends the song's title, artist, album and length. YouTube titles like "Artist - Song (Official Video)" and Spotify's "- Remastered 2011" endings are tidied up first so they're found. If the service is busy or you're offline, it tries again 30 seconds later.
- The **Info** tab tells you about the playing song and its artist:
  - **The song** (left): its album, release date, genre, track number and length from the iTunes catalog (Apple Music's), and, when the song has its own Wikipedia article, what it is ("1991 single by Nirvana") and the article's opening.
  - **The artist** (right): their picture, what they are ("American rock band") and the opening of their Wikipedia article. When the name alone means something else (Nirvana), it finds the band's article.
  - **Song On Wikipedia** and **Artist On Wikipedia** open the full articles in your browser. The top-right corner shows the genre and year.

  Both services are free and need no account or key. Each song is looked up once, and only while the Info tab is open; the lookup sends only the song's title and artist. Live, acoustic, instrumental and karaoke versions are skipped unless that's what's playing.
- On the right of the Now Playing box, Dino Monitor's Spinosaurus skull turns slowly as a 3D hologram above a glowing projector pad, in your accent color, with a beam of light, a scan line sweeping up now and then, and the odd flicker.
- **Previous**, **Play / Pause** and **Next** sit in the Now Playing box, above the progress bar, and control the player (grayed out while nothing is playing).
- The bottom bar:
  - **Quit** closes Dino Visualizer completely: the window, the widget and the tray icon.
  - **Check For Updates** looks for a new version. Once one's found it becomes **Update To v…**, which installs it (see [Updates](#updates)).
  - **Show Widget / Hide Widget** turns the widget on or off.
  - **Minimize** puts the window away on the taskbar.

Closing the window keeps Dino Visualizer running in the notification area (the tray), so the widget stays up. Click the tray icon to open the window again, or right-click it to show or hide the widget or exit. Starting the exe again while it's running just brings its window back.

## Playlists

The **Playlists** tab shows your Spotify playlists and your YouTube playlists (which are also your YouTube Music playlists, plus YouTube's *Liked videos*). Your playlists are listed on the left with a green **SP** or red **YT** badge, the cover and how many songs each has. Click one to see its songs on the right.
- **Click a song** to play the playlist from that song. **Play** plays the whole playlist.
  - **Spotify** plays it in your Spotify app (on whatever device Spotify is open). If Spotify isn't open anywhere, the playlist opens in the Spotify app instead, so you just press play.
  - **YouTube** songs open in YouTube Music or on YouTube in your browser. The **Open YouTube in** button switches between the two.
- **Refresh** reloads the playlists. Each account's button also has *Reload Playlists*, *Change Developer Key...* and *Sign Out*.
- Spotify only shows the songs of playlists you made or collaborate on. Playlists you just follow are listed, and **Play** opens them in Spotify.

### Connecting your accounts (once)

Spotify and Google only share your playlists with apps that have a **developer key**. It's free, takes about 5 minutes each, and you only do it once. Click **Spotify: Connect** or **YouTube: Connect** on the Playlists tab and a window walks you through it, with links to the right pages:

**Spotify**
1. Open the [Spotify developer dashboard](https://developer.spotify.com/dashboard) and log in. Spotify requires the account that makes the key to have **Premium**.
2. Click **Create app**. Give it any name and description. Under **Redirect URIs** add exactly `http://127.0.0.1:43117/callback` (the window has a Copy button), tick **Web API**, agree and save.
3. Copy the app's **Client ID** into the window and click **Sign In**. Approve in your browser.

**YouTube / YouTube Music**
1. [Create a Google Cloud project](https://console.cloud.google.com/projectcreate) (any name).
2. Turn on the [YouTube Data API v3](https://console.cloud.google.com/apis/library/youtube.googleapis.com) for it.
3. Open [Google Auth Platform](https://console.cloud.google.com/auth/overview) and click **Get started**: app name, your email, audience **External**. Then under **Audience**, click **Publish app** so Google doesn't sign you out every 7 days (or add yourself as a test user).
4. Under [Clients](https://console.cloud.google.com/auth/clients), create a **Desktop app** client and copy its **Client ID** and **Client secret** into the window.
5. Click **Sign In**. Google may warn that it hasn't verified the app. It's your own app, so click **Advanced**, then **Go to Dino Visualizer**.

**How signing in works:** Dino Visualizer opens Spotify's or Google's own sign-in page in your browser, so it never sees your password. When you approve, the browser hands a one-time code back to the app on `127.0.0.1` (your own PC only). The app only asks to *read* your playlists, plus permission to start playback in Spotify. Your developer keys and sign-ins are saved encrypted for your Windows account in `%AppData%\Dino Visualizer\accounts.dat`, and **Sign Out** removes the sign-in.

## The Secret tab

The strawberry and the beetle (below) live in the **Secret** tab, next to Playlists in the box under the track. Each has its own **On / Off** switch, and the switch right next to the tab turns them all on or off at once. Your choices are remembered. Switching one off while it's out on screen puts it away straight away.

## The beetle

While Saosin is playing, an atlas beetle does the same: it drifts around the screen DVD-style and changes shell color at every bounce (deep navy, midnight blue, violet, emerald, bronze, crimson and steel). When its song ends it shatters into shards of shell, crumbs, glints and broken legs.

## The strawberry

While Dance Gavin Dance is playing (the band's name as the artist, or in a video's title), a strawberry drifts around your screen like the old DVD player idle logo. It moves in straight diagonal lines, bounces off the edges of the screen and changes color at every bounce: red, pink, orange, purple, blue, gold and green. It floats above everything, never takes focus and lets clicks pass straight through. When its song ends (the player moves on to another track, or stops at the end of the song), the strawberry explodes: a flash, then chunks of fruit, drops of juice, seeds and bits of leaf fly out and fall while a ring of juice spreads, all fading in about a second. If the next song is Dance Gavin Dance too, a fresh strawberry comes out. If you pause partway through or play something else, it just goes away.

## The equalizer

A 10-band graphic equalizer (31 Hz to 16 kHz, -12 to +12 dB) that changes everything you hear: music, videos, games, every app. Windows doesn't let one app change another app's sound, so it works through **Equalizer APO**, a free, open-source audio add-on for Windows.

**One-time setup:**
1. Open the **Equalizer** tab and click **Get Equalizer APO**. Download and run its installer.
2. At the end, the installer shows the Configurator. Tick your speakers and headphones, then restart Windows.
3. Open Dino Visualizer again. The sliders appear in the Equalizer tab by themselves.

**Reading it:** each slider is one range of the sound, from the lowest notes on the left to the highest on the right. Up makes that range louder, down makes it quieter, and 0 dB leaves it unchanged. Under each slider are its frequency, a plain name for what it covers, and whether it's part of the bass, the mids or the treble:

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

The line at the top of the tab explains how it works. Hover over a slider and the line says what that band changes and how much it's turned up or down right now.

**Using it:**
- **Drag a slider** up or down to set a band, in half-dB steps. The sound changes as you drag.
- **Scroll** over a slider to nudge it.
- **Double-click** a slider to put it back to 0 dB.
- **EQ Preset** in Options picks a starting point: *Flat*, *Bass Boost*, *Treble Boost*, *Bass + Treble*, *Vocal*, *Rock*, *Pop*, *Electronic*, *Hip-Hop*, *Jazz*, *Classical*, *Acoustic*, *Loudness* or *Podcast*. Moving a slider afterwards makes it *Custom*.
- **Equalizer** in Options turns it on or off.

Boosts are balanced by turning the overall level down by the same amount (a preamp), so they don't distort. The equalizer keeps working after Dino Visualizer is closed, until you turn it off.

How it works: the bands are saved in `DinoVisualizer.txt` in Equalizer APO's config folder (`C:\Program Files\EqualizerAPO\config`). One `Include: DinoVisualizer.txt` line is added to its `config.txt`; anything else you've set up in Equalizer APO is left as it is.

## The widget

`Ctrl+F11` from anywhere shows or hides it, in the last style picked. Every style shows the song with album art, and the player buttons and volume control that player. The volume control (and the mouse wheel over the card) sets just that app's own volume. Move the widget by grabbing its middle, and drag any corner to resize it (60% to 250%). It remembers its spot, size and style, never takes focus from what you're doing, and shows up in recordings and streams whenever it's inside the captured area.

- **Dino Style**: a small card in the app's own look, with a progress bar you can click or drag to seek, and a volume slider.
- **Windows Style**: like the Windows 10 media flyout. Its volume panel stays hidden until you hover over its spot or the volume changes (the mouse wheel over the card, or the player or Windows changing it). It fades away 2 seconds later.
- **Aero Style**: a glossy Frutiger Aero / Windows Vista gadget: sky-blue glass, a gloss highlight, green aurora swooshes, drifting bubbles, glass orb buttons and a reflection under the album art.
- **XP Style**: a Windows XP-era media player. It has a curved Luna-blue wing with a drop-down to pick the visualizer, a metal frame around a black screen whose green visualizer (any of the 21 styles) follows the music, and a glossy control bar with a chrome play/pause orb. Three lights on the top strip work like window buttons:
  - **red** closes the widget;
  - **yellow** rolls it up into a thin bar with just the title and artist, and back down;
  - **green** blows it up to the biggest size that fits the screen, and back.

- **Orb Style**: inspired by the early-2000s green console dashboards. It's a dark green glass card with a faint wireframe grid curving away behind it and a glowing tube sweeping in from the edge.
  - **The orb:** on the left, a big glowing green orb plays the visualizer inside it, pulsing and glowing brighter with the music. Any of the 21 visualizer styles works, picked with the **VISUALIZER** drop-down in the panel's header (the same setting as the Visualizer option).
  - **The time box:** under the orb, it shows play/pause and the time.
  - **The panel:** on the right, a beveled panel with a cut corner holds the album art, title, artist and album, a glowing lime progress bar (click or drag to jump), beveled ◄◄ / play-pause / ►► buttons and the volume.

  It always stays green.

The Aero and XP styles and the Windows style's volume slider follow the Accent Hue and Saturation sliders.

## VRChat chatbox

The app can show what you're listening to in the chatbox bubble above your head in VRChat.

To set it up:

1. In VRChat, open the Action Menu and go to Options > OSC, then turn on **Enabled**. This only needs doing once.
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

  If one shows as boxes in VRChat, *Classic* and *Hash* are plain text and always work.
- **Song + Lyrics**, **Song + Time + Lyrics** and **Song + Bar + Lyrics**: add the line of the lyrics being sung under the song. A `♪` shows between verses.
  - Lyrics come from LRCLIB, the same as the Lyrics tab, and only synced lyrics can be followed. Without them, you just get the song (and the time or bar).
  - Each new line is sent as it starts. The time waits a moment for it when a new line is about to begin, so lines aren't held up by the 2-second gap.
  - Lines closer together than 2 seconds can't all show.
  - **Chatbox Adlibs** sets how background vocals and adlibs (the parts of a line in brackets) look. VRChat's chatbox has no text sizes, so tiny Unicode letters or small capitals stand in for a smaller font:
    - *Tiny*: `I keep on falling ⁽ᶠᵃˡˡⁱⁿᵍ ᵈᵒʷⁿᵎ⁾` (the default)
    - *Small Caps*: `I keep on falling (ꜰᴀʟʟɪɴɢ ᴅᴏᴡɴ!)`
    - *Normal*: unchanged
    - *Hidden*: leaves them out

    There's no tiny "q", so it stays normal size. If the tiny letters show as boxes in VRChat, use *Small Caps*.

How it behaves:

- It uses the same song as the window, with the text kept short so as much as possible fits in VRChat's 144 characters:
  - YouTube titles are cleaned up: "Saosin - Voices (Official Video) | Epitaph Records #metal" becomes `Voices — Saosin`. Hashtags, `【tags】` and anything after ` | ` (usually the channel or label) go.
  - Featured artists move after the artist: "Unanswered (feat. Phil Bozeman)" by Suicide Silence becomes `Unanswered — Suicide Silence ft. Phil Bozeman`.
  - A short ending after a dash goes in brackets: `Die For You (Remix)`.
  - When it's too long, the song name is shortened first (at the end of a word, with `…`), so the artist still shows.
- When you pause or stop, the bubble clears (or shows just your status, if it's on). It also clears when you turn everything off or quit the app.
- VRChat drops chatbox messages that come too fast, so the app sends at most one every 2 seconds. That means the time moves in 2-second steps.
- The bubble fades after a while, so the text is sent again every 10 seconds.
- Your own typed chatbox messages get replaced while a song plays.

### Status

The **Status** tab is for lines of your own, like `chilling in the club`. The one you pick shows in the bubble above the song, or on its own when nothing is playing. The status works even with VRChat Chatbox set to *Off*.

- **On/Off**: the switch next to the tab (or **Status On** inside it) turns the status on or off.
- **Writing statuses**: type in the box and press Enter (or click **Add**). A status can be up to 100 characters; above a song it's shortened to 70 so everything fits.
- **Changing the list**: each status has **Edit** (Enter saves, Escape cancels), **Delete**, and ▲ ▼ to change the order.
- **Picking one**: click a status to show it now. The one showing is lit up.
- **Cycle**: *Off*, *5 s*, *10 s*, *30 s*, *1 min* or *5 min*. With a time picked, your statuses take turns in list order. The one showing says how long until the next one.
- **In VRChat Now**: with a wide enough window, a box on the right shows exactly what the bubble says right now.

Your statuses are kept in `%AppData%\Dino Visualizer\statuses.txt`, one per line.

Technical details:

- It works through OSC: messages go to `/chatbox/input` on this computer only (`127.0.0.1`, UDP port 9000). Nothing goes over the internet.
- If you've moved VRChat's OSC port, add `vrcport=<port>` to `settings.ini`.

## Options

- **Widget**: *Off*, *Dino Style*, *Windows Style*, *Aero Style*, *XP Style* or *Orb Style*.
- **Widget Opacity**: how see-through the widget is, 20% to 100%. It changes live while you drag the slider.
- **Visualizer**: the visualizer style for this window and the XP widget's screen. The XP widget's own drop-down changes it too.
- **Lyrics + Visualizer**: shows the lyrics over the visualizer (dimmed behind them) on the Visualizer and Lyrics tabs. Turning it on from another tab switches to the Visualizer tab.
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
  - It's the "Dino Visualizer" entry in your user's startup list (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`), so it also shows in Task Manager's Startup apps.
  - If you move the app's folder, open the app once from the new place and the entry follows it.
- **VRChat Chatbox**: *Off*, *Song*, *Song + Time*, *Song + Progress Bar*, or any of those with the lyrics. It shows what's playing above your head in VRChat (see [VRChat chatbox](#vrchat-chatbox)).
- **VR Wrist**, **VR Wrist Look** and **VR Wrist Size**: the hologram card on your wrist in SteamVR (see [VR wrist](#vr-wrist-steamvr)).
- **Chatbox Adlibs**: how background vocals and adlibs look in the chatbox's lyrics: *Tiny*, *Small Caps*, *Normal* or *Hidden*.
- **Chatbox Bar**: how the chatbox's progress bar looks. There are 15 styles, from *Line* to *Hash*; each choice in the list shows a sample.
- **Scanlines**, **Color Preset**, **Accent Hue**, **Accent Saturation**: the same look controls as Dino Monitor.
- **Hue Cycle**: *Off*, *Slow*, *Medium* or *Fast*. The accent color keeps turning round the color wheel: a full turn takes 2 minutes, 40 seconds or 12 seconds. Everything that follows the accent color turns with it, including the window, the visualizer and the widget's tinted styles.
- **Random Colors**: *Off*, *On Beats*, *Every 5 s* or *Every 15 s*. The accent color glides to a new random color on the music's beats (at most twice a second, and at least every 3 seconds while music plays, in case a song has no clear beats) or on a timer, and with more than one color the palette is reshuffled to random spots on the color wheel. It works together with Hue Cycle.
- **Colors**: how many colors the visualizers use at once: *1 Color* (the accent color), *2*, *3*, *4* or *6 Colors* spread round the color wheel from the accent hue, or *Rainbow*. The colors blend across the visualizer from left to right, in every style, in this window and on the XP widget's screen (which is classic green with 1 Color).

  While the color is moving, the hue slider shows where it is; moving the slider or picking a preset carries on from there. Turning the moving colors off keeps the color they were on.

When the window is too short for all the options, scroll the Options panel with the mouse wheel.

Options are remembered between launches in `%AppData%\Dino Visualizer\settings.ini`. The first time it runs, it brings over the widget's style, place, size and opacity, the visualizer and the accent color from Dino Recorder, if you've used Dino Recorder's widget.

## Notes

- Windows doesn't let apps read a player's actual frequencies, so the visualizers follow the player's loudness meter about 30 times a second, spread over a moving pattern.
- Dino Recorder has its own copy of the widget. If both apps are running, only the first one started gets `Ctrl+F11`.
- Memory: the app keeps only what's on screen. The window's backdrop is held at the window's own size, the Earth's NASA maps are loaded at just the detail the planet needs and let go when no Earth is showing, and album art is kept no bigger than it's shown. With the window hidden in the tray or minimized, its backdrop and pictures are let go too (they're made again in a moment when it's shown). Now and then (and right after anything big), unused memory is handed back to Windows.

## VR wrist (SteamVR)

In SteamVR, a hologram of the Now Playing card can sit on your wrist. Turn your wrist toward your face to see it, then point at its buttons with your other hand and pull the trigger to skip back, play or pause, or skip ahead. Turning your wrist toward your face twice quickly opens Dino Visualizer's whole window floating in front of you.

To set it up, pick a wrist in **VR Wrist** in Options: *Left Wrist (Inside)*, *Left Wrist (Outside)*, *Right Wrist (Inside)* or *Right Wrist (Outside)*.
- **Inside** is the palm side of your wrist; **outside** is the back of it, where a watch goes.
- The top bar says *VR Wrist: Waiting For SteamVR* until SteamVR is running, then *VR Wrist: On*.
- It never starts SteamVR itself. It connects when SteamVR is running and lets go when SteamVR closes.

**VR Wrist Look** picks which of the widget's looks the hologram has: *Dino*, *Windows*, *Aero*, *XP* or *Orb*.
- Each look is drawn see-through and glowing in your accent color.
- Dark parts fade away, and there are scanlines, a slight flicker, a band of light sweeping down it, and the odd glitch.
- The XP and Orb looks' visualizers move with the music, as on the desktop.

How to use it:
- **Show it**: turn the chosen side of your wrist toward your face. It fades in within about 40 degrees of facing you, and fades out once you turn it well away.
- **Press a button**: point your other controller at the card. SteamVR's laser beam comes out of it, the button under the beam lights up, and pulling the trigger presses it (Previous, Play/Pause, Next).
  - While the card is showing, your controllers point at it instead of the game; that ends when you turn your wrist away.
- **The buttons under the card** work the same way:

  | Button | What it does |
  |---|---|
  | **Open App** / **Close App** | Opens or closes Dino Visualizer's window in front of you |
  | **Smaller** / **Bigger** | Resizes the card (remembered, and shown in **VR Wrist Size**) |
  | **Rotate L** / **Rotate R** | Turns the card on your wrist, 15° a click (remembered) |
- **Open the app's window in VR**: turn your wrist toward your face twice within about a second (show it, turn away, show it again).
  - Dino Visualizer's window appears about 80 cm in front of where you're looking, a meter wide, even while it's put away in the tray on the desktop.
  - It's live: the visualizer, lyrics and everything else move, redrawn about 6 times a second.
  - Point at anything with the laser and pull the trigger to click it: tabs, player buttons, options, drop-downs. Sliders drag while the trigger's held, and the thumbstick scrolls lyrics, lists and the Options panel.
  - The buttons on its top bar are clicked like anything else:

    | Button | What it does |
    |---|---|
    | **Bring Here** | Moves the window in front of wherever you're looking now |
    | **Smaller** / **Bigger** | Resizes it (remembered) |
    | **Turn Left** / **Turn Right** | Turns it, 15° a click |
    | **✕** | Closes it (so does the double turn, or **Close App** under the wrist card) |

**VR Wrist Size** in Options makes the wrist card *Small*, *Medium* or *Large*. It changes straight away.

**How it works:** it uses SteamVR's own `openvr_api.dll` from your SteamVR folder, so nothing extra is installed. The card and the window are handed to SteamVR as textures on your graphics card (Direct3D 11) that are updated in place, so they don't flicker. The card is updated about 16 times a second while it's showing, and nothing is drawn while it's hidden. It sits up your forearm, clear of the controller, since SteamVR draws controllers in front of overlays. The laser is SteamVR's own (the same as in its dashboard), turned on for the card and the window only while they're showing.

## Updates

Dino Visualizer updates itself from its GitHub releases. It checks 30 seconds after starting and then every 6 hours, or when you click **Check For Updates** at the bottom of the window.

When there's a new version:
1. The button changes to **Update To v1.2.3**, and you click it to install. With **Updates** set to *Install Automatically* in Options, this happens by itself.
2. It downloads the new `DinoVisualizer.exe`, checks it's really Dino Visualizer at that version, and swaps it in.
3. It restarts into the new version, back in the tray if that's where it was.

The old exe is deleted the next time it starts.

What it fetches: only the latest release's details from GitHub's API and the exe attached to it, over HTTPS. Nothing about you or your music is sent.

### Publishing an update

After changing the code, run `release.ps1` with the new version number and a line about what's new:

```
powershell -ExecutionPolicy Bypass -File release.ps1 -Version 1.0.1 -Notes "Smaller adlibs in the chatbox"
```

It does everything in one go:
1. Puts the version in the source.
2. Builds the exe (in `_release`, so the app can stay running; a running copy updates itself from the release).
3. Commits and pushes the code.
4. Makes a GitHub release with `DinoVisualizer.exe` attached.

Every running copy then finds the update on its next check. Versions have to go up (1.0.1, 1.0.2, 1.1.0 and so on).

## Rebuilding

Source is in `src\DinoVisualizer.cs`. The icon is drawn by `make-icon.ps1`: Dino Monitor's skull holding a disc with glowing blue equalizer bars. The Earth visualizer's maps are in the `earth` folder, and `build.ps1` builds them into the exe. Without them, it generates a planet instead. To rebuild:

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
