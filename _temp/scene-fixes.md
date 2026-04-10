# Scene Setup Fixes Required

## 1. Disable Ghost Mode
- Select the **GameManager** GameObject in the scene hierarchy
- In the Inspector, under **Testing & Gameplay Settings**:
  - **Uncheck `Is Ghost`** (set to false)
- This is what prevents lives from being lost

## 2. Fix End Screen Score Display
The GameManager auto-discovers text elements by name. Your EndScreen exists but is missing the expected text children.

**On the EndScreen (game over screen) GameObject**, add a TMP text child:
- Create a child: `EndScreen > Right-click > UI > Text - TextMeshPro`
- **Name it exactly**: `FinalScoreText`
- Anchor: stretch or middle-center
- Font size: ~40-50
- Alignment: center
- Color: white
- The code will set its text to: `"You Win!\nScore: 690"` or `"Game Over!\nScore: 100"`

**Optional — for detailed breakdown**, add another TMP text child:
- Name it exactly: `GameOverText`
- The code will set it to:
  ```
  You Win!

  Walls Cleared: 9 (900)
  Lives Kept: 3 (600)

  Final Score: 1500
  ```

## 3. Fix Score Panel Height (top-right HUD tile)
The `scoreCounterText` IS found (named "Text (TMP)" inside panel "Score").
The text now shows two lines: score + timer countdown.

- Select the **Score** panel (top-right HUD tile)
- In the RectTransform, **increase the height** from its current value to at least **80-100** (or whatever fits two lines of text comfortably)
- Make sure the TMP text child has:
  - **Overflow mode**: Overflow (not Truncate/Ellipsis)
  - **Vertical alignment**: Middle or Top
  - **Enable word wrapping**: Off (so `\n` controls line breaks)

## 4. Optional — Add HUD Panel
Currently `hudScreen` and `hudText` are NULL. These are legacy fallbacks.
The individual counters (livesCounterText, scoreCounterText) ARE working.
No action needed unless you want the legacy combined HUD.

## Summary Checklist
- [ ] GameManager Inspector: uncheck `Is Ghost`
- [ ] EndScreen: add child TMP text named `FinalScoreText`
- [ ] EndScreen: (optional) add child TMP text named `GameOverText`
- [ ] Score panel: increase RectTransform height to ~80-100
- [ ] Score panel text: set overflow to Overflow, not Truncate
