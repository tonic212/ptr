# Poker Companion - automatic bottom-seat hero recognition

C# / WPF, .NET 8. Open PokerCompanion.sln in Visual Studio with the .NET desktop development workload and .NET 8 SDK. Set PokerCompanion.App as startup project. Build > Build Solution; press F5.

## Local vision recognition (Ollama)

Default model: `qwen3-vl:4b-instruct-q4_K_M` (Qwen3-VL 4B Instruct, Q4_K_M). The published model download is about 3.3 GB; runtime VRAM includes image/context buffers. A 10 GB GPU is a reasonable starting point, but real speed and accuracy must be measured on the user's PC. The app uses a 4,096-token context and at most 96 generated tokens per card reading. It does not benchmark tokens/second or claim a latency target.

1. Install the current Windows Ollama application from https://ollama.com/download/windows and leave it running.
2. In PowerShell, run `ollama pull qwen3-vl:4b-instruct-q4_K_M`.
3. Pull this branch, open `PokerCompanion.sln`, set `PokerCompanion.App` as startup project, then Build > Rebuild Solution and F5.
4. Leave **Use local vision model (Ollama)** checked. Click **Check Ollama** to verify the installed model advertises vision support.
5. Start PPPoker preview. If it captures the desktop, use **Select table area** to crop the entire portrait table.
6. The app reads a small bottom hero-card crop, upscaled for readability, through `http://127.0.0.1:11434/api/chat`. No cloud model or external inference endpoint is configured. Redirects/proxies are disabled for this client.
7. For an existing screenshot, click **Load screenshot**, select the table area if needed, then **Read frozen image** to retry. Static readings display cards but do not populate the live chart input.
8. Two matching model readings from separate live captures confirm a hand. If the crop changes, requests and confirmation are invalidated; absent/invalid/duplicate cards clear the detected hand. The UI continues capturing while one asynchronous inference request runs. A 2-second cooldown limits repeated inference; initial model loading may take longer, with a 90-second timeout.
9. Model name and enabled state are saved in `%LOCALAPPDATA%\PokerCompanion\vision-settings.json`. For a smaller comparison model, pull `qwen3-vl:2b-instruct-q4_K_M` and enter that exact name; benchmark both on the same frozen screenshots before choosing.
10. Uncheck local vision to use the existing template recognizer and its calibration/teaching controls. Those controls teach templates, not the LLM.

The vision path does not require manually taught rank templates. It remains capable of visual mistakes: valid JSON and matching responses are not proof that a card is correct. The regression suite verifies parsing, transport, invalid-card rejection, confirmation and crop-change guards with mocked Ollama responses. It does not test inference quality or GPU performance. Windows capture and actual model readings still need verification on the target PC.

Architecture: `IVisionCardReader`, response parsing and confirmation live in Core; `OllamaVisionCardReader`, `HeroVisionImage` and `VisionRecognitionController` adapt local HTTP, WPF image capture and asynchronous UI delivery. `MainWindow` connects the services to capture and controls. There is no additional NuGet dependency.

Future solver integration should consume validated game state (hero/board cards, positions, stacks, pot and action history) and a separate solver interface. Solver action frequencies/EVs remain authoritative; model prose must not invent solver outputs. This change implements hero-card reading only, not board/bet reading, solver calls or LLM strategy generation.

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
Seat/position recognition, board/street detection, betting-history tracking, equity calculation, solver integration and exact tournament ICM. Chart scenario, positions, street and stack must be set manually. Recognition only supplies your two-card hand class.

## Use
1. Select the chart that matches the positions and prior actions.
2. Enter effective stack in BB. For manual entry, uncheck **Use detected hand**, then enter AA, AKs or AKo.
3. Confirm ante. If payout-sensitive ICM is required, tick that option; the app suppresses the chart prescription.
4. Check the action against the source chart before trusting converted data.
5. Show floating result to place the manually selected result beside your table.
6. Refresh windows, select PPPoker (Steam), and start preview. Keep the target visible, unobstructed and restored. Stop preview when finished.

7. With local vision disabled, leave **Find bottom hero cards automatically** checked (default). No calibration is needed for the supplied portrait table layout: the locator searches for two face-up card backgrounds at the bottom seat. It relocates them each frame and after resizing. The detector does not infer your seat in other layouts.
8. If automatic location fails on another layout, select **Freeze frame**, then **Calibrate card 1** and drag tightly around its small rank and small suit. Repeat for card 2. This selects manual mode. Exclude borders, the large central suit artwork and adjacent cards.
9. If symbols remain uncertain, enter the actual visible pair (for example `9c 6s`; use T for ten and c/d/h/s for suits) and select **Teach visible cards**. Resume preview. Three consistent frames are required to accept a hand.
10. **Load screenshot** supports calibration and teaching without a live table. It shows single-frame matches as static previews, without accepting them as live observations.

Calibration is saved to `%LOCALAPPDATA%\PokerCompanion\recognition-profile.json`. Recalibrate after changing table layout or window proportions. Different rendering scales may need more teaching examples. Unknown, obstructed or ambiguous symbols clear the automatic hand/result. Template match scores are similarity measures, not calibrated probabilities of correctness. This version does not calculate equity or claim exact GTO recommendations.

The preview captures the visible desktop rectangle of the selected window, including occluding windows. It does not capture hidden/minimized contents. There is no stealth, detection bypass, automated betting or memory/network interception.

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
