# Poker Companion - automatic bottom-seat hero recognition

C# / WPF, .NET 8. Open PokerCompanion.sln in Visual Studio with the .NET desktop development workload and .NET 8 SDK. Set PokerCompanion.App as startup project. Build > Build Solution; press F5.

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
- Three matching live frames required before a recognized hand feeds chart lookup.
- Capture frames remain in memory; no upload, video storage or account connection.

## Not implemented
Seat/position recognition, board/street detection, betting-history tracking, equity calculation, solver integration and exact tournament ICM. Chart scenario, positions, street and stack must be set manually. Recognition only supplies your two-card hand class.

## Use
1. Select the chart that matches the positions and prior actions.
2. Enter effective stack in BB. For manual entry, uncheck **Use detected hand**, then enter AA, AKs or AKo.
3. Confirm ante. If payout-sensitive ICM is required, tick that option; the app suppresses the chart prescription.
4. Check the action against the source chart before trusting converted data.
5. Show floating result to place the manually selected result beside your table.
6. Refresh windows, select PPPoker (Steam), and start preview. Keep the target visible, unobstructed and restored. Stop preview when finished.

7. Leave **Find bottom hero cards automatically** checked (default). No calibration is needed for the supplied portrait table layout: the locator searches for two face-up card backgrounds at the bottom seat. It relocates them each frame and after resizing. The detector does not infer your seat in other layouts.
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
