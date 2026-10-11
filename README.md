# Poker Companion - automatic bottom-seat hero recognition

C# / WPF, .NET 8. Open PokerCompanion.sln in Visual Studio with the .NET desktop development workload and .NET 8 SDK. Set PokerCompanion.App as startup project. Build > Build Solution; press F5.

## One EXE launches the app and local vision

The standalone publisher creates **PokerCompanion.App.exe** with the .NET runtime, chart data, chart images and recognition seeds embedded. No adjacent Data folder or installed .NET runtime is required. Copy the EXE to the target Windows x64 PC and open it.

First launch (vision enabled):
- The app starts its own local Ollama service on `127.0.0.1:11435`, separate from a user's normal Ollama tray service.
- If Ollama is installed at its standard per-user location, the app uses its CLI. Otherwise it downloads the official pinned `v0.40.2` portable Windows NVIDIA runtime (about 1.47 GB), verifies its published SHA-256 and extracts it into `%LOCALAPPDATA%\PokerCompanion\runtime`.
- Missing Qwen3-VL weights are downloaded through Ollama's model-pull API, with progress shown in the app. Existing models in `%USERPROFILE%\.ollama\models` are reused. The default model download is about 3.3 GB.
- Internet is needed for initial downloads, and the portable setup requires at least 12 GB free space. Closing the app cancels setup. **Prepare local vision** retries it. Later launches use cached files and can run without network access.
- The EXE is the one file distributed and launched. Native runtime files and multi-gigabyte model weights are cached on disk; they are not embedded inside this EXE. This is first-run provisioning, not a fully offline model bundle.
- Only the Ollama process started by this app is stopped on exit. The app does not terminate a user's independent Ollama service. One UI instance per user session is permitted.

Visual Studio publish:
1. Open `PokerCompanion.sln` and rebuild.
2. Right-click **PokerCompanion.App > Publish**, choose the existing **Standalone** profile at `PokerCompanion.App/Properties/PublishProfiles/Standalone.pubxml`, then Publish.
3. Or run from the solution folder: `dotnet publish PokerCompanion.App/PokerCompanion.App.csproj -c Release -p:PublishProfile=Standalone -p:DebugType=embedded -o artifacts/standalone`.
4. Run `artifacts/standalone/PokerCompanion.App.exe`.

GitHub Actions builds, runs recognition checks, publishes, asserts the publish directory contains exactly one EXE, and starts that EXE in an asset smoke-test mode without adjacent Data files. Download the **PokerCompanion-win-x64** artifact from a successful workflow run. The smoke test does not download multi-gigabyte runtime/model files or prove GPU inference. Runtime/model startup still needs a check on the target PC.

Runtime provisioning is handled by `OllamaRuntimeInstaller` and `LocalOllamaHost`; `OllamaVisionCardReader` awaits runtime readiness before requests. Bundled chart extraction is handled by `BundledDataStore`, independent of recognition/UI logic. Bundled assets are content-addressed and repaired if missing/corrupt.

## Local vision recognition (Ollama)

Default model: `qwen3-vl:4b-instruct-q4_K_M` (Qwen3-VL 4B Instruct, Q4_K_M). The published model download is about 3.3 GB; runtime VRAM includes image/context buffers. A 10 GB GPU is a reasonable starting point, but real speed and accuracy must be measured on the user's PC. The app uses a 4,096-token context and at most 96 generated tokens per card reading. It does not benchmark tokens/second or claim a latency target.

1. Open the app. It prepares the local runtime and model automatically; no manual Ollama installation is required.
2. Wait for **Local vision ready** on first launch.
3. Pull this branch, open `PokerCompanion.sln`, set `PokerCompanion.App` as startup project, then Build > Rebuild Solution and F5.
4. Leave **Use local vision model (Ollama)** checked. Click **Prepare local vision** to verify the installed model advertises vision support.
5. Start PPPoker preview. If it captures the desktop, use **Select table area** to crop the entire portrait table.
6. The app reads a small bottom hero-card crop, upscaled for readability, through `http://127.0.0.1:11435/api/chat`. No cloud model or external inference endpoint is configured. Redirects/proxies are disabled for this client.
7. For an existing screenshot, click **Load screenshot**, select the table area if needed, then **Read frozen image** to retry. Static readings display cards but do not populate the live chart input.
8. Two matching model readings from separate live captures confirm a hand. If the crop changes, requests and confirmation are invalidated; absent/invalid/duplicate cards clear the detected hand. The UI continues capturing while one asynchronous inference request runs. A 2-second cooldown limits repeated inference; initial model loading may take longer, with a 90-second timeout.
9. Model name and enabled state are saved in `%LOCALAPPDATA%\PokerCompanion\vision-settings.json`. For a smaller comparison model, pull `qwen3-vl:2b-instruct-q4_K_M` and enter that exact name; benchmark both on the same frozen screenshots before choosing.
10. Uncheck local vision to use the existing template recognizer and its calibration/teaching controls. Those controls teach templates, not the LLM.

The vision path does not require manually taught rank templates. It remains capable of visual mistakes: valid JSON and matching responses are not proof that a card is correct. The regression suite verifies parsing, transport, invalid-card rejection, confirmation and crop-change guards with mocked Ollama responses. It does not test inference quality or GPU performance. Windows capture and actual model readings still need verification on the target PC.

Architecture: `IVisionCardReader`, response parsing and confirmation live in Core; `OllamaVisionCardReader`, `HeroVisionImage` and `VisionRecognitionController` adapt local HTTP, WPF image capture and asynchronous UI delivery. `MainWindow` connects the services to capture and controls. There is no additional NuGet dependency.

Future solver integration should consume validated game state (hero/board cards, positions, stacks, pot and action history) and a separate solver interface. Solver action frequencies/EVs remain authoritative; model prose must not invent solver outputs. The app implements hero-card and seat/button reading, not board/bet reading, solver calls or LLM strategy generation.

References: https://ollama.com/library/qwen3-vl/tags ; https://docs.ollama.com/api/chat ; https://docs.ollama.com/capabilities/vision ; https://docs.ollama.com/capabilities/structured-outputs

## Implemented
- All chart scenarios in the supplied PDF, converted into hand-class lookups.
- Original chart images shown alongside the lookup for verification.
- Hand-class normalization (AKs and KAs select the same entry).
- Effective-stack, ante and ICM coverage guards.
- Movable, topmost manual result window.
- Explicit start/stop visible-window capture preview, including PPPoker (Steam).
- Automatic bottom-seat card location with 13 rank and four suit seed templates.
- PPPoker window selected automatically when available; Start PPPoker preview requests focus once.
- Frozen-frame/screenshot calibration and user teaching; saved local profile.
- Two matching model captures (vision) or three matching frames (templates) before a recognized hand feeds chart lookup.
- Capture frames remain in memory; cropped cards are sent only to local Ollama when vision is enabled. No video storage or account connection.

## Not implemented
Board/street detection, betting-history tracking, equity calculation, solver integration and exact tournament ICM. Action scenario, opponent position, street and stack remain manual. Position detection does not determine whether someone has raised.

## Use
1. Leave **Detect hero position and switch ranges** checked and enable local vision for automatic selection. Select the action scenario and, when facing a raise/3-bet, the opponent position. For exact manual chart selection, turn off automatic position.
2. Enter effective stack in BB. For manual entry, uncheck **Use detected hand**, then enter AA, AKs or AKo.
3. Confirm ante. If payout-sensitive ICM is required, tick that option; the app suppresses the chart prescription.
4. Check the action against the source chart before trusting converted data.
5. Show floating result to display cards, detected position and the selected chart result beside your table.
6. Refresh windows, select PPPoker (Steam), and start preview. Keep the target visible, unobstructed and restored. Stop preview when finished.

7. With local vision disabled, leave **Find bottom hero cards automatically** checked (default). No calibration is needed for the supplied portrait table layout: the locator searches for two face-up card backgrounds at the bottom seat. It relocates them each frame and after resizing. The detector does not infer your seat in other layouts.
8. If automatic location fails on another layout, select **Freeze frame**, then **Calibrate card 1** and drag tightly around its small rank and small suit. Repeat for card 2. This selects manual mode. Exclude borders, the large central suit artwork and adjacent cards.
9. If symbols remain uncertain, enter the actual visible pair (for example `9c 6s`; use T for ten and c/d/h/s for suits) and select **Teach visible cards**. Resume preview. Three consistent frames are required to accept a hand.
10. **Load screenshot** supports calibration and teaching without a live table. It shows single-frame matches as static previews, without accepting them as live observations.

Calibration is saved to `%LOCALAPPDATA%\PokerCompanion\recognition-profile.json`. Recalibrate after changing table layout or window proportions. Different rendering scales may need more teaching examples. Unknown, obstructed or ambiguous symbols clear the automatic hand/result. Template match scores are similarity measures, not calibrated probabilities of correctness. This version does not calculate equity or claim exact GTO recommendations.

The preview captures the visible desktop rectangle of the selected window, including occluding windows. It does not capture hidden/minimized contents. There is no stealth, detection bypass, automated betting or memory/network interception.

## Automatic position and range switching

After confirming the hero cards, the same local vision model reads the freshest selected portrait table plus enlarged left/right/bottom seat-area crops from that same capture. The detail images preserve small white hexagonal/circular D badges; all returned coordinates refer to the full table. Fold labels do not exclude a player. The prompt distinguishes player avatars with name/stack panels from the decorative female dealer and explicitly Empty seats. It returns occupied **opponent** avatar centres and the opponent index holding the small D badge (`dealer=-1` means the badge belongs to hero). The app injects its known bottom hero anchor, instead of trusting a model-generated hero coordinate. Returning hero in the opponent list is rejected rather than silently counted twice. Debug builds write the returned position JSON to Visual Studio Output for diagnosis. Folded players must still count; empty/sitting-out seats and the decorative dealer avatar must not count. Code orders the seats clockwise and derives hero position, rather than asking the model to recommend a position or action.

Two matching layouts from separate live captures are required. Card changes, invalid/unclear layouts, capture/model changes, or expired position readings clear the automatic chart. Position is rechecked after a 4-second cooldown; card and position requests run sequentially to avoid competing GPU requests. Position expires 20 seconds after its source capture; a response already older than this cannot activate a range. Card inference time does not consume the new position capture’s freshness window. Missing-D and unclear-seat model responses now show separate diagnostic messages. Card and position requests both use an 8,192-token context (also the owned runtime default) to fit the multi-image seat prompt; the reported 4,644-token prompt cannot fit the previous 4,096-token limit. Keeping both requests at the same size avoids alternating context configurations. Position timeouts/request failures clear only the position/range; independently confirmed cards remain visible. Capture and inference latency means detection is not instantaneous. A frozen screenshot shows a position preview only and cannot select a live range.

The source charts use nine-seat labels: UTG, UTG+1, UTG+2, LJ, HJ, CO, BTN, SB, BB. At 3–8 player tables, labels map by distance from BTN (six-handed first-to-act maps to LJ). These are approximations using the existing nine-seat source, not independently solved short-handed ranges. Heads-up is unsupported. Opponent labels must use the same source-chart mapping.

Opening (**RFI**) selection switches with hero position. There is no BB RFI chart, so BB shows **No matching range**. Facing raises/3-bets requires an explicit opponent position; grouped source charts such as UTG/UTG+1 are matched automatically. The SB limp-versus-BB-raise chart has its own scenario. Missing combinations never fall back to another range. Select the action scenario each hand; the app does not read bets or action history.

Seat/button inference can be wrong even when two responses agree. Check the displayed position against the table before relying on the chart. Tests cover all dealer rotations for 3–9 seats, the supplied four-player seat geometry, enlargement/preservation of tiny badge evidence, multi-image transport and failure diagnostics, parsing/confirmation, chart matching for all existing scenarios and ViewModel transitions using mocked model responses. Actual PPPoker seat/button inference and GPU latency require testing on the Windows PC.

## Source assumptions
PokerCoaching source PDF: 100 BB effective stacks with an ante; author suggests approximate applicability from 50 BB upwards. These are starting charts, not documented exact solver outputs. No lower-stack/ICM solutions are fabricated.
Source bet sizes: in position initial raise 2.5 BB, 3-bet 3x opposing raise, 4-bet 2.5x opposing 3-bet. Out of position initial raise 3.5 BB, 3-bet 3.5x, 4-bet 2.75x. The app does not automatically verify these bet sizes.
White cells in facing-3-bet charts indicate hands outside the prior range, not new fold recommendations. Gray cells indicate fold. AA/KK/AKo in the SB opening versus BB 3-bet scenario were limped in the source and must use its separate limp scenario.

## Files to edit for future development
- PokerCompanion.Core/Chart.cs: chart model, hand normalization and coverage rules.
- PokerCompanion.App/Services/ScreenCaptureService.cs: window enumeration and explicit screen capture.
- PokerCompanion.App/Services/ChartRepository.cs: strategy file loading.
- PokerCompanion.App/ViewModels/MainViewModel.cs: presentation state and chart lookup.
- PokerCompanion.App/MainWindow.xaml: main UI.
- PokerCompanion.App/MainWindow.xaml.cs: UI lifecycle, capture timer, result-window creation.
- PokerCompanion.App/OverlayWindow.xaml: floating result UI.
- PokerCompanion.App/Data/charts.json: extracted chart data.

## Validation and limitations
Chart structure and image references were checked locally. Every chart has all 169 hand classes accounting for 1,326 card combinations. An OCR comparison fully matched action counts for 38 of 68 charts; the remaining 30 have incomplete or misread OCR and still require manual source comparison. All chart images are provided to support that review. Matching counts alone does not prove every hand cell is correct. C# syntax and XAML structure receive static checks when available, but the Linux generation environment has no .NET SDK or Windows desktop runtime: compilation, window capture and WPF rendering must be verified in Visual Studio on Windows. This is a prototype, not a validated production poker advisor.


## Recognition checks
GitHub Actions builds the WPF solution on Windows and runs the console regression suite. To run locally:

```powershell
dotnet build PokerCompanion.sln --configuration Release
dotnet run --project PokerCompanion.Core.Tests --configuration Release --no-build
```

The suite uses anonymous card crops against plain table-sized backgrounds plus a two-corner fixture. It checks automatic bottom-seat location on four table samples (9c 6s, 5s 8c, As 6c, Js 5c), resizing and single-card rejection, temporal confirmation, stale-result clearing, resize behavior, ambiguous symbols, calibration mapping and hand-class normalization. Full table screenshots and player names are not included. Real window capture, overlay placement and UI calibration still require a Windows desktop check.

Automatic location requires a visible portrait table and two separated neutral/light card backgrounds within the bottom seat search area. Covered cards, unusual skins, hand-history screens and overlapping windows may fail detection. Symbol matching remains template based and can still need teaching for other font/rendering scales. Automatic location does not calculate equity, identify board/street or select a chart scenario.

When PPPoker exposes a desktop-sized capture rectangle, the automatic locator first finds the largest clear blue portrait table within that frame. Similar-sized multiple tables are rejected. Titled PPPoker child windows are included and portrait windows with versioned headings (such as PPPoker-v228) are preferred. A drawn custom heading may not be exposed as native window text; the blue table locator handles that case. Capture status displays the captured dimensions.


## Select the table area
If automatic table location fails because the blue wallpaper joins the table background, choose **Select table area**. The preview temporarily shows the full unmodified capture and holds it still. Drag around the entire PPPoker portrait table, including its heading and bottom controls. The selected area is saved locally with the recognition profile; automatic card detection stays enabled. Capture then shows only that table area. Select it again if PPPoker moves or resizes within the desktop capture. **Reset table area** restores automatic table location. If capture proportions change, a saved selection is rejected until you select the area again.

## Vision correction: printed ten versus ace
The established hero crop is retained for ordinary readings. When a conservative image-geometry check detects a clearly printed ten (left vertical stroke and right enclosed hole), vision uses two separated enlarged rank/suit corners with nearest-neighbour scaling. The output schema accepts the literal printed rank `10`, normalized to `T` by the parser. The image guard vetoes model answers that disagree with a printed ten; it never automatically rewrites a reading. Two matching captures check consistency, not correctness. Regression tests use anonymized 8-spade/10-diamond pixels from the reported failure. Model inference quality remains to be checked on the target PC.
