# Poker Companion - first Windows prototype

C# / WPF, .NET 8. Open PokerCompanion.sln in Visual Studio with the .NET desktop development workload and .NET 8 SDK. Set PokerCompanion.App as startup project. Build > Build Solution; press F5.

## Implemented
- All chart scenarios in the supplied PDF, converted into hand-class lookups.
- Original chart images shown alongside the lookup for verification.
- Hand-class normalization (AKs and KAs select the same entry).
- Effective-stack, ante and ICM coverage guards.
- Movable, topmost manual result window.
- Explicit start/stop visible-window capture preview, including PPPoker (Steam).
- Capture frames remain in memory; no upload, video storage or account connection.

## Not implemented
Automatic card recognition, seat/position recognition, betting-history tracking, equity calculation, solver integration and exact tournament ICM. This version does not automatically recommend an action from captured frames. A PPPoker (Steam) table screenshot is needed for the next recognition/calibration stage.

## Use
1. Select the chart that matches the positions and prior actions.
2. Enter a hand class: AA, AKs or AKo. Enter effective stack in BB.
3. Confirm ante. If payout-sensitive ICM is required, tick that option; the app suppresses the chart prescription.
4. Check the action against the source chart before trusting converted data.
5. Show floating result to place the manually selected result beside your table.
6. Refresh windows, select PPPoker (Steam), and start preview. Keep the target visible, unobstructed and restored. Stop preview when finished.

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
